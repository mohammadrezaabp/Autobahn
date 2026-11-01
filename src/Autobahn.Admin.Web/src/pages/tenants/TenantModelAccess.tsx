import { For, Show, createEffect, untrack } from 'solid-js';
import { A } from '@solidjs/router';
import { Button } from '../../components/primitives';
import { formatCost, formatNum } from '../../domain/format';
import {
  loadTenantGrants,
  saveTenantGrants,
  setTenantProviderOpen,
  toggleModel,
  toggleRestricted,
  useTenantGrantsStore,
} from '../../stores/tenantGrants';

/**
 * Which models a tenant may call. Without a tenant id it edits the signed-in key's own tenant,
 * which is all a tenant's own admin can reach.
 */
export function TenantModelAccess(props: { tenantId?: string; tenantName?: string }) {
  const grants = useTenantGrantsStore();

  // Reloads when the tenant changes, and only then: the load reads signals of its own, and
  // tracking those here would start it again every time it stored what it had fetched.
  createEffect(() => {
    const tenantId = props.tenantId ?? '';
    untrack(() => void loadTenantGrants(tenantId));
  });

  const selectAll = () => {
    for (const m of grants.registryModels()) {
      if (!grants.selected().includes(m.id)) toggleModel(m.id);
    }
  };
  const clearAll = () => {
    for (const id of [...grants.selected()]) toggleModel(id);
  };

  return (
    <div class="card tn-access">
      <div class="card-title-row">
        <div>
          <h3>Model access</h3>
          
        </div>
      </div>
      <Show when={grants.loading()}><p class="loading-hint">Loading…</p></Show>
      <Show when={!grants.loading()}>
        <div class="tn-access-mode" role="radiogroup" aria-label="Model access">
          <label class="tn-choice" classList={{ active: !grants.restricted() }}>
            <input
              type="radio"
              name="tn-access-mode"
              checked={!grants.restricted()}
              onChange={() => toggleRestricted(false)}
            />
            <span>
              <b>All self-hosted models</b>
            </span>
          </label>
          <label class="tn-choice" classList={{ active: grants.restricted() }}>
            <input
              type="radio"
              name="tn-access-mode"
              checked={grants.restricted()}
              onChange={() => toggleRestricted(true)}
            />
            <span>
              <b>Only chosen models</b>
            </span>
          </label>
        </div>

        <Show when={grants.restricted()}>
          <div class="tn-access-head">
            <span class="hint flush">
              {formatNum(grants.selected().length)} of {formatNum(grants.registryModels().length)} models allowed
            </span>
            <span class="spacer" />
            <button type="button" class="rl-link" onClick={selectAll}>Select all</button>
            <button type="button" class="rl-link" onClick={clearAll}>Clear</button>
          </div>
          <div class="tn-model-grid">
            <For each={grants.registryModels()}>
              {(m) => (
                <label class="checkbox-row tn-model">
                  <input
                    type="checkbox"
                    checked={grants.selected().includes(m.id)}
                    onChange={() => toggleModel(m.id)}
                  />
                  <span title={m.id}>{m.id}</span>
                </label>
              )}
            </For>
          </div>
          <Show when={grants.selected().length === 0}>
            <p class="notice warn">
              Tick at least one model. To take all access away, clear the model access of the tenant's keys instead.
            </p>
          </Show>
        </Show>

        <div class="card-foot-actions">
          <Button onClick={() => void saveTenantGrants()} disabled={grants.saving() || (grants.restricted() && grants.selected().length === 0)}>
            {grants.saving() ? 'Saving…' : 'Save model access'}
          </Button>
        </div>

        <Show when={grants.providers().length > 0}>
          <h3>External providers</h3>
          
          <For each={grants.providers()}>
            {(p) => (
              <div class="tn-access-head">
                <span>{p.displayName}</span>
                <span class="tag level-warning">Paid</span>
                <span class="hint flush">
                  {p.open ? `Open · ${formatNum(p.openKeyCount ?? 0)} ${p.openKeyCount === 1 ? 'key' : 'keys'} opened` : 'Closed'}
                  <Show when={p.open}>
                    {' · '}
                    {p.monthlyCapUsd == null
                      ? `${formatCost(p.spentThisMonthUsd ?? 0)} this month, no cap`
                      : `${formatCost(p.spentThisMonthUsd ?? 0)} of ${formatCost(p.monthlyCapUsd)} this month`}
                  </Show>
                </span>
                <span class="spacer" />
                <Show
                  when={props.tenantId}
                  fallback={<span class="hint flush">An operator can {p.open ? 'close' : 'open'} it.</span>}
                >
                  <A class="rl-link" href={`/providers/${encodeURIComponent(p.provider)}`}>Cap and spend</A>
                  <Button
                    variant={p.open ? 'danger' : 'secondary'}
                    size="sm"
                    disabled={grants.saving()}
                    onClick={() => void setTenantProviderOpen(p.provider, !p.open)}
                  >
                    {p.open ? `Close ${p.displayName}` : `Open ${p.displayName}`}
                  </Button>
                </Show>
              </div>
            )}
          </For>
        </Show>
      </Show>
    </div>
  );
}
