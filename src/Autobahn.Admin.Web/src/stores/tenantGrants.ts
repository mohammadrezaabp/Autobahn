import { createSignal } from 'solid-js';
import type { ProviderAccessItem } from '../domain/keyGrants';
import { apiClient, handleApiError, pushToast } from './auth';
import { tenantHeaders } from './keys';

const [restricted, setRestricted] = createSignal(false);
const [selected, setSelected] = createSignal<string[]>([]);
const [registryModels, setRegistryModels] = createSignal<Array<{ id: string }>>([]);
// The paid providers and whether this tenant is opened for each. Empty when the gateway has none.
const [providers, setProviders] = createSignal<ProviderAccessItem[]>([]);
const [loading, setLoading] = createSignal(false);
const [saving, setSaving] = createSignal(false);
// The tenant the grants on screen belong to. Empty is the caller's own tenant.
let currentTenantId = '';
let loadSeq = 0;

export function useTenantGrantsStore() {
  return { restricted, selected, registryModels, providers, loading, saving, toggleRestricted, toggleModel, saveGrants: saveTenantGrants };
}

async function loadRegistryModels(): Promise<void> {
  const data = await apiClient.apiJson<Array<{ model?: { id?: string }; id?: string }>>('/admin/api/models');
  const list = Array.isArray(data) ? data : [];
  setRegistryModels(
    list
      .map((item) => {
        const m = (item.model as { id?: string } | undefined) ?? item;
        return { id: String(m.id ?? '') };
      })
      .filter((m) => m.id),
  );
}

/** Loads one tenant's model access. Without a tenant id it is the caller's own tenant. */
export async function loadTenantGrants(tenantId = ''): Promise<void> {
  currentTenantId = tenantId;
  const seq = ++loadSeq;
  setLoading(true);
  try {
    if (!registryModels().length) await loadRegistryModels();
    const body = await apiClient.apiJson<{ modelIds?: string[]; usesDefaultAccess?: boolean }>(
      '/admin/api/tenant/model-grants',
      { headers: tenantHeaders(tenantId) },
    );
    // Not part of the card's own load: a gateway without provider switches shows no block.
    const access = await apiClient
      .apiJson<{ providers?: ProviderAccessItem[] }>('/admin/api/tenant/provider-access', { headers: tenantHeaders(tenantId) })
      .catch(() => null);
    if (seq !== loadSeq) return;
    const ids = body?.modelIds ?? [];
    setRestricted(!(body?.usesDefaultAccess ?? ids.length === 0));
    setSelected([...ids]);
    setProviders(Array.isArray(access?.providers) ? access.providers.filter((p) => p.paid) : []);
  } catch (e) {
    if (seq === loadSeq) handleApiError(e, 'settings');
  } finally {
    if (seq === loadSeq) setLoading(false);
  }
}

/** Opens or closes a paid provider for the tenant on screen. The gateway lets only an operator. */
export async function setTenantProviderOpen(provider: string, open: boolean): Promise<void> {
  setSaving(true);
  try {
    const body = await apiClient.apiJson<{ providers?: ProviderAccessItem[] }>(
      `/admin/api/tenant/provider-access/${encodeURIComponent(provider)}`,
      { method: 'PUT', headers: tenantHeaders(currentTenantId), body: JSON.stringify({ open }) },
    );
    setProviders((body?.providers ?? []).filter((p) => p.paid));
    pushToast(open ? 'Provider opened for the tenant.' : 'Provider closed for the tenant.');
  } catch (e) {
    handleApiError(e, 'settings');
  } finally {
    setSaving(false);
  }
}

export function toggleRestricted(on: boolean): void {
  setRestricted(on);
  if (!on) setSelected([]);
}

export function toggleModel(modelId: string): void {
  const set = new Set(selected());
  if (set.has(modelId)) set.delete(modelId);
  else set.add(modelId);
  setSelected([...set]);
}

export async function saveTenantGrants(): Promise<boolean> {
  setSaving(true);
  try {
    const payload = restricted()
      ? { modelIds: selected() }
      : { modelIds: [], allowAllModels: true };
    await apiClient.apiJson('/admin/api/tenant/model-grants', {
      method: 'PUT',
      headers: tenantHeaders(currentTenantId),
      body: JSON.stringify(payload),
    });
    pushToast('Tenant model access updated.');
    return true;
  } catch (e) {
    handleApiError(e, 'settings');
    return false;
  } finally {
    setSaving(false);
  }
}
