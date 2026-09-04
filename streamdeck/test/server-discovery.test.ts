import { mkdtemp, mkdir, rm, utimes, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { afterEach, beforeEach, describe, expect, it } from "vitest";

import { discoverServers } from "../src/server-discovery.js";

/**
 * Against a real temp tree rather than a mocked `fs`.
 *
 * Discovery is almost entirely about how the filesystem behaves -- which names exist,
 * which reads throw, what `mtime` says -- so a mock would be asserting that the mock
 * matches the assumptions, which is the thing actually worth doubting.
 */

let roaming: string;
const saved = { appdata: process.env.APPDATA, override: process.env.TEATIMEDECK_CONFIG };

/** Writes a TeaTimeDeck config into a launcher profile, with a fixed mtime. */
async function writeConfig(profile: string, body: string, modified: Date): Promise<string> {
	const dir = join(roaming, profile, "pluginConfigs");
	await mkdir(dir, { recursive: true });

	const path = join(dir, "TeaTimeDeck.json");
	await writeFile(path, body, "utf8");
	// Ordering is by mtime, so set it rather than relying on how fast the test ran.
	await utimes(path, modified, modified);

	return path;
}

beforeEach(async () => {
	roaming = await mkdtemp(join(tmpdir(), "ttd-discovery-"));
	process.env.APPDATA = roaming;
	delete process.env.TEATIMEDECK_CONFIG;
});

afterEach(async () => {
	if (saved.appdata === undefined) delete process.env.APPDATA;
	else process.env.APPDATA = saved.appdata;

	if (saved.override === undefined) delete process.env.TEATIMEDECK_CONFIG;
	else process.env.TEATIMEDECK_CONFIG = saved.override;

	await rm(roaming, { recursive: true, force: true });
});

describe("discoverServers", () => {
	it("returns nothing when no launcher folder exists", async () => {
		await expect(discoverServers()).resolves.toEqual([]);
	});

	it("reads the port out of a single config", async () => {
		const path = await writeConfig("XIVLauncher", '{"ApiPort":37985}', new Date(1_000_000));

		await expect(discoverServers()).resolves.toEqual([{ port: 37985, path }]);
	});

	it("puts the most recently written profile first", async () => {
		const old = await writeConfig("XIVLauncher", '{"ApiPort":37985}', new Date(1_000_000));
		const fresh = await writeConfig("XIVLauncher_alt", '{"ApiPort":37990}', new Date(9_000_000));

		await expect(discoverServers()).resolves.toEqual([
			{ port: 37990, path: fresh },
			{ port: 37985, path: old },
		]);
	});

	it("collapses two profiles left on the same port to one entry", async () => {
		await writeConfig("XIVLauncher", '{"ApiPort":37985}', new Date(1_000_000));
		const fresh = await writeConfig("XIVLauncher_alt", '{"ApiPort":37985}', new Date(9_000_000));

		// The fresher one wins the single slot, since it is tried first.
		await expect(discoverServers()).resolves.toEqual([{ port: 37985, path: fresh }]);
	});

	it("skips a config caught mid-write rather than throwing", async () => {
		await writeConfig("XIVLauncher", '{"ApiPort":379', new Date(9_000_000));
		const good = await writeConfig("XIVLauncher_alt", '{"ApiPort":37990}', new Date(1_000_000));

		await expect(discoverServers()).resolves.toEqual([{ port: 37990, path: good }]);
	});

	it("skips a profile folder with no config file in it", async () => {
		await mkdir(join(roaming, "XIVLauncher_empty"), { recursive: true });
		const good = await writeConfig("XIVLauncher", '{"ApiPort":37985}', new Date(1_000_000));

		await expect(discoverServers()).resolves.toEqual([{ port: 37985, path: good }]);
	});

	it("ignores a config with no ApiPort", async () => {
		// A port of 0 is not a port either, and the check has to treat it as absent.
		await writeConfig("XIVLauncher", '{"HiddenEmoteCategories":[1,2]}', new Date(9_000_000));
		await writeConfig("XIVLauncher_zero", '{"ApiPort":0}', new Date(8_000_000));

		await expect(discoverServers()).resolves.toEqual([]);
	});

	it("ignores folders that are not launcher profiles", async () => {
		const dir = join(roaming, "SomeOtherApp", "pluginConfigs");
		await mkdir(dir, { recursive: true });
		await writeFile(join(dir, "TeaTimeDeck.json"), '{"ApiPort":1234}', "utf8");

		await expect(discoverServers()).resolves.toEqual([]);
	});

	it("lets TEATIMEDECK_CONFIG pin one file and skip enumeration", async () => {
		await writeConfig("XIVLauncher", '{"ApiPort":37985}', new Date(9_000_000));

		const pinned = join(roaming, "pinned.json");
		await writeFile(pinned, '{"ApiPort":40000}', "utf8");
		process.env.TEATIMEDECK_CONFIG = pinned;

		// Only the pinned one, even though the enumerated config is newer.
		await expect(discoverServers()).resolves.toEqual([{ port: 40000, path: pinned }]);
	});

	it("returns nothing when TEATIMEDECK_CONFIG points at a file that is not there", async () => {
		await writeConfig("XIVLauncher", '{"ApiPort":37985}', new Date(9_000_000));
		process.env.TEATIMEDECK_CONFIG = join(roaming, "absent.json");

		await expect(discoverServers()).resolves.toEqual([]);
	});
});
