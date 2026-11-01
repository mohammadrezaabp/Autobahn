import type { Gpu, GpuProcess, NodeView } from '../types';

/** Under this much memory in use, a card with no process on it counts as free. */
export const FREE_BELOW_MIB = 1024;

export type GpuState = 'serving' | 'assigned' | 'other' | 'free' | 'unreadable' | 'error';

export function gpuState(gpu: Gpu): GpuState {
  if (gpu.error) return 'error';
  const models = gpu.processes.filter((p) => p.kind === 'model');
  if (gpu.memoryUsedMiB === null) return models.length ? 'assigned' : 'unreadable';
  if (models.length) return 'serving';
  if (gpu.processes.length) return 'other';
  return gpu.memoryUsedMiB < FREE_BELOW_MIB ? 'free' : 'other';
}

export type HostState = 'waiting' | 'healthy' | 'warning' | 'gpu-error' | 'unreachable';

export function hostState(node: NodeView): HostState {
  if (node.reachable === null) return 'waiting';
  if (!node.reachable) return 'unreachable';
  const gpu = node.status?.gpu;
  if (!gpu || !gpu.ok) return 'gpu-error';
  if (gpu.warnings.length || gpu.gpus.some((g) => g.error)) return 'warning';
  return 'healthy';
}

export interface FleetSummary {
  total: number;
  serving: number;
  free: number;
  unreadable: number;
  memoryUsedMiB: number;
  memoryTotalMiB: number;
  hostsWithProblem: string[];
}

export function fleetSummary(nodes: NodeView[]): FleetSummary {
  const summary: FleetSummary = {
    total: 0, serving: 0, free: 0, unreadable: 0, memoryUsedMiB: 0, memoryTotalMiB: 0, hostsWithProblem: [],
  };
  for (const node of nodes) {
    const state = hostState(node);
    if (state !== 'healthy' && state !== 'waiting') summary.hostsWithProblem.push(node.id);
    for (const gpu of node.status?.gpu.gpus ?? []) {
      summary.total += 1;
      // A host that stopped answering still lists its cards, but none of them is a reading any more.
      const s = node.reachable ? gpuState(gpu) : 'unreadable';
      if (s === 'serving') summary.serving += 1;
      else if (s === 'free') summary.free += 1;
      else if (s === 'unreadable' || s === 'assigned' || s === 'error') summary.unreadable += 1;
      if (node.reachable) {
        summary.memoryUsedMiB += gpu.memoryUsedMiB ?? 0;
        summary.memoryTotalMiB += gpu.memoryTotalMiB ?? 0;
      }
    }
  }
  return summary;
}

/** On how many of the host's cards the same model server runs: the "1 of 8" on a card. */
export function spread(process: GpuProcess, gpus: Gpu[]): number {
  if (process.serverPid === null) return 1;
  return gpus.filter((g) => g.processes.some((p) => p.serverPid === process.serverPid)).length;
}
