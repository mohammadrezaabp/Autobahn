import { For, Show, onMount } from 'solid-js';
import { A, useNavigate } from '@solidjs/router';
import { Button } from '../../components/primitives';
import { IconChevronRight, IconRefresh } from '../../components/icons';
import { formatCost, formatNum, formatTime } from '../../domain/format';
import { connectionOf, isCreditLow, isMoney, type ProviderOverview } from '../../domain/providers';
import { loadProviders, useProvidersStore } from '../../stores/providers';
import '../../styles/providers.css';

/** Remaining credit as one figure, or why there is none to show. */
export function creditText(p: ProviderOverview): string {
  if (!p.supportsBalance) return 'Not reported';
  if (!p.keyConfigured) return '—';
  const balance = p.balance;
  const remaining = balance?.remaining;
  if (remaining == null) {
    // A provider with no balance, only spend (OpenAI): what it says was spent this month.
    return balance?.used == null ? '—' : `${formatCost(balance.used, balance.currency)} spent`;
  }
  return isMoney(balance?.currency)
    ? formatCost(remaining, balance?.currency)
    : `${formatNum(remaining)} ${balance!.currency.toLowerCase()}`;
}

export default function ProvidersPage() {
  const store = useProvidersStore();
  const navigate = useNavigate();

  onMount(() => void loadProviders());

  return (
    <section class="page" id="panel-providers">
      <header class="page-header">
        <div class="titles">
          
          <h2>Providers</h2>
          
        </div>
        <div class="page-actions">
          <Button variant="secondary" onClick={() => void loadProviders()} disabled={store.loading()}>
            <span class="icon"><IconRefresh /></span> Refresh
          </Button>
        </div>
      </header>

      <Show when={store.loadError()}>
        <p class="notice error" role="alert">
          {store.loadError()}{' '}
          <button type="button" class="rl-link" onClick={() => void loadProviders()}>Retry</button>
        </p>
      </Show>
      <Show when={store.loading() && !store.loaded()}>
        <p class="loading-hint">Loading providers…</p>
      </Show>

      <Show when={store.loaded()}>
        <div class="table-wrap">
          <table class="data-table t-providers">
            <thead>
              <tr>
                <th>Provider</th>
                <th>Connection</th>
                <th class="num">Credit left</th>
                <th class="num">Spent this month</th>
                <th class="num">Models</th>
                <th>Who can spend</th>
                <th class="cell-actions"><span class="sr-only">Open</span></th>
              </tr>
            </thead>
            <tbody>
              <For each={store.providers()}>
                {(p) => {
                  const connection = () => connectionOf(p);
                  const open = () => navigate(`/providers/${encodeURIComponent(p.provider)}`);
                  return (
                    <tr class="pv-row" onClick={open}>
                      <td>
                        <span class="pv-name">
                          {p.displayName}
                          <span class="tag level-warning">Paid</span>
                        </span>
                        <span class="pv-sub"><code>{p.upstreamBaseUrl}</code></span>
                      </td>
                      <td>
                        <span class={`status-chip ${connection().state}`} title={connection().detail}>{connection().label}</span>
                        <Show when={p.balance?.readAtUtc}>
                          <span class="pv-sub">read {formatTime(p.balance?.readAtUtc)}</span>
                        </Show>
                      </td>
                      <td class="num" classList={{ 'pv-low': isCreditLow(p) }}>
                        {creditText(p)}
                        <Show when={isCreditLow(p)}><span class="pv-sub">below {formatCost(p.lowBalanceUsd)}</span></Show>
                      </td>
                      <td class="num">
                        {formatCost(p.spentThisMonthUsd)}
                        <span class="pv-sub">{formatNum(p.requestsThisMonth)} requests</span>
                      </td>
                      <td class="num">{formatNum(p.modelCount)}</td>
                      <td>
                        <Show
                          when={p.openTenantCount > 0}
                          fallback={<span class="pv-sub pv-closed">Closed to everyone</span>}
                        >
                          {formatNum(p.openTenantCount)} {p.openTenantCount === 1 ? 'tenant' : 'tenants'}
                          <span class="pv-sub">{formatNum(p.openKeyCount)} {p.openKeyCount === 1 ? 'key' : 'keys'} opened</span>
                        </Show>
                      </td>
                      <td class="cell-actions">
                        <button
                          type="button"
                          class="icon-btn"
                          aria-label={`Open ${p.displayName}`}
                          onClick={(e) => { e.stopPropagation(); open(); }}
                        >
                          <span class="icon"><IconChevronRight /></span>
                        </button>
                      </td>
                    </tr>
                  );
                }}
              </For>
            </tbody>
          </table>
        </div>
        
      </Show>
    </section>
  );
}
