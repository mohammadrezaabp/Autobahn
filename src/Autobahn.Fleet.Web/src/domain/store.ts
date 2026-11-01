import type { DownloadJob, DownloadsNode, StoreModel, StoreNode } from '../types';

/** The shape the agent accepts for a download (Autobahn.Agent). */
export const REPO = /^[A-Za-z0-9][A-Za-z0-9._-]{0,95}\/[A-Za-z0-9][A-Za-z0-9._-]{0,95}$/;

export interface StoreRow {
  node: string;
  model: StoreModel;
  /** Other hosts that hold the same model. */
  alsoOn: string[];
  /** Hosts that answered and do not hold it, for a model Hugging Face can be asked for again. */
  missingOn: string[];
}

/** What makes two entries on different hosts the same model. */
export function modelKey(model: StoreModel): string {
  return model.kind === 'flat' ? `dir:${model.name}` : `${model.repoType}:${model.repoId}`;
}

/** Whether the entry is a model repo a host could download by name. */
export function downloadable(model: StoreModel): boolean {
  return model.kind !== 'flat' && model.repoType === 'model' && REPO.test(model.repoId);
}

/** Whether the entry is a download that stopped part way and can be taken up again where it is. */
export function resumable(model: StoreModel): boolean {
  return model.kind === 'hub' && downloadable(model) && model.incomplete && !model.downloading;
}

/** Every host's entries in one list, largest first. */
export function storeRows(nodes: StoreNode[]): StoreRow[] {
  const holders = new Map<string, string[]>();
  for (const node of nodes) {
    for (const model of node.store?.models ?? []) {
      const key = modelKey(model);
      holders.set(key, [...(holders.get(key) ?? []), node.id]);
    }
  }
  const answering = nodes.filter((n) => n.store).map((n) => n.id);
  const rows: StoreRow[] = [];
  for (const node of nodes) {
    for (const model of node.store?.models ?? []) {
      const on = holders.get(modelKey(model)) ?? [];
      rows.push({
        node: node.id,
        model,
        alsoOn: on.filter((id) => id !== node.id),
        missingOn: downloadable(model) ? answering.filter((id) => !on.includes(id)) : [],
      });
    }
  }
  return rows.sort((a, b) => b.model.sizeBytes - a.model.sizeBytes || a.model.name.localeCompare(b.model.name));
}

/**
 * The copy of a model that `target` can fetch over the LAN instead of downloading it: complete, in
 * the hub cache of a host `target` is set up to copy from. The hub and both hosts check again.
 */
export function copySource(nodes: StoreNode[], target: string, repo: string): { node: string; model: StoreModel } | null {
  const from = nodes.find((n) => n.id === target)?.store?.copyFrom ?? [];
  for (const node of nodes) {
    if (!from.includes(node.id)) continue;
    const model = node.store?.models.find(
      (m) => m.kind === 'hub' && m.repoType === 'model' && m.repoId === repo && !m.incomplete && !m.downloading,
    );
    if (model) return { node: node.id, model };
  }
  return null;
}

export interface StoreFilter {
  /** A host id, or '' for every host. */
  node: string;
  /** A layout, or '' for every layout. */
  kind: string;
  unusedOnly: boolean;
  text: string;
}

export function filterRows(rows: StoreRow[], filter: StoreFilter): StoreRow[] {
  const text = filter.text.trim().toLowerCase();
  return rows.filter(
    (row) =>
      (!filter.node || row.node === filter.node) &&
      (!filter.kind || row.model.kind === filter.kind) &&
      (!filter.unusedOnly || !row.model.inUseBy.length) &&
      (!text ||
        row.model.name.toLowerCase().includes(text) ||
        row.model.inUseBy.some((name) => name.toLowerCase().includes(text))),
  );
}

export function totalBytes(rows: StoreRow[]): number {
  return rows.reduce((sum, row) => sum + row.model.sizeBytes, 0);
}

export interface DiskBreakdown {
  total: number;
  /** Models a running server reads. */
  inUse: number;
  /** Models nothing serves: what deleting could free. */
  unused: number;
  /** Everything else on the filesystem that is not a listed model. */
  other: number;
  free: number;
}

export function diskBreakdown(node: StoreNode): DiskBreakdown | null {
  const disk = node.store?.disk;
  if (!node.store || !disk || !disk.totalBytes || disk.usedBytes === null || disk.freeBytes === null) return null;
  let inUse = 0;
  let unused = 0;
  for (const model of node.store.models) {
    if (model.inUseBy.length) inUse += model.sizeBytes;
    else unused += model.sizeBytes;
  }
  return {
    total: disk.totalBytes, inUse, unused, free: disk.freeBytes,
    // Never negative: a model directory on another filesystem would otherwise push it under zero.
    other: Math.max(0, disk.usedBytes - inUse - unused),
  };
}

/** Why this entry cannot be deleted from the console, or '' when it can. The agent checks again. */
export function deleteBlock(model: StoreModel, store: AgentStoreFlags, canWrite: boolean): string {
  if (!canWrite) return 'This key can look but not change anything.';
  if (!store.allowDelete) return 'Deleting is switched off on this host (NODE_AGENT_ALLOW_DELETE).';
  if (model.inUseBy.length) return `Served by ${model.inUseBy.join(', ')}. Stop that server first.`;
  if (model.downloading) return 'Downloading now. Cancel the download first.';
  return '';
}

type AgentStoreFlags = { allowDelete: boolean };

export interface Fit {
  /** What still has to be fetched. */
  needed: number;
  freeAfter: number;
  /** false when the agent would refuse it: less than the reserve would stay free. */
  fits: boolean;
}

/** The same sum the agent's download worker does before the first byte (hf_worker.py). */
export function fit(total: number, existing: number, free: number, reserve: number): Fit {
  const needed = Math.max(0, total - existing);
  const freeAfter = free - needed;
  return { needed, freeAfter, fits: freeAfter >= reserve };
}

export const ACTIVE: readonly string[] = ['queued', 'running'];

export interface JobRow {
  node: string;
  job: DownloadJob;
  /** Offered on the newest job of a model that stopped short; it continues from the finished files. */
  canRestart: boolean;
}

/** Every host's jobs in one list: what is moving first, then newest first. */
export function jobRows(nodes: DownloadsNode[]): JobRow[] {
  const rows: JobRow[] = [];
  for (const node of nodes) {
    const newest = new Map<string, DownloadJob>();
    for (const job of node.downloads) {
      const seen = newest.get(job.repo);
      if (!seen || job.createdAt > seen.createdAt) newest.set(job.repo, job);
    }
    for (const job of node.downloads) {
      const stopped = job.state === 'failed' || job.state === 'cancelled' || job.state === 'interrupted';
      rows.push({ node: node.id, job, canRestart: stopped && newest.get(job.repo) === job });
    }
  }
  const active = (row: JobRow) => (ACTIVE.includes(row.job.state) ? 1 : 0);
  return rows.sort((a, b) => active(b) - active(a) || b.job.createdAt - a.job.createdAt);
}

/** 0 to 1, or null while the size is not known yet. */
export function progress(job: DownloadJob): number | null {
  if (job.state === 'completed') return 1;
  if (!job.totalBytes) return null;
  return Math.min(1, Math.max(0, job.doneBytes / job.totalBytes));
}

export function secondsLeft(job: DownloadJob): number | null {
  if (job.state !== 'running' || !job.totalBytes || job.bytesPerSecond <= 0) return null;
  return Math.max(0, (job.totalBytes - job.doneBytes) / job.bytesPerSecond);
}
