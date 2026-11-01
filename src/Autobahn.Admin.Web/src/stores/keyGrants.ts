import { createMemo, createSignal } from 'solid-js';
import {
  buildGrantChoices,
  buildReplaceGrantsPayload,
  describeKeyAccess,
  normalizeGrantModels,
  normalizePrefix,
  providerPattern,
  type AccessCheckResult,
  type GrantModel,
  type ModelGrantsResponse,
  parseKeyCap,
  type ProviderAccessItem,
} from '../domain/keyGrants';
import { apiClient, handleApiError, pushToast } from './auth';
import { tenantHeaders } from './keys';

const [drawerOpen, setDrawerOpen] = createSignal(false);
const [keyId, setKeyId] = createSignal('');
const [keyLabel, setKeyLabel] = createSignal('');
const [keyTenantId, setKeyTenantId] = createSignal('');
const [selected, setSelected] = createSignal<string[]>([]);
const [patterns, setPatterns] = createSignal<string[]>([]);
const [denied, setDenied] = createSignal<string[]>([]);
// Null when the registry could not be read: it is the operator's, and a tenant's own admin may
// still set its keys' access.
const [registryModels, setRegistryModels] = createSignal<GrantModel[] | null>(null);
// Null when the tenant may call every model.
const [tenantAllowed, setTenantAllowed] = createSignal<string[] | null>(null);
// The paid providers as the gateway holds them for this key. Null when it did not say (a gateway
// without provider switches); the editor then shows the model list alone.
const [providers, setProviders] = createSignal<ProviderAccessItem[] | null>(null);
// The switches as the editor has them; written on save.
const [providerOpen, setProviderOpenState] = createSignal<Record<string, boolean>>({});
// The monthly caps as typed, per provider; blank is no cap of the key's own. Written on save.
const [providerCap, setProviderCapState] = createSignal<Record<string, string>>({});
const [check, setCheck] = createSignal<AccessCheckResult | null>(null);
const [checking, setChecking] = createSignal(false);
const [loading, setLoading] = createSignal(false);
const [saving, setSaving] = createSignal(false);
let loadSeq = 0;

const isOpen = (provider: string) => providerOpen()[provider] ?? false;

const closedProviders = createMemo(() =>
  (providers() ?? []).filter((p) => !isOpen(p.provider)).map((p) => p.provider));

const choices = createMemo(() =>
  buildGrantChoices(registryModels(), selected(), tenantAllowed(), closedProviders()));

/** Every registered model, for the list of models to hold back. */
const deniedChoices = createMemo(() => buildGrantChoices(registryModels(), denied(), null));

const summary = createMemo(() =>
  describeKeyAccess(
    selected(),
    patterns(),
    denied(),
    providers()?.map((p) => ({ provider: p.provider, displayName: p.displayName, open: isOpen(p.provider) })) ?? null,
  ));

export function useKeyGrantsStore() {
  return {
    drawerOpen,
    keyId,
    keyLabel,
    selected,
    setSelected,
    patterns,
    denied,
    setDenied,
    registryModels,
    choices,
    deniedChoices,
    providers,
    isProviderOpen: isOpen,
    providerCap: (provider: string) => providerCap()[provider] ?? '',
    summary,
    check,
    checking,
    loading,
    saving,
    closeDrawer,
    toggleModel,
    saveGrants,
  };
}

/** What is registered and serving now. Read on every open, so the picker follows the registry. */
async function loadRegistryModels(): Promise<GrantModel[] | null> {
  try {
    const [models, backends] = await Promise.all([
      apiClient.apiJson<unknown>('/admin/api/models'),
      // Health only adds a note to a row; the picker works without it.
      apiClient.apiJson<unknown>('/admin/api/backends').catch(() => null),
    ]);
    return Array.isArray(models) ? normalizeGrantModels(models, backends) : null;
  } catch (e) {
    handleApiError(e, 'keys');
    return null;
  }
}

/** The models the key's tenant may call, or null for all of them. A key cannot be given more. */
async function loadTenantAllowed(tenantId: string): Promise<string[] | null> {
  try {
    const body = await apiClient.apiJson<ModelGrantsResponse>(
      '/admin/api/tenant/model-grants',
      { headers: tenantHeaders(tenantId) },
    );
    const ids = body?.modelIds ?? [];
    return body?.usesDefaultAccess || ids.length === 0 ? null : [...ids];
  } catch {
    // Not known: the gateway still refuses a grant outside the tenant's models.
    return null;
  }
}

/** The key's switches for the paid providers, or null when the gateway has none to report. */
async function loadProviders(id: string, tenantId: string): Promise<ProviderAccessItem[] | null> {
  try {
    const body = await apiClient.apiJson<{ providers?: ProviderAccessItem[] }>(
      `/admin/api/keys/${encodeURIComponent(id)}/provider-access`,
      { headers: tenantHeaders(tenantId) },
    );
    return Array.isArray(body?.providers) ? body.providers.filter((p) => p.paid) : null;
  } catch {
    return null;
  }
}

export async function openKeyAccessDrawer(key: Record<string, unknown>): Promise<void> {
  const id = String(key.id ?? '');
  if (!id) return;
  const seq = ++loadSeq;
  const tenantId = typeof key.tenantId === 'string' ? key.tenantId : '';
  setKeyId(id);
  setKeyLabel(String(key.label ?? key.keyPrefix ?? id));
  setKeyTenantId(tenantId);
  resetDraft();
  setDrawerOpen(true);
  setLoading(true);
  try {
    const [registry, allowed, held, body] = await Promise.all([
      loadRegistryModels(),
      loadTenantAllowed(tenantId),
      loadProviders(id, tenantId),
      apiClient.apiJson<ModelGrantsResponse>(
        `/admin/api/keys/${encodeURIComponent(id)}/model-grants`,
        { headers: tenantHeaders(tenantId) },
      ),
    ]);
    if (seq !== loadSeq) return;
    setRegistryModels(registry);
    setTenantAllowed(allowed);
    setProviders(held);
    setProviderOpenState(Object.fromEntries((held ?? []).map((p) => [p.provider, p.open && p.tenantOpen])));
    setProviderCapState(Object.fromEntries(
      (held ?? []).map((p) => [p.provider, p.monthlyCapUsd == null ? '' : String(p.monthlyCapUsd)])));
    setSelected([...(body?.modelIds ?? [])]);
    setPatterns([...(body?.patterns ?? [])]);
    setDenied([...(body?.deniedModelIds ?? [])]);
  } catch (e) {
    if (seq === loadSeq) handleApiError(e, 'keys');
  } finally {
    if (seq === loadSeq) setLoading(false);
  }
}

function resetDraft(): void {
  setSelected([]);
  setPatterns([]);
  setDenied([]);
  setTenantAllowed(null);
  setProviders(null);
  setProviderOpenState({});
  setProviderCapState({});
  setCheck(null);
}

export function closeDrawer(): void {
  loadSeq++;
  setDrawerOpen(false);
  setKeyId('');
  setKeyLabel('');
  setKeyTenantId('');
  resetDraft();
  setLoading(false);
}

export function toggleModel(modelId: string): void {
  const set = new Set(selected());
  if (set.has(modelId)) set.delete(modelId);
  else set.add(modelId);
  setSelected([...set]);
}

export function setProviderOpen(provider: string, open: boolean): void {
  setProviderOpenState({ ...providerOpen(), [provider]: open });
}

export function setProviderCap(provider: string, text: string): void {
  setProviderCapState({ ...providerCap(), [provider]: text });
}

export const hasPattern = (pattern: string) => patterns().some((p) => p.toLowerCase() === pattern.toLowerCase());

export function removePattern(pattern: string): void {
  setPatterns(patterns().filter((p) => p.toLowerCase() !== pattern.toLowerCase()));
  // Nothing is left to hold a model back from.
  if (patterns().length === 0) setDenied([]);
}

/** Adds a prefix entry from what was typed. Returns why not, when it is not one. */
export function addPrefix(text: string): string | null {
  const { pattern, error } = normalizePrefix(text);
  if (!pattern) return error ?? 'Not a prefix.';
  if (!hasPattern(pattern)) setPatterns([...patterns(), pattern]);
  return null;
}

/** Every model of a provider, including ones registered later. */
export function setAllOfProvider(provider: string, on: boolean): void {
  const pattern = providerPattern(provider);
  if (!on) removePattern(pattern);
  else if (!hasPattern(pattern)) setPatterns([...patterns(), pattern]);
}

/** Asks the gateway what it would answer this key for one model, as access is saved now. */
export async function checkModel(model: string): Promise<void> {
  const id = keyId();
  const name = model.trim();
  if (!id || !name) return;
  setChecking(true);
  try {
    setCheck(await apiClient.apiJson<AccessCheckResult>(
      `/admin/api/access/check?keyId=${encodeURIComponent(id)}&model=${encodeURIComponent(name)}`,
      { headers: tenantHeaders(keyTenantId()) },
    ));
  } catch (e) {
    setCheck(null);
    handleApiError(e, 'keys');
  } finally {
    setChecking(false);
  }
}

export async function saveGrants(): Promise<boolean> {
  const id = keyId();
  if (!id) return false;
  setSaving(true);
  try {
    const headers = tenantHeaders(keyTenantId());
    // The switches first, and only the ones that changed: a tenant's own admin may not have a
    // provider to switch, and must still be able to save the list.
    for (const provider of providers() ?? []) {
      const open = isOpen(provider.provider);
      const cap = parseKeyCap(providerCap()[provider.provider] ?? '', provider.tenantMonthlyCapUsd);
      if (cap.error) {
        pushToast(`${provider.displayName}: ${cap.error}`, 'error');
        return false;
      }
      const capChanged = cap.value !== (provider.monthlyCapUsd ?? null);
      if (open === (provider.open && provider.tenantOpen) && !capChanged) continue;
      await apiClient.apiJson(
        `/admin/api/keys/${encodeURIComponent(id)}/provider-access/${encodeURIComponent(provider.provider)}`,
        {
          method: 'PUT',
          headers,
          body: JSON.stringify(
            !capChanged ? { open } : cap.value == null ? { open, clearCap: true } : { open, monthlyCapUsd: cap.value }),
        },
      );
    }
    await apiClient.apiJson(`/admin/api/keys/${encodeURIComponent(id)}/model-grants`, {
      method: 'PUT',
      headers,
      body: JSON.stringify(buildReplaceGrantsPayload(selected(), registryModels(), patterns(), denied())),
    });
    pushToast('Key access updated.');
    closeDrawer();
    return true;
  } catch (e) {
    handleApiError(e, 'keys');
    return false;
  } finally {
    setSaving(false);
  }
}
