import { describe, expect, it } from "vitest";

import { allowedFrom, layoutPage, sameKinds, wrapTitle } from "../src/browser-util.js";

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

describe("layoutPage", () => {
	/** Names alone are enough here: the layout only ever looks at `pinned`. */
	const list = (...names: string[]) => names.map((name) => ({ name }));

	const reset = { name: "Reset", pinned: true };

	it("pages a plain catalog across the slots", () => {
		const entries = list("a", "b", "c", "d", "e");

		expect(layoutPage(entries, 2, 0)).toEqual({ visible: list("a", "b"), pageCount: 3, page: 0 });
		expect(layoutPage(entries, 2, 1)).toEqual({ visible: list("c", "d"), pageCount: 3, page: 1 });
		// The last page is short rather than padded; the browser blanks what is left over.
		expect(layoutPage(entries, 2, 2)).toEqual({ visible: list("e"), pageCount: 3, page: 2 });
	});

	it("keeps a pinned entry on every page and pages the rest around it", () => {
		const entries = [reset, ...list("a", "b", "c", "d")];

		expect(layoutPage(entries, 3, 0).visible).toEqual([reset, ...list("a", "b")]);
		expect(layoutPage(entries, 3, 1).visible).toEqual([reset, ...list("c", "d")]);
		// Four designs over two slots each, not five entries over three.
		expect(layoutPage(entries, 3, 0).pageCount).toBe(2);
	});

	it("drops the pin when honouring it would leave nothing to page with", () => {
		const entries = [reset, ...list("a", "b")];

		// One slot: pinning Reset would show Reset and nothing else, forever.
		expect(layoutPage(entries, 1, 0)).toEqual({ visible: [reset], pageCount: 3, page: 0 });
		expect(layoutPage(entries, 1, 1).visible).toEqual(list("a"));
	});

	it("clamps a page that no longer exists", () => {
		const entries = list("a", "b", "c");

		// Slots disappear when a profile is edited, taking pages with them.
		expect(layoutPage(entries, 3, 4)).toEqual({ visible: entries, pageCount: 1, page: 0 });
		expect(layoutPage(entries, 1, -2)).toEqual({ visible: list("a"), pageCount: 3, page: 0 });
	});

	it("survives an empty catalog and a browser with no slots", () => {
		expect(layoutPage([], 4, 2)).toEqual({ visible: [], pageCount: 1, page: 0 });
		expect(layoutPage(list("a"), 0, 0)).toEqual({ visible: [], pageCount: 1, page: 0 });
	});

	it("pins a leading run rather than only the first entry", () => {
		const second = { name: "Automation", pinned: true };
		const entries = [reset, second, ...list("a", "b", "c")];

		expect(layoutPage(entries, 3, 0).visible).toEqual([reset, second, ...list("a")]);
		expect(layoutPage(entries, 3, 2).visible).toEqual([reset, second, ...list("c")]);

		// A pin further down the list is not one: only the leading run holds a slot.
		const late = [...list("a"), { name: "Reset", pinned: true }];
		expect(layoutPage(late, 1, 1).visible).toEqual([{ name: "Reset", pinned: true }]);
	});
});
