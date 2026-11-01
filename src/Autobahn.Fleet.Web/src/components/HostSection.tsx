import { For, Match, Show, Switch } from 'solid-js';
import { hostState, type HostState } from '../domain/fleet';
import { ago, bytes, ratio } from '../domain/format';
import { waitingOn } from '../domain/panel';
import type { Gpu, HistoryAnswer, NodeView, WaitingChange } from '../types';
import { GpuCard, type GpuTrend } from './GpuCard';

const RUNBOOK = '/admin/fleet/runbook.html';

const CHIP: Record<HostState, string> = {
  waiting: 'Waiting for the first answer',
  healthy: 'Healthy',
  warning: 'Warning',
  'gpu-error': 'GPUs unreadable',
  unreachable: 'Agent unreachable',
};

function trendOf(history: HistoryAnswer | undefined, index: number, totalMiB: number | null): GpuTrend | undefined {
  if (!history || history.points.length < 2) return undefined;
  const load: (number | null)[] = [];
  const memory: (number | null)[] = [];
  for (const point of history.points) {
    const reading = point.gpus[String(index)];
    memory.push(ratio(reading?.[0], totalMiB));
    load.push(ratio(reading?.[1], 100));
  }
  return { load, memory };
}

export function HostSection(props: {
  node: NodeView;
  now: number;
  history?: HistoryAnswer;
  /** Change requests the console opened that still wait, on any host. */
  waiting: WaitingChange[];
  onOpenGpu: (gpu: Gpu) => void;
}) {
  const state = () => hostState(props.node);
  const status = () => props.node.status;
  const gpus = () => status()?.gpu.gpus ?? [];
  const stale = () => props.node.reachable === false;
  const disk = () => status()?.disk;
  const used = () => ratio(disk()?.usedBytes, disk()?.totalBytes);
  const reserve = () => ratio(status()?.minFreeBytes, disk()?.totalBytes);

  return (
    <section class="host">
      <header class="host-head">
        <h2>{props.node.id}</h2>
        <span class={`chip chip-${state()}`}>{CHIP[state()]}</span>
        <Show when={status()}>
          <span class="muted host-meta">
            agent {status()!.agentVersion}
            <Show when={status()!.gpu.driver.kernelModule}> · driver {status()!.gpu.driver.kernelModule}</Show>
          </span>
        </Show>
      </header>

      <Show when={disk()?.totalBytes}>
        <div class="disk" title={`Model store filesystem at ${disk()!.path}`}>
          <div class="bar bar-disk">
            <div class="bar-fill" style={{ width: `${(used() ?? 0) * 100}%` }} />
            {/* The part a download must leave free. */}
            <div class="bar-reserve" style={{ width: `${(reserve() ?? 0) * 100}%` }} />
          </div>
          <span class="muted">
            {bytes(disk()!.usedBytes)} used · <b>{bytes(disk()!.freeBytes)} free</b> of {bytes(disk()!.totalBytes)}
            {' '}· a download must leave {bytes(status()!.minFreeBytes)}
          </span>
        </div>
      </Show>

      <Switch>
        <Match when={state() === 'waiting'}>
          <p class="alert">The hub has not polled this host yet.</p>
        </Match>
        <Match when={state() === 'unreachable'}>
          <div class="alert alert-error" role="alert">
            <strong>
              {props.node.error?.code === 'token_refused' || props.node.error?.code === 'no_token'
                ? 'The hub cannot sign in to this host'
                : 'The agent on this host is not answering'}
            </strong>
            <p>{props.node.error?.message}</p>
            <p class="muted">
              Last answer: {ago(props.node.lastOkAt, props.now)}.
              <Show when={status()}> What follows is the state it reported then.</Show>
            </p>
          </div>
        </Match>
        <Match when={state() === 'gpu-error'}>
          <div class="alert alert-error" role="alert">
            <strong>{status()!.gpu.error?.title ?? 'The GPUs cannot be read'}</strong>
            <Show when={status()!.gpu.error?.code === 'driver_mismatch'}>
              <p class="versions">
                Kernel module <b>{status()!.gpu.driver.kernelModule || 'unknown'}</b> · library{' '}
                <b>{status()!.gpu.driver.library || 'unknown'}</b>
              </p>
            </Show>
            <p>{status()!.gpu.error?.hint}</p>
            <pre class="raw">{status()!.gpu.error?.message}</pre>
            <Show when={status()!.gpu.error?.code === 'driver_mismatch'}>
              <a class="link" href={RUNBOOK} target="_blank" rel="noreferrer">Runbook: clear the driver mismatch</a>
            </Show>
          </div>
        </Match>
      </Switch>

      <For each={status()?.gpu.warnings ?? []}>
        {(warning) => <p class="alert alert-warn">{warning}</p>}
      </For>

      <div class="gpus">
        <For each={gpus()}>
          {(gpu) => (
            <GpuCard
              gpu={gpu}
              all={gpus()}
              stale={stale()}
              trend={stale() ? undefined : trendOf(props.history, gpu.index, gpu.memoryTotalMiB)}
              waiting={waitingOn(props.waiting, props.node.id, gpu.index)}
              onOpen={() => props.onOpenGpu(gpu)}
            />
          )}
        </For>
      </div>
      <Show when={status() && !gpus().length}>
        <p class="muted">This host lists no GPUs.</p>
      </Show>
    </section>
  );
}
