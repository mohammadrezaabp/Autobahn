import { For, Show, createSignal, onCleanup, onMount } from 'solid-js';
import { Button, Dialog } from '../../components/primitives';
import { IconCopy, IconFileText, IconLightbulb, IconRefresh, IconSearch, IconTrash } from '../../components/icons';
import { formatNum, formatTime } from '../../domain/format';
import { pushToast } from '../../stores/auth';
import { activateLogsPage, clearLogs, disposeLogsPage, useLogsStore, type LogRow } from '../../stores/logs';

function rowClass(entry: LogRow | undefined): string {
  const level = String(entry?.level ?? '').toLowerCase();
  if (level === 'error' || level === 'critical') return 'row-error';
  return level === 'warning' ? 'row-warn' : '';
}

/** Plain-text form of an entry, so an operator can paste one into a bug report or chat. */
function formatForCopy(entry: LogRow): string {
  const lines = [
    `[${entry.level}] ${formatTime(entry.timestampUtc)} ${entry.category ?? ''}` + (entry.eventCode ? ` (${entry.eventCode})` : ''),
    String(entry.message ?? ''),
  ];
  if (Number(entry.repeats) > 1) lines.push(`Occurrences: ${entry.repeats}, last ${formatTime(entry.lastTimestampUtc)}`);
  if (entry.modelId) lines.push(`Model: ${entry.modelId}`);
  if (entry.requestId) lines.push(`Request: ${entry.requestId}`);
  if (entry.hint) lines.push(`Hint: ${entry.hint}`);
  if (entry.detail) lines.push('', entry.detail);
  return lines.join('\n');
}

export default function LogsPage() {
  const store = useLogsStore();
  const [confirmClear, setConfirmClear] = createSignal(false);
  const [busy, setBusy] = createSignal(false);

  onMount(() => {
    activateLogsPage();
    void store.load({ force: true });
  });
  onCleanup(() => disposeLogsPage());

  const runClear = async () => {
    setBusy(true);
    try {
      await clearLogs();
      setConfirmClear(false);
    } finally {
      setBusy(false);
    }
  };

  const copyEntry = async (entry: LogRow) => {
    try {
      await navigator.clipboard.writeText(formatForCopy(entry));
      pushToast('Log entry copied.');
    } catch {
      pushToast('Could not access clipboard.', 'error');
    }
  };

  const loading = () => store.phase() === 'loading';
  const hasLogs = () => store.order().length > 0;
  const truncated = () => store.total() > store.order().length;
  const filtered = () => store.search().trim() !== '' || store.level() !== 'all';

  return (
    <section class="page" id="panel-logs" data-solid-root="logs">
      <div class="page-header">
        <div class="titles">
          
          <h2>Logs</h2>
          
          
        </div>
      </div>

      <div class="toolbar">
        <label class="filter-field">
          <span class="icon"><IconSearch /></span>
          <input
            value={store.search()}
            onInput={(e) => {
              store.setSearch(e.currentTarget.value);
              void store.load({ force: true });
            }}
            placeholder="model id, request id, message, code…"
            aria-label="Search logs"
          />
        </label>
        <select
          class="inline-select"
          aria-label="Minimum severity"
          value={store.level()}
          onChange={(e) => {
            store.setLevel(e.currentTarget.value);
            void store.load({ force: true });
          }}
        >
          <option value="all">All captured (warning+)</option>
          <option value="warning">Warning and above</option>
          <option value="error">Errors only</option>
        </select>
        <label class="checkbox field-inline">
          <input type="checkbox" checked={store.autoRefresh()} onChange={(e) => store.setAutoRefresh(e.currentTarget.checked)} /> Auto-refresh
        </label>
        <span class="spacer" />
        <Button variant="secondary" onClick={() => store.load({ force: true })} disabled={loading()}>
          <span class="icon"><IconRefresh /></span> Refresh
        </Button>
        <Button variant="danger" onClick={() => setConfirmClear(true)} disabled={loading() || !hasLogs()}>
          <span class="icon"><IconTrash /></span> Clear
        </Button>
      </div>

      

      <div class="table-wrap">
        <Show when={hasLogs()}>
          <table class="t-logs">
            <colgroup>
              <col /><col /><col />
              <col /><col />
            </colgroup>
            <thead>
              <tr>
                <th>Time</th><th>Severity</th><th>Source</th><th>Message</th><th>Model</th>
              </tr>
            </thead>
            <For each={store.order()}>
              {(id) => {
                const row = () => store.byId[id] ?? {};
                const expanded = () => store.expandedId() === id;
                const toggle = () => store.setExpandedId(expanded() ? null : id);
                return (
                  <tbody>
                    <tr
                      class={`request-row ${rowClass(row())}`}
                      role="button"
                      tabIndex={0}
                      aria-expanded={expanded()}
                      aria-label={`${row().level} from ${row().category}: ${row().message}`}
                      onClick={toggle}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter' || e.key === ' ') {
                          e.preventDefault();
                          toggle();
                        }
                      }}
                    >
                      <td>{formatTime(row().lastTimestampUtc || row().timestampUtc)}</td>
                      <td>
                        <span class={`tag level-${String(row().level ?? '').toLowerCase()}`}>{String(row().level ?? '')}</span>
                        <Show when={Number(row().repeats) > 1}>
                          <span class="tag" title="Identical events collapsed into this row">×{row().repeats}</span>
                        </Show>
                      </td>
                      <td class="col-truncate" title={String(row().category ?? '')}>{String(row().category ?? '')}</td>
                      <td class="col-truncate" title={String(row().message ?? '')}>{String(row().message ?? '')}</td>
                      <td class="col-truncate" title={row().modelId ?? ''}>{row().modelId ?? '—'}</td>
                    </tr>
                    <Show when={expanded()}>
                      <tr class="request-detail-row">
                        <td colspan="5">
                          <Show when={row().hint}>
                            <p class="log-hint">
                              <span class="icon"><IconLightbulb /></span>
                              <span>{row().hint}</span>
                            </p>
                          </Show>
                          <dl class="request-detail-list">
                            <div><dt>First seen</dt><dd>{formatTime(row().timestampUtc)}</dd></div>
                            <div><dt>Occurrences</dt><dd>{row().repeats ?? 1}</dd></div>
                            <div><dt>Event code</dt><dd><code>{row().eventCode ?? '—'}</code></dd></div>
                            <div><dt>Request ID</dt><dd><code>{row().requestId ?? '—'}</code></dd></div>
                          </dl>
                          <Show when={row().detail}>
                            <p class="mb-0"><strong>Detail</strong></p>
                            <pre class="model-test-content">{row().detail}</pre>
                          </Show>
                          <div class="row-actions" onClick={(e) => e.stopPropagation()}>
                            <Button variant="secondary" onClick={() => void copyEntry(row())}>
                              <span class="icon"><IconCopy /></span> Copy entry
                            </Button>
                          </div>
                        </td>
                      </tr>
                    </Show>
                  </tbody>
                );
              }}
            </For>
          </table>
        </Show>
        <Show when={loading() && !hasLogs()}>
          <table>
            <tbody>
              <tr class="skeleton-row"><td colspan="5" /></tr>
              <tr class="skeleton-row"><td colspan="5" /></tr>
              <tr class="skeleton-row"><td colspan="5" /></tr>
            </tbody>
          </table>
        </Show>
      </div>
      <Show when={!loading() && !hasLogs()}>
        <div class="empty-state">
          <span class="empty-state-icon icon"><IconFileText /></span>
          <p>{filtered() ? 'No log entries match this filter.' : 'No warnings or errors recorded since the gateway started.'}</p>
        </div>
      </Show>

      <Dialog open={confirmClear()} title="Clear the log buffer?" onClose={() => setConfirmClear(false)}>
        <p>
          Discards every entry currently held in memory. This cannot be undone — durable logs written by the
          gateway's configured log providers are unaffected.
        </p>
        <div class="modal-actions">
          <Button variant="secondary" onClick={() => setConfirmClear(false)}>Cancel</Button>
          <Button variant="danger" onClick={runClear} disabled={busy()}>Clear</Button>
        </div>
      </Dialog>
    </section>
  );
}
