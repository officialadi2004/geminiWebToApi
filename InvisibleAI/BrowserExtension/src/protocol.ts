export const HOST = "com.invisibleai.assistant";
export type ResponseMode = "SHORT" | "CONCISE" | "DETAILED";
export interface WireMessage { type: string; id: string; version: 1; payload?: Record<string, unknown> }
export interface Status { connected: boolean; processing: boolean; error?: string; enabled?: boolean; model?: string }
export function wire(type: string, payload?: Record<string, unknown>): WireMessage {
  return { version: 1, type, id: crypto.randomUUID(), ...(payload ? { payload } : {}) };
}
export function validEnvelope(value: unknown): value is WireMessage {
  if (!value || typeof value !== "object") return false;
  const m = value as Partial<WireMessage>;
  return m.version === 1 && typeof m.type === "string" && typeof m.id === "string" &&
    m.id.length > 0 && m.id.length <= 128 &&
    (m.payload === undefined || (m.payload !== null && typeof m.payload === "object" && !Array.isArray(m.payload)));
}
