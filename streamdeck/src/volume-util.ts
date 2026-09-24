import type { StatusSnapshot, VolumeChannel } from "./xiv-client.js";

/**
 * The decisions behind a volume dial, kept apart from the action for the same reason
 * `browser-util.ts` is: the action reaches the SDK on import, and these are worth checking
 * without a deck attached.
 */

/** Wire name to the label the touch strip shows, in the game's own order. */
export const VOLUME_CHANNELS: Readonly<Record<string, string>> = {
	master: "Master",
	bgm: "Music",
	effects: "Effects",
	voice: "Voice",
	system: "System",
	ambient: "Ambient",
	performance: "Perform",
	self: "Self",
	party: "Party",
	others: "Others",
};

export const DEFAULT_CHANNEL = "master";
export const DEFAULT_STEP = 5;

export type VolumeSettings = { channel?: string; step?: number };

/**
 * How far one rotate event moves the slider. Turning while pressed is the fine
 * adjustment, one point a tick whatever the step, because a coarse step cannot land on
 * every value and the game's own slider can.
 */
export function rotationDelta(ticks: number, pressed: boolean, step: number | undefined): number {
	const size = pressed ? 1 : step !== undefined && step > 0 ? Math.round(step) : DEFAULT_STEP;
	return ticks * size;
}

export function channelOf(status: StatusSnapshot | undefined, channel: string): VolumeChannel | undefined {
	return status?.volume?.find((held) => held.channel === channel);
}

/** What the `$B1` layout is handed: a label, a readout and the bar under it. */
export type VolumeFeedback = {
	title: string;
	value: string;
	indicator: { value: number; opacity: 0.4 | 1 };
};

/**
 * A muted channel keeps its level on the bar, dimmed, because that is the level it comes
 * back at -- the game's mute does not move the slider, and a bar dropping to nothing would
 * say it had.
 */
export function volumeFeedback(channel: string, held: VolumeChannel | undefined, connected: boolean): VolumeFeedback {
	const title = VOLUME_CHANNELS[channel] ?? channel;

	if (held === undefined) {
		return { title, value: connected ? "—" : "offline", indicator: { value: 0, opacity: 0.4 } };
	}

	if (held.muted) {
		return { title, value: "Muted", indicator: { value: held.volume, opacity: 0.4 } };
	}

	return { title, value: `${held.volume}%`, indicator: { value: held.volume, opacity: 1 } };
}
