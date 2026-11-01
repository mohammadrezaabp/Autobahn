import { For, Show, createSignal } from 'solid-js';
import { ApiError, api } from '../api';
import { ago } from '../domain/format';
import { createPolling } from '../poll';
import type { AuditEntry } from '../types';

const REFRESH_MS = 15_000;

const ACTION: Record<string, string> = {
  'download.start': 'Started a download',
  'download.cancel': 'Cancelled a download',
  'store.delete': 'Deleted a model',
};

/** The hub's audit log: every change somebody asked for, whether the host did it or refused. */
export function ActivityPage(props: { onSignedOut: () => void }) {
  const [entries, setEntries] = createSignal<AuditEntry[]>([]);
  const [now, setNow] = createSignal(Date.now() / 1000);
  const [loaded, setLoaded] = createSignal(false);
  const [failure, setFailure] = createSignal('');

  createPolling(async () => {
    try {
      const answer = await api.audit();
      setEntries(answer.entries);
      setNow(answer.time);
      setFailure('');
      setLoaded(true);
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) return props.onSignedOut();
      setFailure(error instanceof ApiError ? error.message : 'Could not load the activity.');
    }
  }, REFRESH_MS);

  return (
    <>
      <Show when={failure()}>
        <p class="alert alert-error" role="alert">{failure()}</p>
      </Show>
      <Show when={loaded()} fallback={<p class="muted">Loading the activityâ€¦</p>}>
        <div class="table-wrap">
          <table class="table">
            <thead>
              <tr>
                <th>When</th>
                <th>Who</th>
                <th>What</th>
                <th>Host</th>
                <th>Target</th>
                <th>Outcome</th>
              </tr>
            </thead>
            <tbody>
              <For
                each={entries()}
                fallback={
                  <tr>
                    <td colSpan={6} class="muted">No activity.</td>
                  </tr>
                }
              >
                {(entry) => (
                  <tr>
                    <td class="muted" title={new Date(entry.at * 1000).toLocaleString()}>{ago(entry.at, now())}</td>
                    <td class="mono">{entry.actor}</td>
                    <td>{ACTION[entry.action] ?? entry.action}</td>
                    <td>{entry.node}</td>
                    <td class="mono">{entry.target}</td>
                    <td>
                      <Show
                        when={entry.outcome === 'ok'}
                        fallback={
                          <>
                            <span class="tag tag-bad">not done: {entry.outcome}</span>
                            <span class="muted"> {entry.detail}</span>
                          </>
                        }
                      >
                        <span class="tag tag-live">done</span>
                        <span class="muted"> {entry.detail}</span>
                      </Show>
                    </td>
                  </tr>
                )}
              </For>
            </tbody>
          </table>
        </div>
      </Show>
    </>
  );
}
