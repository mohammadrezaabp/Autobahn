import { createSignal } from 'solid-js';
import { apiClient, handleApiError, pushToast } from './auth';
import { loadKeys } from './keys';

export interface Tenant {
  id: string;
  slug: string;
  name: string;
  planSlug: string;
  costCenter: string;
  isActive: boolean;
  /** Admin keys of this tenant are operators: they manage every tenant. */
  isOperatorTenant: boolean;
  createdAt: string;
  updatedAt: string;
  /** Keys the tenant owns, archived ones excluded. */
  keyCount: number;
  activeKeyCount: number;
}

export interface TenantDraft {
  slug: string;
  name: string;
  planSlug: string;
  costCenter: string;
}

export const EMPTY_TENANT_DRAFT: TenantDraft = { slug: '', name: '', planSlug: '', costCenter: '' };

export interface MoveKeysResult {
  moved: number;
  skipped: Array<{ id: string; code: string; message: string }>;
}

const SLUG_RE = /^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/;

/** Empty when the slug can be sent; otherwise what is wrong with it. */
export function validateTenantSlug(slug: string): string {
  const value = slug.trim();
  if (!value) return 'A tenant needs a slug.';
  if (value.length > 64) return 'A slug can be at most 64 characters.';
  if (!SLUG_RE.test(value)) return 'Slug: lowercase letters, digits and hyphens, starting and ending with a letter or digit.';
  return '';
}

export function normalizeTenant(raw: unknown): Tenant | null {
  if (!raw || typeof raw !== 'object') return null;
  const t = raw as Record<string, unknown>;
  if (!t.id) return null;
  return {
    id: String(t.id),
    slug: String(t.slug ?? ''),
    name: String(t.name ?? t.slug ?? ''),
    planSlug: String(t.planSlug ?? ''),
    costCenter: String(t.costCenter ?? ''),
    isActive: t.isActive !== false,
    isOperatorTenant: t.isOperatorTenant === true,
    createdAt: String(t.createdAt ?? ''),
    updatedAt: String(t.updatedAt ?? ''),
    keyCount: Number(t.keyCount ?? 0),
    activeKeyCount: Number(t.activeKeyCount ?? 0),
  };
}

export function normalizeTenantList(data: unknown): Tenant[] {
  if (!Array.isArray(data)) return [];
  return data.map(normalizeTenant).filter((t): t is Tenant => t !== null);
}

export function normalizeMoveResult(data: unknown): MoveKeysResult {
  const body = (data ?? {}) as { moved?: unknown; skipped?: unknown };
  const skipped = Array.isArray(body.skipped) ? (body.skipped as Array<Record<string, unknown>>) : [];
  return {
    moved: Array.isArray(body.moved) ? body.moved.length : 0,
    skipped: skipped.map((s) => ({
      id: String(s.id ?? ''),
      code: String(s.code ?? ''),
      message: String(s.message ?? ''),
    })),
  };
}

const [tenants, setTenants] = createSignal<Tenant[]>([]);
const [loading, setLoading] = createSignal(false);
const [loaded, setLoaded] = createSignal(false);
const [loadError, setLoadError] = createSignal('');
// Only an operator key may list tenants. Any other admin key manages its own tenant only.
const [operator, setOperator] = createSignal<boolean | null>(null);

let seq = 0;

export function useTenantsStore() {
  return {
    tenants,
    loading,
    loaded,
    loadError,
    operator,
    byId: (id: string) => tenants().find((t) => t.id === id) ?? null,
    load: (opts?: { force?: boolean }) => loadTenants(opts),
  };
}

export async function loadTenants(opts?: { force?: boolean }): Promise<void> {
  if (loading() && !opts?.force) return;
  if (loaded() && !opts?.force) return;
  const mine = ++seq;
  setLoading(true);
  try {
    const data = await apiClient.apiJson<unknown>('/admin/api/tenants');
    if (mine !== seq) return;
    setTenants(normalizeTenantList(data));
    setOperator(true);
    setLoadError('');
    setLoaded(true);
  } catch (e) {
    if (mine !== seq) return;
    const err = e as { status?: number; message?: string; global?: boolean };
    if (err.status === 403 || err.status === 404) {
      setOperator(false);
      setTenants([]);
      setLoadError('');
      setLoaded(true);
    } else {
      setLoadError(err.message || 'Could not load tenants.');
      if (err.global) handleApiError(e);
    }
  } finally {
    if (mine === seq) setLoading(false);
  }
}

export async function createTenant(draft: TenantDraft): Promise<Tenant | null> {
  try {
    const body = await apiClient.apiJson<unknown>('/admin/api/tenants', {
      method: 'POST',
      body: JSON.stringify({
        slug: draft.slug.trim(),
        name: draft.name.trim() || draft.slug.trim(),
        planSlug: draft.planSlug || null,
        costCenter: draft.costCenter.trim() || null,
      }),
    });
    pushToast('Tenant created.');
    await loadTenants({ force: true });
    return normalizeTenant(body);
  } catch (e) {
    handleApiError(e, 'tenants');
    throw e;
  }
}

/** Replaces the tenant's editable fields as a set; an empty plan or cost center clears it. */
export async function updateTenant(id: string, draft: Omit<TenantDraft, 'slug'>): Promise<void> {
  try {
    await apiClient.apiJson(`/admin/api/tenants/${encodeURIComponent(id)}`, {
      method: 'PUT',
      body: JSON.stringify({
        name: draft.name.trim(),
        planSlug: draft.planSlug || null,
        costCenter: draft.costCenter.trim() || null,
      }),
    });
    pushToast('Tenant updated.');
    await loadTenants({ force: true });
  } catch (e) {
    handleApiError(e, 'tenants');
    throw e;
  }
}

export async function moveKeysToTenant(targetTenantId: string, keyIds: string[]): Promise<MoveKeysResult> {
  try {
    const body = await apiClient.apiJson<unknown>(
      `/admin/api/tenants/${encodeURIComponent(targetTenantId)}/keys/move`,
      { method: 'POST', body: JSON.stringify({ keyIds }) },
    );
    const result = normalizeMoveResult(body);
    if (result.moved > 0) pushToast(`${result.moved} key(s) moved.`);
    for (const skip of result.skipped) pushToast(skip.message || `A key was not moved (${skip.code}).`, 'error');
    await Promise.all([loadTenants({ force: true }), loadKeys({ force: true })]);
    return result;
  } catch (e) {
    handleApiError(e, 'tenants');
    throw e;
  }
}

/** Forget what the previous key could see. */
export function resetTenants(): void {
  seq += 1;
  setTenants([]);
  setLoaded(false);
  setOperator(null);
  setLoadError('');
  setLoading(false);
}
