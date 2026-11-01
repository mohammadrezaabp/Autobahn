/**
 * View models for the Overview's detail cards: backends, policy pressure, rate limits, spend,
 * errors by model, the per-model table, recent activity, tenants and the control plane.
 *
 * Everything here is a pure function of what the gateway sent (`/admin/api/summary` and the
 * `/admin/api/overview/*` sections), so the cards hold markup only.
 */
import { formatCost, formatMsShort, formatNum, formatSharePct, formatTime } from './format';

type Rec = Record<string, unknown>;

function rec(v: unknown): Rec {
  return v && typeof v === 'object' && !Array.isArray(v) ? (v as Rec) : {};
}

function list(v: unknown): Rec[] {
  return Array.isArray(v) ? (v.filter((x) => x && typeof x === 'object') as Rec[]) : [];
}

function num(v: unknown, fallback = 0): number {
  const n = Number(v);
  return v === null || v === undefined || !Number.isFinite(n) ? fallback : n;
}

function text(v: unknown): string {
  return v === null || v === undefined ? '' : String(v);
}

/** 1234 -> 1.2k, 6873279392 -> 6.9B. Token and request totals that would not fit a column. */
export function compactNum(v: unknown): string {
  const n = num(v);
  const abs = Math.abs(n);
  if (abs < 1000) return String(Math.round(n));
  const units: Array<[number, string]> = [[1e9, 'B'], [1e6, 'M'], [1e3, 'k']];
  for (const [size, unit] of units) {
    if (abs >= size) {
      const scaled = n / size;
      return `${scaled.toFixed(Math.abs(scaled) >= 100 ? 0 : 1)}${unit}`;
    }
  }
  return String(n);
}

export function formatBytes(v: unknown): string {
  const n = num(v, -1);
  if (n < 0) return '—';
  if (n < 1024) return `${n} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let value = n / 1024;
  let i = 0;
  while (value >= 1024 && i < units.length - 1) {
    value /= 1024;
    i++;
  }
  return `${value.toFixed(value >= 100 ? 0 : 1)} ${units[i]}`;
}

/** "3m ago", "2h ago", "4d ago"; a dash when there is no time. */
export function ago(iso: unknown, now = Date.now()): string {
  if (!iso) return '—';
  const t = Date.parse(String(iso));
  if (!Number.isFinite(t)) return '—';
  const s = Math.max(0, Math.round((now - t) / 1000));
  if (s < 60) return `${s}s ago`;
  if (s < 3600) return `${Math.floor(s / 60)}m ago`;
  if (s < 86400) return `${Math.floor(s / 3600)}h ago`;
  return `${Math.floor(s / 86400)}d ago`;
}

export interface BarRow {
  key: string;
  label: string;
  value: string;
  /** Width of the bar, 0 to 100, relative to the largest row. */
  pct: number;
}

/** Ranked bars: the largest row fills the track and the rest are drawn against it. */
export function barRows(
  rows: ReadonlyArray<{ key: string; label?: string; amount: number }>,
  format: (amount: number) => string = (a) => formatNum(a),
  limit = 6,
): BarRow[] {
  const ranked = rows.filter((r) => r.amount > 0).sort((a, b) => b.amount - a.amount).slice(0, limit);
  const max = ranked[0]?.amount ?? 0;
  return ranked.map((r) => ({
    key: r.key,
    label: r.label || r.key,
    value: format(r.amount),
    pct: max > 0 ? Math.max(2, Math.round((r.amount / max) * 100)) : 0,
  }));
}

/** Money in a headline or a ranked row: two decimals, where the Usage page keeps four. */
export function money(v: unknown, currency = 'USD'): string {
  const n = num(v);
  if (n !== 0 && Math.abs(n) < 0.01) return formatCost(n, currency);
  try {
    return new Intl.NumberFormat(undefined, { style: 'currency', currency, minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(n);
  } catch {
    return n.toFixed(2);
  }
}

// ---------------------------------------------------------------- backends

export interface BackendRowView {
  key: string;
  modelId: string;
  alias: string;
  url: string;
  dotClass: string;
  healthTitle: string;
  circuitText: string;
  circuitClass: string;
  loadPct: number | null;
  loadClass: string;
  loadText: string;
  errorRateText: string;
  errorRateClass: string;
  p95Text: string;
  checkedText: string;
  error: string;
}

const CIRCUIT_CLASS: Record<string, string> = {
  closed: 'tag live',
  open: 'tag level-error',
  'half-open': 'tag warn',
  halfopen: 'tag warn',
};

export function backendRows(summary: Rec | null, now = Date.now()): BackendRowView[] {
  return list(summary?.backends)
    .map((b) => {
      const healthy = b.isHealthy !== false;
      const probed = !!b.lastCheckedUtc;
      const circuit = text(b.circuitState || 'unknown').toLowerCase();
      const inFlight = num(b.inFlight);
      const max = num(b.maxConcurrent);
      const ratio = max > 0 ? inFlight / max : null;
      const errorRate = b.errorRate5m === null || b.errorRate5m === undefined ? null : num(b.errorRate5m);
      // One row per backend. A model with a single url has one backend named "default", which
      // is not worth showing; any other name tells the rows of one model apart.
      const backend = text(b.backend);
      const named = backend !== '' && backend !== 'default';
      return {
        key: `${text(b.modelId)}\u001f${backend}`,
        modelId: named ? `${text(b.modelId)} / ${backend}` : text(b.modelId),
        alias: text(b.alias),
        url: text(b.url),
        dotClass: !healthy ? 'dot-fail' : probed ? 'dot-ok' : 'dot-idle',
        healthTitle: !healthy ? 'Unhealthy' : probed ? 'Healthy' : 'Not probed yet',
        circuitText: circuit === 'unknown' ? 'no traffic' : circuit,
        circuitClass: CIRCUIT_CLASS[circuit] ?? 'tag muted',
        loadPct: ratio === null ? null : Math.min(100, Math.round(ratio * 100)),
        loadClass: ratio !== null && ratio >= 1 ? 'load-fill is-over' : ratio !== null && ratio >= 0.8 ? 'load-fill is-hot' : 'load-fill',
        loadText: max > 0 ? `${inFlight} / ${max}` : `${inFlight} in flight`,
        errorRateText: errorRate === null ? '—' : formatSharePct(errorRate),
        errorRateClass: errorRate === null ? 'num' : errorRate >= 0.25 ? 'num is-error' : errorRate >= 0.05 ? 'num is-warn' : 'num',
        p95Text: formatMsShort(b.latencyP95Ms5m),
        checkedText: probed ? ago(b.lastCheckedUtc, now) : 'not probed',
        error: text(b.error),
        _healthy: healthy,
        _requests: num(b.requests5m),
      };
    })
    .sort((a, b) => Number(a._healthy) - Number(b._healthy) || b._requests - a._requests)
    .map(({ _healthy, _requests, ...row }) => row);
}

export function backendsHealth(summary: Rec | null): { text: string; cls: string } {
  const all = list(summary?.backends);
  const healthy = all.filter((b) => b.isHealthy !== false).length;
  return {
    text: `${healthy} / ${all.length} healthy`,
    cls: all.length > 0 && healthy === all.length ? 'status-chip ok' : healthy === 0 && all.length > 0 ? 'status-chip fail' : 'status-chip warn',
  };
}

// ---------------------------------------------------------------- tenant names

/** Tenant id to slug, from the two sections that carry both. */
export function tenantSlugs(policyBody: Rec | null, tenantsBody: Rec | null): Map<string, string> {
  const map = new Map<string, string>();
  for (const q of list(policyBody?.quotas)) {
    if (q.partitionKey && q.tenantSlug) map.set(text(q.partitionKey), text(q.tenantSlug));
  }
  for (const t of list(tenantsBody?.topConsumersMonthToDate)) {
    if (t.tenantId && t.tenantSlug) map.set(text(t.tenantId), text(t.tenantSlug));
  }
  return map;
}

/** A rejection key is a tenant id, sometimes followed by `|model`; show the slug where known. */
function nameSubject(key: string, slugs: ReadonlyMap<string, string>): string {
  const [id, ...rest] = key.split('|');
  const who = slugs.get(id) ?? (id.length > 20 && /^[0-9a-f-]+$/i.test(id) ? `${id.slice(0, 8)}…` : id);
  return rest.length ? `${who} → ${rest.join('|')}` : who;
}

// ---------------------------------------------------------------- policy pressure

const REASON_LABEL: Record<string, string> = {
  rate_limit: 'rate limit',
  grant_denied: 'model not granted',
  quota: 'quota',
  budget: 'budget',
  unknown_model: 'unknown model',
};

export interface PolicyView {
  rateLimitedText: string;
  rateLimitedClass: string;
  quotaBlockedText: string;
  quotaBlockedClass: string;
  reasonBars: BarRow[];
  tenantBars: BarRow[];
  modelBars: BarRow[];
  grantDenialBars: BarRow[];
  unknownModelBars: BarRow[];
  quotaRows: Array<{ key: string; who: string; plan: string; pct: number; pctText: string; fillClass: string; usedText: string }>;
  quiet: boolean;
}

function countBars(rows: unknown, label: (key: string) => string): BarRow[] {
  return barRows(list(rows).map((r) => ({ key: text(r.key), label: text(r.label) || label(text(r.key)), amount: num(r.count) })));
}

export function policyView(summary: Rec | null, policyBody: Rec | null, slugs: ReadonlyMap<string, string>): PolicyView {
  const policy = rec(summary?.policy);
  const rateLimited = num(summary?.rateLimitRejections);
  const quotaBlocked = num(summary?.quotaRejections);
  const reasonBars = countBars(policy.rejectionsByReason1h, (k) => REASON_LABEL[k] ?? k.replace(/_/g, ' '));
  // A quota with no limit is only a usage counter; it belongs on Usage & cost, not here.
  const quotaRows = list(policyBody?.quotas)
    .filter((q) => num(q.limit) > 0)
    .sort((a, b) => num(b.ratio) - num(a.ratio))
    .slice(0, 5)
    .map((q) => {
      const ratio = num(q.ratio);
      return {
        key: text(q.partitionKey),
        who: text(q.tenantSlug) || nameSubject(text(q.partitionKey), slugs),
        plan: text(q.planSlug),
        pct: Math.min(100, Math.round(ratio * 100)),
        pctText: formatSharePct(ratio),
        fillClass: q.exceeded ? 'load-fill is-over' : q.nearLimit ? 'load-fill is-hot' : 'load-fill',
        usedText: `${compactNum(q.used)} / ${compactNum(q.limit)} tokens`,
      };
    });
  const grantDenialBars = countBars(policy.grantDenials1h, (k) => nameSubject(k, slugs));
  const unknownModelBars = countBars(policy.unknownModels1h, (k) => k);
  return {
    rateLimitedText: formatNum(rateLimited),
    rateLimitedClass: rateLimited > 0 ? 'mini-stat warn' : 'mini-stat',
    quotaBlockedText: formatNum(quotaBlocked),
    quotaBlockedClass: quotaBlocked > 0 ? 'mini-stat warn' : 'mini-stat',
    reasonBars,
    tenantBars: countBars(policy.rejectionsByTenant1h, (k) => nameSubject(k, slugs)),
    modelBars: countBars(policy.rejectionsByModel1h, (k) => k),
    grantDenialBars,
    unknownModelBars,
    quotaRows,
    quiet: reasonBars.length === 0 && grantDenialBars.length === 0 && unknownModelBars.length === 0 && !quotaRows.some((q) => q.pct >= 80),
  };
}

// ---------------------------------------------------------------- rate limits

export interface RateLimitsView {
  title: string;
  headStats: Array<{ key: string; label: string; value: string; cls: string }>;
  notEnforced: boolean;
  limitRows: Array<{ key: string; label: string; pct: number | null; fillClass: string; peakText: string; countText: string; countClass: string }>;
  tenantBars: BarRow[];
  keyBars: BarRow[];
  hints: string[];
  quiet: boolean;
}

function refusedBars(rows: unknown): BarRow[] {
  return barRows(
    list(rows).map((r) => ({
      key: text(r.key ?? r.id ?? r.tenantId ?? r.apiKeyId ?? r.label),
      label: text(r.label ?? r.tenantSlug ?? r.keyLabel ?? r.keyPrefix ?? r.key ?? r.id),
      amount: num(r.refused ?? r.count),
    })),
  );
}

/** Null when the gateway has no usage tracker (the section answered 204 or `available: false`). */
export function rateLimitsView(body: Rec | null): RateLimitsView | null {
  if (!body || body.available === false) return null;
  const hour = rec(body.lastHour);
  const five = rec(body.lastFiveMinutes);
  const refused = num(hour.refused);
  const adaptive = rec(body.adaptive);
  const retention = rec(body.retention);
  const hints: string[] = [];
  if (num(five.refused) > 0) {
    hints.push(`${formatNum(five.refused)} refused in the last 5 minutes (${formatSharePct(five.refusalShare)} of decisions).`);
  }
  const protective = list(body.protective).filter((p) => num(p.refused) > 0);
  if (protective.length) {
    hints.push(`Protective limits refused ${protective.map((p) => `${formatNum(p.refused)} (${text(p.scope).replace(/_/g, ' ')})`).join(', ')}.`);
  }
  if (adaptive.enabled) {
    hints.push(
      num(adaptive.modelsReduced) > 0
        ? `Adaptive limiting has reduced ${formatNum(adaptive.modelsReduced)} model(s); lowest factor ${num(adaptive.lowestFactor).toFixed(2)} on ${text(adaptive.lowestFactorModelId)}.`
        : 'Adaptive limiting is on and not reducing any model.',
    );
  }
  if (retention.historyMinutes) {
    hints.push(`Kept in memory for about ${Math.round(num(retention.historyMinutes) / 60)} hours, since ${formatTime(retention.trackingSinceUtc)}.`);
  }
  return {
    title: refused > 0 ? `${formatNum(refused)} refused in the last hour` : 'No refusals in the last hour',
    headStats: [
      { key: 'decisions', label: 'Decisions', value: compactNum(hour.decisions), cls: 'mini-stat' },
      { key: 'refused', label: 'Refused', value: formatNum(refused), cls: refused > 0 ? 'mini-stat warn' : 'mini-stat' },
      { key: 'rules', label: 'Rules', value: formatNum(num(body.ruleCount) - num(body.disabledRuleCount)), cls: 'mini-stat' },
    ],
    notEnforced: body.enforced === false,
    limitRows: list(body.limits).slice(0, 6).map((l, i) => {
      const peak = l.peakUtilization === null || l.peakUtilization === undefined ? null : num(l.peakUtilization);
      const count = num(l.refused);
      return {
        key: text(l.limitId ?? l.target ?? i),
        label: text(l.label ?? l.target ?? l.limitId ?? l.scope),
        pct: peak === null ? null : Math.min(100, Math.round(peak * 100)),
        fillClass: peak !== null && peak >= 1 ? 'load-fill is-over' : peak !== null && peak >= 0.8 ? 'load-fill is-hot' : 'load-fill',
        peakText: peak === null ? '' : `peak ${formatSharePct(peak)}`,
        countText: count > 0 ? `${formatNum(count)} refused` : 'no refusals',
        countClass: count > 0 ? 'num is-error' : 'num muted',
      };
    }),
    tenantBars: refusedBars(body.topRefusedTenants),
    keyBars: refusedBars(body.topRefusedKeys),
    hints,
    quiet: refused === 0,
  };
}

// ---------------------------------------------------------------- spend

export interface FinOpsView {
  tiles: Array<{ key: string; label: string; value: string; foot: string }>;
  modelBars: BarRow[];
  costCenterBars: BarRow[];
  budgetRows: Array<{ key: string; name: string; pct: number; pctText: string; fillClass: string; spendText: string }>;
  noSpend: boolean;
  coverageText: string;
  coverageClass: string;
  unpricedText: string;
  reconText: string;
  reconClass: string;
}

export function finopsView(body: Rec | null): FinOpsView | null {
  if (!body) return null;
  const currency = text(body.currency) || 'USD';
  const cost = (v: unknown) => money(v, currency);
  const costBars = (rows: unknown) =>
    barRows(list(rows).map((r) => ({ key: text(r.key), amount: num(r.cost) })), (a) => cost(a), 5);
  const priced = num(body.pricedModelCount);
  const registered = num(body.registeredModelCount);
  const unpriced = Array.isArray(body.unpricedModelIds) ? (body.unpricedModelIds as unknown[]).map(text) : [];
  const recon = rec(body.reconciliation);
  const drift = num(recon.discrepancyCount);
  return {
    tiles: [
      { key: 'today', label: 'Today', value: cost(body.todayCost), foot: `${formatNum(body.todayRequests)} requests · yesterday ${cost(body.yesterdayCost)}` },
      { key: 'mtd', label: 'Month to date', value: cost(body.monthToDateCost), foot: `${formatNum(body.monthToDateRequests)} requests` },
      { key: 'projected', label: 'Projected month', value: cost(body.projectedMonthlyCost), foot: `${cost(body.averageDailyCost)} a day on average` },
      { key: 'tokens', label: 'Tokens today', value: compactNum(num(body.todayPromptTokens) + num(body.todayCompletionTokens)), foot: `${compactNum(body.todayPromptTokens)} in · ${compactNum(body.todayCompletionTokens)} out` },
    ],
    modelBars: costBars(body.topModelsMonthToDate),
    costCenterBars: costBars(body.topCostCentersMonthToDate),
    budgetRows: list(body.budgets).slice(0, 5).map((b, i) => {
      const ratio = num(b.ratio);
      return {
        key: text(b.id ?? b.name ?? i),
        name: text(b.name),
        pct: Math.min(100, Math.round(ratio * 100)),
        pctText: formatSharePct(ratio),
        fillClass: ratio >= 1 ? 'load-fill is-over' : ratio >= 0.8 ? 'load-fill is-hot' : 'load-fill',
        spendText: `${cost(b.spent)} of ${cost(b.limit)}`,
      };
    }),
    noSpend: num(body.monthToDateCost) <= 0,
    coverageText: `${priced} of ${registered} models priced.`,
    coverageClass: registered > 0 && priced >= registered ? 'finops-line healthy' : 'finops-line is-warn',
    unpricedText: unpriced.length ? `Not priced, so their traffic counts as free: ${unpriced.join(', ')}.` : '',
    reconText: !recon.enabled
      ? ''
      : drift === 0
        ? `Billing reconciled ${ago(recon.lastRunUtc)}: ${formatNum(recon.bucketsCompared)} buckets, no drift.`
        : `Billing reconciliation found ${formatNum(drift)} discrepancies (${cost(recon.absoluteCostDrift)} drift).`,
    reconClass: drift === 0 ? 'finops-line healthy' : 'finops-line is-error',
  };
}

// ---------------------------------------------------------------- errors and traffic by model

export function errorModelBars(summary: Rec | null): BarRow[] {
  return barRows(Object.entries(rec(summary?.errorsPerModel)).map(([key, count]) => ({ key, amount: num(count) })), undefined, 8);
}

export interface ModelPerfRow {
  key: string;
  modelId: string;
  requestsText: string;
  sharePct: number;
  errorRateText: string;
  errorRateClass: string;
  errorRateTitle: string;
  p95Text: string;
  ttftText: string;
  costText: string;
}

/** Per-model numbers for one trailing window; empty when the gateway sent no windows. */
export function modelPerfRows(summary: Rec | null, window: string): ModelPerfRow[] {
  const match = list(summary?.windows).find((w) => w.window === window);
  const rows = list(match?.perModel).sort((a, b) => num(b.requests) - num(a.requests));
  const max = num(rows[0]?.requests);
  return rows.map((m) => {
    const errorRate = num(m.errorRate);
    return {
      key: text(m.modelId),
      modelId: text(m.modelId),
      requestsText: formatNum(m.requests),
      sharePct: max > 0 ? Math.max(2, Math.round((num(m.requests) / max) * 100)) : 0,
      errorRateText: num(m.errors) > 0 ? formatSharePct(errorRate) : '0%',
      errorRateClass: errorRate >= 0.25 ? 'num is-error' : errorRate >= 0.05 ? 'num is-warn' : 'num',
      errorRateTitle: `${formatNum(m.errors)} of ${formatNum(m.requests)} requests failed`,
      p95Text: formatMsShort(m.latencyP95Ms),
      ttftText: formatMsShort(m.ttftP95Ms),
      costText: m.pricedCost === null || m.pricedCost === undefined ? '—' : money(m.pricedCost),
    };
  });
}

// ---------------------------------------------------------------- activity

export interface ActivityRow {
  key: string;
  ago: string;
  time: string;
  action: string;
  actor: string;
  details: string;
}

/** `{"succeeded":true,"path":"/x"}` reads as `succeeded: true · path: /x` in one table cell. */
export function flattenDetails(raw: unknown): string {
  const source = text(raw).trim();
  if (!source) return '';
  try {
    const parsed: unknown = JSON.parse(source);
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return source;
    return Object.entries(parsed as Rec)
      .filter(([, v]) => v !== null && v !== '')
      .map(([k, v]) => `${k}: ${typeof v === 'object' ? (Array.isArray(v) ? v.map(text).join(', ') : JSON.stringify(v)) : text(v)}`)
      .join(' · ');
  } catch {
    return source;
  }
}

export function activityRows(body: Rec | null, now = Date.now(), limit = 8): ActivityRow[] {
  return list(body?.entries).slice(0, limit).map((e, i) => ({
    key: `${text(e.timestampUtc)}-${i}`,
    ago: ago(e.timestampUtc, now),
    time: formatTime(e.timestampUtc),
    action: text(e.action),
    actor: [text(e.apiKeyLabel), text(e.tenantSlug)].filter(Boolean).join(' · ') || '—',
    details: flattenDetails(e.details),
  }));
}

// ---------------------------------------------------------------- tenants and keys

export interface TenantsView {
  title: string;
  consumers: Array<{ key: string; who: string; plan: string; requestsText: string; tokensText: string; costText: string; recentText: string }>;
  idleKeys: Array<{ key: string; label: string; tenant: string; whenText: string }>;
  expiringKeys: Array<{ key: string; label: string; tenant: string; whenText: string }>;
  anonymousText: string;
  quiet: boolean;
}

export function tenantsView(body: Rec | null, now = Date.now()): TenantsView | null {
  if (!body) return null;
  const currency = text(body.currency) || 'USD';
  const keyRow = (k: Rec, whenText: string) => ({
    key: text(k.id ?? k.keyPrefix),
    label: text(k.label) || text(k.keyPrefix),
    tenant: text(k.tenantSlug),
    whenText,
  });
  const consumers = list(body.topConsumersMonthToDate).slice(0, 6).map((t) => ({
    key: text(t.tenantId ?? t.tenantSlug),
    who: text(t.tenantSlug) || '(unknown)',
    plan: text(t.planSlug),
    requestsText: compactNum(t.requests),
    tokensText: compactNum(num(t.promptTokens) + num(t.completionTokens)),
    costText: money(t.cost, currency),
    recentText: num(t.requests24h) > 0 ? `${compactNum(t.requests24h)} in 24h` : '',
  }));
  const idleKeys = list(body.idleKeys).slice(0, 5).map((k) =>
    keyRow(k, k.lastUsedAt ? `used ${ago(k.lastUsedAt, now)}` : `never used · made ${ago(k.createdAt, now)}`));
  const expiringKeys = list(body.expiringKeys).slice(0, 5).map((k) => keyRow(k, `expires ${formatTime(k.expiresAt)}`));
  const share = num(body.anonymousRequestShare);
  const revoked = num(body.revokedKeyCount);
  return {
    title: `${formatNum(body.tenantCount)} tenants · ${formatNum(body.keyCount)} keys${revoked > 0 ? ` · ${formatNum(revoked)} revoked` : ''}`,
    consumers,
    idleKeys,
    expiringKeys,
    anonymousText: share > 0 ? `${formatSharePct(share)} of requests this month came without a key.` : '',
    quiet: consumers.length === 0 && idleKeys.length === 0 && expiringKeys.length === 0,
  };
}

// ---------------------------------------------------------------- control plane

export interface ControlPlaneStat {
  key: string;
  label: string;
  value: string;
  cls: string;
  title: string;
}

export function controlPlaneStats(body: Rec | null, summary: Rec | null, now = Date.now()): ControlPlaneStat[] {
  if (!body && !summary) return [];
  const backup = rec(body?.lastBackup);
  const database = rec(body?.database);
  const secrets = rec(body?.secrets);
  const runtime = rec(summary?.controlPlane);
  const pipeline = rec(summary?.pipeline);
  const stats: ControlPlaneStat[] = [];
  const add = (key: string, label: string, value: string, cls = '', title = '') => stats.push({ key, label, value, cls, title });
  if (body) {
    add('models', 'Models', formatNum(body.modelCount));
    if (database.configured) add('db', 'Database', formatBytes(database.sizeBytes), '', `${text(database.path)} · ${text(database.journalMode)}`);
    if (backup.attemptedAtUtc) {
      add('backup', 'Last backup', backup.succeeded ? ago(backup.attemptedAtUtc, now) : 'failed', backup.succeeded ? '' : 'is-error',
        backup.succeeded ? `${formatTime(backup.attemptedAtUtc)} · ${formatBytes(backup.sizeBytes)} · integrity ${text(backup.integrityCheck)}` : text(backup.error));
    } else {
      add('backup', 'Last backup', 'never', 'is-warn');
    }
    add('reload', 'Config reload', ago(body.configLastReloadUtc, now), '', formatTime(body.configLastReloadUtc));
    if (num(secrets.undecryptable) > 0) add('secrets', 'Unreadable secrets', formatNum(secrets.undecryptable), 'is-error');
  }
  if (summary) {
    add('uptime', 'Uptime', uptimeText(summary.uptimeSeconds));
    if (runtime.workingSetBytes) add('memory', 'Memory', formatBytes(runtime.workingSetBytes), '', `GC heap ${formatBytes(runtime.gcHeapBytes)}`);
    const dropped = num(pipeline.usageWriterDropped);
    if (dropped > 0) add('dropped', 'Usage rows dropped', formatNum(dropped), 'is-error', 'The usage writer queue overflowed; billing is missing these requests.');
  }
  return stats;
}

function uptimeText(seconds: unknown): string {
  const s = num(seconds);
  if (s <= 0) return '—';
  const d = Math.floor(s / 86400);
  const h = Math.floor((s % 86400) / 3600);
  const m = Math.floor((s % 3600) / 60);
  return d > 0 ? `${d}d ${h}h` : h > 0 ? `${h}h ${m}m` : `${m}m`;
}
