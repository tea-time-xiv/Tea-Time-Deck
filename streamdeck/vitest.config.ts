import { defineConfig } from "vitest/config";

/**
 * Tests live in `test/` rather than beside the source because `tsconfig.json` compiles
 * `src/**` and nothing else -- keeping them out of that tree means the rollup build's
 * type-check never sees them, and the bundle cannot accidentally grow a test import.
 *
 * Everything here runs without a game, a deck or a socket: the Dalamud half is covered
 * by the scripts in `tools/`, against a live client, which is inherent to that half.
 */
export default defineConfig({
	test: {
		include: ["test/**/*.test.ts"],
		environment: "node",
		// A test that hangs on a fake timer it forgot to advance should say so quickly.
		testTimeout: 10_000,
	},
});
