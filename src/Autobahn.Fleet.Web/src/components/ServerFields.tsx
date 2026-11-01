import { For, Show } from 'solid-js';
import type { SetStoreFunction } from 'solid-js/store';
import { gpuUse, type ServerForm } from '../domain/deploy';
import type { AgentStatus, ServerPolicy, StoreModel } from '../types';

/**
 * The fields of a model server's description: name, served name, GPUs, port, image, arguments.
 * The store page's Deploy dialog and the GPUs page's panel both fill these in.
 */
export function ServerFields(props: {
  form: ServerForm;
  set: SetStoreFunction<ServerForm>;
  model: StoreModel;
  policy: ServerPolicy;
  status: AgentStatus | null;
  /** Images the host's servers run already, offered as choices. */
  images: string[];
  /** GPUs that the change being drafted frees: in use now, free when it is rolled out. */
  freed?: number[];
  /** What to say under the GPUs about one that is taken. */
  gpuNote: string;
}) {
  const toggle = (index: number, on: boolean) =>
    props.set('gpus', on ? [...props.form.gpus, index].sort((a, b) => a - b) : props.form.gpus.filter((g) => g !== index));
  const use = (index: number) => {
    const names = gpuUse(props.status, index);
    if (!names) return 'free';
    return props.freed?.includes(index) ? `in use: ${names} · freed by this change` : `in use: ${names}`;
  };

  return (
    <>
      <label class="field">
        <span>Name</span>
        <input
          type="text"
          autocomplete="off"
          spellcheck={false}
          maxLength={40}
          value={props.form.name}
          onInput={(event) => props.set('name', event.currentTarget.value)}
        />
        <span class="muted">The file and the container: fleet-{props.form.name.trim() || '…'}</span>
      </label>
      <label class="field">
        <span>Served name</span>
        <input
          type="text"
          autocomplete="off"
          spellcheck={false}
          value={props.form.servedName}
          onInput={(event) => props.set('servedName', event.currentTarget.value)}
        />
        <span class="muted">What clients put in "model"</span>
      </label>
      <Show when={props.model.kind === 'hub' && props.model.revisions.length > 1}>
        <label class="field">
          <span>Revision</span>
          <select onChange={(event) => props.set('revision', event.currentTarget.value)}>
            <option value="">The newest on the host</option>
            <For each={props.model.revisions}>
              {(rev) => <option value={rev} selected={props.form.revision === rev}>{rev}</option>}
            </For>
          </select>
        </label>
      </Show>

      <fieldset class="choices">
        <legend>GPUs</legend>
        <For each={props.policy.gpus}>
          {(index) => (
            <label class="choice">
              <input
                type="checkbox"
                checked={props.form.gpus.includes(index)}
                onChange={(event) => toggle(index, event.currentTarget.checked)}
              />
              <b>GPU {index}</b>
              <span class="muted">
                {use(index)}
                {props.policy.sharedGpus.includes(index) ? ' · can be shared' : ''}
              </span>
            </label>
          )}
        </For>
        <Show when={props.policy.sharedGpus.length}>
          <label class="choice">
            <input
              type="checkbox"
              checked={props.form.shared}
              onChange={(event) => props.set('shared', event.currentTarget.checked)}
            />
            <b>Share the GPU</b>
            
          </label>
        </Show>
        <span class="muted">{props.gpuNote}</span>
      </fieldset>

      <label class="field">
        <span>Port</span>
        <input
          type="number"
          min={props.policy.ports[0]}
          max={props.policy.ports[1]}
          value={props.form.port}
          onInput={(event) => props.set('port', event.currentTarget.value)}
        />
        <span class="muted">{props.policy.ports[0]} to {props.policy.ports[1]} on this host</span>
      </label>
      <label class="field">
        <span>Image</span>
        <input
          type="text"
          autocomplete="off"
          spellcheck={false}
          list="server-images"
          placeholder={`${props.policy.images[0] ?? 'registry/vllm/vllm-openai'}:<tag>`}
          value={props.form.image}
          onInput={(event) => props.set('image', event.currentTarget.value)}
        />
        <datalist id="server-images">
          <For each={props.images}>{(used) => <option value={used} />}</For>
        </datalist>
        <span class="muted">A fixed tag, from {props.policy.images.join(', ') || 'a repository the host allows'}</span>
      </label>
      <label class="field">
        <span>Arguments to vllm serve</span>
        <textarea
          rows={4}
          spellcheck={false}
          placeholder={'--max-model-len 65536\n--gpu-memory-utilization 0.92'}
          value={props.form.args}
          onInput={(event) => props.set('args', event.currentTarget.value)}
        />
        <span class="muted">One flag to a line, its value after a space</span>
      </label>
      <label class="check">
        <input
          type="checkbox"
          checked={props.form.apiKey}
          onChange={(event) => props.set('apiKey', event.currentTarget.checked)}
        />
        The host makes an API key for the server and keeps it
      </label>
    </>
  );
}
