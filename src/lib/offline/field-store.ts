import { ApiError, api } from "@/lib/api/client";
import type { WorkOrderDetail } from "@/lib/api/types";

/** ADR-GA-07. Field mode keeps one person's own open work orders and their queued changes in IndexedDB — never in the service worker, never anyone else's. */
const DB_NAME = "annaba-field";
const SNAPSHOT_HOURS = 72;

export type Snapshot = { owner: string; takenAt: string; items: WorkOrderDetail[] };
export type QueuedOp = {
  id: string;                 // also the Idempotency-Key
  seq: number; owner: string; woId: string; label: string; createdAt: string;
  kind: "action" | "tick" | "time" | "photo";
  path: string; body?: unknown; blob?: Blob; fileName?: string;
  state: "waiting" | "conflict"; error?: string;
};

function open(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const r = indexedDB.open(DB_NAME, 1);
    r.onupgradeneeded = () => { const db = r.result; db.createObjectStore("kv"); db.createObjectStore("queue", { keyPath: "id" }); };
    r.onsuccess = () => resolve(r.result); r.onerror = () => reject(r.error);
  });
}
async function tx<T>(store: string, mode: IDBTransactionMode, fn: (s: IDBObjectStore) => IDBRequest<T> | void): Promise<T | undefined> {
  const db = await open();
  try {
    return await new Promise<T | undefined>((resolve, reject) => {
      const t = db.transaction(store, mode); const req = fn(t.objectStore(store)); let value: T | undefined;
      if (req) req.onsuccess = () => { value = req.result; };
      t.oncomplete = () => resolve(value); t.onerror = () => reject(t.error); t.onabort = () => reject(t.error);
    });
  } finally { db.close(); }
}

export const fieldSupported = () => typeof indexedDB !== "undefined";

/** The snapshot, if it belongs to this person and is not older than 72 hours; anything else is wiped. */
export async function loadSnapshot(owner: string): Promise<Snapshot | null> {
  const s = await tx<Snapshot>("kv", "readonly", (st) => st.get("snapshot"));
  if (!s) return null;
  if (s.owner !== owner || Date.now() - new Date(s.takenAt).getTime() > SNAPSHOT_HOURS * 3600_000) { if (s.owner !== owner) await wipeFieldData(); else await tx("kv", "readwrite", (st) => st.delete("snapshot")); return null; }
  return s;
}
export async function saveSnapshot(s: Snapshot) {
  const old = await tx<Snapshot>("kv", "readonly", (st) => st.get("snapshot"));
  if (old && old.owner !== s.owner) await wipeFieldData();         // someone else used this device: their data goes first
  await tx("kv", "readwrite", (st) => st.put(s, "snapshot"));
}
export async function listOps(owner: string): Promise<QueuedOp[]> {
  const all = (await tx<QueuedOp[]>("queue", "readonly", (st) => st.getAll())) ?? [];
  return all.filter((o) => o.owner === owner).sort((a, b) => a.seq - b.seq);
}
export async function addOp(op: Omit<QueuedOp, "id" | "seq" | "createdAt" | "state">) {
  const full: QueuedOp = { ...op, id: crypto.randomUUID(), seq: Date.now() + Math.random(), createdAt: new Date().toISOString(), state: "waiting" };
  await tx("queue", "readwrite", (st) => st.put(full));
  return full;
}
export const putOp = (op: QueuedOp) => tx("queue", "readwrite", (st) => st.put(op));
export const removeOp = (id: string) => tx("queue", "readwrite", (st) => st.delete(id));

/** Wiped on sign-out, when another person signs in, and with "Forget this device's data". */
export async function wipeFieldData() {
  if (!fieldSupported()) return;
  await new Promise<void>((resolve) => { const r = indexedDB.deleteDatabase(DB_NAME); r.onsuccess = r.onerror = r.onblocked = () => resolve(); });
}

/** Sends one queued change through the normal API with its Idempotency-Key. A network failure keeps it waiting; a refusal becomes a conflict. */
export async function sendOp(op: QueuedOp): Promise<{ ok: true; wo: WorkOrderDetail | undefined } | { ok: false; offline: boolean; message: string }> {
  const headers = { "Idempotency-Key": op.id };
  try {
    if (op.kind === "photo" && op.blob) {
      const fd = new FormData(); fd.append("file", new File([op.blob], op.fileName ?? "photo.jpg", { type: op.blob.type || "image/jpeg" }));
      return { ok: true, wo: await api<WorkOrderDetail>(op.path, { method: "POST", rawBody: fd, headers, noRoute: true }) };
    }
    return { ok: true, wo: await api<WorkOrderDetail>(op.path, { method: "POST", body: op.body ?? {}, headers, noRoute: true }) };
  } catch (e) {
    if (e instanceof ApiError && e.status !== 0 && e.status < 500 && e.status !== 401 && e.status !== 408 && e.status !== 429)
      return { ok: false, offline: false, message: Object.values(e.fields ?? {})[0] ?? e.message };
    return { ok: false, offline: true, message: e instanceof Error ? e.message : String(e) };
  }
}
