import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('./auth', () => ({
  apiClient: { apiJson: vi.fn(), apiFetch: vi.fn() },
  handleApiError: vi.fn(),
  pushToast: vi.fn(),
}));

import { apiClient, pushToast } from './auth';
import {
  loadTenants,
  moveKeysToTenant,
  normalizeMoveResult,
  normalizeTenantList,
  resetTenants,
  updateTenant,
  useTenantsStore,
  validateTenantSlug,
} from './tenants';

const row = { id: 't1', slug: 'acme', name: 'Acme', planSlug: null, costCenter: null, isActive: true, isOperatorTenant: false, keyCount: 3, activeKeyCount: 2 };

describe('tenants store', () => {
  beforeEach(() => {
    vi.mocked(apiClient.apiJson).mockReset();
    vi.mocked(pushToast).mockReset();
    resetTenants();
  });

  it('normalizesTheListAndDropsRowsWithoutAnId', () => {
    const list = normalizeTenantList([row, { slug: 'no-id' }, null]);
    expect(list).toHaveLength(1);
    expect(list[0]).toMatchObject({ id: 't1', planSlug: '', costCenter: '', keyCount: 3, activeKeyCount: 2 });
  });

  it('loadsTenantsAndMarksTheKeyAsAnOperator', async () => {
    vi.mocked(apiClient.apiJson).mockResolvedValue([row]);
    await loadTenants();
    const store = useTenantsStore();
    expect(store.operator()).toBe(true);
    expect(store.byId('t1')?.slug).toBe('acme');
  });

  // Only an operator key may list tenants; any other admin key is refused with 403.
  it('readsARefusalAsNotAnOperatorRatherThanAsAnError', async () => {
    vi.mocked(apiClient.apiJson).mockRejectedValue({ status: 403, message: 'Forbidden' });
    await loadTenants();
    const store = useTenantsStore();
    expect(store.operator()).toBe(false);
    expect(store.loadError()).toBe('');
    expect(store.loaded()).toBe(true);
  });

  it('keepsAFailureThatIsNotARefusalAsAnError', async () => {
    vi.mocked(apiClient.apiJson).mockRejectedValue({ status: 500, message: 'boom' });
    await loadTenants();
    expect(useTenantsStore().loadError()).toBe('boom');
    expect(useTenantsStore().operator()).toBeNull();
  });

  it('clearsThePlanAndCostCenterWithNullOnUpdate', async () => {
    vi.mocked(apiClient.apiJson).mockResolvedValue([]);
    await updateTenant('t1', { name: ' Acme ', planSlug: '', costCenter: ' ' });
    expect(apiClient.apiJson).toHaveBeenCalledWith(
      '/admin/api/tenants/t1',
      expect.objectContaining({ method: 'PUT', body: JSON.stringify({ name: 'Acme', planSlug: null, costCenter: null }) }),
    );
  });

  it('reportsEachKeyThatWasNotMoved', async () => {
    vi.mocked(apiClient.apiJson).mockImplementation(async (path: string) =>
      path.endsWith('/keys/move')
        ? { moved: [{ id: 'k1' }], skipped: [{ id: 'k2', code: 'last_admin_key', message: 'It is the last admin key.' }] }
        : [],
    );
    const result = await moveKeysToTenant('t2', ['k1', 'k2']);
    expect(result.moved).toBe(1);
    expect(pushToast).toHaveBeenCalledWith('1 key(s) moved.');
    expect(pushToast).toHaveBeenCalledWith('It is the last admin key.', 'error');
  });

  it('readsAnEmptyMoveAnswerAsNothingMoved', () => {
    expect(normalizeMoveResult(null)).toEqual({ moved: 0, skipped: [] });
  });
});

describe('validateTenantSlug', () => {
  it('acceptsWhatTheGatewayAccepts', () => {
    expect(validateTenantSlug('demo-app')).toBe('');
    expect(validateTenantSlug('a')).toBe('');
    expect(validateTenantSlug('team2')).toBe('');
  });

  it('refusesWhatTheGatewayRefuses', () => {
    expect(validateTenantSlug('')).not.toBe('');
    expect(validateTenantSlug('Easy')).not.toBe('');
    expect(validateTenantSlug('-lead')).not.toBe('');
    expect(validateTenantSlug('trail-')).not.toBe('');
    expect(validateTenantSlug('has space')).not.toBe('');
    expect(validateTenantSlug('x'.repeat(65))).not.toBe('');
  });
});
