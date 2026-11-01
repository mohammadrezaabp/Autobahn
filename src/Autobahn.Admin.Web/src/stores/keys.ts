import { createSignal } from 'solid-js';
import { createStore } from 'solid-js/store';
import { filterKeys, type KeyStatusFilter, debounceMs } from '../domain/filters';
import type { SortSpec } from '../domain/sort';
import { createResource, RESOURCE_FRESH_MS } from '../realtime/resources';
import { apiClient, handleApiError, pushToast } from './auth';

export interface NewKeyDraft {
  /** The tenant the key is created in. Empty means the caller's own. */
  tenantId: string;
  role: string;
  label: string;
  assignee: string;
  description: string;
  costCenter: string;
}

export interface KeyEditDraft {
  id: string;
  keyPrefix: string;
  label: string;
  assignee: string;
  description: string;
  costCenter: string;
}

export const DEFAULT_KEY_EDIT: KeyEditDraft = {
  id: '',
  keyPrefix: '',
  label: '',
  assignee: '',
  description: '',
  costCenter: '',
};

export const DEFAULT_NEW_KEY: NewKeyDraft = {
  tenantId: '',
  role: 'Inference',
  label: '',
  assignee: '',
  description: '',
  costCenter: '',
};

export const KEYS_RENDER_CAP = 50;
const [sortSpec, setSortSpec] = createSignal<SortSpec>({ key: 'createdAt', dir: -1 });
const [selectedIds, setSelectedIds] = createSignal<Set<string>>(new Set());

const [statusFilter, setStatusFilter] = createSignal<KeyStatusFilter>('active');
const [textFilter, setTextFilter] = createSignal('');
const [debouncedText, setDebouncedText] = createSignal('');
const [tenantFilter, setTenantFilter] = createSignal('');
const [tenants, setTenants] = createSignal<TenantOption[]>([]);
const applyDebounced = debounceMs((q: string) => setDebouncedText(q), 400);
const [byId, setById] = createStore<Record<string, Record<string, unknown>>>({});
const [order, setOrder] = createSignal<string[]>([]);

export function normalizeKeysListResponse(data: unknown): Record<string, unknown>[] {
  if (Array.isArray(data)) return data;
  if (data && typeof data === 'object' && Array.isArray((data as { items?: unknown }).items)) {
    return (data as { items: Record<string, unknown>[] }).items;
  }
  return [];
}

export interface TenantOption {
  id: string;
  slug: string;
  name: string;
}

/** The request header with which an operator names the tenant a key request acts on. */
export const TENANT_HEADER = 'X-Autobahn-Tenant';

export function tenantHeaders(tenantId: unknown): Record<string, string> {
  return typeof tenantId === 'string' && tenantId ? { [TENANT_HEADER]: tenantId } : {};
}

export function normalizeTenantsResponse(data: unknown): TenantOption[] {
  if (!Array.isArray(data)) return [];
  return data
    .filter((t): t is Record<string, unknown> => !!t && typeof t === 'object' && !!(t as { id?: unknown }).id)
    .map((t) => ({ id: String(t.id), slug: String(t.slug ?? ''), name: String(t.name ?? t.slug ?? '') }));
}

/** Each tenant's keys in one list, every row carrying the tenant it came from. */
export function mergeTenantKeys(
  tenantList: readonly TenantOption[],
  lists: readonly Record<string, unknown>[][],
): Record<string, unknown>[] {
  return tenantList.flatMap((tenant, i) =>
    (lists[i] ?? []).map((row) => ({ ...row, tenantId: tenant.id, tenantSlug: tenant.slug, tenantName: tenant.name })),
  );
}

/**
 * The tenants an operator can see. Only an operator key may list them, so any refusal means
 * "this key manages its own tenant only" and the page keeps its single-tenant shape.
 */
async function fetchTenants(signal?: AbortSignal): Promise<TenantOption[]> {
  try {
    return normalizeTenantsResponse(await apiClient.apiJson<unknown>('/admin/api/tenants', { signal }));
  } catch (e) {
    if (signal?.aborted) throw e;
    return [];
  }
}

/**
 * Every key the caller can see. The key list is scoped to one tenant. An operator sees every
 * tenant's keys, so the list is asked for once per tenant and merged; anyone else gets their own
 * tenant's as before.
 */
export async function fetchAllKeys(
  signal?: AbortSignal,
): Promise<{ items: Record<string, unknown>[]; tenants: TenantOption[] }> {
  const tenantList = await fetchTenants(signal);
  if (tenantList.length <= 1) {
    const data = await apiClient.apiJson<unknown>('/admin/api/keys', { signal });
    return { items: normalizeKeysListResponse(data), tenants: [] };
  }
  const lists = await Promise.all(
    tenantList.map(async (tenant) =>
      normalizeKeysListResponse(
        await apiClient.apiJson<unknown>('/admin/api/keys', { signal, headers: tenantHeaders(tenant.id) }),
      ),
    ),
  );
  return { items: mergeTenantKeys(tenantList, lists), tenants: tenantList };
}

const resource = createResource<{ items?: Record<string, unknown>[]; tenants?: TenantOption[] }>({
  freshMs: RESOURCE_FRESH_MS.keys,
  fetch: (signal) => fetchAllKeys(signal),
});

/** The tenant header for a request about one listed key; empty when the list is single-tenant. */
function keyTenantHeaders(id: string): Record<string, string> {
  return tenantHeaders(byId[id]?.tenantId);
}

export function useKeysStore() {
  return {
    statusFilter,
    setStatusFilter,
    textFilter,
    setTextFilter: (v: string) => {
      setTextFilter(v);
      applyDebounced(v);
    },
    tenants,
    tenantFilter,
    setTenantFilter,
    filteredRows,
    allRows,
    keyById: (id: string): Record<string, unknown> | undefined => byId[id],
    totalMatches,
    capped,
    phase: () => resource.snapshot().phase,
    sortSpec,
    setSortSpec,
    toggleSort,
    selectedIds,
    toggleSelected,
    clearSelected,
    load: (opts?: { force?: boolean }) => loadKeys(opts),
    revokeKey,
    bulkRevoke,
    archiveKey,
    unarchiveKey,
    deleteKey,
    createKey,
    updateKey,
  };
}

export function toggleSort(key: string): void {
  setSortSpec((prev) => {
    if (prev.key === key) return { key, dir: (prev.dir === 1 ? -1 : 1) as SortSpec['dir'] };
    return { key, dir: -1 };
  });
}

export function toggleSelected(id: string): void {
  setSelectedIds((prev) => {
    const next = new Set(prev);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    return next;
  });
}

export function clearSelected(): void {
  setSelectedIds(new Set<string>());
}

/** The API serialises the role enum as a number; everything downstream compares names. */
function normalizeRole(role: unknown): unknown {
  if (role === 0) return 'Inference';
  if (role === 1) return 'Admin';
  if (role === 2) return 'Both';
  return role;
}

function normalizeKey(row: Record<string, unknown>): Record<string, unknown> {
  const isRevoked = !!(row.isRevoked ?? row.revokedAt ?? String(row.status ?? '').toLowerCase() === 'revoked');
  const isArchived = !!(row.isArchived ?? row.archivedAt ?? String(row.status ?? '').toLowerCase() === 'archived');
  return {
    ...row,
    role: normalizeRole(row.role),
    isRevoked,
    isArchived,
    canArchive: !!(row.canArchive ?? (isRevoked && !isArchived)),
    canDelete: !!row.canDelete,
    canUnarchive: isArchived,
    active: !isRevoked && !isArchived,
  };
}

function syncKeysFromItems(items: Record<string, unknown>[]): void {
  const map: Record<string, Record<string, unknown>> = {};
  const ids: string[] = [];
  for (const row of items.map(normalizeKey)) {
    const id = String(row.id ?? '');
    if (!id) continue;
    map[id] = row;
    ids.push(id);
  }
  setById(map);
  setOrder(ids);
}

function allRows(): Record<string, unknown>[] {
  return order()
    .map((id) => byId[id])
    .filter(Boolean);
}

function filteredRows(): Record<string, unknown>[] {
  const tenant = tenantFilter();
  const rows = tenant ? allRows().filter((row) => row.tenantId === tenant) : allRows();
  return filterKeys(rows, statusFilter(), debouncedText(), sortSpec());
}

function totalMatches(): number {
  return filteredRows().length;
}

function capped(): Record<string, unknown>[] {
  return filteredRows().slice(0, KEYS_RENDER_CAP);
}

export async function revokeKey(id: string): Promise<void> {
  try {
    await apiClient.apiFetch(`/admin/api/keys/${encodeURIComponent(id)}/revoke`, {
      method: 'POST',
      headers: keyTenantHeaders(id),
    });
    await loadKeys({ force: true });
  } catch (e) {
    handleApiError(e, 'keys');
    throw e;
  }
}

export async function bulkRevoke(ids: string[]): Promise<void> {
  if (!ids.length) return;
  try {
    // One request per tenant: the endpoint revokes within a single tenant.
    const byTenant = new Map<string, string[]>();
    for (const id of ids) {
      const tenant = String(byId[id]?.tenantId ?? '');
      byTenant.set(tenant, [...(byTenant.get(tenant) ?? []), id]);
    }
    for (const [tenant, keyIds] of byTenant) {
      await apiClient.apiJson('/admin/api/keys/revoke', {
        method: 'POST',
        headers: tenantHeaders(tenant),
        body: JSON.stringify({ keyIds }),
      });
    }
    pushToast(`${ids.length} key(s) revoked.`);
    clearSelected();
    await loadKeys({ force: true });
  } catch (e) {
    handleApiError(e, 'keys');
    throw e;
  }
}

export async function updateKey(draft: KeyEditDraft): Promise<void> {
  if (!draft.id) return;
  try {
    await apiClient.apiJson(`/admin/api/keys/${encodeURIComponent(draft.id)}`, {
      method: 'PATCH',
      headers: keyTenantHeaders(draft.id),
      body: JSON.stringify({
        label: draft.label || null,
        assignee: draft.assignee || null,
        description: draft.description || null,
        costCenter: draft.costCenter || null,
      }),
    });
    pushToast('API key updated.');
    await loadKeys({ force: true });
  } catch (e) {
    handleApiError(e, 'keys');
    throw e;
  }
}

export async function archiveKey(id: string): Promise<void> {
  try {
    await apiClient.apiFetch(`/admin/api/keys/${id}/archive`, { method: 'POST', headers: keyTenantHeaders(id) });
    await loadKeys({ force: true });
  } catch (e) {
    handleApiError(e, 'keys');
    throw e;
  }
}

export async function unarchiveKey(id: string): Promise<void> {
  try {
    await apiClient.apiFetch(`/admin/api/keys/${id}/unarchive`, { method: 'POST', headers: keyTenantHeaders(id) });
    await loadKeys({ force: true });
  } catch (e) {
    handleApiError(e, 'keys');
    throw e;
  }
}

export async function deleteKey(id: string, confirmKeyPrefix: string): Promise<void> {
  try {
    await apiClient.apiFetch(`/admin/api/keys/${encodeURIComponent(id)}`, {
      method: 'DELETE',
      headers: keyTenantHeaders(id),
      body: JSON.stringify({ confirmKeyPrefix }),
    });
    pushToast('API key deleted permanently. Its history is kept.');
    await loadKeys({ force: true });
  } catch (e) {
    handleApiError(e, 'keys');
    throw e;
  }
}

export async function createKey(draft: NewKeyDraft): Promise<string> {
  try {
    const body = await apiClient.apiJson<{ secret?: string }>('/admin/api/keys', {
      method: 'POST',
      headers: tenantHeaders(draft.tenantId),
      body: JSON.stringify({
        role: draft.role,
        scopes: [],
        label: draft.label || null,
        assignee: draft.assignee || null,
        description: draft.description || null,
        costCenter: draft.costCenter || null,
      }),
    });
    pushToast('API key created — copy the secret now.');
    await loadKeys({ force: true });
    return body?.secret ?? '';
  } catch (e) {
    handleApiError(e, 'keys');
    throw e;
  }
}

export async function loadKeys(opts?: { force?: boolean; background?: boolean }): Promise<void> {
  try {
    const data = await resource.load(opts);
    if (!data) return;
    setTenants(data.tenants ?? []);
    if (!(data.tenants ?? []).some((t) => t.id === tenantFilter())) setTenantFilter('');
    syncKeysFromItems(data.items ?? []);
  } catch (e) {
    handleApiError(e, 'keys');
  }
}

export function activateKeysPage(): void {
  const snap = resource.snapshot();
  if (snap.phase === 'idle' || snap.phase === 'stale') void loadKeys({ background: snap.phase === 'stale' });
}

export function disposeKeysPage(): void {
  resource.abort();
}
