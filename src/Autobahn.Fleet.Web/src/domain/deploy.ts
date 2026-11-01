import type { AgentStatus, ManagedServer, ServerAsk, StoreModel } from '../types';
import { REPO } from './store';

/** Whether a server can be described for this entry: a whole model, in a place the agent mounts. */
export function deployable(model: StoreModel): boolean {
  if (model.incomplete || model.downloading) return false;
  if (model.kind === 'flat') return true;
  return model.kind === 'hub' && model.repoType === 'model' && REPO.test(model.repoId);
}

/** What the inventory file names as `model`: owner/name from the hub cache, or the directory. */
export function modelArg(model: StoreModel): string {
  return model.kind === 'hub' ? model.repoId : model.path;
}

/** A server's name from any text: the agent takes lower-case letters, digits and dashes, 40 at most. */
export function serverName(text: string): string {
  return text
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+/, '')
    .slice(0, 40)
    .replace(/-+$/, '');
}

/** The model's own name without its owner: what clients would most likely ask for. */
export function servedNameOf(model: StoreModel): string {
  return model.name.split('/').pop() ?? model.name;
}

/**
 * `vllm serve` arguments from the form: one flag to a line, and everything after the first space
 * is that flag's value, whatever it holds. So `--hf-overrides {"a": 1}` stays two arguments.
 */
export function parseArgs(text: string): string[] {
  const args: string[] = [];
  for (const line of text.split('\n')) {
    const trimmed = line.trim();
    if (!trimmed) continue;
    const space = trimmed.search(/\s/);
    if (space < 0) args.push(trimmed);
    else args.push(trimmed.slice(0, space), trimmed.slice(space).trim());
  }
  return args;
}

/** Ports something on the host has: fleet's servers and every model server the agent sees. */
export function takenPorts(servers: ManagedServer[], status: AgentStatus | null): number[] {
  const ports = new Set<number>();
  for (const server of servers) if (server.port) ports.add(server.port);
  for (const server of status?.servers ?? []) {
    if (server.port) ports.add(server.port);
    for (const port of server.hostPorts ?? []) ports.add(port);
  }
  return [...ports].sort((a, b) => a - b);
}

/** The lowest port of the host's range that nothing has, or null when the range is full. */
export function suggestPort(range: [number, number], taken: number[]): number | null {
  for (let port = range[0]; port <= range[1]; port += 1) {
    if (!taken.includes(port)) return port;
  }
  return null;
}

/** What is on a GPU now, in words, or '' when nothing is. The agent decides; this is for choosing. */
export function gpuUse(status: AgentStatus | null, index: number): string {
  const processes = status?.gpu.gpus.find((gpu) => gpu.index === index)?.processes ?? [];
  const names = [...new Set(processes.map((p) => p.servedName || (p.kind === 'model' ? 'a model server' : 'another process')))];
  return names.join(', ');
}

/** fleet's own servers on the chosen GPUs: what a change request can remove to free them. */
export function holders(servers: ManagedServer[], gpus: number[]): ManagedServer[] {
  return servers.filter((server) => server.state !== 'absent' && (server.gpus ?? []).some((index) => gpus.includes(index)));
}

/** The image most of fleet's servers on the host run: the likeliest one for the next. */
export function usualImage(servers: ManagedServer[]): string {
  const counts = new Map<string, number>();
  for (const server of servers) if (server.image) counts.set(server.image, (counts.get(server.image) ?? 0) + 1);
  return [...counts.entries()].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))[0]?.[0] ?? '';
}

/** The form a server is described in, as typed: the port and the arguments are still text. */
export interface ServerForm {
  name: string;
  servedName: string;
  revision: string;
  gpus: number[];
  port: string;
  image: string;
  args: string;
  apiKey: boolean;
  shared: boolean;
}

/** An empty form for a model: the names come from the model's, the rest from the host later. */
export function newForm(model: StoreModel): ServerForm {
  const served = servedNameOf(model);
  return { name: serverName(served), servedName: served, revision: '', gpus: [], port: '', image: '', args: '', apiKey: true, shared: false };
}

/** The description the form holds, or null while a field the agent needs is still empty. */
export function askOf(form: ServerForm, model: StoreModel): ServerAsk | null {
  const port = Number(form.port);
  if (!form.name.trim() || !form.image.trim() || !form.gpus.length || !Number.isInteger(port) || port <= 0) return null;
  return {
    name: form.name.trim(), model: modelArg(model), revision: form.revision, gpus: [...form.gpus].sort((a, b) => a - b),
    port, image: form.image.trim(), servedName: form.servedName.trim(), args: parseArgs(form.args), apiKey: form.apiKey,
    ...(form.shared ? { shared: true } : {}),
  };
}
