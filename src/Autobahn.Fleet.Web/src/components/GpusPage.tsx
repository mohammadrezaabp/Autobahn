import { For, Show, createMemo, createSignal, onCleanup, onMount } from 'solid-js';
import { createStore, reconcile } from 'solid-js/store';
import { ApiError, api } from '../api';
import { fleetSummary } from '../domain/fleet';
import { gib } from '../domain/format';
import { waitingOn } from '../domain/panel';
import type { HistoryAnswer, Me, NodeView, WaitingChange } from '../types';
import { GpuPanel } from './GpuPanel';
import { HostSection } from './HostSection';

const REFRESH_MS = 5_000;
const HISTORY_MS = 30_000;

export function GpusPage(props: { me: Me; onSignedOut: () => void }) {
  const [nodes, setNodes] = createStore<{ list: NodeView[] }>({ list: [] });
  const [now, setNow] = createSignal(Date.now() / 1000);
  const [histories, setHistories] = createSignal<Record<string, HistoryAnswer>>({});
  const [failure, setFailure] = createSignal('');
  const [loaded, setLoaded] = createSignal(false);
  const [waiting, setWaiting] = createSignal<WaitingChange[]>([]);
  // The GPU whose panel is open, by host and index: the card's own numbers keep moving under it.
  const [open, setOpen] = createSignal<{ node: string; gpu: number } | null>(null);
  const openNode = () => nodes.list.find((node) => node.id === open()?.node);
  const openGpu = () => openNode()?.status?.gpu.gpus.find((gpu) => gpu.index === open()?.gpu);
  const summary = createMemo(() => fleetSummary(nodes.list));

  function failed(error: unknown) {
    if (error instanceof ApiError && error.status === 401) {
      props.onSignedOut();
      return;
    }
    setFailure(error instanceof ApiError ? error.message : 'Could not load the hosts.');
  }

  async function refresh() {
    try {
      const answer = await api.nodes();
      // reconcile keeps the cards in place and changes only the numbers that moved.
      setNodes('list', reconcile(answer.nodes, { key: 'id' }));
      setNow(answer.time);
      setFailure('');
      setLoaded(true);
    } catch (error) {
      failed(error);
    }
  }

  async function refreshHistory() {
    const next: Record<string, HistoryAnswer> = {};
    await Promise.all(
      nodes.list.map(async (node) => {
        try {
          next[node.id] = await api.history(node.id);
        } catch {
          // The cards work without their trend lines.
        }
      }),
    );
    setHistories(next);
  }

  async function refreshWaiting() {
    try {
      setWaiting((await api.changes()).changes);
    } catch {
      // The cards work without the mark of a waiting change.
    }
  }

  async function apply(change: WaitingChange) {
    if (!change.id || !window.confirm(`Apply this reviewed change on ${change.node}? This may stop and replace model servers.`)) return;
    try {
      await api.applyChange(change.id);
      await refreshWaiting();
      await refresh();
    } catch (error) {
      failed(error);
    }
  }

  onMount(() => {
    let lastHistory = 0;
    const tick = async () => {
      // A hidden tab asks for nothing.
      if (document.hidden) return;
      await refresh();
      if (Date.now() - lastHistory >= HISTORY_MS) {
        lastHistory = Date.now();
        void refreshHistory();
        void refreshWaiting();
      }
    };
    void tick();
    const timer = setInterval(tick, REFRESH_MS);
    const onVisible = () => void tick();
    document.addEventListener('visibilitychange', onVisible);
    onCleanup(() => {
      clearInterval(timer);
      document.removeEventListener('visibilitychange', onVisible);
    });
  });

  return (
    <>
      <Show when={failure()}>
        <p class="alert alert-error" role="alert">{failure()}</p>
      </Show>
      <Show when={loaded()} fallback={<p class="muted">Loading the hosts…</p>}>
        <Show when={waiting().some(change => change.id)}>
          <section class="card" aria-label="Deployment changes">
            <h2>Deployment changes</h2>
            <For each={waiting().filter(change => change.id)}>{change => (
              <div class="row">
                <span>{change.node}: remove {change.removes.join(', ') || 'none'}; add {change.adds.join(', ') || 'none'} · {change.status}</span>
                <Show when={change.message}><p role="alert">{change.message}</p></Show>
                <Show when={props.me.canWrite && change.status === 'proposed'}>
                  <button class="btn btn-primary" type="button" onClick={() => void apply(change)}>Apply reviewed change</button>
                </Show>
              </div>
            )}</For>
          </section>
        </Show>
        <section class="fleet" aria-label="Fleet">
          <div class="stat">
            <span class="stat-value">{summary().serving}</span>
            <span class="stat-label">GPUs serving</span>
          </div>
          <div class="stat">
            <span class="stat-value">{summary().free}</span>
            <span class="stat-label">free</span>
          </div>
          <div class="stat" classList={{ 'stat-bad': summary().unreadable > 0 }}>
            <span class="stat-value">{summary().unreadable}</span>
            <span class="stat-label">unreadable</span>
          </div>
          <div class="stat">
            <span class="stat-value">
              {gib(summary().memoryUsedMiB)}
              <small> / {gib(summary().memoryTotalMiB)} GiB</small>
            </span>
            <span class="stat-label">GPU memory in use, measured</span>
          </div>
          <div class="stat" classList={{ 'stat-bad': summary().hostsWithProblem.length > 0 }}>
            <span class="stat-value">{summary().hostsWithProblem.length}</span>
            <span class="stat-label">
              {summary().hostsWithProblem.length === 1 ? 'host' : 'hosts'} with a problem
              {summary().hostsWithProblem.length ? `: ${summary().hostsWithProblem.join(', ')}` : ''}
            </span>
          </div>
        </section>
        <div class="hosts">
          <For each={nodes.list} fallback={
            <section class="ab-empty-fleet" aria-label="Connect your first host">
              <span class="ab-monogram" aria-hidden="true"><i /><i /><i /></span>
              <div>
                <h2>No hosts configured</h2>
                <a href="/admin/fleet/runbook.html">Host configuration guide ↗</a>
              </div>
            </section>
          }>
            {(node) => (
              <HostSection
                node={node}
                now={now()}
                history={histories()[node.id]}
                waiting={waiting()}
                onOpenGpu={(gpu) => setOpen({ node: node.id, gpu: gpu.index })}
              />
            )}
          </For>
        </div>
      </Show>
      <Show when={open() && openNode() && openGpu()}>
        <GpuPanel
          node={openNode()!}
          gpu={openGpu()!}
          canWrite={props.me.canWrite}
          waiting={waitingOn(waiting(), open()!.node, open()!.gpu)}
          onClose={() => setOpen(null)}
          onSignedOut={props.onSignedOut}
          onSaved={() => void refreshWaiting()}
        />
      </Show>
    </>
  );
}
