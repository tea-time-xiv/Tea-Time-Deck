import streamDeck, {
	action,
	SingletonAction,
	type DialAction,
	type DialDownEvent,
	type DialRotateEvent,
	type DialUpEvent,
	type DidReceiveSettingsEvent,
	type TouchTapEvent,
	type WillAppearEvent,
	type WillDisappearEvent,
} from "@elgato/streamdeck";

import {
	channelOf,
	DEFAULT_CHANNEL,
	rotationDelta,
	volumeFeedback,
	type VolumeSettings,
} from "../volume-util.js";
import { type VolumeChange, xiv } from "../xiv-client.js";

/**
 * One of the game's volume sliders on a Stream Deck + dial: turn to move it, press or tap
 * the strip to mute it.
 *
 * The level shown is always the game's, never a local guess -- every turn sends a delta and
 * paints what comes back, and a slider dragged in the game's own settings arrives on the
 * next status push. Two dials on the same channel therefore cannot disagree.
 */
@action({ UUID: "xiv.teatime.deck.volume" })
export class VolumeAction extends SingletonAction<VolumeSettings> {
	readonly #visible = new Map<string, { target: DialAction<VolumeSettings>; settings: VolumeSettings }>();

	/**
	 * Dials held down right now, and whether they have been turned since. Turning while
	 * pressed is the fine adjustment, so a release after one must not also toggle mute.
	 */
	readonly #held = new Map<string, { turned: boolean }>();

	/**
	 * What each dial was last sent. Every status push repaints the dials, and most pushes are
	 * a health tick that changed nothing a dial shows.
	 */
	readonly #shown = new Map<string, string>();

	override async onWillAppear(ev: WillAppearEvent<VolumeSettings>): Promise<void> {
		if (!ev.action.isDial()) {
			return;
		}

		this.#visible.set(ev.action.id, { target: ev.action, settings: ev.payload.settings });
		await this.#paint(ev.action, ev.payload.settings);
	}

	override onWillDisappear(ev: WillDisappearEvent<VolumeSettings>): void {
		this.#visible.delete(ev.action.id);
		this.#held.delete(ev.action.id);
		this.#shown.delete(ev.action.id);
	}

	override async onDidReceiveSettings(ev: DidReceiveSettingsEvent<VolumeSettings>): Promise<void> {
		if (!ev.action.isDial()) {
			return;
		}

		this.#visible.set(ev.action.id, { target: ev.action, settings: ev.payload.settings });
		await this.#paint(ev.action, ev.payload.settings);
	}

	override async onDialRotate(ev: DialRotateEvent<VolumeSettings>): Promise<void> {
		const held = this.#held.get(ev.action.id);
		if (held && ev.payload.pressed) {
			held.turned = true;
		}

		const delta = rotationDelta(ev.payload.ticks, ev.payload.pressed, ev.payload.settings.step);
		await this.#change(ev.action, ev.payload.settings, { delta });
	}

	override onDialDown(ev: DialDownEvent<VolumeSettings>): void {
		this.#held.set(ev.action.id, { turned: false });
	}

	override async onDialUp(ev: DialUpEvent<VolumeSettings>): Promise<void> {
		const held = this.#held.get(ev.action.id);
		this.#held.delete(ev.action.id);

		if (held === undefined || held.turned) {
			return;
		}

		await this.#toggleMute(ev.action, ev.payload.settings);
	}

	override async onTouchTap(ev: TouchTapEvent<VolumeSettings>): Promise<void> {
		await this.#toggleMute(ev.action, ev.payload.settings);
	}

	/** Repaints every visible dial. */
	public async repaintAll(): Promise<void> {
		await Promise.all(
			[...this.#visible.values()].map(async ({ target, settings }) => {
				try {
					await this.#paint(target, settings);
				} catch (error) {
					streamDeck.logger.debug(`Volume repaint failed: ${asMessage(error)}`);
				}
			}),
		);
	}

	async #toggleMute(target: DialAction<VolumeSettings>, settings: VolumeSettings): Promise<void> {
		const channel = settings.channel ?? DEFAULT_CHANNEL;
		const current = channelOf(xiv.status, channel);

		// Self, party and others have no mute in the game, and inventing one -- a zero that
		// remembers what it was -- would be a slider the game's own settings could not explain.
		if (current?.muted === undefined || current.muted === null) {
			await target.showAlert();
			return;
		}

		await this.#change(target, settings, { muted: !current.muted });
	}

	async #change(target: DialAction<VolumeSettings>, settings: VolumeSettings, change: VolumeChange): Promise<void> {
		const channel = settings.channel ?? DEFAULT_CHANNEL;

		try {
			// The client repaints every dial from the answer, so nothing to paint here.
			await xiv.setVolume(channel, change);
		} catch (error) {
			streamDeck.logger.info(`Could not change ${channel} volume: ${asMessage(error)}`);
			await target.showAlert();
		}
	}

	async #paint(target: DialAction<VolumeSettings>, settings: VolumeSettings): Promise<void> {
		const channel = settings.channel ?? DEFAULT_CHANNEL;
		const feedback = volumeFeedback(channel, channelOf(xiv.status, channel), xiv.connected);

		const serialised = JSON.stringify(feedback);
		if (this.#shown.get(target.id) === serialised) {
			return;
		}

		this.#shown.set(target.id, serialised);
		await target.setFeedback(feedback);
	}
}

export const volumeAction = new VolumeAction();

function asMessage(error: unknown): string {
	return error instanceof Error ? error.message : String(error);
}
