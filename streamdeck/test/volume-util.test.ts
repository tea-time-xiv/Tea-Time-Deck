import { describe, expect, it } from "vitest";

import { channelOf, rotationDelta, volumeFeedback } from "../src/volume-util.js";
import type { StatusSnapshot } from "../src/xiv-client.js";

describe("rotationDelta", () => {
	it("moves by the chosen step per tick, in the direction turned", () => {
		expect(rotationDelta(2, false, 5)).toBe(10);
		expect(rotationDelta(-3, false, 2)).toBe(-6);
	});

	it("falls back to five when no step was chosen, or a nonsense one was", () => {
		expect(rotationDelta(1, false, undefined)).toBe(5);
		expect(rotationDelta(1, false, 0)).toBe(5);
		expect(rotationDelta(1, false, -4)).toBe(5);
	});

	it("steps one at a time while pressed, whatever the step", () => {
		expect(rotationDelta(3, true, 10)).toBe(3);
		expect(rotationDelta(-1, true, undefined)).toBe(-1);
	});
});

describe("channelOf", () => {
	const status = {
		volume: [
			{ channel: "master", volume: 80, muted: false },
			{ channel: "party", volume: 40 },
		],
	} as unknown as StatusSnapshot;

	it("finds the channel by wire name", () => {
		expect(channelOf(status, "party")?.volume).toBe(40);
	});

	it("answers undefined for no snapshot, an older server's snapshot, or an unknown channel", () => {
		expect(channelOf(undefined, "master")).toBeUndefined();
		expect(channelOf({} as StatusSnapshot, "master")).toBeUndefined();
		expect(channelOf(status, "bgm")).toBeUndefined();
	});
});

describe("volumeFeedback", () => {
	it("shows the level as a percentage, on a full-strength bar", () => {
		expect(volumeFeedback("bgm", { channel: "bgm", volume: 45, muted: false }, true)).toEqual({
			title: "Music",
			value: "45%",
			indicator: { value: 45, opacity: 1 },
		});
	});

	it("keeps a muted channel's level on the bar, dimmed, because that is where it comes back", () => {
		expect(volumeFeedback("master", { channel: "master", volume: 70, muted: true }, true)).toEqual({
			title: "Master",
			value: "Muted",
			indicator: { value: 70, opacity: 0.4 },
		});
	});

	it("says offline when the game is not there, and nothing more when it is but sent no level", () => {
		expect(volumeFeedback("voice", undefined, false).value).toBe("offline");
		expect(volumeFeedback("voice", undefined, true).value).toBe("—");
	});

	it("titles an unknown channel with its own name rather than nothing", () => {
		expect(volumeFeedback("mystery", undefined, true).title).toBe("mystery");
	});
});
