import { For, Show, createEffect, createSignal, onCleanup, onMount } from 'solid-js';
import { useSearchParams } from '@solidjs/router';
import { Button, Dialog, Select } from '../../components/primitives';
import { IconBug, IconCopy, IconDownload, IconFilter, IconLightbulb, IconRefresh, IconSearch, IconTrash } from '../../components/icons';
import { pushToast } from '../../stores/auth';
import { formatNum, formatTime } from '../../domain/format';
import {
  activateErrorsPage,
  clearErrors,
  disposeErrorsPage,
  exportErrors,
  facetOptions,
  useErrorsStore,
} from '../../stores/errors';
import { setErrorsAutoRefresh } from '../../stores/connection';

const RANGES = [
  { value: '1h', label: 'Last hour' },
  { value: '24h', label: 'Last 24h' },
  { value: '7d', label: 'Last 7 days' },
  { value: '30d', label: 'Last 30 days' },
  { value: 'all', label: 'All time' },
];

const LEVELS = [
  { value: 'all', label: 'All severities' },
  { value: 'warning', label: 'Warning and above' },
  { value: 'error', label: 'Errors only' },
  { value: 'critical', label: 'Critical only' },
];

function endpointText(row: Record<string, unknown> | undefined): string {
  if (!row) return '—';
  const endpoint = [row.endpointMethod, row.endpointPath].filter(Boolean).join(' ');
  return endpoint || '—';
}

function rowClass(row: Record<string, unknown>): string {
  const level = String(row.level ?? '').toLowerCase();
  if (level === 'error' || level === 'critical') return 'row-error';
  return level === 'warning' ? 'row-warn' : '';
}

const SOURCE_LABELS: Record<string, string> = {
  proxy: 'Inference request',
  exception: 'Unhandled exception',
  log: 'Application log',
  modeltest: 'Model test',
};

function sourceLabel(source: unknown): string {
  return SOURCE_LABELS[String(source).toLowerCase()] ?? String(source);
}

export default function ErrorsPage() {
  const store = useErrorsStore();
  const [params, setParams] = useSearchParams();
  const [confirmClear, setConfirmClear] = createSignal(false);
  const [busy, setBusy] = createSignal(false);

  onMount(() => {
    activateErrorsPage();
    if (params.model) store.setModelFilter(String(params.model));
    if (params.status) store.setStatusFilter(String(params.status));
    if (params.code) store.setCodeFilter(String(params.code));
    if (params.range && RANGES.some((r) => r.value === params.range)) store.setRange(String(params.range));
    void store.load({ force: true });
  });
  onCleanup(() => disposeErrorsPage());

  createEffect(() => {
    setParams({
      model: store.modelFilter() || undefined,
      status: store.statusFilter() || undefined,
      code: store.codeFilter() || undefined,
      range: store.range() !== '24h' ? store.range() : undefined,
    });
  });

  createEffect(() => {
    setErrorsAutoRefresh(store.autoRefresh());
  });

  onCleanup(() => setErrorsAutoRefresh(false));

  const onExpand = (id: string, open: boolean) => {
    store.setExpandedId(open ? id : null);
    if (open) void store.loadOccurrences(id);
  };

  const runClear = async () => {
    setBusy(true);
    try {
      await clearErrors();
      setConfirmClear(false);
      await store.load({ force: true });
      void store.loadFacets();
    } finally {
      setBusy(false);
    }
  };

  const modelOptions = () => facetOptions(store.facets()?.models);
  const statusOptions = () => facetOptions(store.facets()?.statusCodes);
  const codeOptions = () => facetOptions(store.facets()?.errorCodes);

  const loading = () => store.phase() === 'loading';
  const hasGroups = () => store.order().length > 0;
  const filterActive = () =>
    store.search().trim() !== '' || !!store.modelFilter() || !!store.statusFilter() || !!store.codeFilter() || store.levelFilter() !== 'all';
  const occurrenceTotal = () => store.order().reduce((sum, id) => sum + Number(store.byId[id]?.count ?? 0), 0);
  const rangeLabel = () => (RANGES.find((r) => r.value === store.range())?.label ?? 'Last 24h').toLowerCase();

  const clearFilters = () => {
    store.setSearch('');
    store.setModelFilter('');
    store.setStatusFilter('');
    store.setCodeFilter('');
    store.setLevelFilter('all');
    store.applyFilters();
  };

  const copyText = async (text: string, done: string) => {
    try {
      await navigator.clipboard.writeText(text);
      pushToast(done);
    } catch {
      pushToast('Could not access clipboard.', 'error');
    }
  };

  return (
    <section class="page" id="panel-errors">
      <div class="page-header">
        <div class="titles">
          {null}
          <h2>Errors</h2>
          {null}
          <Show when={store.groupsTotal() > 0}>
            <p class="page-sub">
              {formatNum(occurrenceTotal())} occurrences across {formatNum(store.groupsTotal())} error groups · {rangeLabel()}
            </p>
          </Show>
          <Show when={store.facetsError()}>
            <p class="page-sub">Could not load model and status filters.</p>
          </Show>
        </div>
      </div>

      <div class="chip-row">
        <For each={RANGES}>
          {(r) => (
            <button
              type="button"
              class={store.range() === r.value ? 'preset active' : 'preset'}
              onClick={() => {
                store.setRange(r.value);
                void store.loadFacets();
                void store.load({ force: true });
              }}
            >
              {r.label}
            </button>
          )}
        </For>
      </div>

      <div class="toolbar">
        <label class="filter-field">
          <span class="icon"><IconSearch /></span>
          <input
            value={store.search()}
            onInput={(e) => {
              store.setSearch(e.currentTarget.value);
              store.applyFilters();
            }}
            placeholder="message, exception, model, request id…"
            aria-label="Search errors"
          />
        </label>
        <Select
          class="inline-select"
          aria-label="Model"
          options={[{ value: '', label: 'All models' }, ...modelOptions()]}
          value={store.modelFilter()}
          onChange={(v) => {
            store.setModelFilter(v);
            store.applyFilters();
          }}
        />
        <Select
          class="inline-select"
          aria-label="Status code"
          options={[{ value: '', label: 'All statuses' }, ...statusOptions()]}
          value={store.statusFilter()}
          onChange={(v) => {
            store.setStatusFilter(v);
            store.applyFilters();
          }}
        />
        <Select
          class="inline-select"
          aria-label="Error code"
          options={[{ value: '', label: 'All codes' }, ...codeOptions()]}
          value={store.codeFilter()}
          onChange={(v) => {
            store.setCodeFilter(v);
            store.applyFilters();
          }}
        />
        <Select
          class="inline-select"
          aria-label="Minimum severity"
          options={LEVELS}
          value={store.levelFilter()}
          onChange={(v) => {
            store.setLevelFilter(v);
            store.applyFilters();
          }}
        />
        <label class="checkbox field-inline">
          <input type="checkbox" checked={store.autoRefresh()} onChange={(e) => store.setAutoRefresh(e.currentTarget.checked)} /> Auto-refresh
        </label>
        <span class="spacer" />
        <Show when={filterActive()}>
          <Button variant="secondary" onClick={clearFilters}>
            <span class="icon"><IconFilter /></span> Clear filters
          </Button>
        </Show>
        <Button variant="secondary" aria-label="Export JSON" onClick={() => void exportErrors('json')} disabled={loading()}>
          <span class="icon"><IconDownload /></span> JSON
        </Button>
        <Button variant="secondary" aria-label="Export CSV" onClick={() => void exportErrors('csv')} disabled={loading()}>
          <span class="icon"><IconDownload /></span> CSV
        </Button>
        <Button variant="secondary" onClick={() => store.load({ force: true })} disabled={loading()}>
          <span class="icon"><IconRefresh /></span> Refresh
        </Button>
        <Button variant="danger" onClick={() => setConfirmClear(true)} disabled={loading() || store.groupsTotal() === 0}>
          <span class="icon"><IconTrash /></span> Clear all
        </Button>
      </div>

      <div class="table-wrap">
        <Show when={hasGroups()}>
          <table class="data-table t-errors">
            <colgroup>
              <col /><col /><col />
              <col /><col /><col />
              <col /><col />
            </colgroup>
            <thead>
              <tr>
                <th>Last seen</th><th>Severity</th><th>Count</th><th>Message</th>
                <th>Model</th><th>Status</th><th>Code</th><th>Endpoint</th>
              </tr>
            </thead>
            <For each={store.order()}>
              {(id) => {
                const row = () => store.byId[id] ?? {};
                const expanded = () => store.expandedId() === id;
                const requestId = () => String(row().lastRequestId ?? '');
                const hasEndpoint = () => endpointText(row()) !== '—';
                const occurrences = () => store.occurrences[id];
                return (
                  <tbody>
                    <tr
                      class={`request-row ${rowClass(row())}`}
                      classList={{ expanded: expanded() }}
                      role="button"
                      tabIndex={0}
                      aria-expanded={expanded()}
                      aria-label={`${row().level}, ${row().count} occurrences: ${row().message}`}
                      onClick={() => onExpand(id, !expanded())}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' || e.key === ' ') {
                          e.preventDefault();
                          onExpand(id, !expanded());
                        }
                      }}
                    >
                      <td>{formatTime(row().lastSeenUtc)}</td>
                      <td><span class={`tag level-${String(row().level ?? '').toLowerCase()}`}>{String(row().level ?? '—')}</span></td>
                      <td><span class="err-count" title="Occurrences of this same failure in the selected window">×{formatNum(row().count)}</span></td>
                      <td class="col-truncate" title={String(row().message ?? '')}>{String(row().message ?? '')}</td>
                      <td class="col-truncate" title={String(row().modelId ?? '')}>{String(row().modelId ?? '—')}</td>
                      <td>{row().statusCode ? String(row().statusCode) : '—'}</td>
                      <td class="col-truncate" title={String(row().errorCode ?? '')}>{String(row().errorCode ?? '—')}</td>
                      <td class="col-truncate" title={endpointText(row())}>{endpointText(row())}</td>
                    </tr>
                    <Show when={expanded()}>
                      <tr class="request-detail-row">
                        <td colspan="8">
                          <p class="error-detail-message">{String(row().message ?? '')}</p>
                          <Show when={row().hint}>
                            <p class="log-hint">
                              <span class="icon"><IconLightbulb /></span>
                              <span>{String(row().hint)}</span>
                            </p>
                          </Show>
                          <Show when={!(row().modelId || hasEndpoint() || row().statusCode || requestId())}>
                            {null}
                          </Show>
                          <dl class="request-detail-list">
                            <Show when={row().source}><div><dt>Source</dt><dd>{sourceLabel(row().source)}</dd></div></Show>
                            <Show when={row().category}><div><dt>Reported by</dt><dd><code>{String(row().category)}</code></dd></div></Show>
                            <Show when={row().exceptionType}><div><dt>Exception</dt><dd>{String(row().exceptionType)}</dd></div></Show>
                            <Show when={row().modelId}><div><dt>Model</dt><dd><code>{String(row().modelId)}</code></dd></div></Show>
                            <Show when={row().statusCode}><div><dt>Status</dt><dd>{String(row().statusCode)}</dd></div></Show>
                            <Show when={row().errorCode}><div><dt>Code</dt><dd><code>{String(row().errorCode)}</code></dd></div></Show>
                            <Show when={hasEndpoint()}><div><dt>Endpoint</dt><dd><code>{endpointText(row())}</code></dd></div></Show>
                            <Show when={row().upstreamTarget}><div><dt>Upstream</dt><dd><code>{String(row().upstreamTarget)}</code></dd></div></Show>
                            <div><dt>First seen</dt><dd>{formatTime(row().firstSeenUtc)}</dd></div>
                            <div><dt>Occurrences</dt><dd>×{formatNum(row().count)}</dd></div>
                            <Show when={requestId()}><div><dt>Latest request</dt><dd><code>{requestId()}</code></dd></div></Show>
                          </dl>

                          <div class="row-actions" onClick={(e) => e.stopPropagation()}>
                            <Show when={requestId()}>
                              <Button variant="secondary" onClick={() => void copyText(requestId(), 'Request ID copied.')}>
                                <span class="icon"><IconCopy /></span> Copy request ID
                              </Button>
                            </Show>
                            <Show when={row().modelId}>
                              <Button
                                variant="secondary"
                                onClick={() => {
                                  store.setModelFilter(String(row().modelId));
                                  store.applyFilters();
                                }}
                              >
                                <span class="icon"><IconFilter /></span> Only this model
                              </Button>
                            </Show>
                            <Button variant="secondary" onClick={() => void copyText(JSON.stringify(row(), null, 2), 'Error entry copied.')}>
                              <span class="icon"><IconCopy /></span> Copy entry
                            </Button>
                          </div>

                          <Show when={row().stackTrace}>
                            <p class="mb-0"><strong>Stack trace</strong></p>
                            <pre class="model-test-content stack-trace">{String(row().stackTrace)}</pre>
                          </Show>

                          <Show when={!occurrences()}>
                            <p class="hint">Loading recent occurrences…</p>
                          </Show>
                          <Show when={occurrences()?.length}>
                            <table class="occurrence-table">
                              <thead>
                                <tr><th>Time</th><th>Request ID</th><th>Status</th><th>Duration</th><th>Tenant</th><th /></tr>
                              </thead>
                              <tbody>
                                <For each={occurrences()}>
                                  {(occ) => (
                                    <tr>
                                      <td>{formatTime(occ.timestampUtc)}</td>
                                      <td><code>{String(occ.requestId ?? '—')}</code></td>
                                      <td>{occ.statusCode ? String(occ.statusCode) : '—'}</td>
                                      <td>{occ.durationMs == null ? '—' : `${formatNum(Math.round(Number(occ.durationMs)))} ms`}</td>
                                      <td class="col-truncate" title={String(occ.tenantId ?? '')}>{String(occ.tenantId || '—')}</td>
                                      <td>
                                        <Show when={occ.requestId}>
                                          <button
                                            type="button"
                                            class="icon-btn"
                                            aria-label="Copy request ID"
                                            onClick={(e) => {
                                              e.stopPropagation();
                                              void copyText(String(occ.requestId), 'Request ID copied.');
                                            }}
                                          >
                                            <span class="icon"><IconCopy /></span>
                                          </button>
                                        </Show>
                                      </td>
                                    </tr>
                                  )}
                                </For>
                              </tbody>
                            </table>
                          </Show>
                        </td>
                      </tr>
                    </Show>
                  </tbody>
                );
              }}
            </For>
          </table>
        </Show>
        <Show when={loading() && !hasGroups()}>
          <table>
            <tbody>
              <tr class="skeleton-row"><td colspan="8" /></tr>
              <tr class="skeleton-row"><td colspan="8" /></tr>
              <tr class="skeleton-row"><td colspan="8" /></tr>
            </tbody>
          </table>
        </Show>
      </div>

      <Show when={!loading() && !hasGroups()}>
        <div class="empty-state">
          <span class="empty-state-icon icon"><IconBug /></span>
          <p>{filterActive() ? 'No errors match these filters.' : 'No errors recorded in this time range.'}</p>
        </div>
      </Show>

      <Dialog open={confirmClear()} title="Clear all recorded errors?" onClose={() => setConfirmClear(false)}>
        <p>
          Deletes every stored error record and resets the gateway error counters. This cannot be undone —
          durable logs written by configured log providers are unaffected.
        </p>
        <div class="modal-actions">
          <Button variant="secondary" onClick={() => setConfirmClear(false)}>Cancel</Button>
          <Button variant="danger" onClick={runClear} disabled={busy()}>Clear errors</Button>
        </div>
      </Dialog>
    </section>
  );
}
