import { For, Show, createMemo, createSignal, onMount } from 'solid-js';
import { useNavigate } from '@solidjs/router';
import { Button, Drawer, Field, Select } from '../../components/primitives';
import { IconChevronRight, IconKey, IconPlus, IconRefresh, IconSearch, IconUsers } from '../../components/icons';
import { formatNum } from '../../domain/format';
import { effectiveTenantTier, limitedKeyCount, tenantUsageRow } from '../../domain/tenantLimits';
import { useKeysStore } from '../../stores/keys';
import {
  activateRateLimitsPage,
  loadRateLimitUsage,
  useRateLimitActivityStore,
  useRateLimitsStore,
} from '../../stores/rateLimits';
import {
  EMPTY_TENANT_DRAFT,
  createTenant,
  useTenantsStore,
  validateTenantSlug,
  type Tenant,
  type TenantDraft,
} from '../../stores/tenants';
import { TenantLimitFigures, tierSourceLabel } from './TenantLimitFigures';
import { TenantModelAccess } from './TenantModelAccess';
import '../../styles/tenants.css';

export default function TenantsPage() {
  const store = useTenantsStore();
  const keys = useKeysStore();
  const limits = useRateLimitsStore();
  const activity = useRateLimitActivityStore();
  const navigate = useNavigate();
  const [filter, setFilter] = createSignal('');
  const [createOpen, setCreateOpen] = createSignal(false);
  const [draft, setDraft] = createSignal<TenantDraft>({ ...EMPTY_TENANT_DRAFT });
  const [draftError, setDraftError] = createSignal('');
  const [busy, setBusy] = createSignal(false);

  const loadAll = (force = false) => {
    void store.load({ force });
    void keys.load({ force });
    // Also starts the usage report the traffic column reads.
    activateRateLimitsPage();
    void limits.load({ force });
    if (force) void loadRateLimitUsage();
  };

  onMount(() => loadAll());

  const rows = createMemo(() => {
    const q = filter().trim().toLowerCase();
    const list = store.tenants().filter(
      (t) => !q || [t.name, t.slug, t.planSlug, t.costCenter].join(' ').toLowerCase().includes(q),
    );
    // The operator tenant first, then by name: the order does not jump as numbers refresh.
    return [...list].sort(
      (a, b) => Number(b.isOperatorTenant) - Number(a.isOperatorTenant) || (a.name || a.slug).localeCompare(b.name || b.slug),
    );
  });

  const keyIdsOf = (tenant: Tenant) =>
    keys
      .allRows()
      .filter((row) => row.tenantId === tenant.id)
      .map((row) => String(row.id ?? ''));

  const totals = createMemo(() => {
    const list = store.tenants();
    const report = activity.usageReport();
    return {
      tenants: list.length,
      keys: list.reduce((n, t) => n + t.activeKeyCount, 0),
      requests: report?.totals.requests ?? null,
      rejected: report?.totals.rejected ?? null,
    };
  });

  const planOptions = createMemo(() => [
    { value: '', label: 'Default tier' },
    ...Object.keys(limits.saved()?.plans ?? {}).map((slug) => ({ value: slug, label: slug })),
  ]);

  const openCreate = () => {
    setDraft({ ...EMPTY_TENANT_DRAFT });
    setDraftError('');
    setCreateOpen(true);
  };

  const submitCreate = async () => {
    const error = validateTenantSlug(draft().slug);
    setDraftError(error);
    if (error) return;
    setBusy(true);
    try {
      const created = await createTenant(draft());
      setCreateOpen(false);
      if (created) navigate(`/tenants/${encodeURIComponent(created.id)}`);
    } catch {
      // Reported as a toast by the store; the drawer stays open with what was typed.
    } finally {
      setBusy(false);
    }
  };

  return (
    <section class="page" id="panel-tenants">
      <header class="page-header">
        <div class="titles">
          {null}
          <h2>Tenants</h2>
          {null}
        </div>
        <Show when={store.operator()}>
          <div class="page-actions">
            <Button variant="secondary" onClick={() => loadAll(true)} disabled={store.loading()}>
              <span class="icon"><IconRefresh /></span> Refresh
            </Button>
            <Button onClick={openCreate}>
              <span class="icon"><IconPlus /></span> New tenant
            </Button>
          </div>
        </Show>
      </header>

      <Show when={store.loadError()}>
        <p class="notice error" role="alert">
          {store.loadError()}{' '}
          <button type="button" class="rl-link" onClick={() => loadAll(true)}>Retry</button>
        </p>
      </Show>

      <Show when={store.loading() && !store.loaded()}>
        <p class="loading-hint">Loading tenants…</p>
      </Show>

      {/* A tenant's own admin key cannot list tenants: it manages the one it belongs to. */}
      <Show when={store.operator() === false}>
        <p class="notice">
          This key manages its own tenant. Listing and creating tenants, and moving keys between them, needs an
          operator key.
        </p>
        <TenantModelAccess />
        <div class="toolbar tn-own-links">
          <Button variant="secondary" onClick={() => navigate('/keys')}>
            <span class="icon"><IconKey /></span> This tenant's API keys
          </Button>
        </div>
      </Show>

      <Show when={store.operator()}>
        <dl class="tn-stats card">
          <div class="tn-stat"><dt>Tenants</dt><dd>{formatNum(totals().tenants)}</dd></div>
          <div class="tn-stat"><dt>Active keys</dt><dd>{formatNum(totals().keys)}</dd></div>
          <div class="tn-stat">
            <dt>Requests · last {formatNum(activity.usageReport()?.windowMinutes ?? 60)} min</dt>
            <dd>{totals().requests == null ? '—' : formatNum(totals().requests)}</dd>
          </div>
          <div class="tn-stat">
            <dt>Refused by a limit</dt>
            <dd classList={{ 'is-warn': (totals().rejected ?? 0) > 0 }}>
              {totals().rejected == null ? '—' : formatNum(totals().rejected)}
            </dd>
          </div>
        </dl>

        <div class="toolbar">
          <label class="filter-field">
            <span class="icon"><IconSearch /></span>
            <input
              placeholder="name, slug, plan, cost center…"
              aria-label="Filter tenants"
              value={filter()}
              onInput={(e) => setFilter(e.currentTarget.value)}
            />
          </label>
          <span class="spacer" />
          <span class="hint flush">
            {rows().length === store.tenants().length
              ? `${formatNum(rows().length)} tenants`
              : `${formatNum(rows().length)} of ${formatNum(store.tenants().length)} tenants`}
          </span>
        </div>

        <div class="table-wrap">
          <table class="data-table t-tenants">
            <thead>
              <tr>
                <th class="tn-col-name">Tenant</th>
                <th class="tn-col-keys num">Keys</th>
                <th class="tn-col-limit">Shared limit</th>
                <th class="tn-col-rules num" title="Keys of this tenant that carry a rule of their own.">Keys with own limit</th>
                <th class="tn-col-traffic" title="From the gateway's live usage tracker, which starts over when the gateway restarts.">
                  Traffic · last {formatNum(activity.usageReport()?.windowMinutes ?? 60)} min
                </th>
                <th class="cell-actions"><span class="sr-only">Open</span></th>
              </tr>
            </thead>
            <tbody>
              <For each={rows()}>
                {(tenant) => {
                  const effective = () => {
                    const cfg = limits.saved();
                    return cfg ? effectiveTenantTier(cfg, tenant) : null;
                  };
                  const ownLimits = () => {
                    const cfg = limits.saved();
                    return cfg ? limitedKeyCount(cfg, keyIdsOf(tenant)) : 0;
                  };
                  const usage = () => tenantUsageRow(activity.usageReport()?.byTenant, tenant);
                  const open = () => navigate(`/tenants/${encodeURIComponent(tenant.id)}`);
                  return (
                    <tr class="tn-row" classList={{ off: !tenant.isActive }} onClick={open}>
                      <td class="tn-col-name">
                        <span class="tn-name">
                          {tenant.name || tenant.slug}
                          <Show when={tenant.isOperatorTenant}>
                            <span class="tag accent" title="Admin keys of this tenant manage every tenant.">operator</span>
                          </Show>
                          <Show when={!tenant.isActive}>
                            <span class="tag muted">inactive</span>
                          </Show>
                        </span>
                        <span class="tn-slug">
                          {tenant.slug}
                          <Show when={tenant.costCenter}> · {tenant.costCenter}</Show>
                        </span>
                      </td>
                      <td class="tn-col-keys num">
                        <b>{formatNum(tenant.activeKeyCount)}</b>
                        <Show when={tenant.keyCount !== tenant.activeKeyCount}>
                          <span class="tn-sub"> of {formatNum(tenant.keyCount)}</span>
                        </Show>
                      </td>
                      <td class="tn-col-limit">
                        <Show when={effective()} fallback={<span class="tn-sub">—</span>}>
                          {(e) => (
                            <>
                              <TenantLimitFigures tier={e().tier} />
                              <span class="tn-sub">{tierSourceLabel(e())}</span>
                            </>
                          )}
                        </Show>
                      </td>
                      <td class="tn-col-rules num">{ownLimits() > 0 ? formatNum(ownLimits()) : <span class="tn-sub">none</span>}</td>
                      <td class="tn-col-traffic">
                        <Show when={usage()} fallback={<span class="tn-sub">no traffic</span>}>
                          {(u) => (
                            <>
                              <span>
                                <b>{formatNum(u().requests)}</b> requests
                                <Show when={u().rejected > 0}>
                                  {' · '}
                                  <span class="tn-refused">{formatNum(u().rejected)} refused</span>
                                </Show>
                              </span>
                              <span class="tn-sub">busiest minute {formatNum(u().peakRequestsInOneMinute)}</span>
                            </>
                          )}
                        </Show>
                      </td>
                      <td class="cell-actions">
                        <button
                          type="button"
                          class="icon-btn"
                          aria-label={`Open ${tenant.name || tenant.slug}`}
                          onClick={(e) => {
                            e.stopPropagation();
                            open();
                          }}
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
        <Show when={rows().length === 0 && store.loaded()}>
          <div class="empty-state">
            <span class="empty-state-icon icon"><IconUsers /></span>
            <p>{store.tenants().length === 0 ? 'No tenants yet.' : 'No tenant matches this filter.'}</p>
          </div>
        </Show>
      </Show>

      <Drawer open={createOpen()} title="New tenant" onClose={() => setCreateOpen(false)}>
        <Field
          label="Slug"
          error={draftError()}
        >
          <input
            value={draft().slug}
            placeholder="e.g. demo-app"
            onInput={(e) => {
              setDraft((d) => ({ ...d, slug: e.currentTarget.value.toLowerCase() }));
              setDraftError('');
            }}
          />
        </Field>
        <Field label="Name">
          <input
            value={draft().name}
            placeholder="Shown in lists; the slug when left empty"
            onInput={(e) => setDraft((d) => ({ ...d, name: e.currentTarget.value }))}
          />
        </Field>
        <Field label="Plan">
          <Select options={planOptions()} value={draft().planSlug} onChange={(v) => setDraft((d) => ({ ...d, planSlug: v }))} />
        </Field>
        <Field label="Cost center">
          <input
            value={draft().costCenter}
            placeholder="FinOps grouping (optional)"
            onInput={(e) => setDraft((d) => ({ ...d, costCenter: e.currentTarget.value }))}
          />
        </Field>
        <div class="modal-actions">
          <Button variant="secondary" onClick={() => setCreateOpen(false)}>Cancel</Button>
          <Button onClick={() => void submitCreate()} disabled={busy() || !draft().slug.trim()}>
            {busy() ? 'Creating…' : 'Create tenant'}
          </Button>
        </div>
      </Drawer>
    </section>
  );
}
