import { formatNum } from '../../domain/format';
import type { EffectiveTenantTier } from '../../domain/tenantLimits';
import type { RateLimitTier } from '../../stores/rateLimits';

/** A tier's four numbers with their units, so a limit reads the same on every tenant screen. */
export function TenantLimitFigures(props: { tier: RateLimitTier }) {
  return (
    <span class="tn-nums">
      <span><b>{formatNum(props.tier.rpm)}</b><i>rpm</i></span>
      <span><b>{formatNum(props.tier.burst)}</b><i>burst</i></span>
      <span>
        <b>{props.tier.maxConcurrentStreams > 0 ? formatNum(props.tier.maxConcurrentStreams) : '∞'}</b>
        <i>streams</i>
      </span>
      <span><b>{(props.tier.tpm ?? 0) > 0 ? formatNum(props.tier.tpm ?? 0) : '∞'}</b><i>tpm</i></span>
    </span>
  );
}

/** Which control the tenant's shared limit comes from, in a few words. */
export function tierSourceLabel(effective: EffectiveTenantTier): string {
  const base = effective.planSlug ? `plan “${effective.planSlug}”` : 'default tier';
  if (effective.rateSource === 'rule') return 'its own tenant rule';
  if (effective.rule) return `${base}, streams and tokens from its tenant rule`;
  return effective.planMissing ? `${base} (its plan is not configured)` : base;
}
