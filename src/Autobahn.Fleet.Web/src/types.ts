// The shapes the hub returns. `status` is the agent's /v1/status answer, passed through unchanged
// (Autobahn.Agent and gpu.py are where each field is produced).

export interface GpuProcess {
  pid: number;
  memoryMiB: number | null;
  kind: 'model' | 'other';
  servedName: string;
  serverPid: number | null;
  user: string;
  containerId: string;
  command: string;
  /** `assignment`: Docker gave the container this card, but nothing was measured. */
  source: 'nvidia-smi' | 'assignment';
}

export interface Gpu {
  index: number;
  uuid: string;
  name: string;
  memoryUsedMiB: number | null;
  memoryTotalMiB: number | null;
  utilizationPct: number | null;
  temperatureC: number | null;
  powerW: number | null;
  powerLimitW: number | null;
  eccUncorrected: number | null;
  error: string | null;
  processes: GpuProcess[];
}

export interface GpuFailure {
  code: string;
  title: string;
  message: string;
  hint: string;
}

export interface ModelServer {
  pid: number;
  model: string;
  servedName: string;
  port: number | null;
  hostPorts: number[];
  containerName: string;
}

export interface AgentStatus {
  name: string;
  hostname: string;
  agentVersion: string;
  time: number;
  disk: { path: string; totalBytes: number | null; usedBytes: number | null; freeBytes: number | null };
  minFreeBytes: number;
  gpu: {
    ok: boolean;
    error: GpuFailure | null;
    warnings: string[];
    source: 'nvidia-smi' | 'proc' | 'none';
    driver: { kernelModule: string; library: string };
    gpus: Gpu[];
  };
  servers: ModelServer[];
}

export interface NodeView {
  id: string;
  address: string;
  /** null until the hub's first poll of this host has finished. */
  reachable: boolean | null;
  error: { code: string; message: string } | null;
  lastOkAt: number | null;
  lastPollAt: number | null;
  status: AgentStatus | null;
}

export interface NodesAnswer {
  time: number;
  pollSeconds: number;
  nodes: NodeView[];
}

export interface HistoryAnswer {
  id: string;
  /** Order of the numbers in each GPU's array. */
  fields: string[];
  points: { t: number; gpus: Record<string, (number | null)[]> }[];
}

export interface Me {
  actor: string;
  role: 'operator' | 'viewer';
  canWrite: boolean;
  expiresAt: number;
  version: string;
}

/** One entry of a host's model store, as the agent's /v1/models lists it (Autobahn.Agent). */
export interface StoreModel {
  path: string;
  name: string;
  /** hub: the Hugging Face cache. flat: a plain directory of weights. stray: a cache directory outside the hub. */
  kind: 'hub' | 'flat' | 'stray';
  repoId: string;
  repoType: string;
  sizeBytes: number;
  fileCount: number;
  modifiedAt: number;
  incomplete: boolean;
  revisions: string[];
  inUseBy: string[];
  downloading: boolean;
}

export interface AgentStore {
  name: string;
  disk: AgentStatus['disk'];
  minFreeBytes: number;
  /** null until the agent has measured the store once. */
  scannedAt: number | null;
  scanning: boolean;
  allowDelete: boolean;
  /** Hosts this one can copy a model from over the LAN (NODE_AGENT_PEERS). Absent before agent 0.4.0. */
  copyFrom?: string[];
  models: StoreModel[];
}

export interface HostFailure {
  code: string;
  message: string;
}

export interface StoreNode {
  id: string;
  ok: boolean;
  error: HostFailure | null;
  store: AgentStore | null;
}

export interface StoreAnswer {
  time: number;
  nodes: StoreNode[];
}

export type JobState = 'queued' | 'running' | 'completed' | 'failed' | 'cancelled' | 'interrupted';

/** A download job, as the agent that runs it records it (Autobahn.Agent). */
export interface DownloadJob {
  id: string;
  repo: string;
  revision: string;
  state: JobState;
  createdAt: number;
  startedAt: number | null;
  finishedAt: number | null;
  totalBytes: number | null;
  doneBytes: number;
  files: number | null;
  bytesPerSecond: number;
  sha: string;
  path: string;
  errorCode: string;
  message: string;
  requestedBy: string;
  /** The host it is copied from; empty or absent for a download from Hugging Face. */
  source?: string;
}

export interface DownloadsNode {
  id: string;
  ok: boolean;
  error: HostFailure | null;
  downloads: DownloadJob[];
}

export interface DownloadsAnswer {
  time: number;
  nodes: DownloadsNode[];
}

export interface HfModel {
  id: string;
  downloads: number | null;
  likes: number | null;
  gated: boolean;
  pipelineTag: string;
  lastModified: string;
}

export interface HfModelInfo {
  id: string;
  sha: string;
  gated: boolean;
  files: number;
  /** null when Hugging Face lists a file without its size. */
  totalBytes: number | null;
}

/** What a host lets fleet run, from its own settings (Autobahn.Agent). */
export interface ServerPolicy {
  enabled: boolean;
  gpus: number[];
  sharedGpus: number[];
  mayStop: string[];
  /** Image repositories, without a tag. */
  images: string[];
  ports: [number, number];
}

/** A model server fleet runs on a host. The fields past `state` are absent for one that is not there. */
export interface ManagedServer {
  name: string;
  state: string;
  gpus?: number[];
  port?: number | null;
  image?: string;
}

export interface ServersAnswer {
  policy: ServerPolicy;
  servers: ManagedServer[];
  /** false when the hub has no GitLab token: it writes the file but cannot open the change request. */
  canPropose: boolean;
}

/** A model server asked for in the console. Free GPUs, or shared ones: nothing here stops a server. */
export interface ServerAsk {
  name: string;
  model: string;
  revision: string;
  gpus: number[];
  port: number;
  image: string;
  servedName: string;
  args: string[];
  apiKey: boolean;
  /** Present and true when the server is to be on a GPU beside others. */
  shared?: boolean;
}

/** The agent's answer to a dry run. */
export interface ServerPlan {
  action: string;
  image: string;
  imagePresent: boolean;
  warnings: string[];
}

export interface PlanAnswer {
  plan: ServerPlan;
  path: string;
  content: string;
}

export interface ProposeAnswer {
  url: string;
  iid: number | null;
  branch: string;
  path: string;
}

/** One file of inventory/servers on main, as the hub read it (Autobahn.Host). */
export interface InventoryServer {
  name: string;
  path: string;
  /** The file's own text, comments included. */
  content: string;
  /** Why the hub could not read the file; '' when it could. */
  error: string;
  host: string;
  gpus: number[];
  shared: boolean;
  model: string;
  revision: string;
  image: string;
  port: number | null;
  servedName: string;
  replaces: string[];
  args: string[];
  hasEnv: boolean;
}

export interface InventoryAnswer {
  /** The commit of main the files were read at. A change is checked against it. */
  commit: string;
  branch: string;
  servers: InventoryServer[];
  canPropose: boolean;
}

/** A change drafted on the GPUs page: files to delete and servers to add, on one host. */
export interface ChangeAsk {
  commit: string;
  remove: string[];
  add: ServerAsk[];
}

export interface PlannedAdd {
  name: string;
  path: string;
  content: string;
  /** The agent's answer to a dry run, or null when it refused. */
  plan: ServerPlan | null;
  refusal: HostFailure | null;
  /** What the files that stay on main say against it. */
  conflicts: string[];
}

export interface ChangePlan {
  commit: string;
  removes: { name: string; path: string; gpus: number[]; servedName: string }[];
  adds: PlannedAdd[];
  ok: boolean;
}

export interface ChangeAnswer {
  url: string;
  iid: number | null;
  branch: string;
  gpus: number[];
  removed: string[];
  added: string[];
}

/** A change request the console opened that is neither merged nor closed yet. */
export interface WaitingChange {
  id?: string;
  status?: string;
  message?: string;
  iid: number;
  url: string;
  node: string;
  gpus: number[];
  removes: string[];
  adds: string[];
  actor: string;
  at: number;
}

export interface AuditEntry {
  id: number;
  at: number;
  actor: string;
  action: string;
  node: string;
  target: string;
  outcome: string;
  detail: string;
}

export interface AuditAnswer {
  time: number;
  entries: AuditEntry[];
}
