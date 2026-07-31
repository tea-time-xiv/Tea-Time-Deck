import commonjs from "@rollup/plugin-commonjs";
import nodeResolve from "@rollup/plugin-node-resolve";
import typescript from "@rollup/plugin-typescript";

const sdPlugin = "xiv.teatime.deck.sdPlugin";

export default {
	input: "src/plugin.ts",
	output: {
		file: `${sdPlugin}/bin/plugin.js`,
		format: "es",
		sourcemap: true,
		sourcemapPathTransform: (relative) => relative.replace(/^\.\.[\\/]/, ""),
	},
	plugins: [
		typescript({ tsconfig: "./tsconfig.json" }),
		nodeResolve({ exportConditions: ["node"], preferBuiltins: true }),
		commonjs(),
	],
	// ws probes for these native speedups inside a try/catch and works without them.
	external: ["bufferutil", "utf-8-validate"],
	onwarn(warning, warn) {
		// ws uses a dynamic require for its optional native deps; not actionable.
		if (warning.code === "CIRCULAR_DEPENDENCY") return;
		warn(warning);
	},
};
