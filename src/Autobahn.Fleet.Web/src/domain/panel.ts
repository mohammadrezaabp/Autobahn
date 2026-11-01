import type { Gpu, GpuProcess, InventoryServer, ManagedServer, WaitingChange } from '../types';

/** The files that say a server should be on this GPU of this host. */
export function wantedOn(files: InventoryServer[], host: string, gpu: number): InventoryServer[] {
  return files.filter((file) => file.host === host && file.gpus.includes(gpu));
}

/** fleet's servers that run on the GPU and have no file: what `the reviewed deployment` calls an orphan. */
export function unfiled(servers: ManagedServer[], files: InventoryServer[], host: string, gpu: number): ManagedServer[] {
  const named = new Set(files.filter((file) => file.host === host).map((file) => file.name));
  return servers.filter((server) => server.state !== 'absent' && (server.gpus ?? []).includes(gpu) && !named.has(server.name));
}

/** How the host's agent describes a file's server now; `absent` when it runs nothing of that name. */
export function runState(file: InventoryServer, servers: ManagedServer[]): string {
  return servers.find((server) => server.name === file.name)?.state ?? 'absent';
}

/** What is on the card that no file on it accounts for: other teams' servers and other processes. */
export function strangers(gpu: Gpu, wanted: InventoryServer[]): GpuProcess[] {
  const served = new Set(wanted.map((file) => file.servedName));
  const seen = new Set<string>();
  return gpu.processes.filter((process) => {
    if (process.kind === 'model' && served.has(process.servedName)) return false;
    // A server shows once per worker process; one line is enough.
    const key = process.kind === 'model' ? `model:${process.servedName}` : `pid:${process.pid}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

/** Every GPU the removed servers give up, not only the one that was clicked. */
export function freed(files: InventoryServer[], removing: string[]): number[] {
  const gpus = new Set<number>();
  for (const file of files) if (removing.includes(file.name)) for (const index of file.gpus) gpus.add(index);
  return [...gpus].sort((a, b) => a - b);
}

/** Whether a file stays on the GPU after the removals: a server added there then has to share it. */
export function staysShared(wanted: InventoryServer[], removing: string[]): boolean {
  return wanted.some((file) => !removing.includes(file.name));
}

/** The ports the files of a host claim, running or not. */
export function inventoryPorts(files: InventoryServer[], host: string): number[] {
  return files.filter((file) => file.host === host && file.port !== null).map((file) => file.port as number);
}

/** The change requests that wait and touch this GPU. */
export function waitingOn(changes: WaitingChange[], host: string, gpu: number): WaitingChange[] {
  return changes.filter((change) => change.node === host && change.gpus.includes(gpu));
}
