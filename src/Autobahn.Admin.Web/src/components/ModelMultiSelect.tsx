import { For, Show, createMemo, createSignal, createUniqueId, onCleanup, onMount } from 'solid-js';
import type { GrantChoice } from '../domain/keyGrants';
import { formatNum } from '../domain/format';
import { IconChevronDown, IconX } from './icons';
import '../styles/model-multi-select.css';

/**
 * A menu to tick several models in. The ticked ones stay listed under it while it is closed.
 */
export function ModelMultiSelect(props: {
  choices: GrantChoice[];
  selected: string[];
  onChange: (selected: string[]) => void;
  label?: string;
}) {
  const [open, setOpen] = createSignal(false);
  const [filter, setFilter] = createSignal('');
  const menuId = createUniqueId();
  let root: HTMLDivElement | undefined;
  let trigger: HTMLButtonElement | undefined;

  const isSelected = (id: string) => props.selected.includes(id);
  const visible = createMemo(() => {
    const q = filter().trim().toLowerCase();
    return q ? props.choices.filter((c) => c.id.toLowerCase().includes(q)) : props.choices;
  });
  const noteOf = (id: string) => props.choices.find((c) => c.id === id)?.note ?? '';

  const toggle = (id: string) =>
    props.onChange(isSelected(id) ? props.selected.filter((s) => s !== id) : [...props.selected, id]);
  // What the filter shows, so a search can be ticked in one go.
  const selectVisible = () => {
    const add = visible().filter((c) => !c.disabled && !isSelected(c.id)).map((c) => c.id);
    if (add.length) props.onChange([...props.selected, ...add]);
  };

  const close = () => {
    setOpen(false);
    setFilter('');
  };

  // Native listeners: the drawer this sits in closes on Escape from a listener on the document,
  // and a delegated handler would run after it.
  const onKeyDown = (e: KeyboardEvent) => {
    if (e.key !== 'Escape' || !open()) return;
    e.preventDefault();
    e.stopPropagation();
    close();
    trigger?.focus();
  };
  const onDocumentPointerDown = (e: MouseEvent) => {
    if (open() && root && !root.contains(e.target as Node)) close();
  };
  onMount(() => {
    document.addEventListener('mousedown', onDocumentPointerDown);
    onCleanup(() => document.removeEventListener('mousedown', onDocumentPointerDown));
  });

  return (
    <div class="msel" ref={root} on:keydown={onKeyDown}>
      <button
        type="button"
        class="msel-trigger"
        ref={trigger}
        aria-haspopup="true"
        aria-expanded={open()}
        aria-controls={menuId}
        onClick={() => (open() ? close() : setOpen(true))}
      >
        <span>
          {props.selected.length === 0
            ? (props.label ?? 'Select models')
            : `${formatNum(props.selected.length)} of ${formatNum(props.choices.length)} models selected`}
        </span>
        <span class="icon"><IconChevronDown /></span>
      </button>

      <Show when={open()}>
        <div class="msel-menu" id={menuId} role="group" aria-label="Models">
          <input
            type="search"
            class="msel-filter"
            placeholder="Filter models"
            aria-label="Filter models"
            value={filter()}
            onInput={(e) => setFilter(e.currentTarget.value)}
            ref={(el) => requestAnimationFrame(() => el.focus())}
          />
          <div class="msel-actions">
            <span class="hint flush">{formatNum(visible().length)} shown</span>
            <span class="spacer" />
            <button type="button" class="rl-link" onClick={selectVisible}>Select all</button>
            <button type="button" class="rl-link" onClick={() => props.onChange([])}>Clear</button>
          </div>
          <div class="msel-options">
            <For each={visible()} fallback={<p class="hint flush">No model matches.</p>}>
              {(choice) => (
                <label class="checkbox-row msel-option" classList={{ disabled: choice.disabled }}>
                  <input
                    type="checkbox"
                    checked={isSelected(choice.id)}
                    disabled={choice.disabled}
                    onChange={() => toggle(choice.id)}
                  />
                  <span class="msel-id" title={choice.id}>{choice.id}</span>
                  <Show when={choice.paid}><span class="tag level-warning">Paid</span></Show>
                  <Show when={choice.note}>
                    <span class="tag" classList={{ warn: choice.note !== 'not allowed for the tenant' }}>{choice.note}</span>
                  </Show>
                </label>
              )}
            </For>
          </div>
        </div>
      </Show>

      <Show when={props.selected.length > 0}>
        <ul class="msel-chips" aria-label="Selected models">
          <For each={[...props.selected].sort((a, b) => a.localeCompare(b))}>
            {(id) => (
              <li class="tag msel-chip" classList={{ warn: noteOf(id) !== '' }} title={noteOf(id) ? `${id} (${noteOf(id)})` : id}>
                <span class="msel-id">{id}</span>
                <button type="button" aria-label={`Remove ${id}`} onClick={() => toggle(id)}>
                  <IconX />
                </button>
              </li>
            )}
          </For>
        </ul>
      </Show>
    </div>
  );
}
