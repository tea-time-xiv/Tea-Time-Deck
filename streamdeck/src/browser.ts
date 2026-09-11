import streamDeck, { type KeyAction } from "@elgato/streamdeck";

import { allowedFrom, layoutPage, sameKinds, wrapTitle, type BrowserKindSettings } from "./browser-util.js";
import { renderBrowserKind, renderEntryFace, renderMessage, toDataUri } from "./status-render.js";
import { xiv, type CatalogEntry, type CatalogKind } from "./xiv-client.js";

/**
 * What a navigation key is for. The paging keys show their own arrow art and no text;
 * the Switch Type key carries the readout for the whole viewport.
 */
export type NavRole = "page" | "kind";

/** Re-exported from browser-util.js: it is still this module's vocabulary. */
export type { BrowserKindSettings };

/**
 * A page of catalog entries spread across whatever browser slots are on a device.
 *
 * The Stream Deck SDK cannot create keys, so a browser is built the other way round:
 * the user places as many slot keys as they want, and the plugin treats them as a
 * viewport onto the catalog. Page size is therefore however many slots are visible,
 * which changes as the user navigates profiles or edits their layout.
 *
 * Only one Stream Deck page is visible per device at a time, so the visible Switch Type
 * key stands in for a page identifier -- the SDK does not expose one. That key owns the
 * type and page number in its own action settings, which is what lets two Stream Deck
 * pages browse different catalogs, and what makes the choice survive a restart.
 *
 * The device still owns the slot registry, because a 15-key and a 32-key deck hold
 * different numbers of slots and cannot share a page number.
 */
class DeviceBrowser {
	readonly #slots = new Map<string, KeyAction>();
	readonly #navKeys = new Map<string, { action: KeyAction; role: NavRole }>();

	/** Visible Switch Type key, if the current Stream Deck page has one. */
	#owner: KeyAction<BrowserKindSettings> | undefined;

	#kind: string | undefined;
	#page = 0;

	/**
	 * Types the Switch Type key cycles through, or undefined for all of them.
	 *
	 * Cycling one press at a time is fine for three types and tiresome for more, and the
	 * server keeps growing them. A deck page that only ever wants emotes and gear sets can
	 * say so here rather than pressing past the rest.
	 */
	#allowed: string[] | undefined;

	/** Guards against overlapping repaints; the last request always wins. */
	#painting = false;
	#repaintQueued = false;

	public addSlot(action: KeyAction): void {
		this.#slots.set(action.id, action);
	}

	public removeSlot(id: string): void {
		this.#slots.delete(id);
	}

	public addNav(action: KeyAction, role: NavRole): void {
		this.#navKeys.set(action.id, { action, role });
	}

	public removeNav(id: string): void {
		this.#navKeys.delete(id);

		// Navigating between Stream Deck pages tears the old page down and builds the new
		// one, in no guaranteed order. Only give up ownership if this key still holds it,
		// or a late teardown would strip the incoming page of its owner.
		if (this.#owner?.id === id) {
			this.#owner = undefined;
		}
	}

	/**
	 * Takes the type and page stored on a newly visible Switch Type key. Called on its
	 * appearance, which is what fires when the user switches Stream Deck pages.
	 */
	public async adopt(action: KeyAction<BrowserKindSettings>, settings: BrowserKindSettings): Promise<void> {
		this.#owner = action;
		this.#allowed = allowedFrom(settings);

		if (settings.kind !== undefined) {
			this.#kind = settings.kind;
		}

		if (settings.page !== undefined) {
			this.#page = Math.max(0, settings.page);
		}

		await this.repaint();
	}

	/**
	 * Takes a settings change from the property inspector. Only the allow-list can be
	 * edited there, and the key writes its own type and page back constantly, so anything
	 * else arriving here is an echo of what this browser just saved.
	 */
	public async applyAllowed(settings: BrowserKindSettings): Promise<void> {
		const allowed = allowedFrom(settings);
		if (sameKinds(allowed, this.#allowed)) {
			return;
		}

		this.#allowed = allowed;

		// The type on show may have just been excluded, in which case land on the first
		// one still in the cycle rather than showing something the key can no longer reach.
		const cycle = await this.#cycle();
		if (cycle.length > 0 && !cycle.some((k) => k.kind === this.#kind)) {
			this.#kind = cycle[0]!.kind;
			this.#page = 0;
			await this.#persist();
		}

		await this.repaint();
	}

	public get isEmpty(): boolean {
		return this.#slots.size === 0 && this.#navKeys.size === 0;
	}

	/** Entry currently shown on the given slot, or undefined if the page is short. */
	public async entryAt(slotId: string): Promise<CatalogEntry | undefined> {
		const ordered = this.#orderedSlots();
		const index = ordered.findIndex((slot) => slot.id === slotId);
		if (index < 0) {
			return undefined;
		}

		// The same layout the paint used, rather than a second sum of it: a pinned entry
		// holds the first slot on every page, so the slot index is not the list index.
		const entries = await this.#entries();
		return layoutPage(entries, ordered.length, this.#page).visible[index];
	}

	public async changePage(delta: number): Promise<void> {
		const pageSize = this.#orderedSlots().length;
		if (pageSize === 0) {
			return;
		}

		const entries = await this.#entries();
		const { pageCount, page } = layoutPage(entries, pageSize, this.#page);

		// Wrap, so holding one nav key can reach everything without a direction change.
		this.#page = (((page + delta) % pageCount) + pageCount) % pageCount;
		await this.repaint();
		await this.#persist();
	}

	public async changeKind(delta: number): Promise<void> {
		const kinds = await this.#cycle();
		if (kinds.length === 0) {
			return;
		}

		const current = kinds.findIndex((k) => k.kind === this.#kind);
		const next = (((current + delta) % kinds.length) + kinds.length) % kinds.length;

		this.#kind = kinds[next]!.kind;
		this.#page = 0;
		await this.repaint();
		await this.#persist();
	}

	/**
	 * Writes the current view back to the owning key, so this Stream Deck page remembers
	 * it. Without an owner the state stays in memory for this device only.
	 */
	async #persist(): Promise<void> {
		if (this.#owner === undefined) {
			return;
		}

		try {
			// The allow-list goes back too: setSettings replaces the whole object, so
			// leaving it out would wipe the inspector's choice on the next press.
			await this.#owner.setSettings({ kind: this.#kind, page: this.#page, kinds: this.#allowed });
		} catch (error) {
			streamDeck.logger.debug(`Could not persist browser state: ${asMessage(error)}`);
		}
	}

	public async repaint(): Promise<void> {
		if (this.#painting) {
			this.#repaintQueued = true;
			return;
		}

		this.#painting = true;
		try {
			do {
				this.#repaintQueued = false;
				await this.#paint();
			} while (this.#repaintQueued);
		} finally {
			this.#painting = false;
		}
	}

	async #paint(): Promise<void> {
		const slots = this.#orderedSlots();

		let entries: CatalogEntry[] = [];
		let failure: string | undefined;

		try {
			entries = await this.#entries();
		} catch (error) {
			failure = error instanceof Error ? error.message : String(error);
		}

		// Clamping lives in the layout: slots can disappear under us, so a page that was
		// valid may no longer be, and the paint is where that is noticed.
		const { visible, pageCount, page } = layoutPage(entries, slots.length, this.#page);
		this.#page = page;

		// One batched fetch beats one request per key before the page can finish drawing.
		if (visible.length > 0) {
			try {
				await xiv.primeIcons(visible.map((entry) => entry.iconId));
			} catch (error) {
				streamDeck.logger.debug(`Could not prefetch icons: ${asMessage(error)}`);
			}
		}

		await Promise.all(slots.map((slot, index) => this.#paintSlot(slot, visible[index], failure)));
		await this.#paintNav(pageCount, failure);
	}

	async #paintSlot(slot: KeyAction, entry: CatalogEntry | undefined, failure: string | undefined): Promise<void> {
		if (failure !== undefined) {
			await slot.setImage();
			await slot.setTitle("");
			return;
		}

		if (entry === undefined) {
			// Past the end of the catalog: blank rather than stale.
			await slot.setImage();
			await slot.setTitle("");
			return;
		}

		// Glamourer designs have no game artwork, so the plugin draws the face itself
		// rather than leaving a black key with a title strip on it: the name is the whole
		// of what the key says, and it is worth the room.
		if (entry.iconId === 0) {
			await slot.setTitle("");
			await slot.setImage(toDataUri(renderEntryFace(entry)));
			return;
		}

		await slot.setTitle(wrapTitle(entry.name));

		try {
			await slot.setImage(await xiv.getIcon(entry.iconId));
		} catch {
			await slot.setImage();
		}
	}

	async #paintNav(pageCount: number, failure: string | undefined): Promise<void> {
		let face: string;

		if (failure !== undefined) {
			face = renderMessage("BROWSE", "offline");
		} else {
			const kind = await this.#kindView();
			face = renderBrowserKind(kind.title, kind.kinds, kind.index, this.#page, pageCount);
		}

		await Promise.all(
			[...this.#navKeys.values()].map(async ({ action, role }) => {
				if (role === "kind") {
					// The drawn face carries the type and the paging, so no title over it.
					await action.setImage(toDataUri(face));
					await action.setTitle("");
					return;
				}

				// Arrows say what they do; a counter on all three keys was just repetition.
				await action.setTitle(failure !== undefined ? "offline" : "");
			}),
		);
	}

	/**
	 * What the Switch Type key shows: the type's own name, and where it sits in the cycle.
	 *
	 * Falls back to the bare kind and a single lit block rather than throwing. The kind
	 * list is cached after the first fetch, and a key that cannot say which type it is on
	 * is worse than one that understates how many there are.
	 */
	async #kindView(): Promise<{ title: string; kinds: string[]; index: number }> {
		const fallback = this.#kind ?? "";

		try {
			const cycle = await this.#cycle();
			const index = cycle.findIndex((k) => k.kind === this.#kind);

			return {
				// The server's own label, so the key reads "Gear Sets" rather than the
				// wire name it happens to be filed under.
				title: cycle[index]?.displayName ?? capitalise(fallback),
				// The wire names, which are what the face colours its blocks by.
				kinds: cycle.map((k) => k.kind),
				index: Math.max(0, index),
			};
		} catch (error) {
			streamDeck.logger.debug(`Could not resolve the type position: ${asMessage(error)}`);
			return { title: capitalise(fallback), kinds: fallback === "" ? [] : [fallback], index: 0 };
		}
	}

	/** The types this browser cycles through, in the server's order. */
	async #cycle(): Promise<CatalogKind[]> {
		const kinds = await xiv.getKinds();
		if (this.#allowed === undefined) {
			return kinds;
		}

		// An allow-list naming nothing the server offers would strand the key on a type it
		// cannot leave, so treat it as no filter at all.
		const filtered = kinds.filter((k) => this.#allowed!.includes(k.kind));
		return filtered.length > 0 ? filtered : kinds;
	}

	async #entries(): Promise<CatalogEntry[]> {
		this.#kind ??= (await this.#cycle())[0]?.kind;
		if (this.#kind === undefined) {
			return [];
		}

		return xiv.getEntries(this.#kind);
	}

	/**
	 * Reading order, so paging matches what the eye expects. Keys in a multi-action have
	 * no coordinates and cannot be part of a grid.
	 */
	#orderedSlots(): KeyAction[] {
		return [...this.#slots.values()]
			.filter((slot) => slot.coordinates !== undefined)
			.sort((a, b) => {
				const rows = a.coordinates!.row - b.coordinates!.row;
				return rows !== 0 ? rows : a.coordinates!.column - b.coordinates!.column;
			});
	}
}

const browsers = new Map<string, DeviceBrowser>();

export function browserFor(deviceId: string): DeviceBrowser {
	let browser = browsers.get(deviceId);
	if (!browser) {
		browser = new DeviceBrowser();
		browsers.set(deviceId, browser);
	}

	return browser;
}

export async function repaintAllBrowsers(): Promise<void> {
	for (const [deviceId, browser] of browsers) {
		if (browser.isEmpty) {
			browsers.delete(deviceId);
			continue;
		}

		await browser.repaint();
	}
}

function capitalise(kind: string): string {
	return kind.charAt(0).toUpperCase() + kind.slice(1);
}

function asMessage(error: unknown): string {
	return error instanceof Error ? error.message : String(error);
}
