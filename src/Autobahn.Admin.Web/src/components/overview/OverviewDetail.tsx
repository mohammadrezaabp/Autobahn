import { For, Show, createMemo } from 'solid-js';
import type { JSX } from 'solid-js';
import { Button } from '../primitives';
import { IconBarChart, IconBug, IconGitBranch, IconKey, IconServer, IconSettings } from '../icons';
import {
  activityRows,
  backendRows,
  backendsHealth,
  controlPlaneStats,
  errorModelBars,
  finopsView,
  modelPerfRows,
  policyView,
  rateLimitsView,
  tenantSlugs,
  tenantsView,
  type BarRow,
} from '../../domain/overviewDetail';

type Rec = Record<string, unknown>;

export interface OverviewDetailProps {
  summary: Rec | null;
  window: string;
  windowLabel: string;
  finops: Rec | null;
  policy: Rec | null;
  controlPlane: Rec | null;
  activity: Rec | null;
  tenants: Rec | null;
  rateLimits: Rec | null;
  errors: Partial<Record<'finops' | 'policy' | 'controlPlane' | 'activity' | 'tenants' | 'rateLimits', string>>;
  navigate: (path: string) => void;
}

function Bars(props: { rows: BarRow[]; error?: boolean; mono?: boolean; onOpen?: (row: BarRow) => void }) {
  return (
    <div class="hbars">
      <For each={props.rows}>
        {(row) => {
          const cells = (
            <>
              <span class="hbar-label" classList={{ mono: props.mono }} title={row.label}>{row.label}</span>
              <span class="hbar-track"><span class="hbar-fill" style={{ width: `${row.pct}%` }} /></span>
              <span class="hbar-value">{row.value}</span>
            </>
          );
          return (
            <Show when={props.onOpen} fallback={<div class="hbar" classList={{ 'is-error': props.error }}>{cells}</div>}>
              <button type="button" class="hbar is-clickable" classList={{ 'is-error': props.error }} onClick={() => props.onOpen?.(row)}>
                {cells}
              </button>
            </Show>
          );
        }}
      </For>
    </div>
  );
}

function CardTitle(props: { eyebrow: string; title: string; children?: JSX.Element }) {
  return (
    <div class="card-title-row">
      <div><span class="eyebrow">{props.eyebrow}</span><h3>{props.title}</h3></div>
      {props.children}
    </div>
  );
}

/**
 * The Overview's detail cards, below the vitals. One family: an eyebrow and a title on the left,
 * a headline figure or an action on the right, then ranked rows.
 */
export function OverviewDetail(props: OverviewDetailProps) {
  const backends = createMemo(() => backendRows(props.summary));
  const health = createMemo(() => backendsHealth(props.summary));
  const slugs = createMemo(() => tenantSlugs(props.policy, props.tenants));
  const policy = createMemo(() => policyView(props.summary, props.policy, slugs()));
  const limits = createMemo(() => rateLimitsView(props.rateLimits));
  const finops = createMemo(() => finopsView(props.finops));
  const errorBars = createMemo(() => errorModelBars(props.summary));
  const perf = createMemo(() => modelPerfRows(props.summary, props.window));
  const activity = createMemo(() => activityRows(props.activity));
  const tenants = createMemo(() => tenantsView(props.tenants));
  const control = createMemo(() => controlPlaneStats(props.controlPlane, props.summary));

  const openErrors = (modelId?: string) =>
    props.navigate(modelId ? `/errors?model=${encodeURIComponent(modelId)}` : '/errors');

  return (
    <>
      <div class="chart-row ov-row">
        <div class="card backends-card">
          <CardTitle eyebrow="Routing" title="Backends & health">
            <span class={health().cls}>{health().text}</span>
          </CardTitle>
          <Show when={backends().length > 0} fallback={<p class="hint">No models registered.  <strong>Routing</strong>.</p>}>
            <div class="backend-rows">
              <For each={backends()}>
                {(bk) => (
                  <button type="button" class="backend-row" title={bk.url} onClick={() => props.navigate('/routing?sub=backends')}>
                    <span class={`health-dot ${bk.dotClass}`} title={bk.healthTitle} />
                    <span class="backend-model">
                      <span class="backend-id">{bk.modelId}</span>
                      <Show when={bk.alias}><span class="backend-alias muted">{bk.alias}</span></Show>
                    </span>
                    <span class={bk.circuitClass} title="Circuit breaker">{bk.circuitText}</span>
                    <span class="backend-load">
                      <Show when={bk.loadPct !== null}>
                        <span class="load-track"><span class={bk.loadClass} style={{ width: `${bk.loadPct ?? 0}%` }} /></span>
                      </Show>
                      <span class="backend-load-text">{bk.loadText}</span>
                    </span>
                    <span class={bk.errorRateClass} title="Error rate, last 5 minutes">{bk.errorRateText}</span>
                    <span class="num" title="p95 latency, last 5 minutes">{bk.p95Text}</span>
                    <span class="backend-checked muted" title="Last health probe">{bk.checkedText}</span>
                    <Show when={bk.error}><span class="backend-error">{bk.error}</span></Show>
                  </button>
                )}
              </For>
            </div>
          </Show>
          <div class="card-foot-actions wb-hide">
            <Button variant="secondary" onClick={() => props.navigate('/routing?sub=backends')}>
              <span class="icon"><IconServer /></span> All backends
            </Button>
          </div>
        </div>

        <div class="ov-stack">
          <div class="card policy-card">
            <CardTitle eyebrow="Policy" title="Pressure">
              <span class="mini-stats policy-totals">
                <span class={policy().rateLimitedClass} title="Rate-limit rejections since the counters were last reset">
                  <span class="ms-label">Rate-limited</span><span class="ms-value">{policy().rateLimitedText}</span>
                </span>
                <span class={policy().quotaBlockedClass} title="Quota and budget rejections since the counters were last reset">
                  <span class="ms-label">Quota-blocked</span><span class="ms-value">{policy().quotaBlockedText}</span>
                </span>
              </span>
            </CardTitle>
            <Show when={props.errors.policy}><p class="notice warn">{props.errors.policy}</p></Show>
            <Show when={policy().reasonBars.length > 0}>
              <div class="policy-section">
                <span class="eyebrow">Rejections by reason · last hour</span>
                <Bars rows={policy().reasonBars} error />
              </div>
            </Show>
            <div class="policy-columns wb-hide">
              <Show when={policy().tenantBars.length > 0}>
                <div class="policy-section"><span class="eyebrow">By tenant</span><Bars rows={policy().tenantBars} /></div>
              </Show>
              <Show when={policy().modelBars.length > 0}>
                <div class="policy-section"><span class="eyebrow">By model</span><Bars rows={policy().modelBars} mono /></div>
              </Show>
              <Show when={policy().grantDenialBars.length > 0}>
                <div class="policy-section"><span class="eyebrow">Keys denied a model</span><Bars rows={policy().grantDenialBars} /></div>
              </Show>
              <Show when={policy().unknownModelBars.length > 0}>
                <div class="policy-section"><span class="eyebrow">Unknown models requested</span><Bars rows={policy().unknownModelBars} mono /></div>
              </Show>
            </div>
            <Show when={policy().quotaRows.length > 0}>
              <div class="policy-section wb-hide">
                <span class="eyebrow">Monthly token quota</span>
                <div class="quota-rows">
                  <For each={policy().quotaRows}>
                    {(q) => (
                      <div class="budget-row">
                        <span class="budget-name">{q.who}<Show when={q.plan}><span class="tag muted">{q.plan}</span></Show></span>
                        <span class="load-track"><span class={q.fillClass} style={{ width: `${q.pct}%` }} /></span>
                        <span class="tag">{q.pctText}</span>
                        <span class="budget-spend mono">{q.usedText}</span>
                      </div>
                    )}
                  </For>
                </div>
              </div>
            </Show>
            <Show when={policy().quiet}>
              <p class="hint healthy">No rejections in the last hour and every quota has headroom.</p>
            </Show>
          </div>

          <Show when={limits()}>
            {(rl) => (
              <div class="card rate-limits-card">
                <CardTitle eyebrow="Policy · rate limits" title={rl().title}>
                  <span class="mini-stats policy-totals">
                    <For each={rl().headStats}>
                      {(m) => <span class={m.cls}><span class="ms-label">{m.label}</span><span class="ms-value">{m.value}</span></span>}
                    </For>
                  </span>
                </CardTitle>
                <Show when={props.errors.rateLimits}><p class="notice warn">{props.errors.rateLimits}</p></Show>
                <Show when={rl().notEnforced}>
                  <p class="notice warn">Rate limiting is switched off: these are the decisions it would have made.</p>
                </Show>
                <Show when={rl().limitRows.length > 0}>
                  <div class="policy-section wb-hide">
                    <span class="eyebrow">Limits that refused, or ran near their rate · last hour</span>
                    <div class="rl-ov-limits">
                      <For each={rl().limitRows}>
                        {(l) => (
                          <div class="rl-ov-limit">
                            <span class="rl-ov-limit-label" title={l.label}>{l.label}</span>
                            <Show when={l.pct !== null}>
                              <span class="load-track"><span class={l.fillClass} style={{ width: `${l.pct ?? 0}%` }} /></span>
                              <span class="rl-ov-peak">{l.peakText}</span>
                            </Show>
                            <span class={`tag ${l.countClass}`}>{l.countText}</span>
                          </div>
                        )}
                      </For>
                    </div>
                  </div>
                </Show>
                <div class="policy-columns wb-hide">
                  <Show when={rl().tenantBars.length > 0}>
                    <div class="policy-section"><span class="eyebrow">Refused tenants</span><Bars rows={rl().tenantBars} error /></div>
                  </Show>
                  <Show when={rl().keyBars.length > 0}>
                    <div class="policy-section"><span class="eyebrow">Refused API keys</span><Bars rows={rl().keyBars} error /></div>
                  </Show>
                </div>
                <For each={rl().hints}>{(h) => <p class="hint wb-hide">{h}</p>}</For>
                <div class="card-foot-actions wb-hide">
                  <Button variant="secondary" onClick={() => props.navigate('/ratelimits')}>
                    <span class="icon"><IconSettings /></span> Rate limits
                  </Button>
                </div>
              </div>
            )}
          </Show>
        </div>
      </div>

      <div class="chart-row ov-row">
        <Show when={finops()}>
          {(f) => (
            <div class="card finops-card">
              <CardTitle eyebrow="FinOps" title="Spend">
                <Button variant="secondary" onClick={() => props.navigate('/usage')}>
                  <span class="icon"><IconBarChart /></span> Usage &amp; cost
                </Button>
              </CardTitle>
              <Show when={props.errors.finops}><p class="notice warn">{props.errors.finops}</p></Show>
              <div class="finops-tiles">
                <For each={f().tiles}>
                  {(t) => (
                    <span class="mini-stat" title={t.foot}>
                      <span class="ms-label">{t.label}</span>
                      <span class="ms-value">{t.value}</span>
                      <span class="ms-foot">{t.foot}</span>
                    </span>
                  )}
                </For>
              </div>
              <Show when={f().noSpend}><p class="hint">No priced spend this month yet.</p></Show>
              <div class="policy-columns">
                <Show when={f().modelBars.length > 0}>
                  <div class="finops-bars"><span class="eyebrow">By model · month to date</span><Bars rows={f().modelBars} mono /></div>
                </Show>
                <Show when={f().costCenterBars.length > 0}>
                  <div class="finops-bars"><span class="eyebrow">By cost center · month to date</span><Bars rows={f().costCenterBars} /></div>
                </Show>
              </div>
              <Show when={f().budgetRows.length > 0}>
                <div class="finops-budgets">
                  <span class="eyebrow">Budgets · this period</span>
                  <For each={f().budgetRows}>
                    {(b) => (
                      <div class="budget-row">
                        <span class="budget-name">{b.name}</span>
                        <span class="load-track"><span class={b.fillClass} style={{ width: `${b.pct}%` }} /></span>
                        <span class="tag">{b.pctText}</span>
                        <span class="budget-spend mono">{b.spendText}</span>
                      </div>
                    )}
                  </For>
                </div>
              </Show>
              <p class={f().coverageClass}>{f().coverageText}</p>
              <Show when={f().unpricedText}>
                <p class="notice warn">
                  {f().unpricedText}{' '}
                  <button type="button" class="link-btn" onClick={() => props.navigate('/routing?sub=models')}>Set prices</button>
                </p>
              </Show>
              <Show when={f().reconText}><p class={f().reconClass}>{f().reconText}</p></Show>
            </div>
          )}
        </Show>

        <div class="ov-stack">
          <div class="card">
            <CardTitle eyebrow="Reliability" title="Errors by model">
              <Button variant="secondary" class="wb-hide" onClick={() => openErrors()}>
                <span class="icon"><IconBug /></span> View all
              </Button>
            </CardTitle>
            <Show when={errorBars().length > 0} fallback={<p class="hint healthy">No errors — every model is clean.</p>}>
              <Bars rows={errorBars()} error mono onOpen={(row) => openErrors(row.key)} />
            </Show>
          </div>

          <Show when={control().length > 0}>
            <div class="card cp-card wb-hide">
              <CardTitle eyebrow="Control plane" title="Gateway" />
              <Show when={props.errors.controlPlane}><p class="notice warn">{props.errors.controlPlane}</p></Show>
              <dl class="glance-stats ov-cp-stats">
                <For each={control()}>
                  {(s) => (
                    <div class="glance-stat" title={s.title}>
                      <dt>{s.label}</dt>
                      <dd class={s.cls}>{s.value}</dd>
                    </div>
                  )}
                </For>
              </dl>
            </div>
          </Show>
        </div>
      </div>

      <div class="card model-perf-card">
        <CardTitle eyebrow="Traffic" title={`By model · ${props.windowLabel}`} />
        <Show when={perf().length > 0} fallback={<p class="hint">No traffic in this window.</p>}>
          <div class="table-wrap">
            <table class="model-perf-table">
              <colgroup><col /><col /><col /><col /><col /><col /><col /><col /></colgroup>
              <thead>
                <tr>
                  <th>Model</th><th class="num">Requests</th><th>Share</th>
                  <th class="num" title="Errors ÷ requests">Errors</th>
                  <th class="num">p95</th><th class="num">TTFT p95</th>
                  <th class="num" title="Priced spend in the window">Cost</th><th />
                </tr>
              </thead>
              <tbody>
                <For each={perf()}>
                  {(m) => (
                    <tr>
                      <td class="col-truncate mono" title={m.modelId}>{m.modelId}</td>
                      <td class="num">{m.requestsText}</td>
                      <td><span class="hbar-track"><span class="hbar-fill" style={{ width: `${m.sharePct}%` }} /></span></td>
                      <td class={m.errorRateClass} title={m.errorRateTitle}>{m.errorRateText}</td>
                      <td class="num">{m.p95Text}</td>
                      <td class="num">{m.ttftText}</td>
                      <td class="num">{m.costText}</td>
                      <td class="row-actions">
                        <button type="button" class="icon-btn" title="Errors for this model" aria-label="Errors for this model" onClick={() => openErrors(m.modelId)}>
                          <span class="icon"><IconBug /></span>
                        </button>
                        <button type="button" class="icon-btn" title="Routing" aria-label="Routing" onClick={() => props.navigate('/routing?sub=models')}>
                          <span class="icon"><IconGitBranch /></span>
                        </button>
                      </td>
                    </tr>
                  )}
                </For>
              </tbody>
            </table>
          </div>
        </Show>
      </div>

      <div class="chart-row ov-row wb-hide">
        <div class="card activity-card">
          <CardTitle eyebrow="Control plane" title="Recent activity" />
          <Show when={props.errors.activity}><p class="notice warn">{props.errors.activity}</p></Show>
          <Show
            when={activity().length > 0}
            fallback={<p class="hint">{props.activity?.available === false ? 'Audit trail unavailable.' : 'No admin activity recorded yet.'}</p>}
          >
            <div class="table-wrap">
              <table class="t-activity">
                <colgroup><col /><col /><col /><col /></colgroup>
                <thead><tr><th>Time</th><th>Action</th><th>By</th><th>Details</th></tr></thead>
                <tbody>
                  <For each={activity()}>
                    {(ev) => (
                      <tr>
                        <td class="cell-time" title={ev.time}>{ev.ago}</td>
                        <td class="col-truncate" title={ev.action}><span class="tag">{ev.action}</span></td>
                        <td class="col-truncate" title={ev.actor}>{ev.actor}</td>
                        <td class="col-truncate mono" title={ev.details}>{ev.details}</td>
                      </tr>
                    )}
                  </For>
                </tbody>
              </table>
            </div>
          </Show>
        </div>

        <Show when={tenants()}>
          {(t) => (
            <div class="card tenants-card">
              <CardTitle eyebrow="Tenants & keys" title={t().title}>
                <Button variant="secondary" onClick={() => props.navigate('/keys')}>
                  <span class="icon"><IconKey /></span> API keys
                </Button>
              </CardTitle>
              <Show when={props.errors.tenants}><p class="notice warn">{props.errors.tenants}</p></Show>
              <Show when={t().consumers.length > 0}>
                <div class="policy-section">
                  <span class="eyebrow">Top consumers · month to date</span>
                  <div class="consumer-rows">
                    <For each={t().consumers}>
                      {(tc) => (
                        <button type="button" class="consumer-row" onClick={() => props.navigate('/usage')}>
                          <span class="consumer-who">{tc.who}<Show when={tc.plan}><span class="tag muted">{tc.plan}</span></Show></span>
                          <span class="num" title="Requests this month">{tc.requestsText}</span>
                          <span class="num muted" title="Tokens this month">{tc.tokensText}</span>
                          <span class="num" title="Priced spend this month">{tc.costText}</span>
                          <span class="consumer-recent muted">{tc.recentText}</span>
                        </button>
                      )}
                    </For>
                  </div>
                </div>
              </Show>
              <div class="policy-columns">
                <Show when={t().expiringKeys.length > 0}>
                  <div class="policy-section">
                    <span class="eyebrow">Expiring soon</span>
                    <For each={t().expiringKeys}>
                      {(k) => (
                        <button type="button" class="key-row" onClick={() => props.navigate('/keys')}>
                          <span class="key-row-label">{k.label}</span><span class="muted">{k.tenant}</span><span class="key-row-when">{k.whenText}</span>
                        </button>
                      )}
                    </For>
                  </div>
                </Show>
                <Show when={t().idleKeys.length > 0}>
                  <div class="policy-section">
                    <span class="eyebrow">Idle keys</span>
                    <For each={t().idleKeys}>
                      {(k) => (
                        <button type="button" class="key-row" onClick={() => props.navigate('/keys')}>
                          <span class="key-row-label">{k.label}</span><span class="muted">{k.tenant}</span><span class="key-row-when">{k.whenText}</span>
                        </button>
                      )}
                    </For>
                  </div>
                </Show>
              </div>
              <Show when={t().anonymousText}><p class="hint">{t().anonymousText}</p></Show>
              <Show when={t().quiet}><p class="hint healthy">No spend recorded this month and every key is in use.</p></Show>
            </div>
          )}
        </Show>
      </div>
    </>
  );
}
