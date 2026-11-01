import { For, Show, createMemo, createSignal, onCleanup, onMount } from 'solid-js';
import { A, useSearchParams } from '@solidjs/router';
import { Button, Field, Select } from '../../components/primitives';
import { IconActivity, IconBarChart, IconClock, IconDollarSign, IconDownload, IconHash, IconKey } from '../../components/icons';
import { formatCost, formatNum, formatSharePct, formatTime } from '../../domain/format';
import { fetchAllKeys } from '../../stores/keys';
import { useModelOptions } from '../../stores/modelOptions';
import { loadProviders, useProvidersStore } from '../../stores/providers';
import { isTenantAdmin } from '../../stores/session';
import {
  activateUsagePage,
  applyHashUsageParams,
  disposeUsagePage,
  usagePresetRange,
  useUsageStore,
} from '../../stores/usage';

export default function UsagePage() {
  const store = useUsageStore();
  const [params] = useSearchParams();
  const [keyOptions, setKeyOptions] = createSignal<{ value: string; label: string }[]>([]);
  const registryModels = useModelOptions();
  const modelOptions = createMemo(() => {
    const ids = new Set([...registryModels.modelIds(), ...store.reportModelIds()]);
    if (store.modelId()) ids.add(store.modelId());
    return [...ids].sort((a, b) => a.localeCompare(b)).map((id) => ({ value: id, label: id }));
  });
  // What was paid to outside providers this month, whatever range the report below is set to.
  // An operator's view: a tenant's own key cannot read the providers.
  const paid = useProvidersStore();
  const paidProviders = createMemo(() => paid.providers().filter((p) => p.requestsThisMonth > 0 || p.openTenantCount > 0));
  const paidTotal = createMemo(() => paidProviders().reduce((sum, p) => sum + p.spentThisMonthUsd, 0));

  onMount(async () => {
    applyHashUsageParams(params);
    activateUsagePage();
    void registryModels.load({ force: true });
    if (!isTenantAdmin()) void loadProviders();
    try {
      // Every tenant's keys for an operator: the report spans all tenants, so its filter must too.
      const { items } = await fetchAllKeys();
      setKeyOptions(
        items.map((k) => {
          const name = String(k.label || k.keyPrefix || k.id || 'key');
          return { value: String(k.id ?? ''), label: k.tenantSlug ? `${name} آ· ${String(k.tenantSlug)}` : name };
        }),
      );
    } catch {
      /* keys optional for filters */
    }
  });
  onCleanup(() => disposeUsagePage());

  const presetActive = (days: number | 'mtd') => {
    const r = usagePresetRange(days);
    return store.from() === r.from && store.to() === r.to;
  };

  const keyShareRows = createMemo(() => {
    const data = store.keyShares();
    const currency = String(data?.currency ?? store.summary()?.currency ?? 'USD');
    return ((data?.keys as Record<string, unknown>[]) ?? []).map((k) => {
      const anonymous = k.apiKeyId == null;
      const share = Math.max(0, Math.min(1, Number(k.requestShare) || 0));
      return {
        key: String(k.apiKeyId ?? 'anonymous'),
        name: anonymous
          ? 'anonymous'
          : String(k.label || k.keyPrefix || `${String(k.apiKeyId).slice(0, 8)}â€¦ (deleted)`),
        assignee: String(k.assignee ?? 'â€”'),
        requestsText: formatNum(k.requests),
        requestShareText: formatSharePct(k.requestShare),
        barStyle: `width:${(share * 100).toFixed(1)}%`,
        tokensText: formatNum(Number(k.promptTokens ?? 0) + Number(k.completionTokens ?? 0)),
        tokenShareText: formatSharePct(k.tokenShare),
        costText: formatCost(k.totalCost, currency),
        costShareText: formatSharePct(k.costShare),
        anonymous,
      };
    });
  });

  const loading = () => store.phase() === 'loading';
  const totals = () => (store.summary()?.summary ?? null) as Record<string, number | undefined> | null;
  const currency = () => String(store.summary()?.currency ?? 'USD');
  const loadedRange = () => {
    const snap = store.loadedSnapshot();
    return snap?.from && snap?.to ? `${snap.from} â†’ ${snap.to} UTC` : 'selected range';
  };

  return (
    <section class="page" id="panel-usage">
      <header class="page-header">
        <div class="titles">
          {null}
          <h2>Usage &amp; cost</h2>
          {null}
        </div>
      </header>

      <Show when={!isTenantAdmin() && paidProviders().length > 0}>
        <p class="notice">
          <strong>Paid providers, this month: {formatCost(paidTotal())}.</strong>{' '}
          <For each={paidProviders()}>
            {(p, i) => (
              <>
                {i() > 0 ? ' آ· ' : ''}
                <A class="rl-link" href={`/providers/${encodeURIComponent(p.provider)}`}>
                  {p.displayName} {formatCost(p.spentThisMonthUsd)}
                </A>
              </>
            )}
          </For>
          . Included in the totals below; each provider's page splits it by tenant, key and model.
        </p>
      </Show>

      <div class="chip-row" role="group" aria-label="Date presets">
        <button type="button" class={presetActive(7) ? 'preset active' : 'preset'} onClick={() => store.setPreset(7)}>Last 7 days</button>
        <button type="button" class={presetActive(30) ? 'preset active' : 'preset'} onClick={() => store.setPreset(30)}>Last 30 days</button>
        <button type="button" class={presetActive('mtd') ? 'preset active' : 'preset'} onClick={() => store.setPreset('mtd')}>Month to date</button>
      </div>

      <div class="card form-card">
        <div class="filter-row grid">
          <Field label="From">
            <input type="date" value={store.from()} onInput={(e) => store.setFrom(e.currentTarget.value)} />
          </Field>
          <Field label="To">
            <input type="date" value={store.to()} onInput={(e) => store.setTo(e.currentTarget.value)} />
          </Field>
          <Field label="Cost center">
            <input
              type="search"
              placeholder="Exact name or (none)"
              value={store.costCenter()}
              onInput={(e) => store.setCostCenter(e.currentTarget.value)}
            />
          </Field>
          <Field label="API key">
            <Select
              options={[{ value: '', label: 'All keys' }, ...keyOptions()]}
              value={store.apiKeyId()}
              onChange={(v) => store.setApiKeyId(v)}
            />
          </Field>
          <Field label="Model">
            <Select
              options={[{ value: '', label: 'All models' }, ...modelOptions()]}
              value={store.modelId()}
              onChange={(v) => store.setModelId(v)}
            />
          </Field>
        </div>
        <Show when={store.rangeError()}>
          <p class="field-error" role="alert">{store.rangeError()}</p>
        </Show>
        <label class="checkbox-label">
          <input
            type="checkbox"
            checked={store.includeAnonymous()}
            onChange={(e) => store.setIncludeAnonymous(e.currentTarget.checked)}
          />
          Include anonymous usage
        </label>
        <div class="toolbar mt-3">
          <Button onClick={() => store.loadReport()} disabled={!!store.rangeError()}>Apply</Button>
          <span class="spacer" />
          <span class="export-group" role="group" aria-label="Export daily rollups">
            <span class="export-label">Rollups</span>
            <Button variant="secondary" onClick={() => void store.exportDataset('rollups', 'json')}><span class="icon"><IconDownload /></span> JSON</Button>
            <Button variant="secondary" onClick={() => void store.exportDataset('rollups', 'csv')}><span class="icon"><IconDownload /></span> CSV</Button>
          </span>
          <span class="export-group" role="group" aria-label="Export billing events">
            <span class="export-label">Events</span>
            <Button variant="secondary" onClick={() => void store.exportDataset('events', 'json')}><span class="icon"><IconDownload /></span> JSON</Button>
            <Button variant="secondary" onClick={() => void store.exportDataset('events', 'csv')}><span class="icon"><IconDownload /></span> CSV</Button>
          </span>
        </div>
      </div>

      <Show when={totals()}>
        {(t) => (
          <div class="vitals-grid mt-4">
            <article class="vital">
              <div class="vital-head"><span class="vital-label">Prompt tokens</span><span class="icon"><IconHash /></span></div>
              <strong class="vital-value">{formatNum(t().totalPromptTokens ?? 0)}</strong>
              <div class="vital-foot"><span>{formatNum(t().totalPromptTokens ?? 0)} total</span></div>
            </article>
            <article class="vital">
              <div class="vital-head"><span class="vital-label">Completion tokens</span><span class="icon"><IconHash /></span></div>
              <strong class="vital-value">{formatNum(t().totalCompletionTokens ?? 0)}</strong>
              <div class="vital-foot"><span>{formatNum(t().totalCompletionTokens ?? 0)} total</span></div>
            </article>
            <article class="vital accent-live">
              <div class="vital-head"><span class="vital-label">Cost</span><span class="icon"><IconDollarSign /></span></div>
              <strong class="vital-value">{formatCost(t().totalCost ?? 0, currency())}</strong>
              <div class="vital-foot"><span>{loadedRange()}</span></div>
            </article>
            <article class="vital">
              <div class="vital-head"><span class="vital-label">Requests</span><span class="icon"><IconActivity /></span></div>
              <strong class="vital-value">{formatNum(t().totalRequests ?? 0)}</strong>
              <div class="vital-foot">
                <span>
                  recorded inference calls
                  <Show when={Number(t().anonymousRequests ?? 0) > 0}> آ· {formatNum(t().anonymousRequests)} anonymous</Show>
                </span>
              </div>
            </article>
          </div>
        )}
      </Show>

      <div class="section-header">
        <div>
          <span class="eyebrow">Contribution</span>
          <h3>{store.modelId() ? `Load on ${store.modelId()} by API key` : 'Load by API key (all models)'}</h3>
        </div>
      </div>
      <Show when={keyShareRows().length > 0}>
        <div class="table-wrap">
          <table class="data-table t-usage-keys">
            <thead>
              <tr>
                <th>Key</th>
                <th>Assignee</th>
                <th>Share of requests</th>
                <th class="num">Requests</th>
                <th class="num">Tokens (share)</th>
                <th class="num">Cost (share)</th>
              </tr>
            </thead>
            <tbody>
              <For each={keyShareRows()}>
                {(row) => (
                  <tr>
                    <td><span classList={{ tag: row.anonymous, muted: row.anonymous }}>{row.name}</span></td>
                    <td>{row.assignee}</td>
                    <td>
                      <div class="share-cell">
                        <span class="load-track"><span class="load-fill" style={row.barStyle} /></span>
                        <span class="share-pct">{row.requestShareText}</span>
                      </div>
                    </td>
                    <td class="num">{row.requestsText}</td>
                    <td class="num">{row.tokensText} <span class="muted">{row.tokenShareText}</span></td>
                    <td class="num">{row.costText} <span class="muted">{row.costShareText}</span></td>
                  </tr>
                )}
              </For>
            </tbody>
          </table>
        </div>
      </Show>
      <Show when={keyShareRows().length === 0 && !loading()}>
        <div class="empty-state">
          <span class="empty-state-icon icon"><IconKey /></span>
          <p>No keyed usage matches these filters.</p>
        </div>
      </Show>

      <div class="section-header">
        <div><span class="eyebrow">Detail</span><h3>Daily rollups</h3></div>
        <Show when={store.rollup().length > 0}>
          <span class="hint flush">{formatNum(store.rollup().length)} {store.rollup().length === 1 ? 'row' : 'rows'}</span>
        </Show>
      </div>
      <Show when={store.rollup().length > 0}>
        <div class="table-wrap">
          <table class="data-table t-usage-rollups">
            <colgroup>
              <col /><col /><col />
              <col /><col /><col /><col />
            </colgroup>
            <thead>
              <tr>
                <th>Date (UTC)</th>
                <th>Model</th>
                <th>Cost center</th>
                <th class="num">Prompt</th>
                <th class="num">Completion</th>
                <th class="num">Cost</th>
                <th class="num">Req</th>
              </tr>
            </thead>
            <tbody>
              <For each={store.rollup()}>
                {(row) => (
                  <tr>
                    <td>{String(row.usageDate ?? row.date ?? 'â€”')}</td>
                    <td>{String(row.modelId ?? row.model ?? 'â€”')}</td>
                    <td>{String(row.costCenter ?? 'â€”')}</td>
                    <td class="num">{formatNum(row.promptTokens)}</td>
                    <td class="num">{formatNum(row.completionTokens)}</td>
                    <td class="num">{formatCost(row.totalCost ?? row.cost, row.currency as string | undefined)}</td>
                    <td class="num">{formatNum(row.requestCount ?? row.count)}</td>
                  </tr>
                )}
              </For>
            </tbody>
          </table>
        </div>
      </Show>
      <Show when={store.rollup().length === 0 && !loading()}>
        <div class="empty-state">
          <span class="empty-state-icon icon"><IconBarChart /></span>
          <p>No usage in this date range.</p>
        </div>
      </Show>

      <div class="section-header">
        <div><span class="eyebrow">Ledger</span><h3>Recent billing events</h3></div>
      </div>
      <Show when={store.events().length > 0}>
        <div class="table-wrap">
          <table class="data-table t-usage-events">
            <colgroup>
              <col /><col /><col /><col />
              <col /><col /><col />
            </colgroup>
            <thead>
              <tr><th>Time</th><th>Key</th><th>Assignee</th><th>Model</th><th class="num">Prompt</th><th class="num">Completion</th><th class="num">Cost</th></tr>
            </thead>
            <tbody>
              <For each={store.events()}>
                {(row) => {
                  const anonymous = row.apiKeyId == null;
                  return (
                    <tr>
                      <td class="nowrap">{formatTime(row.recordedAt ?? row.timestampUtc ?? row.time)}</td>
                      <td>
                        <span classList={{ tag: true, muted: anonymous }} title={anonymous ? 'No API key â€” public-model request' : String(row.apiKeyId)}>
                          {anonymous ? 'anonymous' : String(row.keyPrefix ?? `${String(row.apiKeyId).slice(0, 8)}â€¦`)}
                        </span>
                      </td>
                      <td>{String(row.assignee ?? 'â€”')}</td>
                      <td>{String(row.modelId ?? 'â€”')}</td>
                      <td class="num">{formatNum(row.promptTokens ?? 'â€”')}</td>
                      <td class="num">{formatNum(row.completionTokens ?? 'â€”')}</td>
                      <td
                        classList={{ num: true, muted: row.totalCost == null && row.cost == null }}
                        title={row.totalCost == null && row.cost == null ? 'Unpriced â€” no rate card for this model when the request was recorded' : ''}
                      >
                        {formatCost(row.totalCost ?? row.cost, row.currency as string | undefined)}
                      </td>
                    </tr>
                  );
                }}
              </For>
            </tbody>
          </table>
        </div>
        <Show when={store.eventsHasMore()}>
          <div class="toolbar mt-2">
            <Button variant="secondary" onClick={() => store.loadMoreEvents()}>Load more events</Button>
          </div>
        </Show>
      </Show>
      <Show when={store.events().length === 0 && !loading()}>
        <div class="empty-state">
          <span class="empty-state-icon icon"><IconClock /></span>
          <p>No billing events in this range.</p>
        </div>
      </Show>
    </section>
  );
}
