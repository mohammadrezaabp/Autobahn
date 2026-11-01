import { For, Show, createEffect, createSignal, on, onCleanup, onMount } from 'solid-js';
import { ApiError, api } from '../api';
import { bytes } from '../domain/format';
import { REPO, copySource, fit } from '../domain/store';
import type { HfModel, HfModelInfo, StoreNode } from '../types';
import { Dialog } from './Dialog';

const TYPING_MS = 350;

/**
 * Search Hugging Face or paste owner/name, pick a host, see what it does to the disk, start.
 * A model another host already holds is copied from there when the chosen host is set up for it.
 */
export function DownloadDialog(props: {
  repo?: string;
  node?: string;
  onClose: () => void;
  onStarted: (node: string, repo: string) => void;
  onSignedOut: () => void;
}) {
  const [text, setText] = createSignal(props.repo ?? '');
  const [revision, setRevision] = createSignal('');
  const [target, setTarget] = createSignal(props.node ?? '');
  const [fromHuggingFace, setFromHuggingFace] = createSignal(false);
  const [hosts, setHosts] = createSignal<StoreNode[]>([]);
  const [results, setResults] = createSignal<HfModel[]>([]);
  const [info, setInfo] = createSignal<HfModelInfo | null>(null);
  const [looking, setLooking] = createSignal(false);
  const [lookupProblem, setLookupProblem] = createSignal('');
  const [busy, setBusy] = createSignal(false);
  const [failure, setFailure] = createSignal('');

  const repo = () => text().trim();
  const named = () => REPO.test(repo());
  const host = () => hosts().find((h) => h.id === target())?.store ?? null;
  // A download goes into the hub cache, so only an entry there is continued.
  const existing = () =>
    host()?.models.find((m) => m.kind === 'hub' && m.repoType === 'model' && m.repoId === repo()) ?? null;
  // The other host already holds it: copy it from there unless Hugging Face is asked for.
  const peer = () => (named() ? copySource(hosts(), target(), repo()) : null);
  const copying = () => !!peer() && !fromHuggingFace();
  const sum = () => {
    const total = copying() ? peer()!.model.sizeBytes : info()?.totalBytes;
    const free = host()?.disk.freeBytes;
    if (total === null || total === undefined || free === null || free === undefined) return null;
    return fit(total, existing()?.sizeBytes ?? 0, free, host()!.minFreeBytes);
  };
  const blocked = () => !named() || !host() || busy() || sum()?.fits === false;

  onMount(async () => {
    try {
      const answer = await api.store();
      setHosts(answer.nodes);
      if (!target()) setTarget(answer.nodes.find((n) => n.store)?.id ?? '');
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) return props.onSignedOut();
      setFailure(error instanceof ApiError ? error.message : 'Could not load the hosts.');
    }
  });

  // Each keystroke restarts the wait; an answer to an older question is dropped.
  let asked = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  onCleanup(() => clearTimeout(timer));
  createEffect(
    on([repo, () => revision().trim()], ([name, rev]) => {
      clearTimeout(timer);
      const mine = ++asked;
      setInfo(null);
      setLookupProblem('');
      setLooking(name.length >= 2);
      if (name.length < 2) return setResults([]);
      timer = setTimeout(async () => {
        const [found, size] = await Promise.allSettled([
          api.hfSearch(name),
          REPO.test(name) ? api.hfModel(name, rev) : Promise.resolve(null),
        ]);
        if (mine !== asked) return;
        setLooking(false);
        setResults(found.status === 'fulfilled' ? found.value.models.filter((m) => m.id !== name) : []);
        if (size.status === 'fulfilled') {
          setInfo(size.value);
        } else {
          const reason = size.reason;
          if (reason instanceof ApiError && reason.status === 401) return props.onSignedOut();
          setLookupProblem(reason instanceof ApiError ? reason.message : 'Could not ask Hugging Face for the size.');
        }
        if (found.status === 'rejected' && !REPO.test(name)) {
          const reason = found.reason;
          setLookupProblem(reason instanceof ApiError ? reason.message : 'Could not search Hugging Face.');
        }
      }, TYPING_MS);
    }),
  );

  async function submit(event: Event) {
    event.preventDefault();
    if (blocked()) return;
    setBusy(true);
    setFailure('');
    try {
      if (copying()) await api.startDownload(target(), repo(), '', peer()!.node);
      else await api.startDownload(target(), repo(), revision().trim());
      props.onStarted(target(), repo());
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) return props.onSignedOut();
      setFailure(error instanceof ApiError ? error.message : 'Could not start the download.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog title="Download a model" onClose={props.onClose}>
      <form class="form" onSubmit={submit}>
        <label class="field">
          <span>Model</span>
          <input
            type="text"
            autocomplete="off"
            spellcheck={false}
            placeholder="owner/name, or words to search Hugging Face"
            value={text()}
            onInput={(event) => setText(event.currentTarget.value)}
            autofocus
          />
        </label>
        <Show when={results().length}>
          <ul class="results" aria-label="Models on Hugging Face">
            <For each={results()}>
              {(found) => (
                <li>
                  <button type="button" class="result" onClick={() => setText(found.id)}>
                    <span class="result-name">{found.id}</span>
                    <Show when={found.gated}>
                      <span class="tag tag-warn">gated</span>
                    </Show>
                    <span class="muted">
                      {found.pipelineTag}
                      {found.downloads !== null ? ` · ${found.downloads.toLocaleString()} downloads` : ''}
                    </span>
                  </button>
                </li>
              )}
            </For>
          </ul>
        </Show>

        <label class="field">
          <span>Revision</span>
          <input
            type="text"
            autocomplete="off"
            spellcheck={false}
            placeholder={copying() ? `as on ${peer()!.node}` : 'main (a branch, tag or commit hash)'}
            disabled={copying()}
            value={copying() ? '' : revision()}
            onInput={(event) => setRevision(event.currentTarget.value)}
          />
        </label>

        <fieldset class="choices">
          <legend>Host</legend>
          <For each={hosts()} fallback={<span class="muted">Loading the hosts…</span>}>
            {(node) => (
              <label class="choice" classList={{ 'choice-off': !node.store }}>
                <input
                  type="radio"
                  name="host"
                  value={node.id}
                  checked={target() === node.id}
                  disabled={!node.store}
                  onChange={() => setTarget(node.id)}
                />
                <b>{node.id}</b>
                <span class="muted">
                  {node.store ? `${bytes(node.store.disk.freeBytes)} free` : node.error?.message}
                </span>
              </label>
            )}
          </For>
        </fieldset>

        <Show when={peer()}>
          <fieldset class="choices">
            <legend>From</legend>
            <label class="choice">
              <input type="radio" name="from" checked={copying()} onChange={() => setFromHuggingFace(false)} />
              <b>{peer()!.node}</b>
              <span class="muted">already holds it; copied over the local network, nothing is downloaded</span>
            </label>
            <label class="choice">
              <input type="radio" name="from" checked={!copying()} onChange={() => setFromHuggingFace(true)} />
              <b>Hugging Face</b>
              <span class="muted">downloaded again</span>
            </label>
          </fieldset>
        </Show>

        <Show when={named() && copying()}>
          <div class="alert" classList={{ 'alert-error': sum()?.fits === false }}>
            <span>
              <b>{bytes(peer()!.model.sizeBytes)}</b> in {peer()!.model.fileCount} files on {peer()!.node}, copied as
              it is there
              {peer()!.model.revisions.length > 1 ? `, all ${peer()!.model.revisions.length} revisions` : ''}.
            </span>
            <Show when={existing()}>
              <span>
                {bytes(existing()!.sizeBytes)} of it is already on {target()}; only what is missing is copied.
              </span>
            </Show>
            <Show when={sum()}>
              <span>
                On {target()}: {bytes(host()!.disk.freeBytes)} free now, <b>{bytes(Math.max(0, sum()!.freeAfter))}</b>{' '}
                after. A copy must leave {bytes(host()!.minFreeBytes)}.
              </span>
              <Show when={!sum()!.fits}>
                <strong>
                  It does not fit: {bytes(sum()!.needed)} is needed. Delete models that are not in use first.
                </strong>
              </Show>
            </Show>
          </div>
        </Show>
        <Show when={named() && !copying()}>
          <div class="alert" classList={{ 'alert-error': sum()?.fits === false }}>
            <Show when={info()} fallback={<span>{looking() ? 'Asking Hugging Face for the size…' : lookupProblem()}</span>}>
              <span>
                <b>{info()!.totalBytes === null ? 'Size not listed' : bytes(info()!.totalBytes)}</b> in{' '}
                {info()!.files} files
                <Show when={info()!.sha}>
                  <span class="muted"> · commit {info()!.sha.slice(0, 8)}</span>
                </Show>
              </span>
              <Show when={info()!.gated}>
                <span>
                  Gated model. The host token must have access.
                </span>
              </Show>
            </Show>
            <Show when={existing()}>
              <span>
                {bytes(existing()!.sizeBytes)} of it is already on {target()}; only what is missing is fetched.
              </span>
            </Show>
            <Show when={sum()}>
              <span>
                On {target()}: {bytes(host()!.disk.freeBytes)} free now, <b>{bytes(Math.max(0, sum()!.freeAfter))}</b>{' '}
                after. A download must leave {bytes(host()!.minFreeBytes)}.
              </span>
              <Show when={!sum()!.fits}>
                <strong>
                  It does not fit: {bytes(sum()!.needed)} is needed. Delete models that are not in use first.
                </strong>
              </Show>
            </Show>
          </div>
        </Show>
        <Show when={!named() && lookupProblem()}>
          <p class="alert alert-warn">{lookupProblem()}</p>
        </Show>

        <Show when={failure()}>
          <p class="alert alert-error" role="alert">{failure()}</p>
        </Show>
        <footer class="dialog-actions">
          <button class="btn" type="button" onClick={props.onClose}>Cancel</button>
          <button class="btn btn-primary" type="submit" disabled={blocked()}>
            {busy() ? 'Starting…' : `${copying() ? 'Copy' : 'Download'}${target() ? ` to ${target()}` : ''}`}
          </button>
        </footer>
      </form>
    </Dialog>
  );
}
