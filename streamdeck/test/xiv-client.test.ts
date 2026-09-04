import { EventEmitter } from "node:events";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

/**
 * The client against a fake socket: no port is opened and no game is needed.
 *
 * What is worth defending here is the bookkeeping either side of the wire -- which
 * reply belongs to which request, what a dropped connection invalidates, and how much
 * of the cache an invalidation event is allowed to take. All of it is invisible from
 * the game side, so the PowerShell suite in `tools/` cannot reach any of it.
 */

/** Every socket the client has constructed, oldest first. */
const sockets: FakeSocket[] = [];

class FakeSocket extends EventEmitter {
	static readonly CONNECTING = 0;
	static readonly OPEN = 1;
	static readonly CLOSING = 2;
	static readonly CLOSED = 3;

	public readyState: number = FakeSocket.CONNECTING;
	/** Raw frames the client has written, in order. */
	public readonly sent: string[] = [];
	public closeCalls = 0;

	constructor(public readonly url: string) {
		super();
		sockets.push(this);
	}

	public send(data: string): void {
		this.sent.push(data);
	}

	public close(): void {
		this.closeCalls++;
		this.readyState = FakeSocket.CLOSED;
	}
}

vi.mock("ws", () => ({ default: FakeSocket }));

vi.mock("@elgato/streamdeck", () => ({
	default: { logger: { info: vi.fn(), debug: vi.fn(), warn: vi.fn(), error: vi.fn() } },
}));

// Resolution is server-discovery's job and is tested there; here every test pins a port.
vi.mock("../src/server-discovery.js", () => ({ discoverServers: vi.fn(async () => []) }));

const { XivClient } = await import("../src/xiv-client.js");
type Client = InstanceType<typeof XivClient>;

/** One request frame as it went out on the wire. */
type Frame = { id: string; type: string; payload?: any };

/**
 * Lets queued promise callbacks run. The client hops a microtask between deciding on a
 * port and opening the socket, so nothing observable happens without this.
 */
async function flush(): Promise<void> {
	for (let i = 0; i < 8; i++) {
		await Promise.resolve();
	}
}

/** The frames the client has written, parsed. */
function frames(socket: FakeSocket): Frame[] {
	return socket.sent.map((raw) => JSON.parse(raw) as Frame);
}

/** The request of this type the client most recently sent. */
function frameOf(socket: FakeSocket, type: string): Frame {
	const match = frames(socket)
		.filter((frame) => frame.type === type)
		.at(-1);

	if (!match) {
		throw new Error(`no '${type}' request was sent`);
	}

	return match;
}

/** Hands the client a message, the way `ws` would. */
function deliver(socket: FakeSocket, message: unknown): void {
	socket.emit("message", Buffer.from(JSON.stringify(message)));
}

/** Answers a request the client is waiting on. */
function replyTo(socket: FakeSocket, type: string, payload: unknown): void {
	deliver(socket, { id: frameOf(socket, type).id, type, ok: true, payload });
}

/** Brings a client up on a socket the tests can drive, with nothing left pending. */
async function connect(client: Client, port = 37985): Promise<FakeSocket> {
	client.configure(port);
	await flush();

	const socket = sockets.at(-1)!;
	socket.readyState = FakeSocket.OPEN;
	socket.emit("open");
	await flush();

	// The client asks for a first snapshot as soon as it is up; answer it so later
	// assertions are not reading around it.
	replyTo(socket, "status.get", null);
	await flush();
	socket.sent.length = 0;

	return socket;
}

let client: Client;

beforeEach(() => {
	vi.useFakeTimers();
	sockets.length = 0;
	client = new XivClient();
});

afterEach(() => {
	client.dispose();
	vi.useRealTimers();
});

describe("request correlation", () => {
	it("matches a reply to its own request", async () => {
		const socket = await connect(client);

		const emotes = client.getEntries("emote");
		const mounts = client.getEntries("mount");
		await flush();

		const [first, second] = frames(socket);
		expect(first!.payload).toEqual({ kind: "emote" });
		expect(second!.payload).toEqual({ kind: "mount" });
		expect(first!.id).not.toBe(second!.id);

		// Out of order on purpose: the id is what decides, not arrival.
		deliver(socket, {
			id: second!.id,
			type: "catalog.list",
			ok: true,
			payload: { entries: [{ name: "Bomb Palanquin" }] },
		});
		deliver(socket, {
			id: first!.id,
			type: "catalog.list",
			ok: true,
			payload: { entries: [{ name: "Wave" }] },
		});

		await expect(emotes).resolves.toEqual([{ name: "Wave" }]);
		await expect(mounts).resolves.toEqual([{ name: "Bomb Palanquin" }]);
	});

	it("drops a reply whose id matches nothing, rather than settling something else", async () => {
		const socket = await connect(client);

		const emotes = client.getEntries("emote");
		await flush();

		deliver(socket, { id: "sd-not-a-real-id", type: "catalog.list", ok: true, payload: { entries: [] } });
		await flush();

		// Still waiting: the stray reply took nothing with it.
		replyTo(socket, "catalog.list", { entries: [{ name: "Wave" }] });
		await expect(emotes).resolves.toEqual([{ name: "Wave" }]);
	});

	it("rejects with the server's own error string", async () => {
		const socket = await connect(client);

		const press = client.execute("emote", 12);
		await flush();

		deliver(socket, {
			id: frameOf(socket, "execute").id,
			type: "execute",
			ok: false,
			error: "executing too fast; one action per press",
		});

		await expect(press).rejects.toThrow("executing too fast; one action per press");
	});

	it("rejects on the timeout and forgets the request", async () => {
		const socket = await connect(client);

		const emotes = client.getEntries("emote");
		await flush();

		// Waiting on the rejection before winding the clock on, or the timer fires with
		// nothing yet listening and Node reports it as unhandled.
		const timedOut = expect(emotes).rejects.toThrow("'catalog.list' timed out");
		await vi.advanceTimersByTimeAsync(10_000);
		await timedOut;

		// If the timed-out request were still in the pending map, the close below would
		// reject it a second time -- with nothing left to catch it.
		socket.emit("close", 1006);
		await flush();
	});

	it("refuses a request while there is no connection", async () => {
		await expect(client.getEntries("emote")).rejects.toThrow("not connected to FFXIV");
	});
});

describe("catalog.invalidated", () => {
	/** Loads the kind list and both catalogs into the cache. */
	async function primeCache(socket: FakeSocket): Promise<void> {
		const kinds = client.getKinds();
		await flush();
		replyTo(socket, "catalog.kinds", { kinds: [{ kind: "emote", displayName: "Emotes" }] });
		await kinds;

		for (const kind of ["emote", "mount"]) {
			const entries = client.getEntries(kind);
			await flush();
			replyTo(socket, "catalog.list", { entries: [{ kind }] });
			await entries;
		}

		socket.sent.length = 0;
	}

	it("drops only the kinds it names", async () => {
		const socket = await connect(client);
		await primeCache(socket);

		deliver(socket, { type: "catalog.invalidated", payload: { kinds: ["emote"] } });
		await flush();

		// Emotes have to be refetched...
		const emotes = client.getEntries("emote");
		await flush();
		expect(frameOf(socket, "catalog.list").payload).toEqual({ kind: "emote" });
		replyTo(socket, "catalog.list", { entries: [] });
		await emotes;

		socket.sent.length = 0;

		// ...and nothing else was touched, the kind list included. Asserting on the wire
		// first: a cache that was wrongly dropped would sit waiting for a reply that the
		// test never sends, and fail on the request timeout ten seconds later instead.
		const mounts = client.getEntries("mount");
		const kinds = client.getKinds();
		await flush();
		expect(socket.sent).toEqual([]);

		await expect(mounts).resolves.toEqual([{ kind: "mount" }]);
		await expect(kinds).resolves.toEqual([{ kind: "emote", displayName: "Emotes" }]);
	});

	it("drops every catalog and the kind list when it names none", async () => {
		const socket = await connect(client);
		await primeCache(socket);

		deliver(socket, { type: "catalog.invalidated" });
		await flush();

		for (const kind of ["emote", "mount"]) {
			const entries = client.getEntries(kind);
			await flush();
			expect(frameOf(socket, "catalog.list").payload).toEqual({ kind });
			replyTo(socket, "catalog.list", { entries: [] });
			await entries;
		}

		// Which kinds the server offers can only have changed on an unscoped drop, so
		// this is the one that has to refetch them.
		const kinds = client.getKinds();
		await flush();
		expect(frameOf(socket, "catalog.kinds")).toBeDefined();
		replyTo(socket, "catalog.kinds", { kinds: [] });
		await expect(kinds).resolves.toEqual([]);
	});

	it("announces the drop so the browsers can repaint", async () => {
		const socket = await connect(client);
		const heard = vi.fn();
		client.on("invalidated", heard);

		deliver(socket, { type: "catalog.invalidated", payload: { kinds: ["glamourer"] } });
		await flush();

		expect(heard).toHaveBeenCalledOnce();
	});
});

describe("status", () => {
	it("takes a pushed snapshot and emits", async () => {
		const socket = await connect(client);
		const heard = vi.fn();
		client.on("status", heard);

		deliver(socket, { type: "status.update", payload: { loggedIn: true, job: null } });
		await flush();

		expect(client.status).toEqual({ loggedIn: true, job: null });
		expect(heard).toHaveBeenCalledOnce();
	});

	it("clears the snapshot when the connection drops, rather than leaving it stale", async () => {
		const socket = await connect(client);
		deliver(socket, { type: "status.update", payload: { loggedIn: true, job: null } });
		await flush();

		socket.emit("close", 1006);
		await flush();

		expect(client.status).toBeUndefined();
		expect(client.connected).toBe(false);
	});

	it("ignores a malformed frame instead of throwing", async () => {
		const socket = await connect(client);

		socket.emit("message", Buffer.from("{not json"));
		await flush();

		expect(client.status).toBeUndefined();
	});
});

describe("reconnection", () => {
	it("fails everything in flight when the connection drops", async () => {
		const socket = await connect(client);

		const emotes = client.getEntries("emote");
		await flush();

		socket.emit("close", 1006);

		await expect(emotes).rejects.toThrow("not connected to FFXIV");
	});

	it("backs off from one second to thirty, and no further", async () => {
		const socket = await connect(client);
		socket.emit("close", 1006);
		await flush();

		let opened = sockets.length;

		for (const delay of [1_000, 2_000, 4_000, 8_000, 16_000, 30_000, 30_000]) {
			// A shade under the delay is too early: nothing should have been attempted.
			await vi.advanceTimersByTimeAsync(delay - 1);
			expect(sockets.length, `retried before ${delay}ms`).toBe(opened);

			await vi.advanceTimersByTimeAsync(1);
			expect(sockets.length, `did not retry at ${delay}ms`).toBe(opened + 1);
			opened++;

			// Nothing was listening, so it drops again and the next delay doubles.
			sockets.at(-1)!.emit("close", 1006);
			await flush();
		}
	});

	it("resets the backoff once a connection succeeds", async () => {
		const socket = await connect(client);
		socket.emit("close", 1006);
		await flush();

		// Let it stretch out first, so a reset is distinguishable from never having grown.
		await vi.advanceTimersByTimeAsync(1_000);
		sockets.at(-1)!.emit("close", 1006);
		await flush();
		await vi.advanceTimersByTimeAsync(2_000);

		const revived = sockets.at(-1)!;
		revived.readyState = FakeSocket.OPEN;
		revived.emit("open");
		await flush();

		revived.emit("close", 1006);
		await flush();

		const before = sockets.length;
		await vi.advanceTimersByTimeAsync(1_000);
		expect(sockets.length).toBe(before + 1);
	});
});

describe("configure", () => {
	it("does nothing when the port has not changed", async () => {
		await connect(client, 37985);
		const before = sockets.length;

		client.configure(37985);
		await flush();

		expect(sockets.length).toBe(before);
		expect(client.connected).toBe(true);
	});

	it("reconnects to the new port and drops the icon cache when it has", async () => {
		const socket = await connect(client, 37985);

		const icon = client.getIcon(64);
		await flush();
		replyTo(socket, "icon.get", { data: "AAAA" });
		await expect(icon).resolves.toBe("data:image/png;base64,AAAA");

		// Cached: no second request.
		socket.sent.length = 0;
		await expect(client.getIcon(64)).resolves.toBe("data:image/png;base64,AAAA");
		expect(socket.sent).toEqual([]);

		client.configure(37990);
		await flush();

		const reopened = sockets.at(-1)!;
		expect(reopened.url).toBe("ws://localhost:37990/ws");
		expect(socket.closeCalls).toBe(1);

		reopened.readyState = FakeSocket.OPEN;
		reopened.emit("open");
		await flush();
		replyTo(reopened, "status.get", null);
		await flush();
		reopened.sent.length = 0;

		// A different server can be a different game version, so the artwork is refetched.
		const again = client.getIcon(64);
		await flush();
		expect(frameOf(reopened, "icon.get").payload).toEqual({ iconId: 64 });
		replyTo(reopened, "icon.get", { data: "BBBB" });
		await expect(again).resolves.toBe("data:image/png;base64,BBBB");
	});

	it("reports where the port came from", async () => {
		await connect(client, 37985);
		expect(client.serverStatus).toEqual({ source: "manual", port: 37985 });
	});
});
