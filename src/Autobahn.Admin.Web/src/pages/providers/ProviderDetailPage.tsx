import { For, Show, createEffect, createMemo, createSignal, onCleanup, onMount, untrack } from 'solid-js';
import { A, useParams } from '@solidjs/router';
import { Sparkline } from '../../components/Sparkline';
import { Button, Field, Tabs } from '../../components/primitives';
import { IconArrowLeft, IconRefresh, IconSearch } from '../../components/icons';
import { formatCost, formatNum, formatSharePct, formatTime } from '../../domain/format';
import {
  capRatio,
  connectionOf,
  describePeakPricing,
  describeReconciliation,
  describeTenantChange,
  formatPerMillion,
  isCreditLow,
  parseCapInput,
  type ProviderOverview,
  type ProviderReconciliation,
  type ProviderSpendSlice,
  type ProviderTenantRow,
} from '../../domain/providers';
import { apiClient } from '../../stores/auth';
import {
  addCatalogModels,
  clearProviderKey,
  findProvider,
  loadProviderReconciliation,
  loadProviderSpend,
  loadProviderTenants,
  loadProviders,
  refreshProviderBalance,
  replaceProviderKey,
  resetProviderDetail,
  saveProviderSettings,
  saveProviderTenant,
  searchCatalog,
  useProvidersStore,
} from '../../stores/providers';
import { creditText } from './ProvidersPage';
import '../../styles/providers.css';

interface AddedModel {
  id: string;
  state: string;
  input: number | null;
  output: number | null;
  context: number;
}

export default function ProviderDetailPage() {
  const params = useParams();
  const store = useProvidersStore();
  const provider = createMemo(() => findProvider(params.id ?? ''));

  onMount(() => void loadProviders());
  // The tabs belong to one provider: another one starts with none of them loaded.
  createEffect(() => {
    void params.id;
    untrack(() => resetProviderDetail());
  });
  onCleanup(() => resetProviderDetail());

  return (
    <section class="page" id="panel-provider">
      <A class="pv-back" href="/providers">
        <span class="icon"><IconArrowLeft /></span> Providers
      </A>

      <Show when={store.loadError()}>
        <p class="notice error" role="alert">{store.loadError()}</p>
      </Show>
      <Show when={store.loading() && !store.loaded()}>
        <p class="loading-hint">Loading provider…</p>
      </Show>
      <Show when={store.loaded() && !provider()}>
        <p class="notice warn">The gateway manages no provider called "{params.id}".</p>
      </Show>

      <Show when={provider()}>
        {(p) => (
          <>
            <header class="page-header">
              <div class="titles">
                
                <h2 class="pv-title">
                  {p().displayName}
                  <span class="tag level-warning">Paid</span>
                </h2>
                <p class="page-sub"><code>{p().upstreamBaseUrl}</code></p>
              </div>
              <div class="page-actions">
                <Button
                  variant="secondary"
                  disabled={store.busy()}
                  onClick={() => void (p().supportsBalance ? refreshProviderBalance(p().provider) : loadProviders())}
                >
                  <span class="icon"><IconRefresh /></span> {p().supportsBalance ? 'Read balance now' : 'Refresh'}
                </Button>
              </div>
            </header>

            <ProviderHeadline provider={p()} />

            <Tabs
              tabs={[
                { id: 'models', label: 'Models', content: () => <ModelsTab provider={p()} /> },
                { id: 'access', label: 'Access', content: () => <AccessTab provider={p()} /> },
                { id: 'spend', label: 'Spend', content: () => <SpendTab provider={p()} /> },
                // Not 'settings': the shell's own Settings tab already has that element id.
                { id: 'provider-settings', label: 'Settings', content: () => <SettingsTab provider={p()} /> },
              ]}
            />
          </>
        )}
      </Show>
    </section>
  );
}

/** The three answers the page owes at the top: is it working, how much is left, who can spend it. */
function ProviderHeadline(props: { provider: ProviderOverview }) {
  const connection = () => connectionOf(props.provider);
  return (
    <>
      <dl class="pv-stats card">
        <div class="pv-stat">
          <dt>Connection</dt>
          <dd><span class={`status-chip ${connection().state}`}>{connection().label}</span></dd>
          <Show when={props.provider.balance?.readAtUtc}>
            <span class="pv-sub">balance read {formatTime(props.provider.balance?.readAtUtc)}</span>
          </Show>
        </div>
        <div class="pv-stat">
          <dt>Credit left</dt>
          <dd classList={{ 'is-warn': isCreditLow(props.provider) }}>{creditText(props.provider)}</dd>
          <span class="pv-sub">
            <Show
              when={props.provider.lowBalanceUsd != null}
              fallback={props.provider.supportsBalance ? 'no alert level set' : 'this provider reports none'}
            >
              alert below {formatCost(props.provider.lowBalanceUsd)}
            </Show>
          </span>
        </div>
        <div class="pv-stat">
          <dt>Spent this month</dt>
          <dd>{formatCost(props.provider.spentThisMonthUsd)}</dd>
          <span class="pv-sub">{formatNum(props.provider.requestsThisMonth)} requests through the gateway</span>
        </div>
        <div class="pv-stat">
          <dt>Who can spend</dt>
          <dd>
            {props.provider.openTenantCount > 0
              ? `${formatNum(props.provider.openTenantCount)} ${props.provider.openTenantCount === 1 ? 'tenant' : 'tenants'}`
              : 'Nobody'}
          </dd>
          <span class="pv-sub">
            {props.provider.openTenantCount > 0
              ? `${formatNum(props.provider.openKeyCount)} ${props.provider.openKeyCount === 1 ? 'key' : 'keys'} opened`
              : 'closed to every tenant'}
          </span>
        </div>
      </dl>
      <Show when={connection().detail}>
        <p class="notice" classList={{ warn: connection().state === 'warn' || connection().state === 'muted', error: connection().state === 'fail' }}>
          {connection().detail}
        </p>
      </Show>
    </>
  );
}

/** The provider's peak hours, beside the prices they multiply. */
function PeakPricingNote(props: { provider: ProviderOverview }) {
  const peak = () => describePeakPricing(props.provider);
  return (
    <Show when={peak()}>
      {(p) => (
        <>
          
          <Show when={p().missingYear}>
            {(year) => (
              <p class="notice" classList={{ warn: p().missingIsThisYear }}>
                The holiday calendar has no holidays for {year()} yet, so every weekday of {year()} is priced with its
                peak hours. {p().missingIsThisYear
                  ? 'Update the lunar-csharp package, or list the holidays'
                  : 'They are announced late in the year before; update the lunar-csharp package once they are, or list them'}{' '}
                in <code>Billing:PeakPricing:{props.provider.provider}:ExcludedDates</code>.
              </p>
            )}
          </Show>
        </>
      )}
    </Show>
  );
}

function ModelsTab(props: { provider: ProviderOverview }) {
  const store = useProvidersStore();
  const [added, setAdded] = createSignal<AddedModel[] | null>(null);
  const [query, setQuery] = createSignal('');
  const [ticked, setTicked] = createSignal<string[]>([]);
  let timer: ReturnType<typeof setTimeout> | undefined;

  const loadAdded = async () => {
    try {
      const list = await apiClient.apiJson<Array<Record<string, unknown>>>('/admin/api/models');
      setAdded(
        (Array.isArray(list) ? list : [])
          .filter((item) => String(item.provider ?? '') === props.provider.provider)
          .map((item) => {
            const model = (item.model as Record<string, unknown> | undefined) ?? {};
            const pricing = item.pricing as Record<string, unknown> | null | undefined;
            return {
              id: String(model.id ?? ''),
              state: String(model.state ?? 'serving'),
              input: pricing ? Number(pricing.inputPricePerMillionTokens ?? 0) : null,
              output: pricing ? Number(pricing.outputPricePerMillionTokens ?? 0) : null,
              context: Number(model.maxContextLength ?? 0),
            };
          }),
      );
    } catch {
      setAdded([]);
    }
  };

  onMount(() => {
    void loadAdded();
    if (props.provider.supportsCatalog && props.provider.keyConfigured) void searchCatalog(props.provider.provider, '');
  });
  onCleanup(() => clearTimeout(timer));

  const onSearch = (value: string) => {
    setQuery(value);
    clearTimeout(timer);
    timer = setTimeout(() => void searchCatalog(props.provider.provider, value), 250);
  };

  const toggle = (id: string) =>
    setTicked((list) => (list.includes(id) ? list.filter((x) => x !== id) : [...list, id]));

  const addTicked = async () => {
    const done = await addCatalogModels(props.provider.provider, ticked());
    if (done.length) {
      setTicked([]);
      void loadAdded();
    }
  };

  return (
    <>
      <div class="card">
        <h3>Added models</h3>
        
        <Show when={added()} fallback={<p class="loading-hint">Loading…</p>}>
          <Show
            when={added()!.length > 0}
            fallback={<p class="hint flush">No {props.provider.displayName} model is registered yet.</p>}
          >
            <div class="table-wrap">
              <table class="data-table">
                <thead>
                  <tr>
                    <th>Model</th><th class="num">Price in / out (1M tokens)</th><th class="num">Context</th><th>State</th>
                  </tr>
                </thead>
                <tbody>
                  <For each={added()}>
                    {(m) => (
                      <tr>
                        <td class="cell-code">
                          <span class="cell-code-inner"><code title={m.id}>{m.id}</code></span>
                        </td>
                        <td class="num">
                          {m.input == null
                            ? (
                              <span class="pv-sub pv-low">
                                {props.provider.reportsCost
                                  ? 'no price: a request the provider reports no cost for is recorded as free'
                                  : 'no price: its requests are recorded as free, so no cap can stop them. Set one on Routing.'}
                              </span>
                            )
                            : `${formatPerMillion(m.input)} / ${formatPerMillion(m.output)}`}
                        </td>
                        <td class="num">{m.context ? formatNum(m.context) : '—'}</td>
                        <td>
                          <span class="status-chip" classList={{ fail: m.state === 'stopped', ok: m.state !== 'stopped' }}>
                            {m.state === 'stopped' ? 'Stopped' : 'Serving'}
                          </span>
                        </td>
                      </tr>
                    )}
                  </For>
                </tbody>
              </table>
            </div>
            <PeakPricingNote provider={props.provider} />
            
          </Show>
        </Show>
      </div>

      <div class="card">
        <h3>Add from the catalogue</h3>
        <Show
          when={props.provider.supportsCatalog}
          fallback={null}
        >
          
          <div class="toolbar">
            <label class="filter-field">
              <span class="icon"><IconSearch /></span>
              <input
                placeholder="Search the catalogue…"
                aria-label="Search the catalogue"
                value={query()}
                onInput={(e) => onSearch(e.currentTarget.value)}
              />
            </label>
            <span class="spacer" />
            <Button disabled={store.busy() || ticked().length === 0} onClick={() => void addTicked()}>
              {ticked().length ? `Add ${ticked().length} ${ticked().length === 1 ? 'model' : 'models'}` : 'Add'}
            </Button>
          </div>
          <Show when={store.catalogError()}>
            <p class="notice warn">{store.catalogError()}</p>
          </Show>
          <Show when={store.catalogLoading() && !store.catalog()}>
            <p class="loading-hint">Reading the catalogue…</p>
          </Show>
          <Show when={store.catalog()}>
            {(list) => (
              <>
                <div class="table-wrap pv-catalog">
                  <table class="data-table">
                    <thead>
                      <tr>
                        <th class="pv-tick"><span class="sr-only">Add</span></th>
                        <th>Model</th>
                        <th class="num">Input (1M)</th>
                        <th class="num">Output (1M)</th>
                        <th class="num">Per call</th>
                        <th class="num">Context</th>
                      </tr>
                    </thead>
                    <tbody>
                      <For each={list()}>
                        {(m) => (
                          <tr classList={{ 'pv-added': m.added }}>
                            <td class="pv-tick">
                              <Show when={!m.added} fallback={<span class="tag">added</span>}>
                                <input
                                  type="checkbox"
                                  aria-label={`Add ${m.id}`}
                                  checked={ticked().includes(m.id)}
                                  onChange={() => toggle(m.id)}
                                />
                              </Show>
                            </td>
                            <td>
                              <code title={m.id}>{m.id}</code>
                              <Show when={m.name && m.name !== m.id}><span class="pv-sub">{m.name}</span></Show>
                            </td>
                            <td class="num">{formatPerMillion(m.inputPricePerMillionTokens)}</td>
                            <td class="num">{formatPerMillion(m.outputPricePerMillionTokens)}</td>
                            <td class="num">{m.pricePerRequest ? formatCost(m.pricePerRequest) : '—'}</td>
                            <td class="num">{m.contextLength ? formatNum(m.contextLength) : '—'}</td>
                          </tr>
                        )}
                      </For>
                    </tbody>
                  </table>
                </div>
                <p class="hint">
                  {list().length === 0
                    ? 'No catalogue model matches.'
                    : store.catalogTotal() > list().length
                      ? `Showing ${formatNum(list().length)} of ${formatNum(store.catalogTotal())} matches. Narrow the search to see the rest.`
                      : `${formatNum(list().length)} ${list().length === 1 ? 'model' : 'models'}.`}
                </p>
              </>
            )}
          </Show>
        </Show>
      </div>
    </>
  );
}

function AccessTab(props: { provider: ProviderOverview }) {
  const store = useProvidersStore();
  onMount(() => void loadProviderTenants(props.provider.provider));

  return (
    <div class="card">
      <h3>Tenants</h3>
      
      <Show when={store.tenants()} fallback={<p class="loading-hint">Loading…</p>}>
        {(rows) => (
          <div class="table-wrap">
            <table class="data-table t-pv-access">
              <thead>
                <tr>
                  <th>Tenant</th>
                  <th>{props.provider.displayName}</th>
                  <th>Monthly cap (USD)</th>
                  <th class="num">Spent this month</th>
                  <th class="num">Keys opened</th>
                  <th class="cell-actions"><span class="sr-only">Save</span></th>
                </tr>
              </thead>
              <tbody>
                <For each={rows()}>{(row) => <AccessRow provider={props.provider} row={row} />}</For>
              </tbody>
            </table>
          </div>
        )}
      </Show>
    </div>
  );
}

function AccessRow(props: { provider: ProviderOverview; row: ProviderTenantRow }) {
  const store = useProvidersStore();
  const [open, setOpen] = createSignal(props.row.open);
  const [capText, setCapText] = createSignal(props.row.monthlyCapUsd == null ? '' : String(props.row.monthlyCapUsd));
  const cap = () => parseCapInput(capText());
  const change = () => (cap().error ? '' : describeTenantChange(props.provider.displayName, props.row, open(), cap().value));
  const ratio = () => capRatio(props.row.spentThisMonthUsd, props.row.monthlyCapUsd);

  return (
    <>
      <tr>
        <td>
          <span class="pv-name">{props.row.tenantName || props.row.tenantSlug}</span>
          <span class="pv-sub"><code>{props.row.tenantSlug}</code></span>
        </td>
        <td>
          <label class="checkbox-row">
            <input
              type="checkbox"
              checked={open()}
              aria-label={`${props.provider.displayName} open for ${props.row.tenantSlug}`}
              onChange={(e) => setOpen(e.currentTarget.checked)}
            />
            <span>{open() ? 'Open' : 'Closed'}</span>
          </label>
        </td>
        <td>
          <input
            class="pv-cap"
            type="text"
            inputmode="decimal"
            placeholder="no cap"
            aria-label={`Monthly cap for ${props.row.tenantSlug}`}
            value={capText()}
            onInput={(e) => setCapText(e.currentTarget.value)}
          />
          <Show when={cap().error}><p class="field-error">{cap().error}</p></Show>
        </td>
        <td class="num" classList={{ 'pv-low': (ratio() ?? 0) >= 0.8 }}>
          {formatCost(props.row.spentThisMonthUsd)}
          <Show when={ratio() != null && Number.isFinite(ratio())}>
            <span class="pv-sub">{formatSharePct(ratio())} of the cap</span>
          </Show>
        </td>
        <td class="num">{formatNum(props.row.openKeyCount)}</td>
        <td class="cell-actions">
          <Button
            size="sm"
            disabled={store.busy() || !change()}
            onClick={() => void saveProviderTenant(props.provider.provider, props.row.tenantId, open(), cap().value)}
          >
            Save
          </Button>
        </td>
      </tr>
      <Show when={change()}>
        <tr class="pv-change"><td colspan="6">{change()}</td></tr>
      </Show>
    </>
  );
}

function SpendTab(props: { provider: ProviderOverview }) {
  const store = useProvidersStore();
  onMount(() => {
    void loadProviderSpend(props.provider.provider);
    if (props.provider.supportsBalance) void loadProviderReconciliation(props.provider.provider);
  });

  return (
    <Show when={store.spend()} fallback={<p class="loading-hint">Loading…</p>}>
      {(report) => (
        <>
          <div class="card">
            <div class="toolbar">
              <h3 class="pv-inline-title">Last {formatNum(report().daily.length)} days</h3>
              <span class="spacer" />
              <For each={[7, 30, 90]}>
                {(days) => (
                  <Button
                    size="sm"
                    variant={store.spendDays() === days ? 'primary' : 'secondary'}
                    onClick={() => void loadProviderSpend(props.provider.provider, days)}
                  >
                    {days} days
                  </Button>
                )}
              </For>
            </div>
            <dl class="pv-stats">
              <div class="pv-stat"><dt>Cost</dt><dd>{formatCost(report().totalUsd)}</dd></div>
              <div class="pv-stat"><dt>Requests</dt><dd>{formatNum(report().requests)}</dd></div>
              <div class="pv-stat">
                <dt>Dearest day</dt>
                <dd>{formatCost(Math.max(0, ...report().daily.map((d) => d.costUsd)))}</dd>
              </div>
              <div class="pv-stat">
                <dt>Daily average</dt>
                <dd>{formatCost(report().daily.length ? report().totalUsd / report().daily.length : 0)}</dd>
              </div>
            </dl>
            <Show when={report().totalUsd > 0}>
              <Sparkline class="pv-trend" values={report().daily.map((d) => d.costUsd)} />
              <p class="hint pv-trend-axis">
                <span>{report().from}</span><span>cost per day</span><span>{report().to}</span>
              </p>
            </Show>
            
          </div>
          <Show when={props.provider.supportsBalance && store.reconciliation()}>
            {(r) => <ReconciliationCard provider={props.provider} report={r()} />}
          </Show>
          <SliceTable title="By tenant" first="Tenant" rows={report().byTenant} total={report().totalUsd} />
          <SliceTable title="By key" first="Key" rows={report().byKey} total={report().totalUsd} />
          <SliceTable title="By model" first="Model" rows={report().byModel} total={report().totalUsd} />
        </>
      )}
    </Show>
  );
}

const signedCost = (value: number) => `${value < 0 ? '−' : ''}${formatCost(Math.abs(value))}`;

/**
 * What the provider says it took, from the fall of its balance, beside what the gateway charged
 * over the same stretches. This month in UTC, like the caps.
 */
function ReconciliationCard(props: { provider: ProviderOverview; report: ProviderReconciliation }) {
  const summary = () => describeReconciliation(props.report, props.provider.displayName);
  return (
    <div class="card">
      <div class="toolbar">
        <h3 class="pv-inline-title">Reconciliation, this month</h3>
        <span class="spacer" />
        <Show when={!props.report.unavailable}>
          <span class={`status-chip ${props.report.drifting ? 'warn' : 'ok'}`}>
            {props.report.drifting ? 'Drifting' : 'In line'}
          </span>
        </Show>
      </div>
      <Show when={!props.report.unavailable}>
        <dl class="pv-stats">
          <div class="pv-stat">
            <dt>{props.provider.displayName} says</dt>
            <dd>{formatCost(props.report.providerSpendUsd)}</dd>
            <span class="pv-sub">fall of the balance</span>
          </div>
          <div class="pv-stat">
            <dt>Gateway says</dt>
            <dd>{formatCost(props.report.gatewaySpendUsd)}</dd>
            <span class="pv-sub">its charges, same time</span>
          </div>
          <div class="pv-stat">
            <dt>Difference</dt>
            <dd classList={{ 'is-warn': props.report.drifting }}>{signedCost(props.report.differenceUsd)}</dd>
            <span class="pv-sub">
              {props.report.differencePercent == null ? 'nothing spent' : `${props.report.differencePercent}% of the larger`}
            </span>
          </div>
          <div class="pv-stat">
            <dt>Readings</dt>
            <dd>{formatNum(props.report.readings)}</dd>
            <span class="pv-sub">
              {props.report.topUps > 0 ? `${formatNum(props.report.topUps)} with a top-up, left out` : 'no top-ups'}
            </span>
          </div>
        </dl>
      </Show>
      <p class="notice" classList={{ warn: summary().tone === 'warn' }}>{summary().text}</p>
      <Show when={props.report.daily.length > 0}>
        <div class="table-wrap">
          <table class="data-table">
            <thead>
              <tr>
                <th>Day (UTC)</th>
                <th class="num">{props.provider.displayName}</th>
                <th class="num">Gateway</th>
                <th class="num">Difference</th>
              </tr>
            </thead>
            <tbody>
              <For each={[...props.report.daily].reverse()}>
                {(day) => (
                  <tr>
                    <td>{day.date}</td>
                    <td class="num">{formatCost(day.providerUsd)}</td>
                    <td class="num">{formatCost(day.gatewayUsd)}</td>
                    <td class="num">{signedCost(day.providerUsd - day.gatewayUsd)}</td>
                  </tr>
                )}
              </For>
            </tbody>
          </table>
        </div>
      </Show>
      
    </div>
  );
}

function SliceTable(props: { title: string; first: string; rows: ProviderSpendSlice[]; total: number }) {
  return (
    <div class="card">
      <h3>{props.title}</h3>
      <Show when={props.rows.length > 0} fallback={<p class="hint flush">Nothing spent in this period.</p>}>
        <div class="table-wrap">
          <table class="data-table">
            <thead>
              <tr><th>{props.first}</th><th class="num">Cost</th><th class="num">Share</th><th class="num">Requests</th></tr>
            </thead>
            <tbody>
              <For each={props.rows}>
                {(row) => (
                  <tr>
                    <td>
                      {row.label}
                      <Show when={row.detail}><span class="pv-sub">{row.detail}</span></Show>
                    </td>
                    <td class="num">{formatCost(row.costUsd)}</td>
                    <td class="num">{props.total > 0 ? formatSharePct(row.costUsd / props.total) : '—'}</td>
                    <td class="num">{formatNum(row.requests)}</td>
                  </tr>
                )}
              </For>
            </tbody>
          </table>
        </div>
      </Show>
    </div>
  );
}

function SettingsTab(props: { provider: ProviderOverview }) {
  const store = useProvidersStore();
  const [key, setKey] = createSignal('');
  const [level, setLevel] = createSignal(props.provider.lowBalanceUsd == null ? '' : String(props.provider.lowBalanceUsd));
  const [startStopped, setStartStopped] = createSignal(props.provider.newModelsStartStopped);
  const parsedLevel = () => parseCapInput(level());

  const source = () =>
    props.provider.keySource === 'stored'
      ? 'A key stored from this page. It takes precedence over the environment.'
      : props.provider.keySource === 'environment'
        ? `Read from ${props.provider.envVar} in the gateway's environment.`
        : `No key. Set ${props.provider.envVar} on the gateway, or store one here.`;

  const replace = async () => {
    if (await replaceProviderKey(props.provider.provider, key())) setKey('');
  };

  return (
    <>
      <div class="card form-card">
        <h3>Company key</h3>
        
        <p class="notice" classList={{ warn: props.provider.keySource === 'none' }}>{source()}</p>
        <Field label="Replace the key" for="pv-key">
          <input
            id="pv-key"
            type="password"
            autocomplete="off"
            placeholder="Paste the new key"
            value={key()}
            onInput={(e) => setKey(e.currentTarget.value)}
          />
        </Field>
        <div class="card-foot-actions">
          <Button disabled={store.busy() || key().trim().length < 8} onClick={() => void replace()}>Replace key</Button>
          <Show when={props.provider.keySource === 'stored'}>
            <Button variant="secondary" disabled={store.busy()} onClick={() => void clearProviderKey(props.provider.provider)}>
              Remove the stored key
            </Button>
          </Show>
        </div>
      </div>

      <div class="card form-card">
        <h3>Alerts and new models</h3>
        <Field
          label="Warn when credit falls below (USD)"
          for="pv-level"
          error={parsedLevel().error}
        >
          <input
            id="pv-level"
            type="text"
            inputmode="decimal"
            placeholder="no alert"
            disabled={!props.provider.supportsBalance}
            value={level()}
            onInput={(e) => setLevel(e.currentTarget.value)}
          />
        </Field>
        <label class="checkbox-row">
          <input type="checkbox" checked={startStopped()} onChange={(e) => setStartStopped(e.currentTarget.checked)} />
          <span>Models added from the catalogue start stopped</span>
        </label>
        
        <div class="card-foot-actions">
          <Button
            disabled={store.busy() || !!parsedLevel().error}
            onClick={() => void saveProviderSettings(props.provider.provider, parsedLevel().value, startStopped())}
          >
            Save settings
          </Button>
        </div>
      </div>
    </>
  );
}
