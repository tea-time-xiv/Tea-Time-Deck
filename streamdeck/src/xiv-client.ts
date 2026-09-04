import streamDeck from "@elgato/streamdeck";
import { EventEmitter } from "node:events";
import WebSocket from "ws";

import { discoverServers } from "./server-discovery.js";

/** One thing the player owns, as returned by `catalog.list`. */
export type CatalogEntry = {
	kind: string;
	id: number;
	name: string;
	iconId: number;
	category: string | null;
	sortOrder: number;
	command: string | null;
	/** Set only by kinds the game does not number -- Glamourer designs are GUIDs. */
	key?: string | null;
};

export type CatalogKind = {
	kind: string;
	displayName: string;
	/** Which field names an entry of this kind. Absent from older servers, where it is always "id". */
	addressing?: "id" | "key";
};

/** Read-only game state for the status keys, pushed by the plugin when it changes. */
export type StatusSnapshot = {
	loggedIn: boolean;
	job: {
		id: number;
		abbreviation: string;
		name: string;
		iconId: number;
		level: number;
		effectiveLevel: number;
		isLevelSynced: boolean;
		experience: number;
		experienceToNext: number;
	} | null;
	vitals: {
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
	} | null;
	duty: { state: string; dutyName: string | null };
	retainers: { total: number; active: number; ready: number; soonestCompleteAt?: number | null };
	cooldowns: { id: number; remaining: number; total: number }[];
};

export type CooldownSource = { id: number; name: string; iconId: number };

type Envelope = {
	id?: string;
	type: string;
	ok?: boolean;
	payload?: unknown;
	error?: string;
};

type Pending = {
	resolve: (payload: unknown) => void;
	reject: (error: Error) => void;
	timer: NodeJS.Timeout;
};

const REQUEST_TIMEOUT_MS = 10_000;

/** Matches the server's per-request cap. */
const ICON_BATCH_SIZE = 64;

const RECONNECT_MIN_MS = 1_000;
const RECONNECT_MAX_MS = 30_000;

const DEFAULT_PORT = 37985;

/** Where the port in use came from, for the inspector to report. */
export type PortSource = "auto" | "manual" | "default";

export type ServerStatus = {
	source: PortSource;
	/** The config file the port was read from. Only set for "auto". */
	path?: string;
	port: number;
};

/**
 * Talks to the Dalamud plugin inside FFXIV.
 *
 * The game is not always running, so a dropped connection is the normal state rather
 * than an error. The client reconnects on its own with backoff, and callers just await
 * requests that fail cleanly while it is down.
 */
export class XivClient extends EventEmitter {
	#socket: WebSocket | undefined;
	#pending = new Map<string, Pending>();
	#catalogs = new Map<string, CatalogEntry[]>();
	#kinds: CatalogKind[] | undefined;

	/**
	 * Icon artwork does not change when the player unlocks something, so this survives
	 * catalog invalidation and is only dropped when the server itself changes.
	 */
	#icons = new Map<number, string>();

	/** Latest pushed snapshot. Undefined until the first arrives. */
	#status: StatusSnapshot | undefined;

	/** Typed-in port. Undefined means "work it out from the game's config". */
	#manualPort: number | undefined;

	/** Whether configure has run. Its no-op check cannot tell "no override" apart from
	 * "not started yet", and no override is the normal case. */
	#configured = false;

	/** What the last resolution settled on. */
	#serverStatus: ServerStatus = { source: "default", port: DEFAULT_PORT };

	#nextId = 0;
	#reconnectDelay = RECONNECT_MIN_MS;
	#reconnectTimer: NodeJS.Timeout | undefined;
	#closed = false;

	/** Bumped whenever a connection attempt is superseded, so a slow disk read cannot
	 * open a socket that is no longer wanted. */
	#generation = 0;

	/** Rotates through discovered configs, so a machine with several XIVLauncher
	 * profiles settles on whichever one is actually playing. */
	#attempt = 0;

	/** The config that last connected. Tried first from then on. */
	#preferredPath: string | undefined;

	public get connected(): boolean {
		return this.#socket?.readyState === WebSocket.OPEN;
	}

	/** The port currently in use, and where it came from. */
	public get serverStatus(): ServerStatus {
		return this.#serverStatus;
	}

	/**
	 * Points the client at a server. The port is an override: undefined means the game's
	 * own config file decides, which is the normal case.
	 *
	 * Reconnects if the override changed; does nothing if it did not, so this is safe to
	 * call on every settings change.
	 */
	public configure(port: number | undefined): void {
		if (this.#configured && this.#manualPort === port) {
			return;
		}

		this.#configured = true;
		this.#manualPort = port;
		this.#invalidate();
		// A different server could be a different game version, so the artwork may differ.
		this.#icons.clear();
		this.#disconnect();
		this.#connect();
	}

	/**
	 * A key-addressed kind sends its key instead of an id; the server decides which it
	 * wants from the kind, so sending both is harmless and sending neither is not.
	 */
	public async execute(kind: string, id: number, key?: string | null): Promise<void> {
		await this.#request("execute", key ? { kind, key } : { kind, id });
	}

	public async getKinds(): Promise<CatalogKind[]> {
		if (this.#kinds) {
			return this.#kinds;
		}

		const payload = (await this.#request("catalog.kinds")) as { kinds: CatalogKind[] };
		this.#kinds = payload.kinds;
		return this.#kinds;
	}

	/** Cached until the game says an unlock changed things. */
	public async getEntries(kind: string): Promise<CatalogEntry[]> {
		const cached = this.#catalogs.get(kind);
		if (cached) {
			return cached;
		}

		const payload = (await this.#request("catalog.list", { kind })) as { entries: CatalogEntry[] };
		this.#catalogs.set(kind, payload.entries);
		return payload.entries;
	}

	public get status(): StatusSnapshot | undefined {
		return this.#status;
	}

	public async getCooldownSources(): Promise<CooldownSource[]> {
		const payload = (await this.#request("status.cooldownSources")) as { sources: CooldownSource[] };
		return payload.sources;
	}

	/**
	 * Fetches any of these icons that are not already cached, batched.
	 * A browser page needs a deck's worth at once; one request per key would mean dozens
	 * of round trips before the page finished drawing.
	 */
	public async primeIcons(iconIds: number[]): Promise<void> {
		// 0 means the entry has no game artwork at all, so there is nothing to fetch.
		const missing = [...new Set(iconIds)].filter((id) => id !== 0 && !this.#icons.has(id));

		for (let i = 0; i < missing.length; i += ICON_BATCH_SIZE) {
			const batch = missing.slice(i, i + ICON_BATCH_SIZE);
			const payload = (await this.#request("icon.getMany", { iconIds: batch })) as {
				icons: { iconId: number; data: string }[];
			};

			// Ids that failed to resolve are absent, so match on iconId rather than order.
			for (const icon of payload.icons) {
				this.#icons.set(icon.iconId, `data:image/png;base64,${icon.data}`);
			}
		}
	}

	/** Returns the icon as a data URI, ready to hand to `setImage`. */
	public async getIcon(iconId: number): Promise<string> {
		const cached = this.#icons.get(iconId);
		if (cached) {
			return cached;
		}

		const payload = (await this.#request("icon.get", { iconId })) as { data: string };
		const uri = `data:image/png;base64,${payload.data}`;

		this.#icons.set(iconId, uri);
		return uri;
	}

	public dispose(): void {
		this.#closed = true;
		this.#disconnect();
	}

	#connect(): void {
		if (this.#closed) {
			return;
		}

		clearTimeout(this.#reconnectTimer);

		const generation = ++this.#generation;

		void this.#resolve().then((port) => {
			if (this.#closed || generation !== this.#generation) {
				return;
			}

			this.#open(port);
		});
	}

	/**
	 * Works out which port to use, preferring a typed-in one over what the game's config
	 * file says. Runs before every attempt, so a port changed in game is picked up by the
	 * next reconnect.
	 */
	async #resolve(): Promise<number> {
		if (this.#manualPort !== undefined) {
			this.#reportServerStatus({ source: "manual", port: this.#manualPort });
			return this.#manualPort;
		}

		const found = await discoverServers();

		if (found.length === 0) {
			// The game plugin is not installed yet, or lives somewhere the probes do not
			// reach. The default is still worth trying: it is what it would be listening on.
			this.#reportServerStatus({ source: "default", port: DEFAULT_PORT });
			return DEFAULT_PORT;
		}

		// One config per launcher profile, and only one of them is playing. Whichever
		// worked last goes first; otherwise each attempt tries the next in turn.
		const preferred = found.findIndex((server) => server.path === this.#preferredPath);
		const chosen = found[preferred >= 0 ? preferred : this.#attempt % found.length]!;

		this.#reportServerStatus({ source: "auto", path: chosen.path, port: chosen.port });
		return chosen.port;
	}

	#reportServerStatus(status: ServerStatus): void {
		const previous = this.#serverStatus;
		if (previous.source === status.source && previous.path === status.path && previous.port === status.port) {
			return;
		}

		this.#serverStatus = status;

		if (status.source === "auto") {
			streamDeck.logger.info(`Using port ${status.port}, from ${status.path}.`);
		}

		this.emit("serverstatus", status);
	}

	#open(port: number): void {
		const socket = new WebSocket(`ws://localhost:${port}/ws`);

		this.#socket = socket;

		let opened = false;

		socket.on("open", () => {
			opened = true;
			this.#reconnectDelay = RECONNECT_MIN_MS;
			// This config answered, so stop rotating through the others.
			this.#attempt = 0;
			this.#preferredPath = this.#serverStatus.path;
			streamDeck.logger.info("Connected to FFXIV.");
			this.emit("connected");

			// Status is pushed only when it changes, so ask once rather than wait for the
			// first change -- otherwise a paused game leaves the keys blank indefinitely.
			void this.#request("status.get")
				.then((payload) => {
					if (payload) {
						this.#status = payload as StatusSnapshot;
						this.emit("status");
					}
				})
				.catch(() => {
					// The socket dropped again straight away; the retry will cover it.
				});
		});

		socket.on("message", (data) => this.#onMessage(data.toString()));

		socket.on("error", (error: Error & { message: string }) => {
			// The game not running is the common case; do not shout about it.
			streamDeck.logger.debug(`XIV socket error: ${error.message}`);
		});

		socket.on("close", (code) => {
			if (this.#socket === socket) {
				this.#socket = undefined;
			}

			if (!opened) {
				// Nothing is listening on that port, so this profile is not the one
				// playing. Try the next one next time.
				this.#preferredPath = undefined;
				this.#attempt++;
			}

			// The game not running is the ordinary case, so say it once per drop rather
			// than on every retry.
			if (code === 1006 && this.#reconnectDelay === RECONNECT_MIN_MS) {
				streamDeck.logger.info("Disconnected from FFXIV.");
			}

			this.#failPending(new Error("not connected to FFXIV"));
			this.#invalidate();
			// Stale vitals are worse than none: the keys should say the game is gone.
			this.#status = undefined;
			this.emit("status");
			this.emit("disconnected");
			this.#scheduleReconnect();
		});
	}

	#scheduleReconnect(): void {
		if (this.#closed) {
			return;
		}

		clearTimeout(this.#reconnectTimer);
		this.#reconnectTimer = setTimeout(() => this.#connect(), this.#reconnectDelay);
		this.#reconnectDelay = Math.min(this.#reconnectDelay * 2, RECONNECT_MAX_MS);
	}

	#disconnect(): void {
		clearTimeout(this.#reconnectTimer);
		this.#generation++;
		this.#failPending(new Error("connection replaced"));

		const socket = this.#socket;
		this.#socket = undefined;

		if (socket) {
			socket.removeAllListeners();
			socket.close();
		}
	}

	#onMessage(raw: string): void {
		let message: Envelope;
		try {
			message = JSON.parse(raw) as Envelope;
		} catch {
			streamDeck.logger.warn(`Ignoring malformed message from FFXIV: ${raw.slice(0, 200)}`);
			return;
		}

		// No correlation id means it is a pushed event, not a reply.
		if (message.id === undefined) {
			if (message.type === "catalog.invalidated") {
				// No payload means every kind, which is what this event used to mean always.
				const kinds = (message.payload as { kinds?: string[] } | undefined)?.kinds;
				streamDeck.logger.debug(`Catalogs invalidated (${kinds?.join(", ") ?? "all"}); dropping cache.`);
				this.#invalidate(kinds);
				this.emit("invalidated");
			} else if (message.type === "status.update") {
				this.#status = message.payload as StatusSnapshot;
				this.emit("status");
			}
			return;
		}

		const pending = this.#pending.get(message.id);
		if (!pending) {
			return;
		}

		this.#pending.delete(message.id);
		clearTimeout(pending.timer);

		if (message.ok === false) {
			pending.reject(new Error(message.error ?? "request failed"));
		} else {
			pending.resolve(message.payload);
		}
	}

	#request(type: string, payload?: unknown): Promise<unknown> {
		const socket = this.#socket;
		if (socket?.readyState !== WebSocket.OPEN) {
			return Promise.reject(new Error("not connected to FFXIV"));
		}

		const id = `sd-${this.#nextId++}`;

		return new Promise((resolve, reject) => {
			const timer = setTimeout(() => {
				this.#pending.delete(id);
				reject(new Error(`'${type}' timed out`));
			}, REQUEST_TIMEOUT_MS);

			this.#pending.set(id, { resolve, reject, timer });
			socket.send(JSON.stringify({ id, type, payload }));
		});
	}

	#failPending(error: Error): void {
		for (const pending of this.#pending.values()) {
			clearTimeout(pending.timer);
			pending.reject(error);
		}

		this.#pending.clear();
	}

	/**
	 * Drops cached catalogs. Naming kinds drops only those; naming none drops everything,
	 * which is what a reconnect or an unlock means.
	 */
	#invalidate(kinds?: string[]): void {
		if (kinds === undefined) {
			this.#catalogs.clear();
			// Which kinds a server offers is a property of the server, not of what the
			// character has unlocked, so only an unscoped drop can have changed it.
			this.#kinds = undefined;
			return;
		}

		for (const kind of kinds) {
			this.#catalogs.delete(kind);
		}
	}
}

export const xiv = new XivClient();
