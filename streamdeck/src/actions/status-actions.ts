import streamDeck, {
	action,
	SingletonAction,
	type DidReceiveSettingsEvent,
	type KeyAction,
	type PropertyInspectorDidAppearEvent,
	type SendToPluginEvent,
	type WillAppearEvent,
	type WillDisappearEvent,
} from "@elgato/streamdeck";
import type { JsonObject, JsonValue } from "@elgato/utils";

import {
	renderCooldown,
	renderDuty,
	renderJob,
	renderMessage,
	renderRetainers,
	renderVitals,
	toDataUri,
} from "../status-render.js";
import { xiv } from "../xiv-client.js";

type NoSettings = Record<string, never>;

/**
 * Shared plumbing for read-only keys.
 *
 * Every visible instance repaints whenever the game pushes a new snapshot. Instances are
 * tracked here rather than through `this.actions` so that keys which also need a local
 * clock -- ventures and cooldowns count down between pushes -- can be ticked cheaply.
 */
abstract class StatusAction<T extends JsonObject = NoSettings> extends SingletonAction<T> {
	/** Settings are cached alongside the key so a repaint never round-trips to fetch them. */
	protected readonly visible = new Map<string, { target: KeyAction<T>; settings: T }>();

	/** True for keys whose face changes between pushes and so need a local clock. */
	public readonly ticks: boolean = false;

	override async onWillAppear(ev: WillAppearEvent<T>): Promise<void> {
		if (!ev.action.isKey()) {
			return;
		}

		this.visible.set(ev.action.id, { target: ev.action, settings: ev.payload.settings });

		// Read-only keys never carry a user title; the artwork says everything.
		await ev.action.setTitle("");
		await this.paint(ev.action, ev.payload.settings);
	}

	override onWillDisappear(ev: WillDisappearEvent<T>): void {
		this.visible.delete(ev.action.id);
	}

	override async onDidReceiveSettings(ev: DidReceiveSettingsEvent<T>): Promise<void> {
		if (!ev.action.isKey()) {
			return;
		}

		this.visible.set(ev.action.id, { target: ev.action, settings: ev.payload.settings });
		await this.paint(ev.action, ev.payload.settings);
	}

	/** Repaints every visible instance. */
	public async repaintAll(): Promise<void> {
		await Promise.all(
			[...this.visible.values()].map(async ({ target, settings }) => {
				try {
					await this.paint(target, settings);
				} catch (error) {
					streamDeck.logger.debug(`Status repaint failed: ${asMessage(error)}`);
				}
			}),
		);
	}

	protected abstract paint(target: KeyAction<T>, settings: T): Promise<void>;
}

type VitalsSettings = { resource?: string };

@action({ UUID: "xiv.teatime.deck.status.vitals" })
export class VitalsAction extends StatusAction<VitalsSettings> {
	protected override async paint(target: KeyAction<VitalsSettings>, settings: VitalsSettings): Promise<void> {
		const status = xiv.status;

		if (status === undefined || !status.loggedIn) {
			await target.setImage(toDataUri(renderMessage("HP", offlineText())));
			return;
		}

		// "auto" follows the job: GP on gatherers, CP on crafters, MP on everyone else.
		await target.setImage(toDataUri(renderVitals(status.vitals, settings.resource ?? "auto")));
	}
}

@action({ UUID: "xiv.teatime.deck.status.job" })
export class JobAction extends StatusAction {
	protected override async paint(target: KeyAction<NoSettings>): Promise<void> {
		const status = xiv.status;

		if (status === undefined || !status.loggedIn || status.job === null) {
			await target.setImage(toDataUri(renderMessage("JOB", offlineText())));
			return;
		}

		let icon: string | undefined;
		try {
			icon = await xiv.getIcon(status.job.iconId);
		} catch {
			// Fall back to the abbreviation, which the renderer handles.
		}

		await target.setImage(toDataUri(renderJob(status.job, icon)));
	}
}

@action({ UUID: "xiv.teatime.deck.status.duty" })
export class DutyAction extends StatusAction {
	protected override async paint(target: KeyAction<NoSettings>): Promise<void> {
		const status = xiv.status;

		if (status === undefined || !status.loggedIn) {
			await target.setImage(toDataUri(renderMessage("DUTY", offlineText())));
			return;
		}

		await target.setImage(toDataUri(renderDuty(status.duty)));
	}
}

@action({ UUID: "xiv.teatime.deck.status.retainers" })
export class RetainersAction extends StatusAction {
	public override readonly ticks = true;

	protected override async paint(target: KeyAction<NoSettings>): Promise<void> {
		const status = xiv.status;

		if (status === undefined || !status.loggedIn) {
			await target.setImage(toDataUri(renderMessage("VENTURES", offlineText())));
			return;
		}

		// The completion time is absolute, so the countdown ticks locally without the
		// game having to push anything.
		await target.setImage(toDataUri(renderRetainers(status.retainers, Math.floor(Date.now() / 1000))));
	}
}

type CooldownSettings = { id?: number; name?: string; iconId?: number };

@action({ UUID: "xiv.teatime.deck.status.cooldown" })
export class CooldownAction extends StatusAction<CooldownSettings> {
	public override readonly ticks = true;

	override onPropertyInspectorDidAppear(ev: PropertyInspectorDidAppearEvent<CooldownSettings>): Promise<void> | void {
		return this.#sendSources();
	}

	override async onSendToPlugin(ev: SendToPluginEvent<JsonValue, CooldownSettings>): Promise<void> {
		if ((ev.payload as { event?: string })?.event === "getSources") {
			await this.#sendSources();
		}
	}

	async #sendSources(): Promise<void> {
		try {
			await streamDeck.ui.sendToPropertyInspector({ event: "sources", sources: await xiv.getCooldownSources() });
		} catch (error) {
			await streamDeck.ui.sendToPropertyInspector({ event: "error", message: asMessage(error) });
		}
	}

	protected override async paint(target: KeyAction<CooldownSettings>, settings: CooldownSettings): Promise<void> {
		const status = xiv.status;

		if (status === undefined || !status.loggedIn) {
			await target.setImage(toDataUri(renderMessage("RECAST", offlineText())));
			return;
		}

		if (settings.id === undefined) {
			await target.setImage(toDataUri(renderMessage("RECAST", "pick one")));
			return;
		}

		// Only actions actually on recast are sent, so an absent id means ready.
		const active = status.cooldowns.find((cooldown) => cooldown.id === settings.id);

		let icon: string | undefined;
		if (settings.iconId) {
			try {
				icon = await xiv.getIcon(settings.iconId);
			} catch {
				// Renderer falls back to the name.
			}
		}

		await target.setImage(
			toDataUri(renderCooldown(settings.name ?? "", icon, active?.remaining ?? 0, active?.total ?? 0)),
		);
	}
}

export const statusActions = [
	new VitalsAction(),
	new JobAction(),
	new DutyAction(),
	new RetainersAction(),
	new CooldownAction(),
];

/**
 * Ventures and recasts count down between pushes, so drive a local clock for those two.
 * Vitals, job and duty change only when the game says so and are repainted on the push
 * instead -- ticking them would be a needless image upload every second.
 */
export function startStatusClock(): NodeJS.Timeout {
	const ticking = statusActions.filter((instance) => instance.ticks);

	return setInterval(() => {
		void Promise.all(ticking.map((instance) => instance.repaintAll()));
	}, 1000);
}

export async function repaintAllStatus(): Promise<void> {
	await Promise.all(statusActions.map((instance) => instance.repaintAll()));
}

function offlineText(): string {
	return "offline";
}

function asMessage(error: unknown): string {
	return error instanceof Error ? error.message : String(error);
}
