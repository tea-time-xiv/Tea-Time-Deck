import streamDeck, {
	action,
	SingletonAction,
	type DidReceiveSettingsEvent,
	type KeyAction,
	type KeyDownEvent,
	type SendToPluginEvent,
	type WillAppearEvent,
	type WillDisappearEvent,
} from "@elgato/streamdeck";
import type { JsonObject, JsonValue } from "@elgato/utils";

import { browserFor, type BrowserKindSettings, type NavRole } from "../browser.js";
import { xiv } from "../xiv-client.js";

/** Slot and paging keys hold no configuration of their own. */
type NoSettings = Record<string, never>;

/**
 * One cell of the catalog viewport. Place as many as you like: however many are visible
 * is the page size, so a 15-key deck and a 32-key deck page at their own rates.
 */
@action({ UUID: "xiv.teatime.deck.browser.slot" })
export class BrowserSlotAction extends SingletonAction<NoSettings> {
	override async onWillAppear(ev: WillAppearEvent<NoSettings>): Promise<void> {
		if (!ev.action.isKey()) {
			return;
		}

		const browser = browserFor(ev.action.device.id);
		browser.addSlot(ev.action);

		// Adding a slot changes the page size, so the whole page is repainted, not just this key.
		await browser.repaint();
	}

	override async onWillDisappear(ev: WillDisappearEvent<NoSettings>): Promise<void> {
		const browser = browserFor(ev.action.device.id);
		browser.removeSlot(ev.action.id);
		await browser.repaint();
	}

	override async onKeyDown(ev: KeyDownEvent<NoSettings>): Promise<void> {
		const browser = browserFor(ev.action.device.id);
		const entry = await browser.entryAt(ev.action.id);

		if (entry === undefined) {
			// An empty cell past the end of the catalog.
			await ev.action.showAlert();
			return;
		}

		try {
			await xiv.execute(entry.kind, entry.id, entry.key);
			await ev.action.showOk();
		} catch (error) {
			streamDeck.logger.info(`Could not execute ${entry.kind} ${entry.id}: ${asMessage(error)}`);
			await ev.action.showAlert();
		}
	}
}

/** Shared plumbing for the keys that drive the viewport. */
abstract class BrowserNavAction<T extends JsonObject = NoSettings> extends SingletonAction<T> {
	protected abstract readonly role: NavRole;

	override async onWillAppear(ev: WillAppearEvent<T>): Promise<void> {
		if (!ev.action.isKey()) {
			return;
		}

		const browser = browserFor(ev.action.device.id);
		browser.addNav(ev.action, this.role);
		await browser.repaint();
	}

	override async onWillDisappear(ev: WillDisappearEvent<T>): Promise<void> {
		browserFor(ev.action.device.id).removeNav(ev.action.id);
	}

	override async onKeyDown(ev: KeyDownEvent<T>): Promise<void> {
		try {
			await this.navigate(browserFor(ev.action.device.id));
		} catch (error) {
			streamDeck.logger.info(`Navigation failed: ${asMessage(error)}`);
			await ev.action.showAlert();
		}
	}

	protected abstract navigate(browser: ReturnType<typeof browserFor>): Promise<void>;
}

@action({ UUID: "xiv.teatime.deck.browser.prev" })
export class BrowserPrevAction extends BrowserNavAction {
	protected override readonly role: NavRole = "page";

	protected override navigate(browser: ReturnType<typeof browserFor>): Promise<void> {
		return browser.changePage(-1);
	}
}

@action({ UUID: "xiv.teatime.deck.browser.next" })
export class BrowserNextAction extends BrowserNavAction {
	protected override readonly role: NavRole = "page";

	protected override navigate(browser: ReturnType<typeof browserFor>): Promise<void> {
		return browser.changePage(1);
	}
}

/**
 * Also the owner of the browser state for whichever Stream Deck page it sits on.
 *
 * The SDK gives no page identifier, but only one page per device is visible at a time,
 * so the visible key stands in for one. Its settings are persisted per placed key, which
 * is what lets two Stream Deck pages hold different types and survive a restart.
 */
@action({ UUID: "xiv.teatime.deck.browser.kind" })
export class BrowserKindAction extends BrowserNavAction<BrowserKindSettings> {
	protected override readonly role: NavRole = "kind";

	override async onWillAppear(ev: WillAppearEvent<BrowserKindSettings>): Promise<void> {
		if (!ev.action.isKey()) {
			return;
		}

		const browser = browserFor(ev.action.device.id);
		browser.addNav(ev.action, this.role);

		// Adopting repaints, so this replaces the base class's repaint rather than adding one.
		await browser.adopt(ev.action, ev.payload.settings);
	}

	/**
	 * Fires both when the inspector saves and when this browser persists its own type and
	 * page, so it deliberately only acts on a changed allow-list.
	 */
	override onDidReceiveSettings(ev: DidReceiveSettingsEvent<BrowserKindSettings>): Promise<void> {
		return browserFor(ev.action.device.id).applyAllowed(ev.payload.settings);
	}

	override onPropertyInspectorDidAppear(): Promise<void> | void {
		// The inspector cannot ask until it has registered, so push the list at it.
		return this.#sendKinds();
	}

	override async onSendToPlugin(ev: SendToPluginEvent<JsonValue, BrowserKindSettings>): Promise<void> {
		if ((ev.payload as { event?: string })?.event === "getKinds") {
			await this.#sendKinds();
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

	protected override navigate(browser: ReturnType<typeof browserFor>): Promise<void> {
		return browser.changeKind(1);
	}
}

function asMessage(error: unknown): string {
	return error instanceof Error ? error.message : String(error);
}
