import { For, Show, createEffect, createSignal, on, onCleanup, onMount } from 'solid-js';
import { createStore } from 'solid-js/store';
import { ApiError, api } from '../api';
import { askOf, holders, newForm, suggestPort, takenPorts, usualImage, type ServerForm } from '../domain/deploy';
import type { StoreRow } from '../domain/store';
import type { AgentStatus, PlanAnswer, ProposeAnswer, ServersAnswer } from '../types';
import { Dialog } from './Dialog';
import { ServerFields } from './ServerFields';

const TYPING_MS = 500;

/**
 * Describe a model server for a model the host holds, see what the host's agent would do with it,
 * and open the change request that adds its file to inventory/servers. Nothing is rolled out here:
 * that is the reviewed deployment, after the merge.
 */
export function DeployDialog(props: { row: StoreRow; onClose: () => void; onSignedOut: () => void }) {
  const node = props.row.node;
  const model = props.row.model;

  const [host, setHost] = createSignal<ServersAnswer | null>(null);
  const [status, setStatus] = createSignal<AgentStatus | null>(null);
  const [form, setForm] = createStore<ServerForm>(newForm(model));
  const [planning, setPlanning] = createSignal(false);
  const [planned, setPlanned] = createSignal<PlanAnswer | null>(null);
  const [refusal, setRefusal] = createSignal('');
  const [busy, setBusy] = createSignal(false);
  const [failure, setFailure] = createSignal('');
  const [opened, setOpened] = createSignal<ProposeAnswer | null>(null);

  const policy = () => host()?.policy ?? null;
  /** Servers of ours on the chosen GPUs. Anything else there is not fleet's to remove. */
  const inTheWay = () => holders(host()?.servers ?? [], form.gpus).map((server) => server.name);
  const ask = () => askOf(form, model);

  function failed(error: unknown, fallback: string): string {
    if (error instanceof ApiError && error.status === 401) {
      props.onSignedOut();
      return '';
    }
    return error instanceof ApiError ? error.message : fallback;
  }

  onMount(async () => {
    try {
      const [servers, nodes] = await Promise.all([api.servers(node), api.nodes()]);
      const seen = nodes.nodes.find((n) => n.id === node)?.status ?? null;
      setStatus(seen);
      setForm({
        image: usualImage(servers.servers),
        port: String(suggestPort(servers.policy.ports, takenPorts(servers.servers, seen)) ?? ''),
      });
      setHost(servers);
    } catch (error) {
      setFailure(failed(error, `Could not ask ${node} what it runs.`));
    }
  });

  // Each change restarts the wait; an answer to an older description is dropped.
  let asked = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  onCleanup(() => clearTimeout(timer));
  createEffect(
    on(() => JSON.stringify(ask()), () => {
      clearTimeout(timer);
      const mine = ++asked;
      const described = ask();
      setPlanned(null);
      setRefusal('');
      setPlanning(!!described);
      if (!described) return;
      timer = setTimeout(async () => {
        try {
          const answer = await api.planServer(node, described);
          if (mine === asked) setPlanned(answer);
        } catch (error) {
          if (mine === asked) setRefusal(failed(error, `Could not ask ${node} about this server.`));
        } finally {
          if (mine === asked) setPlanning(false);
        }
      }, TYPING_MS);
    }),
  );

  async function submit(event: Event) {
    event.preventDefault();
    const described = ask();
    if (!described || !planned() || busy()) return;
    setBusy(true);
    setFailure('');
    try {
      setOpened(await api.proposeServer(node, described));
    } catch (error) {
      setFailure(failed(error, 'Could not open the change request.'));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog title={`Deploy on ${node}`} onClose={props.onClose}>
      <Show when={opened()}>
        <div class="form">
          <p class="alert alert-ok" role="status">
            Change request{opened()!.iid ? ` !${opened()!.iid}` : ''} adds <b>{opened()!.path}</b>.
          </p>
          <p>
            <a href={opened()!.url} target="_blank" rel="noopener noreferrer">{opened()!.url}</a>
          </p>
          <p class="muted">
            Pending review and apply.
          </p>
          <footer class="dialog-actions">
            <button class="btn btn-primary" type="button" onClick={props.onClose}>Done</button>
          </footer>
        </div>
      </Show>
      <Show when={!opened()}>
        <form class="form" onSubmit={submit}>

          <Show when={!host() && !failure()}>
            <p class="muted">Asking {node} what it runs…</p>
          </Show>
          <Show when={policy() && !policy()!.enabled}>
            <p class="alert alert-warn">
              {node} runs no model servers for fleet: no GPU is given to it there .
            </p>
          </Show>
          <Show when={policy()?.enabled}>
            <ServerFields
              form={form}
              set={setForm}
              model={model}
              policy={policy()!}
              status={status()}
              images={[...new Set(host()!.servers.map((s) => s.image ?? '').filter(Boolean))]}
              gpuNote={
                'A GPU has to be free, or shared. One that a fleet server holds is changed on the GPUs page, where ' +
                'removing that server and adding this one is a single change request; anything else on it is not ' +
                "fleet's to stop."
              }
            />

            <Show when={planning()}>
              <p class="muted">Asking {node} what it would do…</p>
            </Show>
            <Show when={refusal()}>
              <p class="alert alert-error" role="alert">{refusal()}</p>
            </Show>
            <Show when={refusal() && inTheWay().length}>
              <p class="alert alert-warn">
                <b>{inTheWay().join(', ')}</b> {inTheWay().length === 1 ? 'is a fleet server' : 'are fleet servers'} on
                GPU {form.gpus.join(', ')}. To put this model in {inTheWay().length === 1 ? 'its' : 'their'} place, open
                the <a href="#gpus">GPUs page</a> and click GPU {form.gpus[0]} of {node}: the server to remove and the
                one to add go into one change request there.
              </p>
            </Show>
            <Show when={planned()}>
              <div class="alert alert-ok">
                <span>
                  <b>{node} accepts it.</b>{' '}
                  {planned()!.plan.imagePresent ? 'The image is on the host.' : `It pulls ${planned()!.plan.image} first.`}
                </span>
                <For each={planned()!.plan.warnings}>{(warning) => <span>Note: {warning}</span>}</For>
              </div>
              <details open={!host()!.canPropose}>
                <summary>{planned()!.path}</summary>
                <pre class="raw">{planned()!.content}</pre>
              </details>
              <Show when={!host()!.canPropose}>
                <p class="alert alert-warn">
                  Change requests are unavailable. Check the fleet configuration. Add this file to the repository
                  by hand.
                </p>
              </Show>
            </Show>
          </Show>

          <Show when={failure()}>
            <p class="alert alert-error" role="alert">{failure()}</p>
          </Show>
          <footer class="dialog-actions">
            <button class="btn" type="button" onClick={props.onClose}>Cancel</button>
            <button class="btn btn-primary" type="submit" disabled={!planned() || busy() || !host()?.canPropose}>
              {busy() ? 'Opening…' : 'Open change request'}
            </button>
          </footer>
        </form>
      </Show>
    </Dialog>
  );
}
