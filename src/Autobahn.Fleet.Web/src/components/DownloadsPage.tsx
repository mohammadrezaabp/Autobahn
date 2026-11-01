import { For, Show, createMemo, createSignal } from 'solid-js';
import { ApiError, api } from '../api';
import { ago, bytes, duration } from '../domain/format';
import { ACTIVE, jobRows, progress, secondsLeft, type JobRow } from '../domain/store';
import { createPolling } from '../poll';
import type { DownloadsNode, JobState, Me } from '../types';
import { DownloadDialog } from './DownloadDialog';

const REFRESH_MS = 3_000;

const STATE: Record<JobState, string> = {
  queued: 'Queued',
  running: 'Downloading',
  completed: 'Completed',
  failed: 'Failed',
  cancelled: 'Cancelled',
  interrupted: 'Interrupted',
};

function Job(props: { row: JobRow; now: number; canWrite: boolean; busy: boolean; onCancel: () => void; onRestart: () => void }) {
  const job = () => props.row.job;
  const active = () => ACTIVE.includes(job().state);
  const done = () => progress(job());

  return (
    <li class={`job job-${job().state}`}>
      <div class="job-head">
        <span class="model-name">{job().repo}</span>
        <Show when={job().revision && job().revision !== 'main'}>
          <span class="tag">{job().revision}</span>
        </Show>
        <span class="muted">on {props.row.node}</span>
        <Show when={job().source}>
          <span class="tag" title="Copied over the local network with rsync, not downloaded">from {job().source}</span>
        </Show>
        <span class={`chip chip-job-${job().state}`}>
          {job().source && job().state === 'running' ? 'Copying' : (STATE[job().state] ?? job().state)}
        </span>
        <span class="job-actions">
          <Show when={props.canWrite && active()}>
            <button class="btn" type="button" disabled={props.busy} onClick={props.onCancel}>Cancel</button>
          </Show>
          <Show when={props.canWrite && props.row.canRestart}>
            <button
              class="btn"
              type="button"
              disabled={props.busy}
              title="Files that finished are kept; only the rest is fetched"
              onClick={props.onRestart}
            >
              Start again
            </button>
          </Show>
        </span>
      </div>
      <Show when={job().state !== 'completed' && (active() || job().doneBytes > 0)}>
        <div class="bar" classList={{ 'bar-unknown': done() === null && job().state === 'running' }}>
          <div class="bar-fill" style={{ width: `${(done() ?? 0) * 100}%` }} />
        </div>
      </Show>
      <div class="job-numbers muted">
        <Show when={job().state === 'queued'}>
          <span>Waiting for its turn: a host runs one download at a time.</span>
        </Show>
        <Show when={job().state !== 'queued'}>
          <span>
            {job().state === 'completed' ? bytes(job().totalBytes ?? job().doneBytes) : bytes(job().doneBytes)}
            <Show when={job().state !== 'completed' && job().totalBytes}> of {bytes(job().totalBytes)}</Show>
            <Show when={done() !== null && job().state !== 'completed'}> · {Math.floor(done()! * 100)}%</Show>
          </span>
        </Show>
        <Show when={job().state === 'running'}>
          <span>{job().bytesPerSecond > 0 ? `${bytes(job().bytesPerSecond)}/s` : 'no bytes moving right now'}</span>
          <Show when={secondsLeft(job()) !== null}>
            <span>about {duration(secondsLeft(job()))} left</span>
          </Show>
        </Show>
        <Show when={job().files}>
          <span>{job().files} files</span>
        </Show>
        <span>
          {job().finishedAt
            ? `ended ${ago(job().finishedAt, props.now)}`
            : `started ${ago(job().startedAt ?? job().createdAt, props.now)}`}
        </span>
        <Show when={job().requestedBy}>
          <span>by {job().requestedBy}</span>
        </Show>
      </div>
      <Show when={job().message}>
        <p class="job-message" classList={{ 'job-message-bad': job().state === 'failed' }}>{job().message}</p>
      </Show>
    </li>
  );
}

export function DownloadsPage(props: { me: Me; onSignedOut: () => void }) {
  const [nodes, setNodes] = createSignal<DownloadsNode[]>([]);
  const [now, setNow] = createSignal(Date.now() / 1000);
  const [loaded, setLoaded] = createSignal(false);
  const [failure, setFailure] = createSignal('');
  const [busy, setBusy] = createSignal('');
  const [dialog, setDialog] = createSignal(false);
  const rows = createMemo(() => jobRows(nodes()));

  function failed(error: unknown, fallback: string) {
    if (error instanceof ApiError && error.status === 401) return props.onSignedOut();
    setFailure(error instanceof ApiError ? error.message : fallback);
  }

  async function refresh() {
    try {
      const answer = await api.downloads();
      setNodes(answer.nodes);
      setNow(answer.time);
      setLoaded(true);
    } catch (error) {
      failed(error, 'Could not load the downloads.');
    }
  }

  async function act(row: JobRow, what: () => Promise<unknown>, fallback: string) {
    setBusy(row.job.id);
    setFailure('');
    try {
      await what();
      await refresh();
    } catch (error) {
      failed(error, fallback);
    } finally {
      setBusy('');
    }
  }

  createPolling(refresh, REFRESH_MS);

  return (
    <>
      <div class="toolbar">
        
        <Show when={props.me.canWrite}>
          <button class="btn btn-primary toolbar-end" type="button" onClick={() => setDialog(true)}>
            Download a model
          </button>
        </Show>
      </div>
      <Show when={failure()}>
        <p class="alert alert-error" role="alert">{failure()}</p>
      </Show>
      <For each={nodes().filter((n) => !n.ok)}>
        {(node) => (
          <p class="alert alert-error" role="alert">
            <strong>The downloads on {node.id} cannot be listed</strong>
            {node.error?.message}
          </p>
        )}
      </For>
      <Show when={loaded()} fallback={<p class="muted">Loading the downloads…</p>}>
        <ul class="jobs">
          <For each={rows()} fallback={<li class="muted">No downloads yet.</li>}>
            {(row) => (
              <Job
                row={row}
                now={now()}
                canWrite={props.me.canWrite}
                busy={busy() === row.job.id}
                onCancel={() => void act(row, () => api.cancelDownload(row.node, row.job.id), 'Could not cancel.')}
                onRestart={() =>
                  void act(
                    row,
                    () => api.startDownload(row.node, row.job.repo, row.job.revision, row.job.source),
                    'Could not start it again.',
                  )
                }
              />
            )}
          </For>
        </ul>
      </Show>
      <Show when={dialog()}>
        <DownloadDialog
          onClose={() => setDialog(false)}
          onSignedOut={props.onSignedOut}
          onStarted={() => {
            setDialog(false);
            void refresh();
          }}
        />
      </Show>
    </>
  );
}
