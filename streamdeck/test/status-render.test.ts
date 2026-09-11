import { describe, expect, it } from "vitest";

import { renderDesign, renderEntryFace } from "../src/status-render.js";

/**
 * The faces for entries with no game artwork, which are the only ones where the plugin
 * decides what a key says rather than handing over an icon the game drew.
 *
 * Asserted against the SVG string because that is the whole output: there is no canvas
 * and no layout engine in between. Sizes and positions are deliberately not asserted --
 * they are taste, and pinning them would make every visual tweak a test edit. What is
 * asserted is what would be wrong rather than merely different.
 */

/** The name lines, in order. The folder heading and the colour band carry no name text. */
function nameLines(svg: string): string[] {
	return [...svg.matchAll(/<text[^>]*font-size="(\d+)"[^>]*>([^<]*)<\/text>/g)]
		.filter(([, size]) => Number(size) > 16)
		.map(([, , text]) => text!);
}

function band(svg: string): string {
	const match = /<rect x="14" y="120"[^>]*fill="(#[0-9a-f]{6})"/.exec(svg);
	expect(match).not.toBeNull();

	return match![1]!;
}

describe("renderDesign", () => {
	it("reports the colour it was given", () => {
		expect(band(renderDesign("Elezen F", "Casual", 0x7fb2e5))).toBe("#7fb2e5");
	});

	it("gives designs of one folder one colour, and the folders different ones", () => {
		const casual = band(renderDesign("A", "Casual", 0));
		const jobs = band(renderDesign("B", "Jobs", undefined));

		expect(band(renderDesign("C", "Casual", undefined))).toBe(casual);
		expect(jobs).not.toBe(casual);
	});

	it("borrows from the name when there is no folder, so a flat list is not one colour", () => {
		const first = band(renderDesign("Elezen F", null, 0));

		expect(band(renderDesign("Elezen F", null, 0))).toBe(first);
		expect(band(renderDesign("Hume M", null, 0))).not.toBe(first);
	});

	it("borrows from the deepest folder, which is the one that tells designs apart", () => {
		expect(band(renderDesign("A", "Designs/Jobs", 0))).toBe(band(renderDesign("B", "Jobs", 0)));
	});

	it("shows the deepest folder and not the path above it", () => {
		expect(renderDesign("A", "Designs/Casual", 0)).toContain(">Casual<");
		expect(renderDesign("A", "Designs/Casual", 0)).not.toContain("Designs/");
	});

	it("wraps on words rather than cutting one in half", () => {
		expect(nameLines(renderDesign("Antiquated Shire", null, 0))).toEqual(["Antiquated", "Shire"]);
	});

	it("cuts and ellipsises a name no size could fit", () => {
		const lines = nameLines(renderDesign("Supercalifragilisticexpialidocious Extra", null, 0));

		expect(lines).toHaveLength(3);
		expect(lines[2]).toMatch(/…$/);
	});

	it("never draws more than three lines of name", () => {
		const lines = nameLines(renderDesign("one two three four five six seven eight nine", "Long", 0));

		expect(lines.length).toBeLessThanOrEqual(3);
	});

	it("escapes names the game and Glamourer both allow", () => {
		const svg = renderDesign("Bells & <Whistles>", null, 0);

		expect(svg).toContain("&amp;");
		expect(svg).not.toContain("<Whistles>");
	});
});

describe("renderEntryFace", () => {
	it("draws Glamourer's revert as itself rather than as a design named Reset", () => {
		const svg = renderEntryFace({ kind: "glamourer", name: "Reset", key: "reset", pinned: true } as never);

		expect(svg).toContain("RESET");
		// The design face would have put the name on a colour band; this one has no band.
		expect(svg).not.toContain('y="120"');
	});

	it("draws a design whose key merely looks like the reset one as a design", () => {
		const svg = renderEntryFace({
			kind: "emote",
			name: "Reset",
			key: "reset",
			category: null,
		});

		expect(svg).not.toContain("RESET");
	});
});
