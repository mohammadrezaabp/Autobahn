import { describe, expect, it } from 'vitest';
import {
  KEYS_RENDER_CAP,
  mergeTenantKeys,
  normalizeKeysListResponse,
  normalizeTenantsResponse,
  tenantHeaders,
} from './keys';

describe('keys store constants', () => {
  it('renderCap_isFifty', () => {
    expect(KEYS_RENDER_CAP).toBe(50);
  });
});

describe('normalizeKeysListResponse', () => {
  it('acceptsBareArrayFromGateway', () => {
    const rows = [{ id: '1', label: 'a' }];
    expect(normalizeKeysListResponse(rows)).toEqual(rows);
  });

  it('acceptsItemsWrapper', () => {
    const rows = [{ id: '2', label: 'b' }];
    expect(normalizeKeysListResponse({ items: rows })).toEqual(rows);
  });

  it('returnsEmptyForUnexpectedShape', () => {
    expect(normalizeKeysListResponse(null)).toEqual([]);
    expect(normalizeKeysListResponse({})).toEqual([]);
  });
});

describe('keys render cap slice', () => {
  it('capsFilteredListAtFifty', () => {
    const filtered = Array.from({ length: 120 }, (_, i) => ({ id: String(i) }));
    const capped = filtered.slice(0, KEYS_RENDER_CAP);
    expect(capped).toHaveLength(50);
    expect(filtered.length).toBe(120);
  });
});

describe('keys across tenants', () => {
  const tenants = [
    { id: 't-default', slug: 'default', name: 'Default Tenant' },
    { id: 't-easy', slug: 'easy', name: 'Easy' },
  ];

  it('mergeTenantKeys_tagsEveryRowWithTheTenantItCameFrom', () => {
    const rows = mergeTenantKeys(tenants, [[{ id: '1' }], [{ id: '2' }, { id: '3' }]]);
    expect(rows.map((r) => [r.id, r.tenantId, r.tenantSlug])).toEqual([
      ['1', 't-default', 'default'],
      ['2', 't-easy', 'easy'],
      ['3', 't-easy', 'easy'],
    ]);
  });

  it('mergeTenantKeys_toleratesATenantWithNoList', () => {
    expect(mergeTenantKeys(tenants, [[{ id: '1' }]])).toHaveLength(1);
  });

  it('tenantHeaders_sendsNothingForTheCallersOwnTenant', () => {
    expect(tenantHeaders('')).toEqual({});
    expect(tenantHeaders(undefined)).toEqual({});
    expect(tenantHeaders('t-easy')).toEqual({ 'X-Autobahn-Tenant': 't-easy' });
  });

  it('normalizeTenantsResponse_dropsAnythingThatIsNotATenantList', () => {
    expect(normalizeTenantsResponse({ code: 'forbidden' })).toEqual([]);
    expect(normalizeTenantsResponse([{ id: 'a', slug: 's' }, null, {}])).toEqual([{ id: 'a', slug: 's', name: 's' }]);
  });
});
