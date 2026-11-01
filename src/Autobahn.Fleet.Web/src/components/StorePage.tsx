import { For, Show, createMemo, createSignal } from 'solid-js';
import { ApiError, api } from '../api';
import { deployable } from '../domain/deploy';
import { ago, bytes } from '../domain/format';
import {
  copySource, deleteBlock, diskBreakdown, filterRows, resumable, storeRows, totalBytes, type StoreFilter, type StoreRow,
} from '../domain/store';
import { createPolling } from '../poll';
import type { Me, StoreNode } from '../types';
import { DeployDialog } from './DeployDialog';
import { Dialog } from './Dialog';
import { DownloadDialog } from './DownloadDialog';

const REFRESH_MS = 10_000;

const LAYOUT: Record<string, string> = {
  hub: 'hub cache',
  flat: 'plain directory',
  stray: 'outside the hub',
};

function HostDisk(props: { node: StoreNode; now: number; canWrite: boolean; onRescan: () => void }) {
  const store = () => props.node.store;
  const parts = () => diskBreakdown(props.node);
  const share = (value: number) => `${(value / parts()!.total) * 100}%`;

  return (
    <section class="host store-host">
      <header class="host-head">
        <h2>{props.node.id}</h2>
        <Show when={store()}>
          <span class="muted host-meta">
            {store()!.scanning
              ? 'measuring now…'
              : store()!.scannedAt === null
                ? 'not measured yet'
                : `measured ${ago(store()!.scannedAt, props.now)}`}
          </span>
          <Show when={props.canWrite}>
            <button class="btn" type="button" disabled={store()!.scanning} onClick={props.onRescan}>
              Measure again
            </button>
          </Show>
        </Show>
      </header>
      <Show when={!store()}>
        <p class="alert alert-error" role="alert">{props.node.error?.message}</p>
      </Show>
      <Show when={parts()}>
        <div class="bar bar-stack" title={`Model store filesystem at ${store()!.disk.path}`}>
          <div class="seg seg-used" style={{ width: share(parts()!.inUse) }} />
          <div class="seg seg-unused" style={{ width: share(parts()!.unused) }} />
          <div class="seg seg-other" style={{ width: share(parts()!.other) }} />
        </div>
        <dl class="legend">
          <div><dt><i class="key seg-used" />Served</dt><dd>{bytes(parts()!.inUse)}</dd></div>
          <div title="What deleting could free">
            <dt><i class="key seg-unused" />Not in use</dt><dd>{bytes(parts()!.unused)}</dd>
          </div>
          <div title="Everything on the filesystem that is not a listed model">
            <dt><i class="key seg-other" />Other data</dt><dd>{bytes(parts()!.other)}</dd>
          </div>
          <div title={`A download must leave ${bytes(store()!.minFreeBytes)}`}>
            <dt><i class="key" />Free</dt><dd>{bytes(parts()!.free)} of {bytes(parts()!.total)}</dd>
          </div>
        </dl>
      </Show>
      <Show when={store() && !store()!.allowDelete}>
        <p class="muted store-note">Deleting is switched off on this host.</p>
      </Show>
    </section>
  );
}

function DeleteDialog(props: { row: StoreRow; onClose: () => void; onDeleted: (freed: number) => void; onSignedOut: () => void }) {
  const [typed, setTyped] = createSignal('');
  const [busy, setBusy] = createSignal(false);
  const [failure, setFailure] = createSignal('');
  const model = () => props.row.model;

  async function submit(event: Event) {
    event.preventDefault();
    if (typed() !== model().name || busy()) return;
    setBusy(true);
    setFailure('');
    try {
      const answer = await api.deleteModel(props.row.node, model().path, typed());
      props.onDeleted(answer.freedBytes);
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) return props.onSignedOut();
      setFailure(error instanceof ApiError ? error.message : 'Could not delete the model.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog title={`Delete from ${props.row.node}`} onClose={props.onClose}>
      <form class="form" onSubmit={submit}>
        <p>
          This removes <b>{model().name}</b> ({bytes(model().sizeBytes)}, {model().fileCount} files) from{' '}
          {props.row.node}. It cannot be undone.
        </p>
        <pre class="raw">{model().path}</pre>
        <Show when={props.row.alsoOn.length} fallback={<p class="alert alert-warn">No other host holds this model.</p>}>
          <p class="muted">Also on {props.row.alsoOn.join(', ')}; that copy stays.</p>
        </Show>
        <label class="field">
          <span>Type the model's name to confirm</span>
          <input
            type="text"
            autocomplete="off"
            spellcheck={false}
            placeholder={model().name}
            value={typed()}
            onInput={(event) => setTyped(event.currentTarget.value)}
            autofocus
          />
        </label>
        <Show when={failure()}>
          <p class="alert alert-error" role="alert">{failure()}</p>
        </Show>
        <footer class="dialog-actions">
          <button class="btn" type="button" onClick={props.onClose}>Cancel</button>
          <button class="btn btn-danger" type="submit" disabled={typed() !== model().name || busy()}>
            {busy() ? 'Deleting…' : `Delete ${bytes(model().sizeBytes)}`}
          </button>
        </footer>
      </form>
    </Dialog>
  );
}

export function StorePage(props: { me: Me; onSignedOut: () => void; onDownloadStarted: () => void }) {
  const [nodes, setNodes] = createSignal<StoreNode[]>([]);
  const [now, setNow] = createSignal(Date.now() / 1000);
  const [loaded, setLoaded] = createSignal(false);
  const [failure, setFailure] = createSignal('');
  const [notice, setNotice] = createSignal('');
  const [filter, setFilter] = createSignal<StoreFilter>({ node: '', kind: '', unusedOnly: false, text: '' });
  const [deleting, setDeleting] = createSignal<StoreRow | null>(null);
  const [downloading, setDownloading] = createSignal<{ repo?: string; node?: string } | null>(null);
  const [deploying, setDeploying] = createSignal<StoreRow | null>(null);

  const rows = createMemo(() => storeRows(nodes()));
  const shown = createMemo(() => filterRows(rows(), filter()));
  const storeOf = (id: string) => nodes().find((n) => n.id === id)!.store!;
  const patch = (change: Partial<StoreFilter>) => setFilter({ ...filter(), ...change });

  function failed(error: unknown, fallback: string) {
    if (error instanceof ApiError && error.status === 401) return props.onSignedOut();
    setFailure(error instanceof ApiError ? error.message : fallback);
  }

  async function refresh() {
    try {
      const answer = await api.store();
      setNodes(answer.nodes);
      setNow(answer.time);
      setFailure('');
      setLoaded(true);
    } catch (error) {
      failed(error, 'Could not load the model store.');
    }
  }

  async function rescan(node: string) {
    try {
      await api.rescan(node);
      await refresh();
    } catch (error) {
      failed(error, 'Could not start measuring.');
    }
  }

  createPolling(refresh, REFRESH_MS);

  return (
    <>
      <Show when={failure()}>
        <p class="alert alert-error" role="alert">{failure()}</p>
      </Show>
      <Show when={notice()}>
        <p class="alert alert-ok" role="status">{notice()}</p>
      </Show>
      <Show when={loaded()} fallback={<p class="muted">Loading the model store…</p>}>
        <div class="hosts">
          <For each={nodes()} fallback={<p class="alert">No hosts configured.</p>}>
            {(node) => (
              <HostDisk node={node} now={now()} canWrite={props.me.canWrite} onRescan={() => void rescan(node.id)} />
            )}
          </For>
        </div>

        <div class="toolbar">
          <input
            class="input"
            type="search"
            placeholder="Filter by model or served name"
            aria-label="Filter by model or served name"
            value={filter().text}
            onInput={(event) => patch({ text: event.currentTarget.value })}
          />
          <select class="input" aria-label="Host" onChange={(event) => patch({ node: event.currentTarget.value })}>
            <option value="">Every host</option>
            <For each={nodes()}>{(node) => <option value={node.id}>{node.id}</option>}</For>
          </select>
          <select class="input" aria-label="Layout" onChange={(event) => patch({ kind: event.currentTarget.value })}>
            <option value="">Every layout</option>
            <For each={Object.entries(LAYOUT)}>{([kind, label]) => <option value={kind}>{label}</option>}</For>
          </select>
          <label class="check">
            <input
              type="checkbox"
              checked={filter().unusedOnly}
              onChange={(event) => patch({ unusedOnly: event.currentTarget.checked })}
            />
            Not in use only
          </label>
          <Show when={props.me.canWrite}>
            <button class="btn btn-primary toolbar-end" type="button" onClick={() => setDownloading({})}>
              Download a model
            </button>
          </Show>
        </div>

        <div class="table-wrap">
          <table class="table">
            <thead>
              <tr>
                <th>Model</th>
                <th>Host</th>
                <th>Layout</th>
                <th class="num">Size</th>
                <th class="num">Revisions</th>
                <th>Changed</th>
                <th>Serving</th>
                <th />
              </tr>
            </thead>
            <tbody>
              <For
                each={shown()}
                fallback={
                  <tr>
                    <td colSpan={8} class="muted">
                      {rows().length ? 'No model matches the filter.' : 'No models listed.'}
                    </td>
                  </tr>
                }
              >
                {(row) => {
                  const block = () => deleteBlock(row.model, storeOf(row.node), props.me.canWrite);
                  return (
                    <tr>
                      <td>
                        <span class="model-name" title={row.model.path}>{row.model.name}</span>
                        <Show when={row.model.repoType !== 'model'}>
                          <span class="tag">{row.model.repoType}</span>
                        </Show>
                        <Show when={row.model.downloading}>
                          <span class="tag">downloading</span>
                        </Show>
                        <Show when={row.model.incomplete && !row.model.downloading}>
                          <span class="tag tag-warn" title="A download of this model stopped part way">incomplete</span>
                        </Show>
                      </td>
                      <td>
                        {row.node}
                        <Show when={row.alsoOn.length}>
                          <span class="muted"> · also on {row.alsoOn.join(', ')}</span>
                        </Show>
                      </td>
                      <td class="muted">{LAYOUT[row.model.kind] ?? row.model.kind}</td>
                      <td class="num">{bytes(row.model.sizeBytes)}</td>
                      <td class="num" title={row.model.revisions.join('\n')}>
                        {row.model.kind === 'flat' ? '' : row.model.revisions.length}
                      </td>
                      <td class="muted">{row.model.modifiedAt ? ago(row.model.modifiedAt, now()) : ''}</td>
                      <td>
                        <Show when={row.model.inUseBy.length} fallback={<span class="muted">not in use</span>}>
                          <For each={row.model.inUseBy}>{(name) => <span class="tag tag-live">{name}</span>}</For>
                        </Show>
                      </td>
                      <td class="actions">
                        <Show when={props.me.canWrite}>
                          {/* Half a model is finished where it is before it is offered to another host. */}
                          <Show when={resumable(row.model)}>
                            <button
                              class="btn"
                              type="button"
                              title={
                                copySource(nodes(), row.node, row.model.repoId)
                                  ? `Fetch what is missing of ${row.model.repoId} on ${row.node}: from ` +
                                    `${copySource(nodes(), row.node, row.model.repoId)!.node} over the local network, or from Hugging Face`
                                  : `Download what is missing of ${row.model.repoId} from Hugging Face to ${row.node}`
                              }
                              onClick={() => setDownloading({ repo: row.model.repoId, node: row.node })}
                            >
                              Resume on {row.node}
                            </button>
                          </Show>
                          <For each={resumable(row.model) ? [] : row.missingOn}>
                            {(other) => (
                              <button
                                class="btn"
                                type="button"
                                title={
                                  copySource(nodes(), other, row.model.repoId)
                                    ? `Copy ${row.model.repoId} to ${other} over the local network, without downloading it again`
                                    : `Download ${row.model.repoId} from Hugging Face to ${other}`
                                }
                                onClick={() => setDownloading({ repo: row.model.repoId, node: other })}
                              >
                                {copySource(nodes(), other, row.model.repoId) ? 'Copy to' : 'Get on'} {other}
                              </button>
                            )}
                          </For>
                          <Show when={deployable(row.model)}>
                            <button
                              class="btn"
                              type="button"
                              title={`Describe a model server for ${row.model.name} on ${row.node} and open its change request`}
                              onClick={() => setDeploying(row)}
                            >
                              Deploy
                            </button>
                          </Show>
                          <button
                            class="btn btn-danger"
                            type="button"
                            disabled={!!block()}
                            title={block()}
                            onClick={() => setDeleting(row)}
                          >
                            Delete
                          </button>
                        </Show>
                      </td>
                    </tr>
                  );
                }}
              </For>
            </tbody>
            <tfoot>
              <tr>
                <td colSpan={3}>
                  {shown().length} of {rows().length} {rows().length === 1 ? 'entry' : 'entries'}
                </td>
                <td class="num"><b>{bytes(totalBytes(shown()))}</b></td>
                <td colSpan={4} />
              </tr>
            </tfoot>
          </table>
        </div>
      </Show>

      <Show when={deleting()}>
        <DeleteDialog
          row={deleting()!}
          onClose={() => setDeleting(null)}
          onSignedOut={props.onSignedOut}
          onDeleted={(freed) => {
            setNotice(`Deleted ${deleting()!.model.name} from ${deleting()!.node}: ${bytes(freed)} freed.`);
            setDeleting(null);
            void refresh();
          }}
        />
      </Show>
      <Show when={deploying()}>
        <DeployDialog row={deploying()!} onClose={() => setDeploying(null)} onSignedOut={props.onSignedOut} />
      </Show>
      <Show when={downloading()}>
        <DownloadDialog
          repo={downloading()!.repo}
          node={downloading()!.node}
          onClose={() => setDownloading(null)}
          onSignedOut={props.onSignedOut}
          onStarted={props.onDownloadStarted}
        />
      </Show>
    </>
  );
}
