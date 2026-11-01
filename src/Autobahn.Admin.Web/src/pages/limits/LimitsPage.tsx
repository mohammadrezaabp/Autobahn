import { For, Show, createMemo, onMount } from 'solid-js';
import { Button } from '../../components/primitives';
import { IconRefresh } from '../../components/icons';
import { formatNum } from '../../domain/format';
import { effectiveTenantTier, ruleModel, ruleSubject, tenantRules } from '../../domain/tenantLimits';
import { useKeysStore } from '../../stores/keys';
import type { RateLimitRule } from '../../stores/rateLimits';
import { useOwnTenantLimits } from '../../stores/tenantLimits';
import { TenantLimitFigures, tierSourceLabel } from '../tenants/TenantLimitFigures';
import '../../styles/tenants.css';

/** A rule's numbers. A rule sets only what it limits, so a zero is shown as "no value", not as 0. */
function RuleFigures(props: { rule: RateLimitRule }) {
  const or = (value: number | undefined) => ((value ?? 0) > 0 ? formatNum(value ?? 0) : '—');
  return (
    <span class="tn-nums" classList={{ 'tn-rule-off': !props.rule.enabled }}>
      <span><b>{or(props.rule.rpm)}</b><i>rpm</i></span>
      <span><b>{or(props.rule.burst)}</b><i>burst</i></span>
      <span><b>{or(props.rule.maxConcurrentStreams)}</b><i>streams</i></span>
      <span><b>{or(props.rule.tpm)}</b><i>tpm</i></span>
    </span>
  );
}

/**
 * The limits of the signed-in key's own tenant, read-only. The page a tenant's admin has in place
 * of Rate limits, which shows and edits every tenant's and is the operator's.
 */
export default function LimitsPage() {
  const store = useOwnTenantLimits();
  const keys = useKeysStore();

  const loadAll = (force = false) => {
    void store.load();
    void keys.load({ force });
  };

  onMount(() => loadAll());

  const effective = createMemo(() => {
    const own = store.limits();
    return own ? effectiveTenantTier(own.config, own.tenant) : null;
  });

  // The gateway has already left out every rule that is not this tenant's or one of its keys'.
  const rules = createMemo(() => {
    const own = store.limits();
    if (!own) return [];
    const keyIds = own.config.rules
      .filter((r) => r.scope.toLowerCase().startsWith('api_key'))
      .map((r) => ruleSubject(r));
    return tenantRules(own.config, own.tenant, keyIds);
  });

  const who = (rule: RateLimitRule) => {
    if (!rule.scope.toLowerCase().startsWith('api_key')) return { name: 'Every key of this tenant', sub: 'tenant' };
    const key = keys.keyById(ruleSubject(rule));
    return { name: String(key?.label || key?.keyPrefix || ruleSubject(rule)), sub: 'one key' };
  };

  return (
    <section class="page" id="panel-limits">
      <header class="page-header">
        <div class="titles">
          {null}
          <h2>Limits</h2>
          {null}
        </div>
        <div class="page-actions">
          <Button variant="secondary" onClick={() => loadAll(true)} disabled={store.loading()}>
            <span class="icon"><IconRefresh /></span> Refresh
          </Button>
        </div>
      </header>

      <Show when={store.loadError()}>
        <p class="notice error" role="alert">{store.loadError()}</p>
      </Show>
      <Show when={store.loading() && !store.limits()}>
        <p class="loading-hint">Loading limits…</p>
      </Show>

      <Show when={store.limits()}>
        {(own) => (
          <div class="tn-limits">
            <Show when={!own().config.enabled}>
              <p class="notice warn" role="status">
                Rate limiting is switched off on this gateway, so none of the limits below is enforced right now.
              </p>
            </Show>

            <div class="card tn-shared">
              <div class="tn-shared-main">
                <span class="eyebrow">Shared limit</span>
                <Show when={effective()}>
                  {(e) => (
                    <>
                      <TenantLimitFigures tier={e().tier} />
                      {null}
                    </>
                  )}
                </Show>
              </div>
            </div>

            <div class="rl-section">
              <div class="rl-section-head">
                <div>
                  <h3>Limits on this tenant and its keys ({formatNum(rules().length)})</h3>
                  {null}
                </div>
              </div>
              <div class="table-wrap card">
                <table class="data-table">
                  <thead>
                    <tr>
                      <th>Who</th>
                      <th>Model</th>
                      <th>Limit</th>
                      <th>State</th>
                    </tr>
                  </thead>
                  <tbody>
                    <For each={rules()}>
                      {(rule) => (
                        <tr>
                          <td>
                            <span class="tn-name">{who(rule).name}</span>
                            <span class="tn-sub">{who(rule).sub}</span>
                          </td>
                          <td>{ruleModel(rule) || 'All models'}</td>
                          <td>
                            <RuleFigures rule={rule} />
                            <Show when={rule.schedule.length > 0}>
                              <span class="tn-sub">changes on a schedule ({rule.schedule.length} window(s))</span>
                            </Show>
                          </td>
                          <td>
                            <span class="status-chip" classList={{ ok: rule.enabled, muted: !rule.enabled }}>
                              {rule.enabled ? 'On' : 'Off'}
                            </span>
                          </td>
                        </tr>
                      )}
                    </For>
                  </tbody>
                </table>
                <Show when={rules().length === 0}>
                  <p class="hint rl-no-match flush">No key limits configured.</p>
                </Show>
              </div>
              {null}
            </div>
          </div>
        )}
      </Show>
    </section>
  );
}
