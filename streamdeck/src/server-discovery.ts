import { readdir, readFile, stat } from "node:fs/promises";
import { homedir } from "node:os";
import { join } from "node:path";

/**
 * Finds which port the game plugin is listening on by reading its own config file.
 *
 * There is no key to find: the server does not use one. This is only here so that a
 * user who moved the port, or who runs more than one XIVLauncher profile, does not
 * have to type it in on the deck side as well.
 */

/** The field we need out of what the Dalamud plugin serialises. */
type DalamudConfig = {
	ApiPort?: number;
};

export type DiscoveredServer = {
	port: number;
	/** The config file this came from. */
	path: string;
};

const CONFIG_LEAF = join("pluginConfigs", "TeaTimeDeck.json");

/**
 * Every port a TeaTimeDeck config on this machine points at, most recently written
 * first.
 *
 * More than one config is normal: a second XIVLauncher profile is a whole second
 * roaming folder with its own plugin configs. Recency is a good guess at which one is
 * playing but not a certain one, so the caller works down the list.
 */
export async function discoverServers(): Promise<DiscoveredServer[]> {
	const found: DiscoveredServer[] = [];

	for (const path of await candidatePaths()) {
		const server = await readConfig(path);
		// Two profiles left on the default port are one server as far as we care.
		if (server && !found.some((other) => other.port === server.port)) {
			found.push(server);
		}
	}

	return found;
}

async function readConfig(path: string): Promise<DiscoveredServer | undefined> {
	let raw: string;
	try {
		raw = await readFile(path, "utf8");
	} catch {
		return undefined;
	}

	let parsed: DalamudConfig;
	try {
		parsed = JSON.parse(raw) as DalamudConfig;
	} catch {
		// Caught mid-write, most likely. The next reconnect will read it again.
		return undefined;
	}

	return parsed.ApiPort ? { port: parsed.ApiPort, path } : undefined;
}

async function candidatePaths(): Promise<string[]> {
	const override = process.env.TEATIMEDECK_CONFIG?.trim();
	if (override) {
		return [override];
	}

	const paths: string[] = [];

	for (const roaming of await roamingDirs()) {
		paths.push(...(await launcherConfigs(roaming)));
	}

	return await freshestFirst(paths);
}

/** The roaming folders XIVLauncher could have written into. */
async function roamingDirs(): Promise<string[]> {
	if (process.platform === "win32") {
		return [process.env.APPDATA ?? join(homedir(), "AppData", "Roaming")];
	}

	// The game runs under Wine on macOS, so the roaming folder sits inside the prefix.
	// The Wine user name is not always the macOS one, so enumerate rather than guess.
	const dirs: string[] = [];

	for (const prefix of [
		join(homedir(), "Library", "Application Support", "XIV on Mac", "wineprefix"),
		join(homedir(), ".xlcore", "wineprefix"),
	]) {
		const users = join(prefix, "drive_c", "users");

		let names: string[];
		try {
			names = await readdir(users);
		} catch {
			continue;
		}

		for (const name of names) {
			dirs.push(join(users, name, "AppData", "Roaming"));
		}
	}

	return dirs;
}

/**
 * One entry per launcher folder. The default is `XIVLauncher`; extra profiles get a
 * suffix, e.g. `XIVLauncher_alt`.
 */
async function launcherConfigs(roaming: string): Promise<string[]> {
	let names: string[];
	try {
		names = await readdir(roaming);
	} catch {
		return [];
	}

	return names
		.filter((name) => /^XIVLauncher/i.test(name))
		.map((name) => join(roaming, name, CONFIG_LEAF));
}

/** Drops what does not exist, and puts the most recently written config first. */
async function freshestFirst(paths: string[]): Promise<string[]> {
	const stamped = await Promise.all(
		paths.map(async (path) => {
			try {
				return { path, modified: (await stat(path)).mtimeMs };
			} catch {
				return undefined;
			}
		}),
	);

	return stamped
		.filter((entry): entry is { path: string; modified: number } => entry !== undefined)
		.sort((a, b) => b.modified - a.modified)
		.map((entry) => entry.path);
}
