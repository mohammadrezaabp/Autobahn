import type {
  AuditAnswer, ChangeAnswer, ChangeAsk, ChangePlan, DownloadJob, DownloadsAnswer, HfModel, HfModelInfo, HistoryAnswer,
  InventoryAnswer, Me, NodesAnswer, PlanAnswer, ProposeAnswer, ServerAsk, ServersAnswer, StoreAnswer, WaitingChange,
} from './types';

/** The hub's answer to a failed request: its own `{code, message}`, or what the network did. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
  ) {
    super(message);
  }
}

// Compute pages share the gateway shell while retaining their API boundary.
const apiRoot = new URL('/admin/fleet/api/', window.location.origin);

async function call<T>(method: string, path: string, body?: unknown): Promise<T> {
  let response: Response;
  try {
    response = await fetch(new URL(path, apiRoot), {
      method,
      credentials: 'same-origin',
      headers: {
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
        ...(method === 'GET' ? {} : { 'X-AutobahnFleet-Request': '1' }),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch {
    throw new ApiError(0, 'network', 'The Autobahn fleet service did not answer. Check the connection and try again.');
  }
  if (response.status === 204) return undefined as T;
  let answer: unknown = null;
  try {
    answer = await response.json();
  } catch {
    // Not JSON: a proxy in front answered instead of the hub.
  }
  if (!response.ok) {
    const failure = answer as { code?: string; message?: string } | null;
    throw new ApiError(
      response.status,
      failure?.code ?? 'http_' + response.status,
      failure?.message ?? `The hub answered ${response.status}.`,
    );
  }
  return answer as T;
}

export const api = {
  applyChange: (id: string) => call<unknown>('POST', `changes/${encodeURIComponent(id)}/apply`, {}),
  me: () => call<Me>('GET', 'me'),
  signIn: (key: string) => call<{ actor: string; role: string }>('POST', 'session', { key }),
  signOut: () => call<void>('DELETE', 'session'),
  nodes: () => call<NodesAnswer>('GET', 'nodes'),
  history: (id: string) => call<HistoryAnswer>('GET', `nodes/${encodeURIComponent(id)}/history`),

  store: () => call<StoreAnswer>('GET', 'store'),
  rescan: (node: string) => call<{ scanning: boolean }>('POST', `nodes/${encodeURIComponent(node)}/store/rescan`, {}),
  deleteModel: (node: string, path: string, confirm: string) =>
    call<{ deleted: string; name: string; freedBytes: number }>(
      'POST', `nodes/${encodeURIComponent(node)}/store/delete`, { path, confirm }),

  downloads: () => call<DownloadsAnswer>('GET', 'downloads'),
  // `from` names a host that holds the model: it is copied from there instead of downloaded.
  startDownload: (node: string, repo: string, revision: string, from = '') =>
    call<DownloadJob>('POST', `nodes/${encodeURIComponent(node)}/downloads`, { repo, revision, ...(from ? { from } : {}) }),
  cancelDownload: (node: string, job: string) =>
    call<DownloadJob>('POST', `nodes/${encodeURIComponent(node)}/downloads/${encodeURIComponent(job)}/cancel`, {}),

  hfSearch: (search: string) => call<{ models: HfModel[] }>('GET', `hf/models?search=${encodeURIComponent(search)}`),
  // The repo's own slash stays a slash: the hub's route is hf/models/<owner>/<name>.
  hfModel: (repo: string, revision: string) =>
    call<HfModelInfo>(
      'GET',
      `hf/models/${repo.split('/').map(encodeURIComponent).join('/')}` +
        (revision ? `?revision=${encodeURIComponent(revision)}` : ''),
    ),

  servers: (node: string) => call<ServersAnswer>('GET', `nodes/${encodeURIComponent(node)}/servers`),
  // A dry run on the host: what a rollout would do, and the inventory file. Changes nothing.
  planServer: (node: string, ask: ServerAsk) =>
    call<PlanAnswer>('POST', `nodes/${encodeURIComponent(node)}/servers/plan`, ask),
  // Opens the change request that adds the file. The rollout is still the reviewed deployment, after the merge.
  proposeServer: (node: string, ask: ServerAsk) =>
    call<ProposeAnswer>('POST', `nodes/${encodeURIComponent(node)}/servers/propose`, ask),

  // What should run: the files of inventory/servers on main.
  inventory: () => call<InventoryAnswer>('GET', 'inventory'),
  // A change drafted on the GPUs page, checked by the hub and the host. Changes nothing.
  planChange: (node: string, change: ChangeAsk) =>
    call<ChangePlan>('POST', `nodes/${encodeURIComponent(node)}/changes/plan`, change),
  // Opens the one change request that deletes and adds those files.
  proposeChange: (node: string, change: ChangeAsk) =>
    call<ChangeAnswer>('POST', `nodes/${encodeURIComponent(node)}/changes/propose`, change),
  // The change requests opened that way that still wait.
  changes: () => call<{ time: number; changes: WaitingChange[] }>('GET', 'changes'),

  audit: () => call<AuditAnswer>('GET', 'audit'),
};
