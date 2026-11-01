import { For, Show, createEffect, createSignal, on, onCleanup, onMount } from 'solid-js';
import { createStore } from 'solid-js/store';
import { ApiError, api } from '../api';
import { askOf, deployable, newForm, suggestPort, takenPorts, usualImage, type ServerForm } from '../domain/deploy';
import { freed, inventoryPorts, runState, staysShared, strangers, unfiled, wantedOn } from '../domain/panel';
import type {
  ChangeAnswer, ChangeAsk, ChangePlan, Gpu, InventoryAnswer, NodeView, ServersAnswer, StoreModel, WaitingChange,
} from '../types';
import { Dialog } from './Dialog';
import { ServerFields } from './ServerFields';

const TYPING_MS = 500;

const STATE: Record<string, string> = {
  ready: 'runs',
  loading: 'loading',
  applying: 'being rolled out',
  stopped: 'stopped',
  absent: 'not running',
};

/**
 * One GPU: what the inventory on main says should run on it, beside what runs. An operator drafts
 * a change here (servers to remove, one to add) and saves it as a single change request on
 * inventory/servers. Nothing is rolled out from here: that is the reviewed deployment, after the merge.
 */
export function GpuPanel(props: {
  node: NodeView;
  gpu: Gpu;
  canWrite: boolean;
  /** Change requests that wait and touch this GPU. */
  waiting: WaitingChange[];
  onClose: () => void;
  onSignedOut: () => void;
  onSaved: () => void;
}) {
  const node = props.node.id;
  const index = props.gpu.index;

  const [inventory, setInventory] = createSignal<InventoryAnswer | null>(null);
  const [noInventory, setNoInventory] = createSignal('');
  const [host, setHost] = createSignal<ServersAnswer | null>(null);
  const [noHost, setNoHost] = createSignal('');
  const [removing, setRemoving] = createSignal<string[]>([]);
  const [adding, setAdding] = createSignal(false);
  const [models, setModels] = createSignal<StoreModel[] | null>(null);
  const [modelPath, setModelPath] = createSignal('');
  const [form, setForm] = createStore<ServerForm>({
    name: '', servedName: '', revision: '', gpus: [], port: '', image: '', args: '', apiKey: true, shared: false,
  });
  const [planning, setPlanning] = createSignal(false);
  const [planned, setPlanned] = createSignal<ChangePlan | null>(null);
  const [refusal, setRefusal] = createSignal('');
  const [busy, setBusy] = createSignal(false);
  const [failure, setFailure] = createSignal('');
  const [saved, setSaved] = createSignal<ChangeAnswer | null>(null);

  const files = () => inventory()?.servers ?? [];
  const wanted = () => wantedOn(files(), node, index);
  // Without the files, every server would look like one that has none.
  const orphans = () => (inventory() ? unfiled(host()?.servers ?? [], files(), node, index) : []);
  const others = () => strangers(props.gpu, wanted());
  const policy = () => host()?.policy ?? null;
  const canChange = () => props.canWrite && !!inventory() && !!policy()?.enabled;
  const model = () => models()?.find((entry) => entry.path === modelPath()) ?? null;
  const ask = () => (adding() && model() ? askOf(form, model()!) : null);
  /** The change as it stands, or null while there is nothing to check yet. */
  const draft = (): ChangeAsk | null => {
    const add = ask();
    if (!inventory() || (adding() && !add) || (!removing().length && !add)) return null;
    return { commit: inventory()!.commit, remove: removing(), add: add ? [add] : [] };
  };
  const leaving = () => removing().join(', ');
  const one = () => removing().length === 1;

  function failed(error: unknown, fallback: string): string {
    if (error instanceof ApiError && error.status === 401) {
      props.onSignedOut();
      return '';
    }
    return error instanceof ApiError ? error.message : fallback;
  }

  async function readInventory() {
    try {
      setInventory(await api.inventory());
      setNoInventory('');
    } catch (error) {
      setNoInventory(failed(error, 'Could not read the inventory.'));
    }
  }

  onMount(() => {
    void readInventory();
    void (async () => {
      try {
        setHost(await api.servers(node));
      } catch (error) {
        setNoHost(failed(error, `Could not ask ${node} what it runs.`));
      }
    })();
  });

  // Each change to the draft restarts the wait; an answer to an older draft is dropped.
  let asked = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  onCleanup(() => clearTimeout(timer));
  createEffect(
    on(() => JSON.stringify(draft()), () => {
      clearTimeout(timer);
      const mine = ++asked;
      const change = draft();
      setPlanned(null);
      setRefusal('');
      setPlanning(!!change);
      if (!change) return;
      timer = setTimeout(async () => {
        try {
          const answer = await api.planChange(node, change);
          if (mine !== asked) return;
          setPlanned(answer);
          // main moved while the panel was open: show the files as they are now.
          if (answer.commit !== change.commit) void readInventory();
        } catch (error) {
          if (mine === asked) setRefusal(failed(error, `Could not check this change with ${node}.`));
        } finally {
          if (mine === asked) setPlanning(false);
        }
      }, TYPING_MS);
    }),
  );

  function toggleRemove(name: string) {
    setRemoving(removing().includes(name) ? removing().filter((n) => n !== name) : [...removing(), name].sort());
  }

  function pickModel(path: string) {
    setModelPath(path);
    const picked = model();
    if (picked) {
      const fresh = newForm(picked);
      setForm({ name: fresh.name, servedName: fresh.servedName, revision: '' });
    }
  }

  async function startAdding() {
    const limits = policy();
    if (!limits) return;
    setAdding(true);
    setForm({
      gpus: limits.gpus.includes(index) ? [index] : [],
      // What stays on the card after the removals is a neighbour, so the new server has to say `shared`.
      shared: limits.sharedGpus.includes(index) && staysShared(wanted(), removing()),
      image: usualImage(host()!.servers),
      port: String(
        suggestPort(limits.ports, [...takenPorts(host()!.servers, props.node.status), ...inventoryPorts(files(), node)]) ?? '',
      ),
    });
    if (models()) return;
    try {
      const store = (await api.store()).nodes.find((entry) => entry.id === node);
      if (!store?.store) {
        setFailure(store?.error?.message ?? `Could not read the model store of ${node}.`);
        return;
      }
      const held = store.store.models.filter(deployable).sort((a, b) => a.name.localeCompare(b.name));
      setModels(held);
      if (held.length) pickModel(held[0].path);
    } catch (error) {
      setFailure(failed(error, `Could not read the model store of ${node}.`));
    }
  }

  async function save(event: Event) {
    event.preventDefault();
    const change = draft();
    if (!change || !planned()?.ok || busy()) return;
    setBusy(true);
    setFailure('');
    try {
      setSaved(await api.proposeChange(node, change));
      props.onSaved();
    } catch (error) {
      setFailure(failed(error, 'Could not open the change request.'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog title={`GPU ${index} on ${node}`} onClose={props.onClose}>
      <Show when={saved()}>
        <div class="form">
          <p class="alert alert-ok" role="status">
            Change request{saved()!.iid ? ` !${saved()!.iid}` : ''} holds the change
            {saved()!.removed.length ? `: removes ${saved()!.removed.join(', ')}` : ''}
            {saved()!.added.length ? `${saved()!.removed.length ? ';' : ':'} adds ${saved()!.added.join(', ')}` : ''}.
          </p>
          <p>
            <a href={saved()!.url} target="_blank" rel="noopener noreferrer">{saved()!.url}</a>
          </p>
          <ol class="steps">
            <Show when={saved()!.removed.length}>
              <li>Check that nothing still calls what is removed (its route in Autobahn).</li>
            </Show>
            <li>Merge the request. Nothing changes on {node} yet.</li>
            <li>
              Apply this reviewed change on the GPUs page. For Git-managed inventory, merge the request and run the inventory deployment command.
              {saved()!.removed.length ? ' It removes the servers first; from then on they do not answer.' : ''}
              {saved()!.added.length ? ' It starts the new server and waits until it answers.' : ''}
            </li>
            <Show when={saved()!.removed.length && saved()!.added.length}>
              <li>
                If the new server does not load, what was removed is <b>not put back</b>: create a new reviewed change using the previous specification.
              </li>
            </Show>
          </ol>
          <footer class="dialog-actions">
            <button class="btn btn-primary" type="button" onClick={props.onClose}>Done</button>
          </footer>
        </div>
      </Show>

      <Show when={!saved()}>
        <form class="form" onSubmit={save}>
          <For each={props.waiting}>
            {(change) => (
              <p class="alert alert-warn">
                A change to this GPU waits in{' '}
                <a href={change.url} target="_blank" rel="noopener noreferrer">change request !{change.iid}</a>, opened by{' '}
                {change.actor}
                {change.removes.length ? `: removes ${change.removes.join(', ')}` : ''}
                {change.adds.length ? `${change.removes.length ? ';' : ':'} adds ${change.adds.join(', ')}` : ''}. What
                follows is main without it.
              </p>
            )}
          </For>

          <section class="panel-section">
            <h3>Should run here</h3>
            <Show when={inventory()}>
              <p class="muted">
                The files of inventory/servers on {inventory()!.branch}, at commit {inventory()!.commit.slice(0, 8)}.
              </p>
            </Show>
            <Show when={!inventory() && !noInventory()}>
              <p class="muted">Reading the inventory…</p>
            </Show>
            <Show when={noInventory()}>
              <p class="alert alert-warn">{noInventory()}</p>
            </Show>
            <Show when={inventory()}>
              <ul class="panel-list">
                <For each={wanted()} fallback={<li class="muted">No file claims this GPU.</li>}>
                  {(file) => (
                    <li class="panel-item" classList={{ 'panel-leaving': removing().includes(file.name) }}>
                      <div class="panel-item-head">
                        <b class="mono">{file.name}</b>
                        <Show when={host()}>
                          <span class="tag" classList={{ 'tag-warn': runState(file, host()!.servers) !== 'ready' }}>
                            {STATE[runState(file, host()!.servers)] ?? runState(file, host()!.servers)}
                          </span>
                        </Show>
                        <Show when={file.shared}>
                          <span class="tag">shares the GPU</span>
                        </Show>
                        <Show when={removing().includes(file.name)}>
                          <span class="tag tag-bad">removed by this change</span>
                        </Show>
                        <Show when={canChange()}>
                          <button class="btn panel-item-action" type="button" onClick={() => toggleRemove(file.name)}>
                            {removing().includes(file.name) ? 'Keep' : 'Remove'}
                          </button>
                        </Show>
                      </div>
                      <Show when={file.error} fallback={
                        <span class="muted">
                          {file.model} as <b>{file.servedName}</b> · port {file.port ?? '?'}
                          {file.gpus.length > 1 ? ` · GPU ${file.gpus.join(', ')}` : ''}
                        </span>
                      }>
                        <span class="alert alert-warn">{file.error}</span>
                      </Show>
                      <details>
                        <summary>{file.path}</summary>
                        <pre class="raw">{file.content}</pre>
                      </details>
                    </li>
                  )}
                </For>
              </ul>
            </Show>
          </section>

          <Show when={orphans().length}>
            <section class="panel-section">
              <h3>Runs here without a file</h3>
              <ul class="panel-list">
                <For each={orphans()}>
                  {(server) => (
                    <li class="panel-item">
                      <div class="panel-item-head">
                        <b class="mono">{server.name}</b>
                        <span class="tag tag-warn">{STATE[server.state] ?? server.state}</span>
                      </div>
                      
                    </li>
                  )}
                </For>
              </ul>
            </section>
          </Show>

          <Show when={others().length}>
            <section class="panel-section">
              <h3>Also on this card</h3>
              <ul class="panel-list">
                <For each={others()}>
                  {(process) => (
                    <li class="panel-item">
                      <div class="panel-item-head">
                        <b class="mono">{process.kind === 'model' ? process.servedName : process.command || `pid ${process.pid}`}</b>
                        <span class="tag tag-warn">
                          {process.kind === 'model' ? 'a model server with no file on this GPU' : `not a model server · ${process.user || 'unknown user'}`}
                        </span>
                      </div>
                    </li>
                  )}
                </For>
              </ul>
              <p class="muted">Not in the inventory for this GPU, so not changed from here.</p>
            </section>
          </Show>

          <Show when={noHost()}>
            <p class="alert alert-warn">{noHost()}</p>
          </Show>
          <Show when={props.canWrite && policy() && !policy()!.enabled}>
            <p class="alert alert-warn">
              {node} runs no model servers for fleet: no GPU is given to it there . Nothing
              is changed from here.
            </p>
          </Show>

          <Show when={canChange()}>
            <section class="panel-section">
              <h3>Change</h3>
              <Show when={!adding()}>
                <p>
                  <button class="btn" type="button" onClick={() => void startAdding()}>
                    {removing().length ? 'Put a model server in its place' : 'Add a model server'}
                  </button>
                </p>
              </Show>
              <Show when={adding()}>
                <Show when={!policy()!.gpus.includes(index)}>
                  <p class="alert alert-warn">
                    GPU {index} is not among the GPUs {node} gives fleet ({policy()!.gpus.join(', ')}). Choose from
                    those below.
                  </p>
                </Show>
                <Show when={models()} fallback={<Show when={!failure()}><p class="muted">Reading the model store of {node}…</p></Show>}>
                  <Show when={models()!.length} fallback={
                    <p class="alert alert-warn">
                      {node} holds no whole model to serve. Download one on the <a href="#models">Models page</a> first.
                    </p>
                  }>
                    <label class="field">
                      <span>Model</span>
                      <select onChange={(event) => pickModel(event.currentTarget.value)}>
                        <For each={models()!}>
                          {(entry) => <option value={entry.path} selected={entry.path === modelPath()}>{entry.name}</option>}
                        </For>
                      </select>
                      <span class="muted">From the model store of {node}. A rollout never downloads.</span>
                    </label>
                    <Show when={model()}>
                      <ServerFields
                        form={form}
                        set={setForm}
                        model={model()!}
                        policy={policy()!}
                        status={props.node.status}
                        images={[...new Set(host()!.servers.map((s) => s.image ?? '').filter(Boolean))]}
                        freed={freed(files(), removing())}
                        gpuNote={
                          'A GPU has to be free, shared, or freed by this change. Anything on it that is not in the ' +
                          "inventory is not fleet's to stop."
                        }
                      />
                    </Show>
                  </Show>
                </Show>
                <p>
                  <button class="btn btn-quiet" type="button" onClick={() => setAdding(false)}>Add nothing</button>
                </p>
              </Show>

              <Show when={removing().length}>
                <p class="alert alert-warn">
                  <b>{leaving()}</b> {one() ? 'stops' : 'stop'} answering when the reviewed deployment runs, and {one() ? 'its route' : 'their routes'} in
                  Autobahn {one() ? 'is' : 'are'} stopped
                  {freed(files(), removing()).length > 1 || freed(files(), removing())[0] !== index
                    ? `. That frees GPU ${freed(files(), removing()).join(', ')}`
                    : ''}
                  .
                  <Show when={adding()}>
                    {' '}If the new server does not load, {one() ? 'it is' : 'they are'} <b>not put back</b>: to go back,
                    create a new reviewed change using the previous specification.
                  </Show>
                </p>
              </Show>

              <Show when={planning()}>
                <p class="muted">Checking the change with the inventory and {node}…</p>
              </Show>
              <Show when={refusal()}>
                <p class="alert alert-error" role="alert">{refusal()}</p>
              </Show>
              <For each={planned()?.adds ?? []}>
                {(add) => (
                  <>
                    <For each={add.conflicts}>
                      {(conflict) => <p class="alert alert-error" role="alert">{conflict}</p>}
                    </For>
                    <Show when={add.refusal}>
                      <p class="alert alert-error" role="alert">{add.refusal!.message}</p>
                    </Show>
                    <Show when={add.plan}>
                      <div class="alert alert-ok">
                        <span>
                          <b>{node} accepts {add.name}.</b>{' '}
                          {add.plan!.imagePresent ? 'The image is on the host.' : `It pulls ${add.plan!.image} first.`}
                        </span>
                        <For each={add.plan!.warnings}>{(warning) => <span>Note: {warning}</span>}</For>
                      </div>
                    </Show>
                    <details>
                      <summary>{add.path}</summary>
                      <pre class="raw">{add.content}</pre>
                    </details>
                  </>
                )}
              </For>
              <Show when={planned()?.ok && !inventory()!.canPropose}>
                <p class="alert alert-warn">Change requests are unavailable. Check the fleet configuration.</p>
              </Show>
            </section>
          </Show>

          <Show when={failure()}>
            <p class="alert alert-error" role="alert">{failure()}</p>
          </Show>
          <footer class="dialog-actions">
            <button class="btn" type="button" onClick={props.onClose}>{canChange() ? 'Cancel' : 'Close'}</button>
            <Show when={canChange()}>
              <button class="btn btn-primary" type="submit" disabled={!planned()?.ok || busy() || !inventory()!.canPropose}>
                {busy() ? 'Opening…' : 'Open change request'}
              </button>
            </Show>
          </footer>
        </form>
      </Show>
    </Dialog>
  );
}
