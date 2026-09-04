/**
 * The parts of the browser that are decisions rather than side effects.
 *
 * They live apart from `browser.ts` because that module reaches the Stream Deck SDK the
 * moment it is imported, and these three answers are worth checking without a deck
 * attached. Nothing here touches the SDK, the socket or the catalog.
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
