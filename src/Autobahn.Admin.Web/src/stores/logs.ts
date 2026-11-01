import { createSignal } from 'solid-js';
import { createStore } from 'solid-js/store';
import { debounceMs } from '../domain/filters';
import { createResource, RESOURCE_FRESH_MS } from '../realtime/resources';
import { apiClient, handleApiError, pushToast } from './auth';

const PAGE_SIZE = 50;

export interface LogRow extends Record<string, unknown> {
  id?: string;
  timestampUtc?: string;
  lastTimestampUtc?: string;
  level?: string;
  category?: string;
  eventCode?: string | null;
  message?: string;
  detail?: string | null;
  hint?: string | null;
  modelId?: string | null;
  requestId?: string | null;
  repeats?: number;
}

/** The API has answered with both shapes; `entries` is what the gateway sends today. */
interface LogsBody {
  entries?: LogRow[];
  items?: LogRow[];
  total?: number;
  capacity?: number;
}

const [search, setSearch] = createSignal('');
const [debouncedSearch, setDebouncedSearch] = createSignal('');
const [level, setLevel] = createSignal('all');
const [autoRefresh, setAutoRefresh] = createSignal(true);
const [byId, setById] = createStore<Record<string, LogRow>>({});
const [order, setOrder] = createSignal<string[]>([]);
const [expandedId, setExpandedId] = createSignal<string | null>(null);
const [offset, setOffset] = createSignal(0);
const [total, setTotal] = createSignal(0);
const [capacity, setCapacity] = createSignal(0);
const [active, setActive] = createSignal(false);

const applyDebounced = debounceMs((q: string) => setDebouncedSearch(q), 400);

const resource = createResource<LogsBody>({
  freshMs: RESOURCE_FRESH_MS.logs,
  fetch: async (signal) => {
    const params = new URLSearchParams({ limit: String(PAGE_SIZE), offset: String(offset()) });
    const q = debouncedSearch().trim();
    if (q) params.set('search', q);
    if (level() && level() !== 'all') params.set('level', level());
    const data = await apiClient.apiJson<LogsBody>(
      `/admin/api/logs?${params}`,
      { signal },
    );
    return data ?? { entries: [] };
  },
});

export function useLogsStore() {
  return {
    search,
    setSearch: (v: string) => {
      setSearch(v);
      applyDebounced(v);
    },
    level,
    setLevel,
    autoRefresh,
    setAutoRefresh,
    byId,
    order,
    expandedId,
    setExpandedId,
    offset,
    total,
    capacity,
    pageSize: () => PAGE_SIZE,
    setOffset: (v: number) => {
      setOffset(Math.max(0, v));
      void loadLogs({ force: true });
    },
    phase: () => resource.snapshot().phase,
    load: (opts?: { force?: boolean }) => loadLogs(opts),
    clearLogs,
    setActive,
  };
}

export async function clearLogs(): Promise<void> {
  try {
    await apiClient.apiJson('/admin/api/logs', { method: 'DELETE' });
    setById({});
    setOrder([]);
    setTotal(0);
    setOffset(0);
    setExpandedId(null);
    pushToast('Log buffer cleared.');
  } catch (e) {
    handleApiError(e, 'logs');
    throw e;
  }
}

export async function loadLogs(opts?: { force?: boolean; background?: boolean }): Promise<void> {
  try {
    const data = await resource.load(opts);
    if (!data) return;
    const items = data.entries ?? data.items ?? [];
    const map: Record<string, LogRow> = {};
    const ids: string[] = [];
    for (const row of items) {
      const id = String(row.id ?? `${row.timestampUtc}-${row.message}`);
      map[id] = row;
      ids.push(id);
    }
    setById(map);
    setOrder(ids);
    setTotal(Number(data.total ?? items.length));
    setCapacity(Number(data.capacity ?? 0));
  } catch (e) {
    handleApiError(e, 'logs');
  }
}

export function refreshLogsIfActive(): void {
  if (!active() || !autoRefresh()) return;
  void loadLogs({ background: true });
}

export function disposeLogsPage(): void {
  setActive(false);
  resource.abort();
}

export function activateLogsPage(): void {
  setActive(true);
  const snap = resource.snapshot();
  if (snap.phase === 'idle' || snap.phase === 'stale') void loadLogs({ background: snap.phase === 'stale' });
  else resource.tick();
}
