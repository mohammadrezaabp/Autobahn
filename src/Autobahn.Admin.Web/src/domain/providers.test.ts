import { describe, expect, it } from 'vitest';
import { parseKeyCap } from './keyGrants';
import {
  capRatio,
  connectionOf,
  describePeakPricing,
  describeReconciliation,
  describeTenantChange,
  isCreditLow,
  parseCapInput,
  type ProviderOverview,
  type ProviderReconciliation,
  type ProviderTenantRow,
} from './providers';

const reconciliation = (over: Partial<ProviderReconciliation> = {}): ProviderReconciliation => ({
  provider: 'deepseek',
  currency: 'USD',
  windowFrom: '2026-10-01T00:00:00Z',
  windowTo: '2026-10-17T12:00:00Z',
  coveredFrom: '2026-10-14T08:00:00Z',
  coveredTo: '2026-10-17T12:00:00Z',
  readings: 900,
  topUps: 0,
  providerSpendUsd: 1.76,
  gatewaySpendUsd: 50.45,
  differenceUsd: -48.69,
  differencePercent: 96.51,
  drifting: true,
  threshold: { percent: 10, minimumUsd: 1 },
  unavailable: null,
  daily: [],
  ...over,
});

describe('describeReconciliation', () => {
  it('points at the price when the gateway counted more', () => {
    const s = describeReconciliation(reconciliation(), 'DeepSeek');
    expect(s.tone).toBe('warn');
    expect(s.text).toBe(
      "The gateway charged $48.69 (96.51%) more than DeepSeek took. A model's price is likely too high, or has no cached-input price; fix it, then reprice the stored usage.",
    );
  });

  it('points outside the gateway when the provider took more', () => {
    const s = describeReconciliation(reconciliation({ differenceUsd: 5, differencePercent: 50 }), 'DeepSeek');
    expect(s.text).toMatch(/^DeepSeek took \$5\.00 \(50%\) more than the gateway charged\./);
  });

  it('says the figures are within the threshold, and what the threshold is', () => {
    const s = describeReconciliation(reconciliation({ drifting: false, differenceUsd: 0.2, differencePercent: 2 }), 'DeepSeek');
    expect(s).toEqual({ tone: 'ok', text: 'Within the threshold: $0.20 (2%) apart, flagged from 10% and $1.00.' });
  });

  it('passes on why there is no comparison', () => {
    const s = describeReconciliation(reconciliation({ unavailable: 'At least two balance readings are needed.' }), 'DeepSeek');
    expect(s).toEqual({ tone: 'muted', text: 'At least two balance readings are needed.' });
  });
});

const provider = (over: Partial<ProviderOverview> = {}): ProviderOverview => ({
  provider: 'openrouter',
  displayName: 'OpenRouter',
  upstreamBaseUrl: 'https://openrouter.ai/api',
  envVar: 'OPENROUTER_API_KEY',
  keyConfigured: true,
  keySource: 'environment',
  forwardable: true,
  supportsCatalog: true,
  reportsCost: true,
  supportsBalance: true,
  balance: { remaining: 40, limit: 50, used: 10, currency: 'USD', readAtUtc: '2026-10-13T10:00:00Z', error: null },
  lowBalanceUsd: null,
  newModelsStartStopped: false,
  modelCount: 0,
  openTenantCount: 0,
  openKeyCount: 0,
  spentThisMonthUsd: 0,
  requestsThisMonth: 0,
  ...over,
});

const row = (over: Partial<ProviderTenantRow> = {}): ProviderTenantRow => ({
  tenantId: 't1',
  tenantSlug: 'easy',
  tenantName: 'Easy',
  open: false,
  monthlyCapUsd: null,
  spentThisMonthUsd: 0,
  openKeyCount: 0,
  ...over,
});

describe('connectionOf', () => {
  it('is connected once the balance has been read', () => {
    expect(connectionOf(provider()).state).toBe('ok');
  });

  it('names the variable to set when there is no key', () => {
    const c = connectionOf(provider({ keyConfigured: false, keySource: 'none', balance: null }));
    expect(c.state).toBe('fail');
    expect(c.detail).toContain('OPENROUTER_API_KEY');
  });

  it('shows why the last read failed', () => {
    const c = connectionOf(provider({ balance: { remaining: 40, limit: null, used: null, currency: 'USD', readAtUtc: null, error: 'The provider answered HTTP 401.' } }));
    expect(c).toMatchObject({ state: 'fail', detail: 'The provider answered HTTP 401.' });
  });

  it('does not call a provider without a balance broken', () => {
    expect(connectionOf(provider({ supportsBalance: false, balance: null })).state).toBe('ok');
  });

  it('says a speech provider is not callable yet, whatever its key', () => {
    expect(connectionOf(provider({ forwardable: false })).label).toBe('Not callable yet');
  });
});

describe('describePeakPricing', () => {
  const deepseek = (over: Partial<NonNullable<ProviderOverview['peakPricing']>> = {}) =>
    provider({
      provider: 'deepseek',
      displayName: 'DeepSeek',
      peakPricing: {
        multiplier: 2,
        effectiveFromUtc: '2026-08-16T16:00:00Z',
        days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'],
        windowsUtc: ['01:00-04:00', '06:00-10:00'],
        holidayCalendar: 'china',
        upcomingExcludedDates: ['2027-01-01'],
        yearsWithoutHolidays: [],
        ...over,
      },
    });

  it('says when the price is multiplied, and that the prices shown are the off-peak ones', () => {
    expect(describePeakPricing(deepseek())).toEqual({
      text: "DeepSeek bills 2× the model's price Monday to Friday, 01:00–04:00 and 06:00–10:00 UTC, except on its public holidays. The prices here are the off-peak ones.",
      missingYear: null,
      missingIsThisYear: false,
    });
  });

  it('lists days that are not a run', () => {
    expect(describePeakPricing(deepseek({ days: ['Monday', 'Wednesday'], windowsUtc: ['01:00-04:00'] }))?.text).toContain(
      'Monday, Wednesday, 01:00–04:00 UTC',
    );
  });

  it('names the first year whose holidays are not known yet', () => {
    expect(describePeakPricing(deepseek({ yearsWithoutHolidays: [2027] }), 2026)).toMatchObject({
      missingYear: 2027,
      missingIsThisYear: false,
    });
    expect(describePeakPricing(deepseek({ yearsWithoutHolidays: [2027, 2026] }), 2026)).toMatchObject({
      missingYear: 2026,
      missingIsThisYear: true,
    });
  });

  it('is nothing for a provider that charges the same at every hour', () => {
    expect(describePeakPricing(provider())).toBeNull();
    expect(describePeakPricing(provider({ peakPricing: null }))).toBeNull();
  });
});

describe('isCreditLow', () => {
  it('compares what is left with the level the operator set', () => {
    expect(isCreditLow(provider({ lowBalanceUsd: 50 }))).toBe(true);
    expect(isCreditLow(provider({ lowBalanceUsd: 20 }))).toBe(false);
  });

  it('is never low without a level, or without a reading', () => {
    expect(isCreditLow(provider())).toBe(false);
    expect(isCreditLow(provider({ lowBalanceUsd: 50, balance: null }))).toBe(false);
  });
});

describe('parseCapInput', () => {
  it('reads blank as no cap', () => {
    expect(parseCapInput('  ')).toEqual({ value: null, error: '' });
  });

  it('rounds to cents', () => {
    expect(parseCapInput('12.345').value).toBe(12.35);
    expect(parseCapInput('0').value).toBe(0);
  });

  it('refuses what is not an amount', () => {
    expect(parseCapInput('ten').error).not.toBe('');
    expect(parseCapInput('-5').error).not.toBe('');
  });
});

describe('parseKeyCap', () => {
  it('refuses a key cap above the tenant cap, as the gateway does', () => {
    expect(parseKeyCap('25', 10).error).toContain('$10.00');
    expect(parseKeyCap('10', 10)).toEqual({ value: 10, error: '' });
  });

  it('accepts any amount when the tenant has no cap', () => {
    expect(parseKeyCap('25', null)).toEqual({ value: 25, error: '' });
  });
});

describe('capRatio', () => {
  it('is null without a cap', () => {
    expect(capRatio(5, null)).toBeNull();
  });

  it('divides spend by the cap', () => {
    expect(capRatio(8, 10)).toBe(0.8);
  });

  it('treats a zero cap as used up', () => {
    expect(capRatio(0, 0)).toBe(1);
  });
});

describe('describeTenantChange', () => {
  it('says nothing when nothing changes', () => {
    expect(describeTenantChange('OpenRouter', row(), false, null)).toBe('');
  });

  it('says what opening with a cap does', () => {
    expect(describeTenantChange('OpenRouter', row(), true, 50))
      .toBe('Saving opens OpenRouter for easy and sets the monthly cap to $50.00.');
  });

  it('warns that closing refuses the opened keys at once', () => {
    expect(describeTenantChange('OpenRouter', row({ open: true, openKeyCount: 3, monthlyCapUsd: 50 }), false, 50))
      .toBe('Saving closes OpenRouter for easy: its 3 opened keys are refused at once.');
  });

  it('says a cap is removed', () => {
    expect(describeTenantChange('OpenRouter', row({ open: true, monthlyCapUsd: 50 }), true, null))
      .toBe('Saving removes the monthly cap.');
  });
});
