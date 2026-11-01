import { createSignal } from 'solid-js';
import { apiClient } from './auth';

const [modelIds, setModelIds] = createSignal<string[]>([]);
let inFlight: Promise<void> | null = null;

export function normalizeModelIds(data: unknown): string[] {
  if (!Array.isArray(data)) return [];
  const ids = data
    .map((item) => {
      const row = (item ?? {}) as { model?: { id?: unknown }; id?: unknown };
      return String(row.model?.id ?? row.id ?? '');
    })
    .filter(Boolean);
  return [...new Set(ids)].sort((a, b) => a.localeCompare(b));
}

/**
 * The registry's model ids, for pickers. A failure leaves the list empty and the picker falls back
 * to free text, so it is not reported: the page it sits on has its own errors to show.
 */
export function loadModelOptions(opts?: { force?: boolean }): Promise<void> {
  if (inFlight) return inFlight;
  if (modelIds().length && !opts?.force) return Promise.resolve();
  inFlight = apiClient
    .apiJson<unknown>('/admin/api/models')
    .then((data) => {
      setModelIds(normalizeModelIds(data));
    })
    .catch(() => {})
    .finally(() => {
      inFlight = null;
    });
  return inFlight;
}

export function useModelOptions() {
  return { modelIds, load: loadModelOptions };
}
