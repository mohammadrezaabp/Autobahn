import { describe, expect, it } from 'vitest';
import {
  activityRows,
  ago,
  backendRows,
  backendsHealth,
  barRows,
  compactNum,
  controlPlaneStats,
  errorModelBars,
  finopsView,
  flattenDetails,
  formatBytes,
  modelPerfRows,
  policyView,
  rateLimitsView,
  tenantSlugs,
  tenantsView,
} from './overviewDetail';

const NOW = Date.parse('2026-10-10T06:32:00Z');

describe('formatting helpers', () => {
  it('compactNum_keepsSmallNumbersAndShortensLargeOnes', () => {
    expect(compactNum(999)).toBe('999');
    expect(compactNum(1234)).toBe('1.2k');
    expect(compactNum(281917)).toBe('282k');
    expect(compactNum(6873279392)).toBe('6.9B');
  });

  it('formatBytes_picksAUnit', () => {
    expect(formatBytes(1691351064)).toBe('1.6 GB');
    expect(formatBytes(undefined)).toBe('—');
  });

  it('ago_readsAsElapsedTime', () => {
    expect(ago('2026-10-10T06:31:30Z', NOW)).toBe('30s ago');
    expect(ago('2026-10-10T02:15:02Z', NOW)).toBe('4h ago');
    expect(ago('2026-10-08T06:00:00Z', NOW)).toBe('2d ago');
    expect(ago(null, NOW)).toBe('—');
  });

  it('flattenDetails_turnsAuditJsonIntoOneLine', () => {
    expect(flattenDetails('{"panels":["grafana","prometheus"]}')).toBe('panels: grafana, prometheus');
    expect(flattenDetails('{"succeeded":true,"path":"/data/x","error":null}')).toBe('succeeded: true · path: /data/x');
    expect(flattenDetails('plain text')).toBe('plain text');
    expect(flattenDetails(null)).toBe('');
  });
});

describe('barRows', () => {
  it('ranksAndScalesAgainstTheLargest_droppingZeroRows', () => {
    const rows = barRows([{ key: 'a', amount: 50 }, { key: 'b', amount: 200 }, { key: 'c', amount: 0 }]);
    expect(rows.map((r) => [r.key, r.pct])).toEqual([['b', 100], ['a', 25]]);
  });

  it('keepsATinyRowVisible', () => {
    expect(barRows([{ key: 'a', amount: 10000 }, { key: 'b', amount: 1 }])[1].pct).toBe(2);
  });
});

describe('backends', () => {
  const summary = {
    backends: [
      { modelId: 'idle', isHealthy: true, lastCheckedUtc: null, circuitState: 'unknown', inFlight: 0, maxConcurrent: 0, requests5m: 0, errorRate5m: null, latencyP95Ms5m: null },
      { modelId: 'busy', isHealthy: true, lastCheckedUtc: '2026-10-10T06:31:50Z', circuitState: 'closed', inFlight: 230, maxConcurrent: 256, requests5m: 72, errorRate5m: 0.2778, latencyP95Ms5m: 11250 },
      { modelId: 'down', isHealthy: false, lastCheckedUtc: '2026-10-10T06:31:50Z', circuitState: 'open', inFlight: 0, maxConcurrent: 256, requests5m: 0, errorRate5m: null, error: 'connection refused' },
    ],
  };

  it('backendRows_putsUnhealthyFirstThenBusiest', () => {
    expect(backendRows(summary, NOW).map((b) => b.modelId)).toEqual(['down', 'busy', 'idle']);
  });

  it('backendRows_namesTheBackendWhenAModelHasItsOwn', () => {
    const rows = backendRows({
      backends: [
        { modelId: 'm1', backend: 'default', url: 'http://x' },
        { modelId: 'm2', backend: 'a', url: 'http://a' },
        { modelId: 'm2', backend: 'b', url: 'http://b' },
      ],
    }, NOW);
    expect(rows.map((b) => b.modelId).sort()).toEqual(['m1', 'm2 / a', 'm2 / b']);
    expect(new Set(rows.map((b) => b.key)).size).toBe(3);
  });

  it('backendRows_classifiesHealthCircuitLoadAndErrorRate', () => {
    const [down, busy, idle] = backendRows(summary, NOW);
    expect(down.dotClass).toBe('dot-fail');
    expect(down.circuitClass).toBe('tag level-error');
    expect(down.error).toBe('connection refused');
    expect(busy.dotClass).toBe('dot-ok');
    expect(busy.loadPct).toBe(90);
    expect(busy.loadClass).toBe('load-fill is-hot');
    expect(busy.errorRateClass).toBe('num is-error');
    expect(busy.checkedText).toBe('10s ago');
    expect(idle.dotClass).toBe('dot-idle');
    expect(idle.loadPct).toBeNull();
    expect(idle.circuitText).toBe('no traffic');
    expect(idle.errorRateText).toBe('—');
  });

  it('backendsHealth_countsHealthy', () => {
    expect(backendsHealth(summary)).toEqual({ text: '2 / 3 healthy', cls: 'status-chip warn' });
    expect(backendsHealth({ backends: [{ isHealthy: true }] }).cls).toBe('status-chip ok');
  });
});

describe('policy pressure', () => {
  const tenantId = 'ba11ef71-843d-49dc-9b75-7c9a8fa9bfc6';
  const policyBody = {
    quotas: [
      { partitionKey: tenantId, tenantSlug: 'personal-keys', used: 632703249, limit: 0, ratio: 0 },
      { partitionKey: 'q2', tenantSlug: 'easy', planSlug: 'gold', used: 90, limit: 100, ratio: 0.9, nearLimit: true },
    ],
  };
  const summary = {
    rateLimitRejections: 318666,
    quotaRejections: 0,
    policy: {
      rejectionsByReason1h: [{ key: 'grant_denied', count: 119 }, { key: 'rate_limit', count: 3 }],
      rejectionsByTenant1h: [{ key: tenantId, count: 119 }, { key: 'authfail:172.18.0.3', count: 3 }],
      rejectionsByModel1h: [{ key: 'Gemma-4-31B-IT', count: 119 }],
      grantDenials1h: [{ key: `${tenantId}|Gemma-4-31B-IT`, count: 119 }],
      unknownModels1h: [],
    },
  };

  it('namesTenantsBySlugInsteadOfId', () => {
    const view = policyView(summary, policyBody, tenantSlugs(policyBody, null));
    expect(view.tenantBars.map((r) => r.label)).toEqual(['personal-keys', 'authfail:172.18.0.3']);
    expect(view.grantDenialBars[0].label).toBe('personal-keys → Gemma-4-31B-IT');
    expect(view.reasonBars.map((r) => r.label)).toEqual(['model not granted', 'rate limit']);
  });

  it('showsOnlyQuotasThatHaveALimit', () => {
    const view = policyView(summary, policyBody, new Map());
    expect(view.quotaRows).toHaveLength(1);
    expect(view.quotaRows[0]).toMatchObject({ who: 'easy', plan: 'gold', pct: 90, fillClass: 'load-fill is-hot' });
  });

  it('isQuietOnlyWithNoRejectionsAndHeadroom', () => {
    expect(policyView(summary, policyBody, new Map()).quiet).toBe(false);
    expect(policyView({ policy: {} }, { quotas: [] }, new Map()).quiet).toBe(true);
    expect(policyView(summary, policyBody, new Map()).rateLimitedClass).toBe('mini-stat warn');
  });
});

describe('rate limits', () => {
  it('isNullWithoutATracker', () => {
    expect(rateLimitsView(null)).toBeNull();
    expect(rateLimitsView({ available: false })).toBeNull();
  });

  it('summarisesAQuietHour', () => {
    const view = rateLimitsView({
      enforced: true,
      ruleCount: 13,
      disabledRuleCount: 1,
      lastHour: { decisions: 3278, refused: 0 },
      lastFiveMinutes: { refused: 0 },
      protective: [{ scope: 'auth_failure', refused: 3 }, { scope: 'anonymous', refused: 0 }],
      adaptive: { enabled: true, modelsReduced: 0 },
      limits: [],
    })!;
    expect(view.title).toBe('No refusals in the last hour');
    expect(view.quiet).toBe(true);
    expect(view.headStats.map((s) => s.value)).toEqual(['3.3k', '0', '12']);
    expect(view.hints[0]).toContain('3 (auth failure)');
    expect(view.hints[1]).toBe('Adaptive limiting is on and not reducing any model.');
  });

  it('listsRefusingLimits', () => {
    const view = rateLimitsView({
      lastHour: { decisions: 100, refused: 40 },
      limits: [{ limitId: 'tenant:easy', target: 'easy', refused: 40, peakUtilization: 1.2 }],
      topRefusedTenants: [{ tenantSlug: 'easy', refused: 40 }],
    })!;
    expect(view.title).toBe('40 refused in the last hour');
    expect(view.limitRows[0]).toMatchObject({ label: 'easy', pct: 100, fillClass: 'load-fill is-over', countText: '40 refused' });
    expect(view.tenantBars[0].label).toBe('easy');
  });
});

describe('spend', () => {
  it('buildsTilesBarsAndCoverage', () => {
    const view = finopsView({
      currency: 'USD',
      todayCost: 240.93,
      yesterdayCost: 838.01,
      monthToDateCost: 8024.52,
      projectedMonthlyCost: 29194.57,
      averageDailyCost: 1008.1,
      todayRequests: 11472,
      monthToDateRequests: 344109,
      todayPromptTokens: 238179920,
      todayCompletionTokens: 9149369,
      pricedModelCount: 14,
      registeredModelCount: 14,
      unpricedModelIds: [],
      topModelsMonthToDate: [{ key: 'Qwen3.8-27B', cost: 3590.22 }, { key: 'DeepSeek-V4.1-Flash', cost: 2129.58 }],
      topCostCentersMonthToDate: [{ key: 'rahman-jahdasa', cost: 2498.44 }],
      reconciliation: { enabled: true, lastRunUtc: '2026-10-10T06:00:11Z', bucketsCompared: 142, discrepancyCount: 0 },
      budgets: [],
    })!;
    expect(view.tiles.map((t) => t.label)).toEqual(['Today', 'Month to date', 'Projected month', 'Tokens today']);
    expect(view.tiles[3].value).toBe('247M');
    expect(view.modelBars.map((r) => r.pct)).toEqual([100, 59]);
    expect(view.costCenterBars).toHaveLength(1);
    expect(view.coverageClass).toBe('finops-line healthy');
    expect(view.unpricedText).toBe('');
    expect(view.reconClass).toBe('finops-line healthy');
    expect(view.noSpend).toBe(false);
  });

  it('warnsAboutUnpricedModels', () => {
    const view = finopsView({ pricedModelCount: 1, registeredModelCount: 2, unpricedModelIds: ['free-one'] })!;
    expect(view.coverageClass).toBe('finops-line is-warn');
    expect(view.unpricedText).toContain('free-one');
    expect(view.noSpend).toBe(true);
  });
});

describe('by model', () => {
  const summary = {
    errorsPerModel: { a: 7, b: 3177, c: 0 },
    windows: [
      { window: '5m', perModel: [
        { modelId: 'small', requests: 10, errors: 0, errorRate: 0, latencyP95Ms: 240.6, ttftP95Ms: null, pricedCost: 0.19 },
        { modelId: 'big', requests: 72, errors: 20, errorRate: 0.2778, latencyP95Ms: 11250, ttftP95Ms: 982, pricedCost: null },
      ] },
    ],
  };

  it('errorModelBars_ranksModelsWithErrors', () => {
    expect(errorModelBars(summary).map((r) => r.key)).toEqual(['b', 'a']);
  });

  it('modelPerfRows_ranksByRequestsForTheChosenWindow', () => {
    const rows = modelPerfRows(summary, '5m');
    expect(rows.map((r) => r.modelId)).toEqual(['big', 'small']);
    expect(rows[0]).toMatchObject({ sharePct: 100, errorRateClass: 'num is-error', p95Text: '11.3s', costText: '—' });
    expect(rows[1]).toMatchObject({ sharePct: 14, errorRateText: '0%', ttftText: '—' });
    expect(modelPerfRows(summary, '1h')).toEqual([]);
  });
});

describe('activity, tenants, control plane', () => {
  it('activityRows_namesTheActorAndFlattensDetails', () => {
    const rows = activityRows({ entries: [
      { timestampUtc: '2026-10-10T06:02:53Z', action: 'panels.session.open', tenantSlug: 'default', apiKeyLabel: 'operator', details: '{"panels":["grafana"]}' },
    ] }, NOW);
    expect(rows[0]).toMatchObject({ ago: '29m ago', action: 'panels.session.open', actor: 'operator · default', details: 'panels: grafana' });
  });

  it('tenantsView_listsConsumersAndIdleKeys', () => {
    const view = tenantsView({
      currency: 'USD',
      tenantCount: 11,
      keyCount: 53,
      revokedKeyCount: 3,
      topConsumersMonthToDate: [{ tenantId: 't1', tenantSlug: 'default', requests: 281917, promptTokens: 6652662303, completionTokens: 221949579, cost: 6699.94, requests24h: 0 }],
      idleKeys: [{ id: 'k1', keyPrefix: 'sk-Autobahn-797', label: null, tenantSlug: 'default', createdAt: '2026-07-20T05:42:54Z', lastUsedAt: null }],
      expiringKeys: [],
      anonymousRequestShare: 0,
    }, NOW)!;
    expect(view.title).toBe('11 tenants · 53 keys · 3 revoked');
    expect(view.consumers[0]).toMatchObject({ who: 'default', requestsText: '282k', tokensText: '6.9B', recentText: '' });
    expect(view.idleKeys[0]).toMatchObject({ label: 'sk-Autobahn-797', tenant: 'default', whenText: 'never used · made 82d ago' });
    expect(view.anonymousText).toBe('');
    expect(view.quiet).toBe(false);
  });

  it('controlPlaneStats_flagsAFailedBackup', () => {
    const ok = controlPlaneStats({
      modelCount: 14,
      database: { configured: true, sizeBytes: 1691351064, path: '/data/gateway.db', journalMode: 'wal' },
      lastBackup: { attemptedAtUtc: '2026-10-10T02:15:02Z', succeeded: true, sizeBytes: 1609916416, integrityCheck: 'ok' },
      configLastReloadUtc: '2026-10-08T17:59:47Z',
      secrets: { undecryptable: 0 },
    }, { uptimeSeconds: 131552, controlPlane: { workingSetBytes: 506818560 }, pipeline: { usageWriterDropped: 0 } }, NOW);
    expect(ok.map((s) => s.key)).toEqual(['models', 'db', 'backup', 'reload', 'uptime', 'memory']);
    expect(ok.find((s) => s.key === 'backup')).toMatchObject({ value: '4h ago', cls: '' });
    expect(ok.find((s) => s.key === 'uptime')?.value).toBe('1d 12h');

    const failed = controlPlaneStats({ lastBackup: { attemptedAtUtc: '2026-10-10T02:15:02Z', succeeded: false, error: 'disk full' } }, null, NOW);
    expect(failed.find((s) => s.key === 'backup')).toMatchObject({ value: 'failed', cls: 'is-error', title: 'disk full' });
  });
});
