/**
 * The parts of the browser that are decisions rather than side effects.
 *
 * They live apart from `browser.ts` because that module reaches the Stream Deck SDK the
 * moment it is imported, and these answers are worth checking without a deck attached.
 * Nothing here touches the SDK, the socket or the catalog.
 */

/** Stored on the Switch Type key, which is what makes browser state per Stream Deck page. */
export type BrowserKindSettings = {
	kind?: string;
	page?: number;
	/** Types this key cycles through. Absent or empty means all of them. */
	kinds?: string[];
};

/** Stream Deck renders titles on one line unless told otherwise; long names need help. */
export function wrapTitle(name: string): string {
	if (name.length <= 9) {
		return name;
	}

	const words = name.split(" ");
	if (words.length === 1) {
		return name;
	}

	const half = Math.ceil(words.length / 2);
	return `${words.slice(0, half).join(" ")}\n${words.slice(half).join(" ")}`;
}

/** An empty allow-list means the same as no allow-list: cycle everything. */
export function allowedFrom(settings: BrowserKindSettings): string[] | undefined {
	return settings.kinds !== undefined && settings.kinds.length > 0 ? settings.kinds : undefined;
}

export function sameKinds(a: string[] | undefined, b: string[] | undefined): boolean {
	if (a === undefined || b === undefined) {
		return a === b;
	}

	return a.length === b.length && a.every((kind, index) => kind === b[index]);
}

/** One viewport's worth of a catalog: what each slot shows, and where in the list it sits. */
export type PageLayout<T> = {
	/** In slot order. Shorter than the viewport when the last page does not fill it. */
	visible: T[];
	pageCount: number;
	/** The page actually laid out — the one asked for, clamped to what exists. */
	page: number;
};

/**
 * Spreads a catalog across the slots a device has.
 *
 * Leading entries the server marks `pinned` keep a key of their own on every page instead
 * of paging away with the rest; Glamourer's Reset is the only one so far, and it is worth
 * as much from page four as from page one. Everything else pages through the slots left
 * over, so a pin costs one key of the viewport rather than one key of every page.
 *
 * A pin is dropped rather than honoured when it would leave nothing to page with: a
 * two-slot browser that spent one slot on Reset would still work, a one-slot browser
 * showing only Reset would not be a browser at all.
 *
 * Structural in `T` rather than typed to CatalogEntry: this file stays free of the client,
 * which reaches the socket the moment it is imported.
 */
export function layoutPage<T extends { pinned?: boolean }>(
	entries: T[],
	pageSize: number,
	page: number,
): PageLayout<T> {
	if (pageSize <= 0) {
		return { visible: [], pageCount: 1, page: 0 };
	}

	let pins = 0;
	while (pins < entries.length && entries[pins]!.pinned === true) {
		pins++;
	}

	if (pins >= pageSize) {
		pins = 0;
	}

	const rest = entries.slice(pins);
	const listSize = pageSize - pins;
	const pageCount = Math.max(1, Math.ceil(rest.length / listSize));

	// Slots come and go as the user edits a profile, so a page that was valid may not be.
	const clamped = Math.min(Math.max(0, page), pageCount - 1);
	const start = clamped * listSize;

	return {
		visible: [...entries.slice(0, pins), ...rest.slice(start, start + listSize)],
		pageCount,
		page: clamped,
	};
}
