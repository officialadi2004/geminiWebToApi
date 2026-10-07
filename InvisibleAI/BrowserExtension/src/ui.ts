export async function invoke(action: string, values?: Record<string, unknown>): Promise<Record<string, unknown>> {
  const response = await chrome.runtime.sendMessage({ action, ...(values ? { values } : {}) });
  if (!response?.ok) throw new Error(response?.error ?? "Could not connect to the local helper.");
  return response.data ?? {};
}
export function node<T extends HTMLElement>(id: string): T {
  const element = document.getElementById(id);
  if (!element) throw new Error(`Missing UI element: ${id}`);
  return element as T;
}
