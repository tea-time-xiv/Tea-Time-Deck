/**
 * SVG renderers for the read-only status keys.
 *
 * SVG rather than a canvas library: Stream Deck accepts an SVG data URI directly, so the
 * keys stay crisp on every hardware generation and the plugin keeps its zero native
 * dependencies. Everything here is pure string building.
 *
 * The look follows the game's own UI: near-black blue panels, a thin warm gold hairline,
 * and the gauge colours FFXIV uses for each resource.
 */

const SIZE = 144;

/** Panel and text furniture, shared so every key reads as part of one set. */
const INK = {
	panelTop: "#1b2133",
	panelBottom: "#0d1018",
	edge: "rgba(201,172,112,0.55)",
	edgeHighlight: "rgba(255,255,255,0.10)",
	trough: "#070910",
	label: "#c9ac70",
	labelEdge: "rgba(201,172,112,0.30)",
	text: "#f2efe6",
	muted: "#8d93a6",
	// Cool blue against the gold, for the browser's second row of blocks. Two pagers of
	// one colour would read as one long strip rather than two separate things.
	accent: "#7fb2e5",
	accentEdge: "rgba(127,178,229,0.30)",
} as const;

/** Gauge colours, taken from the bars the game draws for each resource. */
const GAUGE: Record<string, { dark: string; light: string }> = {
	hp: { dark: "#3f9d4a", light: "#7fdc86" },
	mp: { dark: "#a94fbd", light: "#dd9bea" },
	gp: { dark: "#c79a2a", light: "#f0d268" },
	cp: { dark: "#8558c4", light: "#c2a2ee" },
	xp: { dark: "#b8912f", light: "#f2e0a0" },
};

export type Vitals = {
	hp: number;
	maxHp: number;
	mp: number;
	maxMp: number;
	gp: number;
	maxGp: number;
	cp: number;
	maxCp: number;
	shieldPercent: number;
	preferred: string;
};

export type Job = {
	id: number;
	abbreviation: string;
	name: string;
	iconId: number;
	level: number;
	effectiveLevel: number;
	isLevelSynced: boolean;
	experience: number;
	experienceToNext: number;
};

export type Duty = { state: string; dutyName: string | null };
export type Retainers = { total: number; active: number; ready: number; soonestCompleteAt?: number | null };

export function toDataUri(svg: string): string {
	return `data:image/svg+xml;base64,${Buffer.from(svg, "utf8").toString("base64")}`;
}

function escapeText(value: string): string {
	return value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

/** The panel every key sits on. */
function frame(body: string): string {
	return `<svg xmlns="http://www.w3.org/2000/svg" width="${SIZE}" height="${SIZE}" viewBox="0 0 ${SIZE} ${SIZE}">
<defs>
<linearGradient id="panel" x1="0" y1="0" x2="0" y2="1">
<stop offset="0" stop-color="${INK.panelTop}"/><stop offset="1" stop-color="${INK.panelBottom}"/>
</linearGradient>
</defs>
<rect x="1" y="1" width="${SIZE - 2}" height="${SIZE - 2}" rx="18" fill="url(#panel)"/>
<rect x="1.75" y="1.75" width="${SIZE - 3.5}" height="${SIZE - 3.5}" rx="17" fill="none" stroke="${INK.edge}" stroke-width="1.5"/>
<rect x="4" y="4" width="${SIZE - 8}" height="${SIZE - 8}" rx="15" fill="none" stroke="${INK.edgeHighlight}" stroke-width="1"/>
${body}
</svg>`;
}

/** Small caps heading, the way the game labels its gauges. */
function label(text: string, colour: string = INK.label): string {
	return `<text x="${SIZE / 2}" y="26" text-anchor="middle" font-family="Segoe UI, sans-serif" font-size="16" font-weight="600" letter-spacing="2.5" fill="${colour}">${escapeText(text)}</text>`;
}

function centeredValue(text: string, y: number, size: number, colour: string = INK.text): string {
	return `<text x="${SIZE / 2}" y="${y}" text-anchor="middle" font-family="Segoe UI, sans-serif" font-size="${size}" font-weight="700" fill="${colour}">${escapeText(text)}</text>`;
}

/**
 * Horizontal gauge with a recessed trough, matching the party list bars.
 *
 * Gradient ids are suffixed with the resource so two bars on one key cannot collide --
 * duplicate ids in one SVG silently resolve to whichever came first.
 */
function bar(fraction: number, resource: string, y: number, height: number, inset: number): string {
	const colours = GAUGE[resource] ?? GAUGE.hp!;
	const width = SIZE - inset * 2;
	const radius = height / 2;
	const filled = Math.max(0, Math.min(1, fraction)) * (width - 4);

	return `<defs>
<linearGradient id="fill-${resource}" x1="0" y1="0" x2="0" y2="1">
<stop offset="0" stop-color="${colours.light}"/><stop offset="0.55" stop-color="${colours.dark}"/><stop offset="1" stop-color="${colours.dark}"/>
</linearGradient>
</defs>
<rect x="${inset}" y="${y}" width="${width}" height="${height}" rx="${radius}" fill="${INK.trough}" stroke="rgba(0,0,0,0.9)" stroke-width="1"/>
${filled > 1 ? `<rect x="${inset + 2}" y="${y + 2}" width="${filled}" height="${height - 4}" rx="${Math.max(1, radius - 2)}" fill="url(#fill-${resource})"/>` : ""}
<rect x="${inset}" y="${y}" width="${width}" height="${height}" rx="${radius}" fill="none" stroke="rgba(201,172,112,0.30)" stroke-width="1"/>`;
}

function compact(value: number): string {
	return value >= 10000 ? `${Math.floor(value / 1000)}k` : String(value);
}

export function renderVitals(vitals: Vitals | null, resource: string): string {
	if (vitals === null) {
		return frame(label("HP") + centeredValue("--", 88, 34, INK.muted));
	}

	const picked = resource === "auto" ? vitals.preferred : resource;

	const table: Record<string, [number, number]> = {
		hp: [vitals.hp, vitals.maxHp],
		mp: [vitals.mp, vitals.maxMp],
		gp: [vitals.gp, vitals.maxGp],
		cp: [vitals.cp, vitals.maxCp],
	};

	const [current, max] = table[picked] ?? table.hp!;

	// A job without this gauge reports a maximum of zero; say so rather than divide by it.
	if (max === 0) {
		return frame(label(picked.toUpperCase()) + centeredValue("n/a", 88, 30, INK.muted));
	}

	const fraction = current / max;

	// The game turns the health bar amber then red as it drops; mirror that on HP only.
	const critical = picked === "hp" && fraction <= 0.3;
	const low = picked === "hp" && fraction <= 0.6;
	const valueColour = critical ? "#ff8a7a" : low ? "#ffd479" : INK.text;

	// The maximum is dropped: at a glance the bar already says how full you are, and the
	// space buys a gauge big enough to read without looking directly at the key.
	return frame(
		label(picked.toUpperCase()) +
			centeredValue(compact(current), 88, current >= 10000 ? 44 : 50, valueColour) +
			bar(fraction, picked, 104, 24, 11),
	);
}

export function renderJob(job: Job | null, iconDataUri: string | undefined): string {
	if (job === null) {
		return frame(label("JOB") + centeredValue("--", 88, 34, INK.muted));
	}

	const art =
		iconDataUri === undefined
			? centeredValue(job.abbreviation, 52, 34)
			: `<image href="${iconDataUri}" x="40" y="6" width="64" height="64" preserveAspectRatio="xMidYMid meet"/>`;

	// Synced level is what actually applies, so it is what gets shown, tinted to say why.
	const shown = job.isLevelSynced ? job.effectiveLevel : job.level;
	const levelColour = job.isLevelSynced ? "#7fd4ff" : INK.text;

	const level =
		`<rect x="36" y="72" width="72" height="26" rx="9" fill="rgba(5,7,12,0.82)" stroke="rgba(201,172,112,0.5)" stroke-width="1"/>` +
		`<text x="${SIZE / 2}" y="91" text-anchor="middle" font-family="Segoe UI, sans-serif" font-size="17" font-weight="700" fill="${levelColour}">${escapeText(`LV ${shown}`)}${job.isLevelSynced ? " ⌄" : ""}</text>`;

	// Experience is always the real level's, so a synced job still shows real progress.
	// At maximum level ParamGrow has nothing further to describe, hence no bar at all.
	const experience =
		job.experienceToNext > 0
			? bar(job.experience / job.experienceToNext, "xp", 110, 18, 11)
			: `<text x="${SIZE / 2}" y="126" text-anchor="middle" font-family="Segoe UI, sans-serif" font-size="15" font-weight="700" letter-spacing="2" fill="${INK.label}">MAX</text>`;

	return frame(art + level + experience);
}

export function renderDuty(duty: Duty): string {
	const styles: Record<string, { text: string; colour: string; glow: boolean }> = {
		idle: { text: "NOT\nQUEUED", colour: INK.muted, glow: false },
		queued: { text: "IN\nQUEUE", colour: "#ffc94d", glow: false },
		ready: { text: "READY", colour: "#63e07a", glow: true },
		inDuty: { text: "IN DUTY", colour: "#7fb6ff", glow: false },
	};

	const style = styles[duty.state] ?? styles.idle!;
	const lines = style.text.split("\n");

	// A pop expires in seconds, so the one state that must be unmissable gets a halo.
	const halo = style.glow
		? `<circle cx="${SIZE / 2}" cy="${SIZE / 2}" r="54" fill="none" stroke="${style.colour}" stroke-width="3" opacity="0.35"/>`
		: "";

	const startY = lines.length > 1 ? 66 : 78;
	const body = lines
		.map((line, index) => centeredValue(line, startY + index * 28, 24, style.colour))
		.join("");

	const caption =
		duty.state === "inDuty" && duty.dutyName
			? `<text x="${SIZE / 2}" y="122" text-anchor="middle" font-family="Segoe UI, sans-serif" font-size="12" fill="${INK.muted}">${escapeText(truncate(duty.dutyName, 20))}</text>`
			: "";

	return frame(label("DUTY") + halo + body + caption);
}

export function renderRetainers(retainers: Retainers, now: number): string {
	if (retainers.total === 0) {
		return frame(label("RETAINERS") + centeredValue("--", 88, 34, INK.muted));
	}

	if (retainers.ready > 0) {
		return frame(
			label("VENTURES") +
				centeredValue(String(retainers.ready), 84, 46, "#63e07a") +
				`<text x="${SIZE / 2}" y="112" text-anchor="middle" font-family="Segoe UI, sans-serif" font-size="14" font-weight="600" fill="#63e07a">READY</text>`,
		);
	}

	if (retainers.soonestCompleteAt == null) {
		return frame(
			label("VENTURES") +
				centeredValue("idle", 84, 28, INK.muted) +
				`<text x="${SIZE / 2}" y="112" text-anchor="middle" font-family="Segoe UI, sans-serif" font-size="13" fill="${INK.muted}">${retainers.total} retainers</text>`,
		);
	}

	const remaining = Math.max(0, retainers.soonestCompleteAt - now);

	return frame(
		label("VENTURES") +
			centeredValue(formatDuration(remaining), 84, remaining >= 3600 ? 32 : 38) +
			`<text x="${SIZE / 2}" y="112" text-anchor="middle" font-family="Segoe UI, sans-serif" font-size="13" fill="${INK.muted}">${retainers.active} out</text>`,
	);
}

export function renderCooldown(
	name: string,
	iconDataUri: string | undefined,
	remaining: number,
	total: number,
): string {
	const ready = remaining <= 0;
	const fraction = ready || total <= 0 ? 1 : 1 - remaining / total;

	const radius = 52;
	const circumference = 2 * Math.PI * radius;
	const centre = SIZE / 2;

	const art =
		iconDataUri === undefined
			? centeredValue(truncate(name, 6), centre + 8, 20)
			: `<image href="${iconDataUri}" x="${centre - 30}" y="${centre - 30}" width="60" height="60" opacity="${ready ? 1 : 0.45}"/>`;

	// The arc sweeps clockwise from the top as the recast completes, like the game's own
	// cooldown overlay, so a glance at the gap reads as time left.
	const ring =
		`<circle cx="${centre}" cy="${centre}" r="${radius}" fill="none" stroke="rgba(0,0,0,0.55)" stroke-width="9"/>` +
		`<circle cx="${centre}" cy="${centre}" r="${radius}" fill="none" stroke="${ready ? "#c9ac70" : "#7fb6ff"}" stroke-width="7"
 stroke-dasharray="${(circumference * fraction).toFixed(2)} ${circumference.toFixed(2)}"
 stroke-linecap="round" transform="rotate(-90 ${centre} ${centre})"/>`;

	const readout = ready
		? ""
		: `<rect x="${centre - 30}" y="${SIZE - 40}" width="60" height="26" rx="8" fill="rgba(5,7,12,0.88)" stroke="rgba(201,172,112,0.45)" stroke-width="1"/>` +
			`<text x="${centre}" y="${SIZE - 21}" text-anchor="middle" font-family="Segoe UI, sans-serif" font-size="17" font-weight="700" fill="${INK.text}">${escapeText(formatDuration(remaining))}</text>`;

	return frame(ring + art + readout);
}

export function renderMessage(heading: string, body: string): string {
	return frame(label(heading) + centeredValue(body, 88, 22, INK.muted));
}

/**
 * Face for the Switch Type key: which catalog is being browsed, and where in it you are.
 *
 * It carries the paging readout for the whole viewport. The Previous and Next keys show
 * their arrows and nothing else -- three keys repeating the same counter was noise, and
 * this one is the key that is always present when a browser is set up.
 */
/**
 * The readout for the whole viewport: which catalog, and where in it.
 *
 * Two rows of blocks rather than one row and a "3/8" caption. Both axes are positions in
 * a list, so both read fastest the same way, and the number said nothing the blocks did
 * not. The rows are told apart by colour and by height -- the type row is the shorter of
 * the two, because it is the one that changes least.
 */
export function renderBrowserKind(
	kind: string,
	kindIndex: number,
	kindCount: number,
	page: number,
	pageCount: number,
): string {
	const title = kind.charAt(0).toUpperCase() + kind.slice(1);

	return frame(
		centeredValue(truncate(title, 9), 62, title.length > 7 ? 28 : 34) +
			pager(kindIndex, kindCount, 88, { max: 10, fill: INK.accent, edge: INK.accentEdge }) +
			pager(page, pageCount, 110, { max: 16, fill: INK.label, edge: INK.labelEdge }),
	);
}

type PagerStyle = { max: number; fill: string; edge: string };

/**
 * A block per position, the current one lit. Reads at a glance, which a bare "3/8" does
 * not.
 */
function pager(index: number, count: number, y: number, style: PagerStyle): string {
	const inset = 12;
	const width = SIZE - inset * 2;
	const gap = 4;
	const size = Math.min(style.max, (width - gap * (count - 1)) / count);

	// Past roughly twenty pages the blocks are too small to count, so stop pretending
	// they are countable and show the position on a continuous track instead.
	if (size < 5) {
		const height = Math.min(12, style.max);
		// A marker that slides, not a fill that grows: the same reading as the blocks,
		// which light one at a time rather than accumulating.
		const marker = Math.max(6, width / count);
		const x = inset + (width - marker) * (count > 1 ? index / (count - 1) : 0);

		return (
			`<rect x="${inset}" y="${y}" width="${width}" height="${height}" rx="3" fill="${INK.trough}" stroke="${style.edge}" stroke-width="1"/>` +
			`<rect x="${x.toFixed(1)}" y="${y}" width="${marker.toFixed(1)}" height="${height}" rx="3" fill="${style.fill}"/>`
		);
	}

	const span = size * count + gap * (count - 1);
	const start = (SIZE - span) / 2;

	return Array.from({ length: count }, (_, position) => {
		const x = start + position * (size + gap);
		const current = position === index;

		return `<rect x="${x.toFixed(1)}" y="${y}" width="${size.toFixed(1)}" height="${size.toFixed(1)}" rx="2" fill="${current ? style.fill : INK.trough}" stroke="${current ? style.fill : style.edge}" stroke-width="1"/>`;
	}).join("");
}

function formatDuration(seconds: number): string {
	const whole = Math.ceil(seconds);

	if (whole >= 3600) {
		const hours = Math.floor(whole / 3600);
		const minutes = Math.floor((whole % 3600) / 60);
		return `${hours}h${String(minutes).padStart(2, "0")}`;
	}

	if (whole >= 60) {
		const minutes = Math.floor(whole / 60);
		return `${minutes}:${String(whole % 60).padStart(2, "0")}`;
	}

	return `${whole}s`;
}

function truncate(value: string, max: number): string {
	return value.length <= max ? value : `${value.slice(0, max - 1)}…`;
}
