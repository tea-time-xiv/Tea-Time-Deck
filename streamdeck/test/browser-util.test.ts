import { describe, expect, it } from "vitest";

import { allowedFrom, sameKinds, wrapTitle } from "../src/browser-util.js";

describe("wrapTitle", () => {
	it("leaves a short name alone", () => {
		expect(wrapTitle("Wave")).toBe("Wave");
		// Nine characters is the boundary the renderer is happy with.
		expect(wrapTitle("Sit Chair")).toBe("Sit Chair");
	});

	it("leaves a long single word alone, because there is nowhere to break it", () => {
		expect(wrapTitle("Ultramarathon")).toBe("Ultramarathon");
	});

	it("splits a long name across two lines, weighting the first", () => {
		expect(wrapTitle("Company Chocobo")).toBe("Company\nChocobo");
		// Three words: the extra one goes above, not below.
		expect(wrapTitle("Wind-up Airship Model")).toBe("Wind-up Airship\nModel");
	});

	it("handles an empty name", () => {
		expect(wrapTitle("")).toBe("");
	});
});

describe("allowedFrom", () => {
	it("treats an empty allow-list as no allow-list", () => {
		expect(allowedFrom({ kinds: [] })).toBeUndefined();
	});

	it("treats an absent allow-list as no allow-list", () => {
		expect(allowedFrom({})).toBeUndefined();
		expect(allowedFrom({ kind: "emote", page: 2 })).toBeUndefined();
	});

	it("passes a populated allow-list through", () => {
		expect(allowedFrom({ kinds: ["emote", "gearset"] })).toEqual(["emote", "gearset"]);
	});
});

describe("sameKinds", () => {
	it("counts two absent lists as the same", () => {
		expect(sameKinds(undefined, undefined)).toBe(true);
	});

	it("counts an absent list and a present one as different", () => {
		expect(sameKinds(undefined, ["emote"])).toBe(false);
		expect(sameKinds(["emote"], undefined)).toBe(false);
	});

	it("compares contents and order", () => {
		expect(sameKinds(["emote", "mount"], ["emote", "mount"])).toBe(true);
		expect(sameKinds(["emote", "mount"], ["mount", "emote"])).toBe(false);
		expect(sameKinds(["emote"], ["emote", "mount"])).toBe(false);
		expect(sameKinds([], [])).toBe(true);
	});
});
