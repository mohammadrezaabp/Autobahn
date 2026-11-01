import type { RateLimitConfig, RateLimitRule, RateLimitTier } from '../stores/rateLimits';

/** The part of a tenant the rate-limit configuration refers to. */
export interface TenantRef {
  id: string;
  slug: string;
  planSlug?: string | null;
}

export type TenantTierSource = 'default' | 'plan' | 'rule';

export interface EffectiveTenantTier {
  tier: RateLimitTier;
  /** Where the request rate (rpm and burst) comes from. */
  rateSource: TenantTierSource;
  /** The configured plan the tenant is on; empty when it falls back to the default tier. */
  planSlug: string;
  /** The tenant names a plan that the configuration does not have, so the default tier applies. */
  planMissing: boolean;
  /** The enabled tenant rule that overrides the tier, when there is one. */
  rule: RateLimitRule | null;
}

const same = (a: unknown, b: unknown) =>
  String(a ?? '').trim().toLowerCase() === String(b ?? '').trim().toLowerCase() && String(a ?? '').trim() !== '';

/** The subject half of a rule target: `subject` or `subject|model`. */
export function ruleSubject(rule: Pick<RateLimitRule, 'target'>): string {
  return String(rule.target ?? '').split('|')[0];
}

/** The model half of a `subject|model` target; empty for a rule on all models. */
export function ruleModel(rule: Pick<RateLimitRule, 'scope' | 'target'>): string {
  const scope = String(rule.scope ?? '').toLowerCase();
  if (scope === 'model') return String(rule.target ?? '');
  if (!scope.endsWith('_model')) return '';
  const target = String(rule.target ?? '');
  const at = target.indexOf('|');
  return at >= 0 ? target.slice(at + 1) : '';
}

/** A tenant rule may be written against the tenant's id or its slug; the gateway accepts both. */
export function isTenantTarget(subject: string, tenant: TenantRef): boolean {
  return same(subject, tenant.id) || same(subject, tenant.slug);
}

function clamp(tier: RateLimitTier | undefined): RateLimitTier {
  return {
    rpm: Math.max(1, Number(tier?.rpm) || 0),
    burst: Math.max(0, Number(tier?.burst) || 0),
    maxConcurrentStreams: Math.max(0, Number(tier?.maxConcurrentStreams) || 0),
    tpm: Math.max(0, Number(tier?.tpm) || 0),
  };
}

/**
 * The tier the gateway applies to a tenant, by the gateway's own precedence: an enabled tenant
 * rule wins over the tenant's plan, which wins over the default. A tenant rule with rpm 0 keeps
 * the plan's rate and contributes its stream cap and, when set, its token limit.
 */
export function effectiveTenantTier(cfg: RateLimitConfig, tenant: TenantRef): EffectiveTenantTier {
  const wanted = String(tenant.planSlug ?? '').trim();
  const planSlug = wanted ? (Object.keys(cfg.plans).find((k) => same(k, wanted)) ?? '') : '';
  const base = clamp(planSlug ? cfg.plans[planSlug] : cfg.default);
  const baseSource: TenantTierSource = planSlug ? 'plan' : 'default';
  const planMissing = !!wanted && !planSlug;

  const enabled = cfg.rules.filter(
    (r) => String(r.scope).toLowerCase() === 'tenant' && r.enabled !== false,
  );
  const rule =
    enabled.find((r) => same(r.target, tenant.id)) ?? enabled.find((r) => same(r.target, tenant.slug)) ?? null;
  if (!rule) return { tier: base, rateSource: baseSource, planSlug, planMissing, rule: null };

  if (Number(rule.rpm) > 0) {
    return { tier: clamp(rule), rateSource: 'rule', planSlug, planMissing, rule };
  }
  return {
    tier: {
      ...base,
      maxConcurrentStreams: Math.max(0, Number(rule.maxConcurrentStreams) || 0),
      tpm: Number(rule.tpm) > 0 ? Number(rule.tpm) : base.tpm,
    },
    rateSource: baseSource,
    planSlug,
    planMissing,
    rule,
  };
}

/**
 * Every rule that limits this tenant or one of its keys: the tenant's own rules first, then its
 * keys' rules. Rules on a model for everyone, and the gateway-wide ones, are not a tenant's.
 */
export function tenantRules(
  cfg: RateLimitConfig,
  tenant: TenantRef,
  keyIds: readonly string[],
): RateLimitRule[] {
  const keys = new Set(keyIds.map((id) => id.toLowerCase()));
  const own: RateLimitRule[] = [];
  const ofKeys: RateLimitRule[] = [];
  for (const rule of cfg.rules) {
    const scope = String(rule.scope).toLowerCase();
    const subject = ruleSubject(rule);
    if ((scope === 'tenant' || scope === 'tenant_model') && isTenantTarget(subject, tenant)) own.push(rule);
    else if ((scope === 'api_key' || scope === 'api_key_model') && keys.has(subject.toLowerCase())) ofKeys.push(rule);
  }
  return [...own, ...ofKeys];
}

/** How many of a tenant's keys carry a rule of their own. */
export function limitedKeyCount(cfg: RateLimitConfig, keyIds: readonly string[]): number {
  const keys = new Set(keyIds.map((id) => id.toLowerCase()));
  const limited = new Set<string>();
  for (const rule of cfg.rules) {
    const scope = String(rule.scope).toLowerCase();
    if (scope !== 'api_key' && scope !== 'api_key_model') continue;
    const subject = ruleSubject(rule).toLowerCase();
    if (keys.has(subject)) limited.add(subject);
  }
  return limited.size;
}

/** The stored scope and target for a limit on a tenant or one of its keys, optionally on one model. */
export function ruleAddress(
  subject: { kind: 'tenant'; slug: string } | { kind: 'key'; keyId: string },
  modelId: string,
): { scope: string; target: string } {
  const model = modelId.trim();
  const who = subject.kind === 'tenant' ? subject.slug : subject.keyId;
  const scope = subject.kind === 'tenant' ? 'tenant' : 'api_key';
  return model ? { scope: `${scope}_model`, target: `${who}|${model}` } : { scope, target: who };
}

/** The tenant's row in a usage report, whose rows name a tenant by id on some builds and by slug on others. */
export function tenantUsageRow<T extends { key: string; tenantId: string | null }>(
  rows: readonly T[] | undefined,
  tenant: TenantRef,
): T | null {
  return (rows ?? []).find((r) => isTenantTarget(String(r.tenantId ?? ''), tenant) || isTenantTarget(r.key, tenant)) ?? null;
}
