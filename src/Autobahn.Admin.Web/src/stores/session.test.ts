import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('./auth', () => ({
  apiClient: { apiJson: vi.fn(), apiFetch: vi.fn() },
  handleApiError: vi.fn(),
  pushToast: vi.fn(),
}));

import { apiClient } from './auth';
import { isTenantAdmin, normalizeSession, setSession, useSession } from './session';
import { loadOwnTenantLimits, normalizeOwnTenantLimits, resetOwnTenantLimits, useOwnTenantLimits } from './tenantLimits';

const tenantAdmin = { tenantId: 't1', tenantSlug: 'workspace', tenantName: 'Workspace', operator: false };

describe('session', () => {
  beforeEach(() => {
    localStorage.clear();
    setSession(null);
  });

  it('readsTheTenantAndTheOperatorFlag', () => {
    expect(normalizeSession(tenantAdmin)).toEqual(tenantAdmin);
    expect(normalizeSession({ ...tenantAdmin, tenantName: '' })?.tenantName).toBe('workspace');
  });

  it('refusesABodyThatDoesNotSayWhetherTheKeyIsAnOperator', () => {
    expect(normalizeSession({ tenantId: 't1' })).toBeNull();
    expect(normalizeSession(null)).toBeNull();
  });

  it('treatsAnUnknownKeyAsAnOperatorUntilTheGatewayAnswers', () => {
    expect(isTenantAdmin()).toBe(false);
    setSession(normalizeSession(tenantAdmin));
    expect(isTenantAdmin()).toBe(true);
    setSession(normalizeSession({ ...tenantAdmin, operator: true }));
    expect(isTenantAdmin()).toBe(false);
  });

  it('keepsTheSessionBesideTheKeyAndForgetsItOnSignOut', () => {
    setSession(normalizeSession(tenantAdmin));
    expect(JSON.parse(localStorage.getItem('Autobahn-admin-session') || 'null')).toEqual(tenantAdmin);
    setSession(null);
    expect(localStorage.getItem('Autobahn-admin-session')).toBeNull();
    expect(useSession()()).toBeNull();
  });
});

describe('own tenant limits', () => {
  const body = {
    tenant: { id: 't1', slug: 'workspace', name: 'Workspace', planSlug: null },
    version: 14,
    enabled: true,
    default: { rpm: 300, burst: 15, maxConcurrentStreams: 30, tpm: 0 },
    plans: {},
    rules: [{ scope: 'api_key', target: 'k1', rpm: 60, burst: 10, maxConcurrentStreams: 32, tpm: 2000000, enabled: true, schedule: [] }],
  };

  beforeEach(() => {
    vi.mocked(apiClient.apiJson).mockReset();
    resetOwnTenantLimits();
  });

  it('readsTheTenantAndItsPartOfTheConfiguration', () => {
    const own = normalizeOwnTenantLimits(body);
    expect(own?.tenant).toEqual({ id: 't1', slug: 'workspace', name: 'Workspace', planSlug: null });
    expect(own?.config.default.rpm).toBe(300);
    expect(own?.config.rules[0]).toMatchObject({ scope: 'api_key', target: 'k1', tpm: 2000000 });
  });

  it('isNothingWithoutATenant', () => {
    expect(normalizeOwnTenantLimits({ ...body, tenant: undefined })).toBeNull();
  });

  it('loadsFromTheTenantScopedRoute', async () => {
    vi.mocked(apiClient.apiJson).mockResolvedValue(body);
    await loadOwnTenantLimits();
    expect(apiClient.apiJson).toHaveBeenCalledWith('/admin/api/tenant/rate-limits');
    expect(useOwnTenantLimits().limits()?.tenant.slug).toBe('workspace');
  });

  it('keepsTheMessageWhenTheGatewayRefuses', async () => {
    vi.mocked(apiClient.apiJson).mockRejectedValue({ status: 403, message: 'Not permitted', global: false });
    await loadOwnTenantLimits();
    expect(useOwnTenantLimits().loadError()).toBe('Not permitted');
    expect(useOwnTenantLimits().limits()).toBeNull();
  });
});
