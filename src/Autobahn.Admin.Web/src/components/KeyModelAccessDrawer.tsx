import { For, Show, createSignal } from 'solid-js';
import {
  addPrefix,
  checkModel,
  closeDrawer,
  hasPattern,
  removePattern,
  saveGrants,
  setAllOfProvider,
  setProviderCap,
  setProviderOpen,
  useKeyGrantsStore,
} from '../stores/keyGrants';
import { parseKeyCap, providerPattern } from '../domain/keyGrants';
import { formatCost } from '../domain/format';
import { ModelMultiSelect } from './ModelMultiSelect';
import { IconX } from './icons';
import { Button, Drawer } from './primitives';

/**
 * What one key may call, top to bottom in the order the gateway checks it: providers, then
 * models, then a box to test one model. Opened with openKeyAccessDrawer, from any list of keys.
 */
export function KeyModelAccessDrawer() {
  const grants = useKeyGrantsStore();
  const [prefix, setPrefix] = createSignal('');
  const [prefixError, setPrefixError] = createSignal('');
  const [probe, setProbe] = createSignal('');
  const removed = () => grants.choices().filter((c) => c.note === 'removed').length;
  const prefixes = () =>
    grants.patterns().filter((p) => !(grants.providers() ?? []).some((x) => providerPattern(x.provider) === p.toLowerCase()));

  const submitPrefix = (e: Event) => {
    e.preventDefault();
    const error = addPrefix(prefix());
    setPrefixError(error ?? '');
    if (!error) setPrefix('');
  };

  return (
    <Drawer open={grants.drawerOpen()} title={`Key access — ${grants.keyLabel()}`} onClose={() => closeDrawer()}>
      <Show when={grants.loading()}><p class="loading-hint">Loading…</p></Show>
      <Show when={!grants.loading()}>
        <Show when={grants.providers()?.length}>
          <h4 class="ka-step">1. Providers</h4>
          
          <For each={grants.providers() ?? []}>
            {(p) => (
              <>
                <label class="checkbox-row" classList={{ disabled: !p.tenantOpen }}>
                  <input
                    type="checkbox"
                    checked={grants.isProviderOpen(p.provider)}
                    disabled={!p.tenantOpen}
                    onChange={(e) => setProviderOpen(p.provider, e.currentTarget.checked)}
                  />
                  <span>{p.displayName}</span>
                  <span class="tag level-warning">Paid</span>
                </label>
                
                
                <Show when={p.tenantOpen && grants.isProviderOpen(p.provider)}>
                  <div class="ka-cap">
                    <label for={`ka-cap-${p.provider}`}>Monthly cap (USD)</label>
                    <input
                      id={`ka-cap-${p.provider}`}
                      type="text"
                      inputmode="decimal"
                      placeholder="no cap of its own"
                      value={grants.providerCap(p.provider)}
                      onInput={(e) => setProviderCap(p.provider, e.currentTarget.value)}
                    />
                  </div>
                  <Show when={parseKeyCap(grants.providerCap(p.provider), p.tenantMonthlyCapUsd).error}>
                    <p class="field-error">{parseKeyCap(grants.providerCap(p.provider), p.tenantMonthlyCapUsd).error}</p>
                  </Show>
                  
                </Show>
              </>
            )}
          </For>
        </Show>

        <h4 class="ka-step">{grants.providers()?.length ? '2. Models' : 'Models'}</h4>
        
        <ModelMultiSelect
          choices={grants.choices()}
          selected={grants.selected()}
          onChange={(next) => grants.setSelected(next)}
        />
        <For each={(grants.providers() ?? []).filter((p) => grants.isProviderOpen(p.provider))}>
          {(p) => (
            <label class="checkbox-row">
              <input
                type="checkbox"
                checked={hasPattern(providerPattern(p.provider))}
                onChange={(e) => setAllOfProvider(p.provider, e.currentTarget.checked)}
              />
              <span>All {p.displayName} models, including ones added later</span>
            </label>
          )}
        </For>

        <form class="ka-prefix" onSubmit={submitPrefix}>
          <input
            type="text"
            placeholder="qwen/"
            aria-label="Model id prefix"
            value={prefix()}
            onInput={(e) => setPrefix(e.currentTarget.value)}
          />
          <Button type="submit" variant="secondary" size="sm">Add prefix</Button>
        </form>
        
        <Show when={prefixError()}><p class="field-error">{prefixError()}</p></Show>
        <Show when={prefixes().length > 0}>
          <ul class="msel-chips" aria-label="Prefixes">
            <For each={prefixes()}>
              {(p) => (
                <li class="tag msel-chip">
                  <span class="msel-id">{p}</span>
                  <button type="button" aria-label={`Remove ${p}`} onClick={() => removePattern(p)}><IconX /></button>
                </li>
              )}
            </For>
          </ul>
        </Show>

        <Show when={grants.patterns().length > 0}>
          
          <ModelMultiSelect
            label="Hold back models"
            choices={grants.deniedChoices()}
            selected={grants.denied()}
            onChange={(next) => grants.setDenied(next)}
          />
        </Show>

        

        <p class="notice" classList={{ warn: grants.selected().length === 0 && grants.patterns().length === 0 }}>
          {grants.summary()}
        </p>
        <Button onClick={() => void saveGrants()} disabled={grants.saving()}>{grants.saving() ? 'Saving…' : 'Save access'}</Button>

        <Show when={grants.providers()}>
          <h4 class="ka-step">Check a model</h4>
          
          <form class="ka-prefix" onSubmit={(e) => { e.preventDefault(); void checkModel(probe()); }}>
            <input
              type="text"
              placeholder="Model name"
              aria-label="Model to check"
              value={probe()}
              onInput={(e) => setProbe(e.currentTarget.value)}
            />
            <Button type="submit" variant="secondary" size="sm" disabled={grants.checking() || !probe().trim()}>
              {grants.checking() ? 'Checking…' : 'Check'}
            </Button>
          </form>
          <Show when={grants.check()}>
            {(result) => (
              <p class="notice" classList={{ warn: !result().allowed }} role="status">
                <strong>{result().allowed ? 'Allowed' : `Refused: ${result().reason ?? 'unknown'}`}</strong>
                {' '}{result().message}
              </p>
            )}
          </Show>
        </Show>
      </Show>
    </Drawer>
  );
}
