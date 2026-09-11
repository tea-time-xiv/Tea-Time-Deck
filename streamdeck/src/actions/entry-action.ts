import streamDeck, {
	action,
	SingletonAction,
	type DidReceiveSettingsEvent,
	type KeyDownEvent,
	type PropertyInspectorDidAppearEvent,
	type SendToPluginEvent,
	type WillAppearEvent,
} from "@elgato/streamdeck";
// The SDK uses these types in its own public signatures but re-exports them from here.
import type { JsonValue } from "@elgato/utils";

import { renderEntryFace, toDataUri } from "../status-render.js";
import { xiv } from "../xiv-client.js";

/** What the property inspector stores against a key. */
export type EntrySettings = {
	kind?: string;
	id?: number;
	name?: string;
	iconId?: number;
	/**
	 * For kinds the game does not number. Saved rather than the position in the list, so
	 * a key still points at the design it was set to after others are added or deleted.
	 */
	key?: string;
	/**
	 * Only used to draw kinds with no game artwork, and saved alongside the name for the
	 * same reason the name is: the key has to paint on appearance, before the game is
	 * necessarily running. A recolour in Glamourer therefore shows here when the entry is
	 * picked again, which is a staler face than the browser's and a cheap one to live with.
	 */
	category?: string | null;
	color?: number;
};

/** Messages the property inspector sends us. */
type UiMessage = { event: "getKinds" } | { event: "getEntries"; kind: string };

@action({ UUID: "xiv.teatime.deck.entry" })
export class EntryAction extends SingletonAction<EntrySettings> {
	override onWillAppear(ev: WillAppearEvent<EntrySettings>): Promise<void> | void {
		return this.#render(ev.action, ev.payload.settings);
	}

	/** Fires when the inspector saves a different entry; repaint to match. */
	override onDidReceiveSettings(ev: DidReceiveSettingsEvent<EntrySettings>): Promise<void> | void {
		return this.#render(ev.action, ev.payload.settings);
	}

	override async onKeyDown(ev: KeyDownEvent<EntrySettings>): Promise<void> {
		const { kind, id, key } = ev.payload.settings;

		if (kind === undefined || (id === undefined && key === undefined)) {
			streamDeck.logger.info("Key pressed before an entry was chosen.");
			await ev.action.showAlert();
			return;
		}

		try {
			await xiv.execute(kind, id ?? 0, key);
			await ev.action.showOk();
		} catch (error) {
			// Refusals are routine -- game closed, already mounted, wrong zone.
			streamDeck.logger.info(`Could not execute ${kind} ${key ?? id}: ${asMessage(error)}`);
			await ev.action.showAlert();
		}
	}

	override onPropertyInspectorDidAppear(ev: PropertyInspectorDidAppearEvent<EntrySettings>): Promise<void> | void {
		// The inspector cannot ask until it has registered, so push the list list at it.
		return this.#sendKinds();
	}

	override async onSendToPlugin(ev: SendToPluginEvent<JsonValue, EntrySettings>): Promise<void> {
		const message = ev.payload as UiMessage;

		if (message?.event === "getKinds") {
			await this.#sendKinds();
			return;
		}

		if (message?.event === "getEntries") {
			await this.#sendEntries(message.kind);
		}
	}

	async #sendKinds(): Promise<void> {
		try {
			const kinds = await xiv.getKinds();
			await streamDeck.ui.sendToPropertyInspector({ event: "kinds", kinds });
		} catch (error) {
			await streamDeck.ui.sendToPropertyInspector({ event: "error", message: asMessage(error) });
		}
	}

	async #sendEntries(kind: string): Promise<void> {
		try {
			const entries = await xiv.getEntries(kind);
			await streamDeck.ui.sendToPropertyInspector({ event: "entries", kind, entries });
		} catch (error) {
			await streamDeck.ui.sendToPropertyInspector({ event: "error", message: asMessage(error) });
		}
	}

	async #render(target: WillAppearEvent<EntrySettings>["action"], settings: EntrySettings): Promise<void> {
		if (settings.name === undefined) {
			// Nothing chosen yet: say so over the artwork the manifest declares.
			await target.setTitle("Set\nentry");
			await target.setImage();
			return;
		}

		// 0 is an entry with no game artwork of its own -- a Glamourer design. Drawn here
		// the same way the browser draws it, so one design looks the same on either key.
		if (settings.iconId === undefined || settings.iconId === 0) {
			await target.setTitle("");
			await target.setImage(toDataUri(renderEntryFace(settings)));
			return;
		}

		await target.setTitle(settings.name);

		try {
			await target.setImage(await xiv.getIcon(settings.iconId));
		} catch (error) {
			// Keep the default artwork and the title; the key still works.
			streamDeck.logger.debug(`No icon for ${settings.iconId}: ${asMessage(error)}`);
			await target.setImage();
		}
	}
}

function asMessage(error: unknown): string {
	return error instanceof Error ? error.message : String(error);
}
