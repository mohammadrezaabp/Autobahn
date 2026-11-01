import { describe, expect, it } from 'vitest';
import type { RateLimitConfig, RateLimitRule } from '../stores/rateLimits';
import {
  effectiveTenantTier,
  limitedKeyCount,
  ruleAddress,
  ruleModel,
  ruleSubject,
  tenantRules,
  tenantUsageRow,
} from './tenantLimits';

const rule = (scope: string, target: string, rpm: number, extra: Partial<RateLimitRule> = {}): RateLimitRule => ({
  scope,
  target,
  rpm,
  burst: 0,
  maxConcurrentStreams: 0,
  tpm: 0,
  enabled: true,
  schedule: [],
  ...extra,
});

const config = (rules: RateLimitRule[] = []): RateLimitConfig => ({
  version: 1,
  enabled: true,
  adaptiveEnabled: false,
  default: { rpm: 300, burst: 15, maxConcurrentStreams: 30, tpm: 0 },
  plans: { premium: { rpm: 600, burst: 30, maxConcurrentStreams: 60, tpm: 2_000_000 } },
  rules,
});

const acme = { id: 'AAAA-1111', slug: 'acme', planSlug: 'premium' };

describe('effectiveTenantTier', () => {
  it('usesTheDefaultTierWithoutAPlan', () => {
    const e = effectiveTenantTier(config(), { id: 't', slug: 'solo', planSlug: null });
    expect(e.tier.rpm).toBe(300);
    expect(e.rateSource).toBe('default');
    expect(e.planMissing).toBe(false);
  });

  it('usesThePlanMatchedWithoutRegardToCase', () => {
    const e = effectiveTenantTier(config(), { ...acme, planSlug: 'Premium' });
    expect(e.tier).toEqual({ rpm: 600, burst: 30, maxConcurrentStreams: 60, tpm: 2_000_000 });
    expect(e.rateSource).toBe('plan');
    expect(e.planSlug).toBe('premium');
  });

  it('fallsBackToTheDefaultAndSaysSoWhenThePlanIsNotConfigured', () => {
    const e = effectiveTenantTier(config(), { ...acme, planSlug: 'gone' });
    expect(e.tier.rpm).toBe(300);
    expect(e.planMissing).toBe(true);
  });

  it('letsATenantRuleWithARateReplaceTheWholeTier', () => {
    const e = effectiveTenantTier(config([rule('tenant', 'acme', 60, { burst: 6, maxConcurrentStreams: 8 })]), acme);
    expect(e.tier).toEqual({ rpm: 60, burst: 6, maxConcurrentStreams: 8, tpm: 0 });
    expect(e.rateSource).toBe('rule');
  });

  it('keepsThePlanRateWhenTheTenantRuleHasNoRate', () => {
    const e = effectiveTenantTier(config([rule('tenant', 'acme', 0, { maxConcurrentStreams: 4 })]), acme);
    expect(e.tier).toEqual({ rpm: 600, burst: 30, maxConcurrentStreams: 4, tpm: 2_000_000 });
    expect(e.rateSource).toBe('plan');
    expect(e.rule).not.toBeNull();
  });

  it('matchesATenantRuleWrittenAgainstTheIdAndPrefersItOverTheSlug', () => {
    const e = effectiveTenantTier(config([rule('tenant', 'acme', 10), rule('tenant', 'aaaa-1111', 20)]), acme);
    expect(e.tier.rpm).toBe(20);
  });

  it('ignoresASwitchedOffTenantRule', () => {
    const e = effectiveTenantTier(config([rule('tenant', 'acme', 60, { enabled: false })]), acme);
    expect(e.tier.rpm).toBe(600);
    expect(e.rule).toBeNull();
  });

  it('floorsATierAtOneRequestPerMinuteAsTheGatewayDoes', () => {
    const cfg = config();
    cfg.default = { rpm: 0, burst: 0, maxConcurrentStreams: 0 };
    expect(effectiveTenantTier(cfg, { id: 't', slug: 'solo' }).tier.rpm).toBe(1);
  });
});

describe('tenantRules', () => {
  const cfg = config([
    rule('model', 'gpt-4', 400),
    rule('api_key', 'KEY-1', 30),
    rule('tenant_model', 'acme|gpt-4', 90),
    rule('api_key_model', 'key-2|bge-m3', 200),
    rule('api_key', 'key-of-someone-else', 5),
    rule('tenant', 'other', 10),
    rule('global', '*', 1000),
  ]);

  it('listsTheTenantsOwnRulesFirstThenItsKeys', () => {
    const got = tenantRules(cfg, acme, ['key-1', 'key-2']).map((r) => `${r.scope}:${r.target}`);
    expect(got).toEqual(['tenant_model:acme|gpt-4', 'api_key:KEY-1', 'api_key_model:key-2|bge-m3']);
  });

  it('countsKeysWithARuleOnceEach', () => {
    const more = { ...cfg, rules: [...cfg.rules, rule('api_key_model', 'key-1|gpt-4', 10)] };
    expect(limitedKeyCount(more, ['key-1', 'key-2', 'key-3'])).toBe(2);
  });
});

describe('rule targets', () => {
  it('splitsSubjectAndModel', () => {
    expect(ruleSubject({ target: 'acme|gpt-4' })).toBe('acme');
    expect(ruleModel({ scope: 'tenant_model', target: 'acme|gpt-4' })).toBe('gpt-4');
    expect(ruleModel({ scope: 'model', target: 'gpt-4' })).toBe('gpt-4');
    expect(ruleModel({ scope: 'api_key', target: 'key-1' })).toBe('');
  });

  it('buildsTheStoredScopeAndTarget', () => {
    expect(ruleAddress({ kind: 'tenant', slug: 'acme' }, '')).toEqual({ scope: 'tenant', target: 'acme' });
    expect(ruleAddress({ kind: 'tenant', slug: 'acme' }, 'gpt-4')).toEqual({ scope: 'tenant_model', target: 'acme|gpt-4' });
    expect(ruleAddress({ kind: 'key', keyId: 'k1' }, ' ')).toEqual({ scope: 'api_key', target: 'k1' });
    expect(ruleAddress({ kind: 'key', keyId: 'k1' }, 'bge-m3')).toEqual({ scope: 'api_key_model', target: 'k1|bge-m3' });
  });
});

describe('tenantUsageRow', () => {
  it('findsTheTenantByIdOrBySlug', () => {
    const rows = [
      { key: 'x', tenantId: 'zzzz', requests: 1 },
      { key: 'aaaa-1111', tenantId: 'aaaa-1111', requests: 7 },
    ];
    expect(tenantUsageRow(rows, acme)?.requests).toBe(7);
    expect(tenantUsageRow([{ key: 'acme', tenantId: null, requests: 3 }], acme)?.requests).toBe(3);
    expect(tenantUsageRow(undefined, acme)).toBeNull();
  });
});
