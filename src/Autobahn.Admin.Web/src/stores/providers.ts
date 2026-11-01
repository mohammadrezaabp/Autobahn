import { createSignal } from 'solid-js';
import type {
  ProviderCatalogItem,
  ProviderOverview,
  ProviderReconciliation,
  ProviderSpendReport,
  ProviderTenantRow,
} from '../domain/providers';
import { apiClient, handleApiError, pushToast } from './auth';

const [providers, setProviders] = createSignal<ProviderOverview[]>([]);
const [loaded, setLoaded] = createSignal(false);
const [loading, setLoading] = createSignal(false);
const [loadError, setLoadError] = createSignal('');

// The tabs of the provider on screen. Each is loaded when its tab is first opened.
const [tenants, setTenants] = createSignal<ProviderTenantRow[] | null>(null);
const [spend, setSpend] = createSignal<ProviderSpendReport | null>(null);
const [spendDays, setSpendDays] = createSignal(30);
const [reconciliation, setReconciliation] = createSignal<ProviderReconciliation | null>(null);
const [catalog, setCatalog] = createSignal<ProviderCatalogItem[] | null>(null);
const [catalogTotal, setCatalogTotal] = createSignal(0);
const [catalogError, setCatalogError] = createSignal('');
const [catalogLoading, setCatalogLoading] = createSignal(false);
const [busy, setBusy] = createSignal(false);
let catalogSeq = 0;

const base = (provider: string) => `/admin/api/providers/${encodeURIComponent(provider)}`;

export function useProvidersStore() {
  return {
    providers,
    loaded,
    loading,
    loadError,
    tenants,
    spend,
    spendDays,
    reconciliation,
    catalog,
    catalogTotal,
    catalogError,
    catalogLoading,
    busy,
  };
}

export const findProvider = (id: string) => providers().find((p) => p.provider === id) ?? null;

export async function loadProviders(): Promise<void> {
  setLoading(true);
  setLoadError('');
  try {
    const body = await apiClient.apiJson<{ providers?: ProviderOverview[] }>('/admin/api/providers');
    setProviders(Array.isArray(body?.providers) ? body.providers : []);
    setLoaded(true);
  } catch (e) {
    setLoadError((e as { message?: string })?.message || 'Could not load the providers.');
  } finally {
    setLoading(false);
  }
}

/** Forgets the tabs of the provider that was on screen. */
export function resetProviderDetail(): void {
  catalogSeq++;
  setTenants(null);
  setSpend(null);
  setReconciliation(null);
  setCatalog(null);
  setCatalogTotal(0);
  setCatalogError('');
}

export async function loadProviderTenants(provider: string): Promise<void> {
  try {
    const body = await apiClient.apiJson<{ tenants?: ProviderTenantRow[] }>(`${base(provider)}/access`);
    setTenants(Array.isArray(body?.tenants) ? body.tenants : []);
  } catch (e) {
    handleApiError(e, 'providers');
  }
}

/** Opens or closes a tenant and sets its cap in one write. A null cap removes it. */
export async function saveProviderTenant(
  provider: string,
  tenantId: string,
  open: boolean,
  monthlyCapUsd: number | null,
): Promise<boolean> {
  setBusy(true);
  try {
    const body = await apiClient.apiJson<{ tenants?: ProviderTenantRow[] }>(
      `${base(provider)}/access/${encodeURIComponent(tenantId)}`,
      {
        method: 'PUT',
        body: JSON.stringify(monthlyCapUsd == null ? { open, clearCap: true } : { open, monthlyCapUsd }),
      },
    );
    setTenants(Array.isArray(body?.tenants) ? body.tenants : []);
    pushToast('Tenant access saved.');
    void loadProviders();
    return true;
  } catch (e) {
    handleApiError(e, 'providers');
    return false;
  } finally {
    setBusy(false);
  }
}

export async function loadProviderSpend(provider: string, days = spendDays()): Promise<void> {
  setSpendDays(days);
  try {
    setSpend(await apiClient.apiJson<ProviderSpendReport>(`${base(provider)}/spend?days=${days}`));
  } catch (e) {
    handleApiError(e, 'providers');
  }
}

/** This month's reconciliation. A failure leaves the card out rather than raising a page error. */
export async function loadProviderReconciliation(provider: string): Promise<void> {
  try {
    setReconciliation(await apiClient.apiJson<ProviderReconciliation>(`${base(provider)}/reconciliation`));
  } catch {
    setReconciliation(null);
  }
}

export async function searchCatalog(provider: string, query: string): Promise<void> {
  const seq = ++catalogSeq;
  setCatalogLoading(true);
  setCatalogError('');
  try {
    const body = await apiClient.apiJson<{ models?: ProviderCatalogItem[]; total?: number }>(
      `${base(provider)}/catalog?q=${encodeURIComponent(query.trim())}`,
    );
    if (seq !== catalogSeq) return;
    setCatalog(Array.isArray(body?.models) ? body.models : []);
    setCatalogTotal(Number(body?.total ?? 0));
  } catch (e) {
    if (seq !== catalogSeq) return;
    // Said on the tab, not as a page error: a missing key is the normal state of a new provider.
    setCatalog(null);
    setCatalogError((e as { message?: string })?.message || 'The catalogue could not be read.');
  } finally {
    if (seq === catalogSeq) setCatalogLoading(false);
  }
}

/** Registers catalogue models. Returns the ids that were added. */
export async function addCatalogModels(provider: string, ids: string[]): Promise<string[]> {
  if (!ids.length) return [];
  setBusy(true);
  try {
    const body = await apiClient.apiJson<{ results?: Array<{ id: string; added: boolean; message: string }> }>(
      `${base(provider)}/catalog/add`,
      { method: 'POST', body: JSON.stringify({ models: ids }) },
    );
    const results = body?.results ?? [];
    const added = results.filter((r) => r.added).map((r) => r.id);
    const refused = results.filter((r) => !r.added);
    if (added.length) pushToast(added.length === 1 ? `Added ${added[0]}.` : `Added ${added.length} models.`);
    if (refused.length) pushToast(`${refused[0].id}: ${refused[0].message}`, 'error');
    setCatalog((list) => list?.map((m) => (added.includes(m.id) ? { ...m, added: true } : m)) ?? null);
    void loadProviders();
    return added;
  } catch (e) {
    handleApiError(e, 'providers');
    return [];
  } finally {
    setBusy(false);
  }
}

export async function saveProviderSettings(
  provider: string,
  lowBalanceUsd: number | null,
  newModelsStartStopped: boolean,
): Promise<boolean> {
  return write(`${base(provider)}/settings`, 'PUT', { lowBalanceUsd, newModelsStartStopped }, 'Settings saved.');
}

export async function replaceProviderKey(provider: string, apiKey: string): Promise<boolean> {
  return write(`${base(provider)}/key`, 'PUT', { apiKey }, 'Key replaced.');
}

export async function clearProviderKey(provider: string): Promise<boolean> {
  return write(`${base(provider)}/key`, 'DELETE', null, 'Stored key removed.');
}

export async function refreshProviderBalance(provider: string): Promise<void> {
  setBusy(true);
  try {
    const item = await apiClient.apiJson<ProviderOverview>(`${base(provider)}/balance/refresh`, { method: 'POST', body: '{}' });
    if (item?.provider) setProviders((list) => list.map((p) => (p.provider === item.provider ? item : p)));
  } catch (e) {
    handleApiError(e, 'providers');
  } finally {
    setBusy(false);
  }
}

/** The settings, key and clear writes all answer with the provider list as it now stands. */
async function write(url: string, method: string, payload: unknown, done: string): Promise<boolean> {
  setBusy(true);
  try {
    const body = await apiClient.apiJson<{ providers?: ProviderOverview[] }>(url, {
      method,
      ...(payload == null ? {} : { body: JSON.stringify(payload) }),
    });
    if (Array.isArray(body?.providers)) setProviders(body.providers);
    pushToast(done);
    return true;
  } catch (e) {
    handleApiError(e, 'providers');
    return false;
  } finally {
    setBusy(false);
  }
}
