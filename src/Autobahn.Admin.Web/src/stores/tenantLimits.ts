import { createSignal } from 'solid-js';
import type { TenantRef } from '../domain/tenantLimits';
import { apiClient, handleApiError } from './auth';
import { normalizeRateLimitsPayload, type AdminRateLimitsDto, type RateLimitConfig } from './rateLimits';

/** The limits of the key's own tenant, as `GET /admin/api/tenant/rate-limits` answers them. */
export interface OwnTenantLimits {
  tenant: TenantRef & { name: string };
  /** The tenant's part of the configuration: its tier, its rules and its keys' rules. */
  config: RateLimitConfig;
}

export function normalizeOwnTenantLimits(data: unknown): OwnTenantLimits | null {
  const body = (data ?? {}) as { tenant?: Record<string, unknown> };
  const config = normalizeRateLimitsPayload(data as AdminRateLimitsDto);
  if (!body.tenant?.id || !config) return null;
  const slug = String(body.tenant.slug ?? '');
  return {
    tenant: {
      id: String(body.tenant.id),
      slug,
      name: String(body.tenant.name ?? '') || slug,
      planSlug: body.tenant.planSlug ? String(body.tenant.planSlug) : null,
    },
    config,
  };
}

const [limits, setLimits] = createSignal<OwnTenantLimits | null>(null);
const [loading, setLoading] = createSignal(false);
const [loadError, setLoadError] = createSignal('');

let seq = 0;

export function useOwnTenantLimits() {
  return { limits, loading, loadError, load: loadOwnTenantLimits };
}

export async function loadOwnTenantLimits(): Promise<void> {
  const mine = ++seq;
  setLoading(true);
  try {
    const data = await apiClient.apiJson<unknown>('/admin/api/tenant/rate-limits');
    if (mine !== seq) return;
    setLimits(normalizeOwnTenantLimits(data));
    setLoadError('');
  } catch (e) {
    if (mine !== seq) return;
    const err = e as { message?: string; global?: boolean };
    setLoadError(err.message || 'Could not load the limits.');
    if (err.global) handleApiError(e);
  } finally {
    if (mine === seq) setLoading(false);
  }
}

/** Forget what the previous key could see. */
export function resetOwnTenantLimits(): void {
  seq += 1;
  setLimits(null);
  setLoadError('');
  setLoading(false);
}
