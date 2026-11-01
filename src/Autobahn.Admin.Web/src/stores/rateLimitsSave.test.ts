import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('./auth', () => ({
  apiClient: { apiJson: vi.fn(), apiJsonWithMeta: vi.fn() },
  handleApiError: vi.fn(),
  pushToast: vi.fn(),
}));

import { apiClient } from './auth';
import { activateRateLimitsPage, loadRateLimits, saveRateLimits, useRateLimitsStore } from './rateLimits';

const stored = (version: number, rules: unknown[]) => ({
  version,
  enabled: true,
  adaptiveEnabled: false,
  writable: true,
  default: { rpm: 300, burst: 15, maxConcurrentStreams: 30, tpm: 0 },
  plans: { standard: { rpm: 120, burst: 10, maxConcurrentStreams: 10, tpm: 0 } },
  rules,
});

const keyRule = { scope: 'api_key', target: 'k1', rpm: 60, burst: 5, maxConcurrentStreams: 0, tpm: 0, enabled: true, schedule: [] };

describe('saveRateLimits', () => {
  beforeEach(() => {
    vi.mocked(apiClient.apiJson).mockReset().mockResolvedValue(null);
    vi.mocked(apiClient.apiJsonWithMeta).mockReset();
  });

  // The save answers `{ message, version }`. Read as a configuration, that answer emptied the page.
  it('keepsWhatWasSentAsTheSavedConfigurationWhenTheSaveAnswersWithoutOne', async () => {
    const store = useRateLimitsStore();
    let reloads = 0;
    vi.mocked(apiClient.apiJsonWithMeta).mockImplementation((async (_url: string, options?: RequestInit) => {
      if (options?.method === 'PUT') return { data: { message: 'Saved.', version: 8 }, etag: 'W/"8"' };
      // A reload that never arrives: what the store holds until then is under test.
      if (reloads++ > 0) return new Promise(() => {});
      return { data: stored(7, [keyRule]), etag: 'W/"7"' };
    }) as never);

    activateRateLimitsPage();
    await loadRateLimits();
    store.addRule({ scope: 'tenant', target: 'acme', rpm: 42, burst: 4 });
    expect(store.dirty()).toBe(true);

    await saveRateLimits();

    const put = vi.mocked(apiClient.apiJsonWithMeta).mock.calls.find(([, o]) => (o as RequestInit)?.method === 'PUT');
    expect((put?.[1] as RequestInit).headers).toEqual({ 'If-Match': 'W/"7"' });
    expect(store.dirty()).toBe(false);
    expect(store.saved()?.version).toBe(8);
    expect(store.saved()?.default.rpm).toBe(300);
    expect(Object.keys(store.saved()?.plans ?? {})).toEqual(['standard']);
    expect(store.draft()?.rules.map((r) => `${r.scope}:${r.target}`)).toEqual(['api_key:k1', 'tenant:acme']);
  });
});
