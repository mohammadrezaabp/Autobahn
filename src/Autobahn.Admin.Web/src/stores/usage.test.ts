import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('./auth', () => ({
  apiClient: { apiJson: vi.fn() },
  handleApiError: vi.fn(),
  pushToast: vi.fn(),
}));

import { apiClient } from './auth';
import { applyHashUsageParams, loadReport, usageParamsFrom, usagePresetRange, usageRangeError, usageSnapshot, useUsageStore } from './usage';

describe('usage store', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.mocked(apiClient.apiJson).mockReset();
    const store = useUsageStore();
    store.setModelId('');
    store.setApiKeyId('');
    store.setCostCenter('');
    store.setFrom('2026-10-01');
    store.setTo('2026-10-19');
  });

  it('usagePresetRange_lastSevenDays', () => {
    const { from, to } = usagePresetRange(7);
    expect(from <= to).toBe(true);
    const days = (Date.parse(to) - Date.parse(from)) / 86400000 + 1;
    expect(days).toBe(7);
  });

  it('usageRangeError_rejectsInvertedRange', () => {
    expect(usageRangeError('2026-01-10', '2026-01-01')).toContain('From');
  });

  it('applyHashUsageParams_setsKeyFilter', async () => {
    const { useUsageStore } = await import('./usage');
    applyHashUsageParams({ apiKeyId: 'key-123', costCenter: 'eng' });
    const store = useUsageStore();
    expect(store.apiKeyId()).toBe('key-123');
    expect(store.costCenter()).toBe('eng');
    expect(usageSnapshot().apiKeyId).toBe('key-123');
  });

  it('usageParamsFrom_asksForEveryTenant', () => {
    const q = new URLSearchParams(
      usageParamsFrom({ from: '2026-10-01', to: '2026-10-11', costCenter: '', apiKeyId: 'k1', modelId: '', includeAnonymous: false }),
    );
    expect(q.get('allTenants')).toBe('true');
    expect(q.get('apiKeyId')).toBe('k1');
    expect(q.has('includeAnonymous')).toBe(false);
  });

  it('keeps models beyond the table cap available after filtering', async () => {
    const rows = [
      ...Array.from({ length: 100 }, () => ({ modelId: 'older-model' })),
      { modelId: 'DeepSeek-V4.1-Flash' },
    ];
    vi.mocked(apiClient.apiJson).mockImplementation(async (path) =>
      String(path).startsWith('/admin/api/usage?') ? { rollups: rows } : {},
    );
    await loadReport();
    const store = useUsageStore();
    expect(store.rollup()).toHaveLength(100);
    expect(store.rollup().some((row) => row.modelId === 'DeepSeek-V4.1-Flash')).toBe(false);
    expect(store.reportModelIds()).toEqual(['DeepSeek-V4.1-Flash', 'older-model']);

    store.setModelId('DeepSeek-V4.1-Flash');
    vi.mocked(apiClient.apiJson).mockImplementation(async (path) =>
      String(path).startsWith('/admin/api/usage?') ? { rollups: [{ modelId: 'DeepSeek-V4.1-Flash' }] } : {},
    );
    await loadReport();
    expect(store.reportModelIds()).toContain('older-model');

    store.setModelId('');
    await loadReport();
    expect(store.reportModelIds()).toEqual(['DeepSeek-V4.1-Flash']);
  });
});
