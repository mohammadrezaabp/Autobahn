import { For, Show, createEffect, createMemo, createSignal, onCleanup, onMount } from 'solid-js';
import { useNavigate, useSearchParams } from '@solidjs/router';
import { OverviewDetail } from '../../components/overview/OverviewDetail';
import { Button } from '../../components/primitives';
import { Sparkline } from '../../components/Sparkline';
import {
  IconAlertTriangle,
  IconCopy,
  IconMaximize,
  IconMinimize,
  IconPause,
  IconPin,
  IconPlay,
  IconRefresh,
  IconSearch,
  IconZap,
} from '../../components/icons';
import { DurationCell } from '../../components/DurationCell';
import { liveBadgeView } from '../../app/liveBadge';
import { useWallboard } from '../../app/useWallboard';
import { attentionRows, hasCriticalAttention } from '../../domain/attention';
import { formatDurationMs, formatMsParts, formatMsShort, formatNum, formatTime } from '../../domain/format';
import {
  formatClock,
  formatRequestCost,
  hasTokens,
  pricingStatusLabel,
  requestCostClass,
  requestCostText,
  requestRowClass,
  requestTokensText,
  requestTokensTitle,
  shortRequestId,
  tokenSourceLabel,
  tokensPerSecText,
  type RequestRowFields,
} from '../../domain/requestRow';
import { useConnectionSnapshot, syncConnection } from '../../stores/connection';
import {
  activateOverviewSections,
  disposeOverviewSections,
  loadOverviewSections,
  setOverviewWallboardMode,
  useOverviewSections,
} from '../../stores/overviewSections';
import { pushToast } from '../../stores/auth';
import { resumeFromPause, useRequestsStore } from '../../stores/requests';
import { useSummary } from '../../stores/summary';
import {
  hasTrailingWindows,
  sparkSourceText,
  sparkValues,
  useVitalsHistory,
  windowStats,
} from '../../stores/vitalsHistory';

const WINDOWS = [
  { value: '1m', label: '1 min' },
  { value: '5m', label: '5 min' },
  { value: '1h', label: '1 hour' },
  { value: '24h', label: '24 h' },
];

const DISMISSED_KEY = 'Autobahn-admin-attention-dismissed';

function loadDismissed(): string[] {
  try {
    const raw = sessionStorage.getItem(DISMISSED_KEY);
    const list = raw ? JSON.parse(raw) : [];
    return Array.isArray(list) ? list.filter((x) => typeof x === 'string') : [];
  } catch {
    return [];
  }
}

function onboardingModelId(summary: Record<string, unknown> | null): string {
  const backends = summary?.backends;
  if (Array.isArray(backends) && backends[0] && typeof backends[0] === 'object') {
    const model = (backends[0] as Record<string, unknown>).modelId;
    if (model) return String(model);
  }
  return 'your-model-id';
}

function onboardingCurl(model: string): string {
  const origin = typeof location !== 'undefined' ? location.origin : 'http://localhost:8080';
  return (
    `curl -s ${origin}/v1/chat/completions \\\n` +
    "  -H 'Authorization: Bearer <inference-key>' \\\n" +
    "  -H 'Content-Type: application/json' \\\n" +
    `  -d '{"model":"${model}","messages":[{"role":"user","content":"Hello"}}]}'`
  );
}

export default function OverviewPage() {
  const { summary, updatedAt } = useSummary();
  const req = useRequestsStore();
  const conn = useConnectionSnapshot();
  const sections = useOverviewSections();
  const vitals = useVitalsHistory();
  const navigate = useNavigate();
  const [params, setParams] = useSearchParams();
  const initialWindow = () => {
    const fromHash = params.window;
    if (typeof fromHash === 'string' && WINDOWS.some((w) => w.value === fromHash)) return fromHash;
    return localStorage.getItem('Autobahn-admin-overview-window') || '5m';
  };
  const [window, setWindow] = createSignal(initialWindow());
  const [dismissed, setDismissed] = createSignal<string[]>(loadDismissed());
  const [attentionCollapsed, setAttentionCollapsed] = createSignal(false);

  const wallboard = createMemo(() => params.wall === '1');
  const s = () => summary();
  const rows = () => req.visibleOrder();
  const attention = createMemo(() =>
    attentionRows(s()?.attention, dismissed(), wallboard()),
  );
  const criticalAttention = createMemo(() => hasCriticalAttention(attention()));
  const live = createMemo(() => liveBadgeView(conn()));
  const stats = createMemo(() => windowStats(s(), window(), vitals()));
  const sparkTitle = createMemo(() => sparkSourceText(s()));
  const showOnboarding = createMemo(
    () =>
      !!s() &&
      Number(s()?.totalInferenceRequests ?? 0) === 0 &&
      rows().length === 0,
  );
  const showOverviewBody = createMemo(() => !!s() && !showOnboarding());
  const showStaleNotice = createMemo(() => conn().degraded && conn().status !== 'fail');
  const connectionFailed = createMemo(() => conn().status === 'fail');

  const wb = useWallboard({
    active: () => wallboard(),
    summaryUpdatedAt: updatedAt,
    connectionFailed,
    hasCriticalAttention: criticalAttention,
    requestFilters: () => ({
      model: req.modelFilter(),
      tenant: req.tenantFilter(),
      status: req.statusFilter(),
      slowOnly: req.slowOnly(),
    }),
    onExit: () => setParams({ wall: undefined, window: window() }),
  });

  onMount(() => {
    syncConnection();
    activateOverviewSections(wallboard());
  });

  onCleanup(() => {
    disposeOverviewSections();
  });

  createEffect(() => {
    const on = wallboard();
    setOverviewWallboardMode(on);
    if (on) {
      req.setExpandedId(null);
      if (req.paused()) {
        resumeFromPause();
        pushToast('Live tail resumed — the wallboard has no pause control.');
      }
      void wb.enterFullscreen();
    }
  });

  createEffect(() => {
    const w = params.window;
    if (typeof w === 'string' && WINDOWS.some((x) => x.value === w) && w !== window()) {
      setWindow(w);
      localStorage.setItem('Autobahn-admin-overview-window', w);
    }
  });

  const dismissAttention = (key: string) => {
    const next = [...dismissed(), key];
    setDismissed(next);
    try {
      sessionStorage.setItem(DISMISSED_KEY, JSON.stringify(next));
    } catch {
      /* storage unavailable */
    }
  };

  const copyOnboardingCurl = async () => {
    const text = onboardingCurl(onboardingModelId(s()));
    try {
      await navigator.clipboard.writeText(text);
      pushToast('curl command copied.');
    } catch {
      pushToast('Could not access clipboard.', 'error');
    }
  };

  const copyRequestId = async (id: string) => {
    try {
      await navigator.clipboard.writeText(id);
      pushToast('Request ID copied.');
    } catch {
      pushToast('Could not access clipboard.', 'error');
    }
  };

  const togglePause = () => {
    if (req.paused()) resumeFromPause();
    else req.setPaused(true);
  };

  const setOverviewWindow = (v: string) => {
    setWindow(v);
    localStorage.setItem('Autobahn-admin-overview-window', v);
    setParams({ window: v, wall: wallboard() ? '1' : undefined });
  };

  const toggleWallboard = () => {
    if (wallboard()) setParams({ wall: undefined, window: window() });
    else setParams({ wall: '1', window: window() });
  };

  const cardTitle = (body: Record<string, unknown> | null, fallback: string) =>
    String(body?.title ?? body?.headline ?? fallback);

  const windowLabel = () => {
    if (!hasTrailingWindows(s())) return 'lifetime';
    return `last ${WINDOWS.find((w) => w.value === window())?.label ?? window()}`;
  };

  const latencyParts = () => {
    const w = stats();
    return formatMsParts(w.lifetime ? w.avgMs : (w.p95Ms ?? 0));
  };

  const ttftParts = () => {
    const w = stats();
    if (w.lifetime || w.ttftSamples === 0) return { value: '—', unit: '' };
    return formatMsParts(w.ttftP95Ms);
  };

  const spark = (metric: Parameters<typeof sparkValues>[1]) =>
    sparkValues(s(), metric, vitals());

  const inFlightFoot = () => {
    const streams = Number(s()?.activeStreams ?? 0);
    const inflight = Number(s()?.activeRequests ?? 0);
    if (streams > 0) {
      return `${streams} streaming · ${inflight - streams} buffered`;
    }
    return inflight > 0 ? 'active' : 'idle';
  };

  return (
    <section id="panel-dashboard" class="page overview-page" classList={{ 'is-wallboard': wallboard() }}>
      <Show when={wallboard()}>
        <div class="wallboard-bar wb-hide">
          <span class="wb-brand">Autobahn</span>
          <span class="wb-scope">{cardTitle(sections.controlPlane(), 'Live gateway overview')}</span>
          <span class="wb-gap" />
          <span class="wb-window">{WINDOWS.find((w) => w.value === window())?.label ?? window()}</span>
          <Show when={wb.hasWallboardFilters()}>
            <span class="wb-filter">{wb.wallboardFilterText()}</span>
          </Show>
          <span class={live().className} title={live().title} role="status">
            <span class="pulse-dot" classList={{ live: live().dotClass === 'live' }} />
            <span>{live().text}</span>
          </span>
          <span class="wb-clock">{wb.clockText()}</span>
          <button type="button" class="icon-btn wb-chrome" onClick={() => wb.enterFullscreen()} title="Fullscreen" aria-label="Fullscreen">
            <span class="icon"><IconMaximize /></span>
          </button>
          <button type="button" class="icon-btn wb-chrome" onClick={() => setParams(wb.exitWallboard())} title="Exit wallboard" aria-label="Exit wallboard">
            <span class="icon"><IconMinimize /></span>
          </button>
        </div>
        <Show when={wb.stale()}>
          <div class="wallboard-stale-band" role="status" aria-live="polite">
            <span class="icon"><IconAlertTriangle /></span>
            <strong>{wb.staleTitle()}</strong>
            <span>{wb.staleText()}</span>
          </div>
        </Show>
      </Show>

      <header class="page-header">
        <div class="titles">
          <h2>Overview</h2>
        </div>
        <div class="page-actions wb-hide">
          <div class="window-picker" role="group" aria-label="Time window">
            <For each={WINDOWS}>
              {(w) => (
                <button
                  type="button"
                  class={window() === w.value ? 'preset active' : 'preset'}
                  aria-pressed={window() === w.value}
                  disabled={!hasTrailingWindows(s())}
                  onClick={() => setOverviewWindow(w.value)}
                >
                  {w.label}
                </button>
              )}
            </For>
          </div>
          <span class={live().className} title={live().title} role="status">
            <span class="pulse-dot" classList={{ live: live().dotClass === 'live' }} />
            <span>{live().text}</span>
          </span>
          <Button variant="secondary" onClick={() => { syncConnection(); void loadOverviewSections(); }}>
            <span class="icon"><IconRefresh /></span> Refresh
          </Button>
          <Button variant="secondary" onClick={togglePause}>
            <span class="icon">{req.paused() ? <IconPlay /> : <IconPause />}</span>
            {req.paused() ? 'Resume' : 'Pause'}
          </Button>
          <Button variant="secondary" onClick={toggleWallboard}>
            <span class="icon">{wallboard() ? <IconMinimize /> : <IconMaximize />}</span>
            {wallboard() ? 'Exit wallboard' : 'Wallboard'}
          </Button>
        </div>
      </header>

      <Show when={showOnboarding()}>
        <section class="ab-launchpad onboarding wb-hide" aria-labelledby="ab-launchpad-heading">
          <h3 id="ab-launchpad-heading">No traffic yet</h3>
          <div class="ab-setup-actions">
            <Button variant="secondary" onClick={() => navigate('/routing?sub=models')}>Add a model</Button>
            <Button variant="secondary" onClick={() => navigate('/keys')}>Create an inference key</Button>
            <Button onClick={copyOnboardingCurl}>Copy curl</Button>
          </div>
          <details class="ab-request-example">
            <summary>View a request example</summary>
            <pre class="onboarding-snippet mono">{onboardingCurl(onboardingModelId(s()))}</pre>
          </details>
        </section>
      </Show>

      <Show when={showStaleNotice()}>
        <p class="notice warn wb-hide" role="status">Live data may be stale — the last refresh failed. Check your connection.</p>
      </Show>
      <Show when={connectionFailed()}>
        <p class="notice error wb-hide" role="status">
          Your admin API key was rejected, so these figures have stopped updating. Use <strong>Change key</strong> in the top bar to sign in again.
        </p>
      </Show>

      <Show when={attention().length > 0}>
        <section class="attention wb-hide" classList={{ 'is-critical': hasCriticalAttention(attention()) }} aria-label="Attention">
          <header class="attention-head">
            <h2>Needs attention</h2>
            <button type="button" class="link-btn" onClick={() => setAttentionCollapsed((v) => !v)}>
              {attentionCollapsed() ? 'Show' : 'Hide'}
            </button>
          </header>
          <Show when={!attentionCollapsed() || wallboard()}>
            <ul class="attention-list">
              <For each={attention()}>
                {(row) => (
                  <li class={row.cls}>
                    <div class="attention-body">
                      <strong>{String(row.title ?? row.code ?? 'Notice')}</strong>
                      <span class="attention-detail">{String(row.message ?? row.detail ?? '')}</span>
                    </div>
                    <div class="attention-actions">
                      <Show when={row.hasLink && row.linkPath && !wallboard()}>
                        <Button variant="secondary" size="sm" onClick={() => navigate(row.linkPath!)}>Open</Button>
                      </Show>
                      <Show when={!wallboard()}>
                        <button type="button" class="link-btn" onClick={() => dismissAttention(row.key)}>Dismiss</button>
                      </Show>
                    </div>
                  </li>
                )}
              </For>
            </ul>
          </Show>
        </section>
      </Show>

      <Show when={showOverviewBody()}>
        <div class="vitals-grid vitals-5">
          <article class="vital" title={sparkTitle()}>
            <div class="vital-head"><span class="vital-label">Requests</span></div>
            <strong class="vital-value">{formatNum(stats().requests)}</strong>
            <div class="vital-foot">
              <span>{stats().rps.toFixed(stats().rps < 10 ? 1 : 0)}/s · {windowLabel()}</span>
            </div>
            <Sparkline values={spark('throughput')} />
          </article>

          <article
            class="vital is-actionable"
            classList={{ 'accent-error': stats().errors > 0 }}
            role="button"
            tabIndex={0}
            aria-label="Open the Errors tab"
            onClick={() => navigate('/errors')}
            onKeyDown={(e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                navigate('/errors');
              }
            }}
          >
            <div class="vital-head"><span class="vital-label">Errors</span></div>
            <strong class="vital-value">{formatNum(stats().errors)}</strong>
            <div class="vital-foot">
              <span>{(stats().errorRate * 100).toFixed(2)}% error rate · {windowLabel()}</span>
            </div>
            <Sparkline values={spark('errorRate')} />
          </article>

          <article class="vital" title={sparkTitle()}>
            <div class="vital-head">
              <span class="vital-label">{stats().lifetime ? 'Avg latency' : 'Latency p95'}</span>
            </div>
            <strong class="vital-value">
              <span>{latencyParts().value}</span>
              <span class="unit">{latencyParts().unit}</span>
            </strong>
            <div class="vital-foot">
              <Show when={stats().lifetime} fallback={
                <span>
                  p50 {formatMsShort(stats().p50Ms)} · p99 {formatMsShort(stats().p99Ms)} · {windowLabel()}
                </span>
              }>
                <span>mean upstream round-trip</span>
              </Show>
            </div>
            <Sparkline values={spark('latency')} />
          </article>

          <article class="vital" title={sparkTitle()}>
            <div class="vital-head"><span class="vital-label">TTFT p95</span></div>
            <strong class="vital-value">
              <span>{ttftParts().value}</span>
              <span class="unit">{ttftParts().unit}</span>
            </strong>
            <div class="vital-foot">
              <Show when={stats().lifetime} fallback={
                <Show when={stats().ttftSamples > 0} fallback={<span>no streams · {windowLabel()}</span>}>
                  <span>
                    p50 {formatMsShort(stats().ttftP50Ms)} · {formatNum(stats().ttftSamples)} streams · {windowLabel()}
                  </span>
                </Show>
              }>
                <span>needs a newer gateway</span>
              </Show>
            </div>
            <Sparkline values={spark('ttft')} />
          </article>

          <article class="vital accent-live">
            <div class="vital-head"><span class="vital-label">In flight</span></div>
            <strong class="vital-value">{formatNum(s()?.activeRequests ?? 0)}</strong>
            <div class="vital-foot"><span>{inFlightFoot()}</span></div>
            <Sparkline values={spark('inflight')} />
          </article>
        </div>
      </Show>

      <Show when={showOverviewBody()}>
        <OverviewDetail
          summary={s()}
          window={hasTrailingWindows(s()) ? window() : ''}
          windowLabel={windowLabel()}
          finops={sections.finops() as Record<string, unknown> | null}
          policy={sections.policy() as Record<string, unknown> | null}
          controlPlane={sections.controlPlane() as Record<string, unknown> | null}
          activity={sections.activity() as Record<string, unknown> | null}
          tenants={sections.tenants() as Record<string, unknown> | null}
          rateLimits={sections.rateLimits() as Record<string, unknown> | null}
          errors={sections.sectionErrors}
          navigate={navigate}
        />

        <div class="section-header">
          <div><span class="eyebrow">Live tail</span><h3>Recent requests</h3></div>
          <div class="tail-filters wb-hide">
            <label class="filter-field narrow">
              <span class="icon"><IconSearch /></span>
              <input placeholder="model" aria-label="Filter by model" value={req.modelFilter()} onInput={(e) => req.setModelFilter(e.currentTarget.value)} />
            </label>
            <label class="filter-field narrow">
              <span class="icon"><IconSearch /></span>
              <input placeholder="tenant" aria-label="Filter by tenant" value={req.tenantFilter()} onInput={(e) => req.setTenantFilter(e.currentTarget.value)} />
            </label>
            <label class="filter-field narrow">
              <span class="icon"><IconSearch /></span>
              <input placeholder="status code" aria-label="Filter by status code" value={req.statusFilter()} onInput={(e) => req.setStatusFilter(e.currentTarget.value)} />
            </label>
            <label class="checkbox field-inline" title="Slower than 5 s">
              <input type="checkbox" checked={req.slowOnly()} onChange={(e) => req.setSlowOnly(e.currentTarget.checked)} /> Slow
            </label>
          </div>
        </div>
        <div>
          <div class="table-wrap">
            <table class="data-table t-requests">
              {/* Column shares drive the fixed table layout: the wide free-text columns (route,
                  model) absorb the space, the rest stay at their natural size. The wallboard
                  hides columns by position, so the order here is part of that contract. */}
              <colgroup>
                <col /><col /><col /><col />
                <col /><col /><col />
                <col /><col /><col /><col /><col />
              </colgroup>
              <thead>
                <tr>
                  <th aria-label="Pin" />
                  <th>Time</th>
                  <th>Request ID</th>
                  <th>Route</th>
                  <th>Model</th>
                  <th>Cost center</th>
                  <th>Status</th>
                  <th class="num" title="Prompt → completion tokens">Tokens</th>
                  <th class="num" title="Input + output cost from the model's rate card">Cost</th>
                  <th class="num" title="Time to first token (streaming)">TTFT</th>
                  <th class="num" title="Completion tokens per second">tok/s</th>
                  <th class="num">Duration</th>
                </tr>
              </thead>
              <tbody>
                <For each={rows()}>
                  {(id) => {
                    const row = () => (req.byId[id] ?? {}) as RequestRowFields;
                    const expanded = () => req.expandedId() === id;
                    const pinned = () => req.pinnedIds().includes(id);
                    const inFlight = () => row().isInFlight === true;
                    const priced = () => row().pricingStatus === 'priced';
                    const toggle = () => req.setExpandedId(expanded() ? null : id);
                    return (
                      <>
                        <tr
                          class={`request-row ${requestRowClass(row())}`}
                          classList={{ 'row-enter': req.isNewRow(id), expanded: expanded(), 'is-pinned': pinned() }}
                          role="button"
                          tabIndex={0}
                          aria-expanded={expanded()}
                          onClick={toggle}
                          onKeyDown={(e) => {
                            if (e.key === 'Enter' || e.key === ' ') {
                              e.preventDefault();
                              toggle();
                            }
                          }}
                        >
                          <td class="cell-pin" onClick={(e) => e.stopPropagation()}>
                            <button
                              type="button"
                              class="icon-btn"
                              classList={{ 'is-on': pinned() }}
                              title={pinned() ? 'Unpin' : 'Pin to the top of the feed'}
                              aria-label="Pin request"
                              onClick={() => req.togglePin(id)}
                            >
                              <span class="icon"><IconPin /></span>
                            </button>
                          </td>
                          <td class="cell-time" title={formatTime(row().timestampUtc)}>{formatClock(row().timestampUtc)}</td>
                          <td class="cell-code" onClick={(e) => e.stopPropagation()}>
                            <span class="cell-code-inner">
                              <code title={String(row().requestId ?? id)}>{shortRequestId(row().requestId ?? id)}</code>
                              <button type="button" class="icon-btn" title="Copy request ID" aria-label="Copy request ID" onClick={() => void copyRequestId(String(row().requestId ?? id))}>
                                <span class="icon"><IconCopy /></span>
                              </button>
                            </span>
                          </td>
                          <td class="cell-route" title={row().path ?? ''}>
                            <span class="tag">{row().method ?? '—'}</span>
                            <span class="route-path">{row().path ?? ''}</span>
                          </td>
                          <td class="col-truncate" title={row().modelId ?? ''}>{row().modelId ?? '—'}</td>
                          <td class="col-truncate">
                            <span class="cc-tag" classList={{ 'is-empty': !row().costCenter }}>{row().costCenter || '—'}</span>
                          </td>
                          <td class="cell-status">
                            <span class="status-line">
                              <Show when={inFlight()}><span class="pulse-dot live" aria-hidden="true" /></Show>
                              <span class="status-code">{inFlight() ? '···' : String(row().statusCode ?? '—')}</span>
                            </span>
                            <Show when={inFlight() || row().errorCode}>
                              <span class="status-error" classList={{ live: inFlight(), error: !inFlight() }} title={inFlight() ? 'running' : String(row().errorCode)}>
                                {inFlight() ? 'running' : String(row().errorCode)}
                              </span>
                            </Show>
                          </td>
                          <td class="num" title={requestTokensTitle(row())}>{requestTokensText(row())}</td>
                          <td class={`num cost-cell ${requestCostClass(row())}`} title={pricingStatusLabel(row())}>{requestCostText(row())}</td>
                          <td class="num" title={row().isStreaming ? 'Time to first token' : 'Buffered response'}>
                            {row().timeToFirstTokenMs != null ? formatDurationMs(row().timeToFirstTokenMs) : inFlight() ? '…' : '—'}
                          </td>
                          <td class="num" title="Completion tokens per second">{tokensPerSecText(row())}</td>
                          <td class="num">
                            <DurationCell startedAt={row().timestampUtc} durationMs={row().durationMs} inFlight={inFlight()} />
                          </td>
                        </tr>
                        <Show when={expanded()}>
                          <tr class="request-detail-row">
                            <td colspan="12">
                              <dl class="request-detail-list">
                                <div><dt>Request ID</dt><dd><code>{String(row().requestId ?? id)}</code></dd></div>
                                <div><dt>Started</dt><dd>{formatTime(row().timestampUtc)}</dd></div>
                                <div><dt>Status</dt><dd>{inFlight() ? 'In progress' : String(row().statusCode ?? '—')}</dd></div>
                                <div><dt>Error code</dt><dd>{inFlight() ? 'running' : (row().errorCode ?? '—')}</dd></div>
                                <div><dt>Duration</dt><dd>{formatDurationMs(row().durationMs)}{inFlight() ? ' so far' : ''}</dd></div>
                                <div><dt>Tenant</dt><dd>{row().tenantId ?? '—'}</dd></div>
                                <div><dt>Cost center</dt><dd>{row().costCenter || '—'}</dd></div>
                                <div><dt>Streaming</dt><dd>{row().isStreaming ? 'Yes' : 'No'}</dd></div>
                              </dl>
                              <dl class="request-detail-list request-detail-usage">
                                <div><dt>Prompt tokens</dt><dd>{hasTokens(row()) ? formatNum(row().promptTokens ?? 0) : '—'}</dd></div>
                                <div><dt>Completion tokens</dt><dd>{hasTokens(row()) ? formatNum(row().completionTokens ?? 0) : '—'}</dd></div>
                                <div><dt>Total tokens</dt><dd>{hasTokens(row()) ? formatNum(row().totalTokens ?? 0) : '—'}</dd></div>
                                <div><dt>Output speed</dt><dd>{tokensPerSecText(row()) === '—' || inFlight() ? '—' : `${tokensPerSecText(row())} tok/s`}</dd></div>
                                <div><dt>Input cost</dt><dd class="cost-detail">{priced() ? formatRequestCost(row().inputCost, row().currency) : requestCostText(row())}</dd></div>
                                <div><dt>Output cost</dt><dd class="cost-detail">{priced() ? formatRequestCost(row().outputCost, row().currency) : requestCostText(row())}</dd></div>
                                <div><dt>Total cost</dt><dd class="cost-detail is-total">{priced() ? formatRequestCost(row().totalCost, row().currency) : requestCostText(row())}</dd></div>
                                <div><dt>Token source</dt><dd>{hasTokens(row()) ? tokenSourceLabel(row().tokenSource) : '—'}</dd></div>
                                <div class="span-2"><dt>Pricing</dt><dd>{pricingStatusLabel(row())}</dd></div>
                              </dl>
                            </td>
                          </tr>
                        </Show>
                      </>
                    );
                  }}
                </For>
              </tbody>
            </table>
          </div>
          <Show when={rows().length === 0}>
            <p class="hint">No matching requests.</p>
          </Show>
        </div>
      </Show>
    </section>
  );
}
