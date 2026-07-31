import streamDeck from "@elgato/streamdeck";

import {
	BrowserKindAction,
	BrowserNextAction,
	BrowserPrevAction,
	BrowserSlotAction,
} from "./actions/browser-actions.js";
import { EntryAction } from "./actions/entry-action.js";
import { repaintAllStatus, startStatusClock, statusActions } from "./actions/status-actions.js";
import { repaintAllBrowsers } from "./browser.js";
import { type ServerStatus, xiv } from "./xiv-client.js";

/**
 * Connection details, shared by every key rather than set per key.
 *
 * `port` is an override, normally unset: the client reads the game plugin's own config
 * file. `portSource`, `portPath` and `activePort` go the other way -- the plugin writes
 * them so the inspector can say what is actually in use.
 */
type GlobalSettings = {
	port?: number;
	portSource?: ServerStatus["source"];
	portPath?: string;
	activePort?: number;
};

let globalSettings: GlobalSettings = {};

streamDeck.actions.registerAction(new EntryAction());
streamDeck.actions.registerAction(new BrowserSlotAction());
streamDeck.actions.registerAction(new BrowserPrevAction());
streamDeck.actions.registerAction(new BrowserNextAction());
streamDeck.actions.registerAction(new BrowserKindAction());

for (const status of statusActions) {
	streamDeck.actions.registerAction(status);
}

streamDeck.settings.onDidReceiveGlobalSettings<GlobalSettings>((ev) => {
	applyGlobalSettings(ev.settings);
});

// Browser keys show live catalog data, so they have to be repainted when the game
// arrives, leaves, or reports that something was unlocked.
xiv.on("connected", () => void repaintAllBrowsers());
xiv.on("disconnected", () => void repaintAllBrowsers());
xiv.on("invalidated", () => void repaintAllBrowsers());

// Status keys repaint on each pushed snapshot; ventures and recasts also tick locally
// between pushes because their countdowns are derived from an absolute time.
xiv.on("status", () => void repaintAllStatus());
startStatusClock();

// The inspector runs in a browser and cannot read the game's config itself, so tell it
// where the port came from.
xiv.on("serverstatus", (status: ServerStatus) => void publishServerStatus(status));

await streamDeck.connect();

// Settings survive restarts, so pick up whatever the inspector saved last time.
applyGlobalSettings(await streamDeck.settings.getGlobalSettings<GlobalSettings>());

function applyGlobalSettings(settings: GlobalSettings): void {
	globalSettings = settings;
	// An override; unset means "read it from the game's config file".
	xiv.configure(settings.port);
	// The inspector writes the whole settings object back, so it can carry a stale copy
	// of the fields we own. Put them right rather than wait for the next change.
	void publishServerStatus(xiv.serverStatus);
}

async function publishServerStatus(status: ServerStatus): Promise<void> {
	if (
		globalSettings.portSource === status.source &&
		globalSettings.portPath === status.path &&
		globalSettings.activePort === status.port
	) {
		return;
	}

	globalSettings = {
		...globalSettings,
		portSource: status.source,
		portPath: status.path,
		activePort: status.port,
	};

	await streamDeck.settings.setGlobalSettings(globalSettings);
}
