/** A paid provider as `GET /admin/api/providers` describes it. */
export interface ProviderOverview {
  provider: string;
  displayName: string;
  upstreamBaseUrl: string;
  envVar: string;
  keyConfigured: boolean;
  keySource: 'stored' | 'environment' | 'none';
  /** False for a provider whose API the gateway does not forward yet (speech). */
  forwardable: boolean;
  supportsCatalog: boolean;
  reportsCost: boolean;
  supportsBalance: boolean;
  /** The environment variable the balance is read with, when it is not the company key (OpenAI's admin key). */
  balanceEnvVar?: string | null;
  /** Whether the key the balance is read with is set. */
  balanceKeyConfigured?: boolean;
  balance: ProviderBalance | null;
  lowBalanceUsd: number | null;
  newModelsStartStopped: boolean;
  modelCount: number;
  openTenantCount: number;
  openKeyCount: number;
  spentThisMonthUsd: number;
  requestsThisMonth: number;
  /** The hours the provider bills more for; absent or null when it charges the same at every hour. */
  peakPricing?: ProviderPeakPricing | null;
}

/** A provider's peak hours. The model's price is the off-peak one; peak is that times `multiplier`. */
export interface ProviderPeakPricing {
  multiplier: number;
  effectiveFromUtc: string | null;
  /** English day names, Monday first. */
  days: string[];
  /** `HH:mm-HH:mm` in UTC, the end excluded. */
  windowsUtc: string[];
  /** The holiday calendar whose days off have no peak hours, e.g. `china`. */
  holidayCalendar: string | null;
  /** Days with no peak hours from today to the end of next year. */
  upcomingExcludedDates: string[];
  /** This year and next, where the calendar has no holidays for them yet. */
  yearsWithoutHolidays: number[];
}

export interface ProviderBalance {
  remaining: number | null;
  limit: number | null;
  used: number | null;
  currency: string;
  readAtUtc: string | null;
  error: string | null;
}

export interface ProviderTenantRow {
  tenantId: string;
  tenantSlug: string;
  tenantName: string;
  open: boolean;
  monthlyCapUsd: number | null;
  spentThisMonthUsd: number;
  openKeyCount: number;
}

export interface ProviderSpendSlice {
  id: string;
  label: string;
  detail: string | null;
  costUsd: number;
  requests: number;
}

export interface ProviderSpendReport {
  provider: string;
  from: string;
  to: string;
  totalUsd: number;
  requests: number;
  daily: Array<{ date: string; costUsd: number; requests: number }>;
  byTenant: ProviderSpendSlice[];
  byKey: ProviderSpendSlice[];
  byModel: ProviderSpendSlice[];
}

export interface ProviderCatalogItem {
  id: string;
  name: string | null;
  contextLength: number | null;
  inputPricePerMillionTokens: number | null;
  outputPricePerMillionTokens: number | null;
  cachedInputPricePerMillionTokens: number | null;
  pricePerRequest: number | null;
  added: boolean;
}

export type ConnectionState = 'ok' | 'warn' | 'fail' | 'muted';

/**
 * Is the connection working? One answer for the top of the page, from the key and the last
 * balance read. A provider with no balance to read can only say whether a key is set.
 */
export function connectionOf(p: ProviderOverview): { state: ConnectionState; label: string; detail: string } {
  if (!p.forwardable) {
    return {
      state: 'muted',
      label: 'Not callable yet',
      detail: 'The gateway forwards chat, completions, embeddings and rerank. This provider\'s speech API is not one of them yet; access and caps set here are kept for when it is.',
    };
  }
  if (!p.keyConfigured) {
    return {
      state: 'fail',
      label: 'No key',
      detail: `Set ${p.envVar} on the gateway, or store a key under Settings.`,
    };
  }
  if (!p.supportsBalance) {
    return { state: 'ok', label: 'Key set', detail: `${p.displayName} has no balance the gateway can read with an API key.` };
  }
  if (p.balanceEnvVar && !p.balanceKeyConfigured) {
    return {
      state: 'ok',
      label: 'Key set',
      detail: `Set ${p.balanceEnvVar} on the gateway to read ${p.displayName}'s spend this month; the inference key cannot read it.`,
    };
  }
  if (p.balance?.error) {
    return { state: 'fail', label: 'Not answering', detail: p.balance.error };
  }
  if (!p.balance?.readAtUtc) {
    return { state: 'warn', label: 'Not read yet', detail: 'The balance is read a few seconds after the gateway starts, then every few minutes.' };
  }
  return { state: 'ok', label: 'Connected', detail: '' };
}

/** Whether remaining credit is under the level the operator asked to be warned at. */
export function isCreditLow(p: ProviderOverview): boolean {
  const remaining = p.balance?.remaining;
  // In the balance's own unit, as the alert compares it: credits for ElevenLabs.
  return remaining != null && p.lowBalanceUsd != null && remaining < p.lowBalanceUsd;
}

/** A balance in a currency, as opposed to a provider's own credits (ElevenLabs). */
export function isMoney(currency: string | null | undefined): boolean {
  return !currency || /^[A-Za-z]{3}$/.test(currency);
}

/** How much of a cap is used, 0 to 1 and beyond; null without a cap. */
export function capRatio(spent: number, cap: number | null | undefined): number | null {
  if (cap == null) return null;
  if (cap <= 0) return spent > 0 ? Infinity : 1;
  return spent / cap;
}

/** `'12.50'` → 12.5; blank → null (no cap); anything else → an error to show beside the field. */
export function parseCapInput(text: string): { value: number | null; error: string } {
  const trimmed = text.trim();
  if (!trimmed) return { value: null, error: '' };
  const n = Number(trimmed);
  if (!Number.isFinite(n) || n < 0) return { value: null, error: 'Enter an amount in USD, or leave it empty for no cap.' };
  return { value: Math.round(n * 100) / 100, error: '' };
}

/** One sentence for what a change to a tenant's row will do, shown before it is saved. */
export function describeTenantChange(
  displayName: string,
  row: ProviderTenantRow,
  open: boolean,
  cap: number | null,
): string {
  const parts: string[] = [];
  if (open !== row.open) {
    parts.push(open
      ? `opens ${displayName} for ${row.tenantSlug}`
      : `closes ${displayName} for ${row.tenantSlug}: its ${row.openKeyCount} opened ${row.openKeyCount === 1 ? 'key is' : 'keys are'} refused at once`);
  }
  if (cap !== row.monthlyCapUsd) {
    parts.push(cap == null ? 'removes the monthly cap' : `sets the monthly cap to $${cap.toFixed(2)}`);
  }
  if (!parts.length) return '';
  const sentence = parts.join(' and ');
  return `Saving ${sentence}.`;
}

export function formatPerMillion(value: number | null | undefined): string {
  if (value == null) return '—';
  return `$${Number(value.toFixed(4))}`;
}

const WEEK = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

/** "Monday to Friday" for a run of days, otherwise the days listed. */
function describeDays(days: string[]): string {
  const at = days.map((d) => WEEK.indexOf(d));
  const run = at.length > 2 && at.every((d, i) => d >= 0 && (i === 0 || d === at[i - 1] + 1));
  return run ? `${days[0]} to ${days[days.length - 1]}` : days.join(', ');
}

/**
 * What the provider's peak hours do to the prices on its page, or null when it has none. Names the
 * years whose holidays are not known yet: China announces them late in the year before, and until
 * a calendar update brings them every weekday of that year is priced with its peak hours.
 */
export function describePeakPricing(
  p: ProviderOverview,
  thisYear = new Date().getUTCFullYear(),
): { text: string; missingYear: number | null; missingIsThisYear: boolean } | null {
  const peak = p.peakPricing;
  if (!peak || !peak.windowsUtc.length || !peak.days.length) return null;
  const windows = peak.windowsUtc.map((w) => w.replace('-', '–'));
  const hours = windows.length > 1 ? `${windows.slice(0, -1).join(', ')} and ${windows[windows.length - 1]}` : windows[0];
  const text =
    `${p.displayName} bills ${Number(peak.multiplier.toFixed(4))}× the model's price ${describeDays(peak.days)}, ` +
    `${hours} UTC, except on its public holidays. The prices here are the off-peak ones.`;
  const missing = [...(peak.yearsWithoutHolidays ?? [])].sort((a, b) => a - b);
  return { text, missingYear: missing[0] ?? null, missingIsThisYear: missing[0] === thisYear };
}

/** `GET /admin/api/providers/{id}/reconciliation`: the balance's fall beside the gateway's charges. */
export interface ProviderReconciliation {
  provider: string;
  currency: string;
  windowFrom: string;
  windowTo: string;
  coveredFrom: string | null;
  coveredTo: string | null;
  readings: number;
  topUps: number;
  providerSpendUsd: number;
  gatewaySpendUsd: number;
  differenceUsd: number;
  differencePercent: number | null;
  drifting: boolean;
  threshold: { percent: number; minimumUsd: number };
  unavailable: string | null;
  daily: Array<{ date: string; providerUsd: number; gatewayUsd: number }>;
}

/**
 * One line on whether the two figures agree, and which way they lean when they do not. The
 * direction says where to look: a provider that took more points outside the gateway, a gateway
 * that counted more points at a price.
 */
export function describeReconciliation(
  r: ProviderReconciliation,
  displayName: string,
): { tone: 'ok' | 'warn' | 'muted'; text: string } {
  if (r.unavailable) return { tone: 'muted', text: r.unavailable };
  const gap = `$${Math.abs(r.differenceUsd).toFixed(2)}`;
  const share = r.differencePercent == null ? '' : ` (${r.differencePercent}%)`;
  if (!r.drifting) {
    return {
      tone: 'ok',
      text: `Within the threshold: ${gap}${share} apart, flagged from ${r.threshold.percent}% and $${r.threshold.minimumUsd.toFixed(2)}.`,
    };
  }
  return r.differenceUsd > 0
    ? {
        tone: 'warn',
        text: `${displayName} took ${gap}${share} more than the gateway charged. Another client may be spending on the same account, or some usage did not reach the ledger.`,
      }
    : {
        tone: 'warn',
        text: `The gateway charged ${gap}${share} more than ${displayName} took. A model's price is likely too high, or has no cached-input price; fix it, then reprice the stored usage.`,
      };
}
