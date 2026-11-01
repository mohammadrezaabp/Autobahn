import { createSignal } from 'solid-js';
import { createResource, RESOURCE_FRESH_MS } from '../realtime/resources';
import { apiClient, handleApiError, pushToast } from './auth';

export type SettingsSubTab = 'runtime' | 'cors' | 'observability';

const [subTab, setSubTab] = createSignal<SettingsSubTab>('runtime');
const [configStatus, setConfigStatus] = createSignal<Record<string, unknown> | null>(null);

const resource = createResource<Record<string, unknown>>({
  freshMs: RESOURCE_FRESH_MS.settings,
  fetch: async (signal) => {
    const data = await apiClient.apiJson<Record<string, unknown>>('/admin/api/config/status', { signal });
    return data ?? {};
  },
});

export function useSettingsStore() {
  return {
    subTab,
    setSubTab,
    configStatus,
    phase: () => resource.snapshot().phase,
    load: (opts?: { force?: boolean }) => loadSettings(opts),
  };
}

export async function loadSettings(opts?: { force?: boolean; background?: boolean }): Promise<void> {
  try {
    const data = await resource.load(opts);
    if (data) setConfigStatus(data);
  } catch (e) {
    handleApiError(e, 'settings');
  }
}

export function activateSettingsPage(): void {
  const snap = resource.snapshot();
  if (snap.phase === 'idle' || snap.phase === 'stale') void loadSettings({ background: snap.phase === 'stale' });
}

export function disposeSettingsPage(): void {
  resource.abort();
}

export async function reloadConfigFromDisk(): Promise<void> {
  try {
    const body = await apiClient.apiJson<{ message?: string; status?: string }>('/admin/api/config/reload', {
      method: 'POST',
    });
    pushToast(body?.message || 'Config reloaded from disk.');
    await loadSettings({ force: true });
  } catch (e) {
    handleApiError(e, 'settings');
    throw e;
  }
}
