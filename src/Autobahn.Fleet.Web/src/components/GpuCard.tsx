import { For, Show } from 'solid-js';
import { gpuState, spread, type GpuState } from '../domain/fleet';
import { gib, ratio, whole } from '../domain/format';
import type { Gpu, WaitingChange } from '../types';
import { Sparkline } from './Sparkline';

const STATE_LABEL: Record<GpuState, string> = {
  serving: 'Serving',
  assigned: 'Assigned, not measured',
  other: 'In use, not by a model server',
  free: 'Free',
  unreadable: 'No readings',
  error: 'Error',
};

export interface GpuTrend {
  load: (number | null)[];
  memory: (number | null)[];
}

export function GpuCard(props: {
  gpu: Gpu;
  all: Gpu[];
  stale: boolean;
  trend?: GpuTrend;
  /** Change requests the console opened that touch this GPU and still wait. */
  waiting?: WaitingChange[];
  /** Opens the panel: what should run here, and changing it. */
  onOpen?: () => void;
}) {
  const state = () => (props.stale ? 'unreadable' : gpuState(props.gpu));
  const memory = () => ratio(props.gpu.memoryUsedMiB, props.gpu.memoryTotalMiB);
  const hasReadings = () => props.gpu.memoryUsedMiB !== null;

  return (
    <article class={`gpu gpu-${state()}`} classList={{ stale: props.stale }}>
      <header class="gpu-head">
        <span class="dot" title={STATE_LABEL[state()]} />
        <Show when={props.onOpen} fallback={<span class="gpu-index">GPU {props.gpu.index}</span>}>
          <button
            class="gpu-index gpu-open"
            type="button"
            title="What should run on this GPU, and changing it"
            onClick={() => props.onOpen!()}
          >
            GPU {props.gpu.index}
          </button>
        </Show>
        <span class="gpu-name muted">{props.gpu.name.replace(/^NVIDIA /, '')}</span>
      </header>

      <For each={props.waiting ?? []}>
        {(change) => (
          <a
            class="tag tag-warn gpu-waiting"
            href={change.url}
            target="_blank"
            rel="noopener noreferrer"
            title={
              `Opened in the console by ${change.actor}` +
              (change.removes.length ? `; removes ${change.removes.join(', ')}` : '') +
              (change.adds.length ? `; adds ${change.adds.join(', ')}` : '')
            }
          >
            change in !{change.iid}
          </a>
        )}
      </For>

      <Show when={props.gpu.error}>
        <p class="alert alert-error">{props.gpu.error}</p>
      </Show>

      <Show
        when={hasReadings()}
        fallback={<p class="muted gpu-noread">{props.stale ? 'Last known state' : 'No readings'}</p>}
      >
        <div class="bar" title="GPU memory in use">
          <div class="bar-fill" style={{ width: `${(memory() ?? 0) * 100}%` }} />
        </div>
        <div class="gpu-numbers">
          <span>
            <b>{gib(props.gpu.memoryUsedMiB)}</b> / {gib(props.gpu.memoryTotalMiB)} GiB
          </span>
          <span title="Load">{whole(props.gpu.utilizationPct, '%')}</span>
          <span title="Temperature">{whole(props.gpu.temperatureC, '°C')}</span>
          <span title={`Power, limit ${whole(props.gpu.powerLimitW, ' W')}`}>{whole(props.gpu.powerW, ' W')}</span>
        </div>
        <Show when={props.trend}>
          <div class="gpu-trend" title="The last hour: load (line) and memory (faint line)">
            <Sparkline class="spark-memory" points={props.trend!.memory} label="Memory in use over the last hour" />
            <Sparkline class="spark-load" points={props.trend!.load} label="Load over the last hour" />
          </div>
        </Show>
      </Show>

      <ul class="gpu-procs">
        <For each={props.gpu.processes} fallback={<li class="muted">Nothing on this card</li>}>
          {(process) => (
            <li classList={{ foreign: process.kind === 'other' }}>
              <Show
                when={process.kind === 'model'}
                fallback={
                  <>
                    <span class="proc-name" title={process.command}>{process.command || `pid ${process.pid}`}</span>
                    <span class="tag tag-warn">not a model server · {process.user || 'unknown user'}</span>
                  </>
                }
              >
                <span class="proc-name">{process.servedName}</span>
                <Show when={spread(process, props.all) > 1}>
                  <span class="tag">1 of {spread(process, props.all)}</span>
                </Show>
                <Show when={process.source === 'assignment'}>
                  <span class="tag tag-warn">assigned, not measured</span>
                </Show>
              </Show>
              <Show when={process.memoryMiB !== null}>
                <span class="proc-mem">{gib(process.memoryMiB)} GiB</span>
              </Show>
            </li>
          )}
        </For>
      </ul>
    </article>
  );
}
