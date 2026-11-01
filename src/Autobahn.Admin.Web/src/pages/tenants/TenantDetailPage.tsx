import { For, Show, createEffect, createMemo, createSignal, onMount } from 'solid-js';
import { A, useNavigate, useParams, useSearchParams } from '@solidjs/router';
import { Button, Dialog, Drawer, Field, Select, Tabs } from '../../components/primitives';
import {
  IconArrowLeft,
  IconBarChart,
  IconCpu,
  IconEdit,
  IconGauge,
  IconKey,
  IconPlus,
  IconRefresh,
  IconSearch,
} from '../../components/icons';
import { canGrantKey } from '../../domain/keyGrants';
import { formatNum, formatTime } from '../../domain/format';
import {
  effectiveTenantTier,
  ruleAddress,
  ruleSubject,
  tenantRules,
  tenantUsageRow,
} from '../../domain/tenantLimits';
import { openKeyAccessDrawer } from '../../stores/keyGrants';
import { KeyModelAccessDrawer } from '../../components/KeyModelAccessDrawer';
import { DEFAULT_NEW_KEY, createKey, useKeysStore, type NewKeyDraft } from '../../stores/keys';
import { useModelOptions } from '../../stores/modelOptions';
import {
  activateRateLimitsPage,
  loadRateLimitUsage,
  rateLimitRuleIdentity,
  useRateLimitActivityStore,
  useRateLimitDrawersStore,
  useRateLimitsStore,
} from '../../stores/rateLimits';
import { moveKeysToTenant, updateTenant, useTenantsStore } from '../../stores/tenants';
import { RateLimitRuleDrawer } from '../ratelimits/RateLimitRuleDrawer';
import { RateLimitRulesTable } from '../ratelimits/RateLimitRulesTable';
import { RateLimitSaveBar } from '../ratelimits/RateLimitSaveBar';
import { TenantLimitFigures, tierSourceLabel } from './TenantLimitFigures';
import { TenantModelAccess } from './TenantModelAccess';
import '../../styles/tenants.css';

const ROLES = [
  { value: 'Inference', label: 'Inference' },
  { value: 'Admin', label: 'Admin' },
];

const KEY_FILTERS = [
  { value: 'active', label: 'Active' },
  { value: 'all', label: 'All' },
];

type DetailTab = 'keys' | 'limits' | 'access';

export default function TenantDetailPage() {
  const params = useParams();
  const [search, setSearch] = useSearchParams();
  const navigate = useNavigate();
  const store = useTenantsStore();
  const keys = useKeysStore();
  const limits = useRateLimitsStore();
  const drawers = useRateLimitDrawersStore();
  const activity = useRateLimitActivityStore();
  const models = useModelOptions();

  const [keyFilter, setKeyFilter] = createSignal('');
  const [keyStatus, setKeyStatus] = createSignal('active');
  const [selected, setSelected] = createSignal<Set<string>>(new Set());
  const [moveOpen, setMoveOpen] = createSignal(false);
  const [moveTarget, setMoveTarget] = createSignal('');
  const [editOpen, setEditOpen] = createSignal(false);
  const [editDraft, setEditDraft] = createSignal({ name: '', planSlug: '', costCenter: '' });
  const [createOpen, setCreateOpen] = createSignal(false);
  const [keyDraft, setKeyDraft] = createSignal<NewKeyDraft>({ ...DEFAULT_NEW_KEY });
  const [createdSecret, setCreatedSecret] = createSignal('');
  const [createdAck, setCreatedAck] = createSignal(false);
  const [limitSubject, setLimitSubject] = createSignal('');
  const [limitModel, setLimitModel] = createSignal('');
  const [busy, setBusy] = createSignal(false);

  const tenant = createMemo(() => store.byId(String(params.id ?? '')));

  // Declared before the memos that sort and map with them: a memo runs as it is created.
  const keyId = (row: Record<string, unknown>) => String(row.id ?? '');
  const keyName = (row: Record<string, unknown>) => String(row.label || row.keyPrefix || row.id || '');

  const loadAll = (force = false) => {
    void store.load({ force });
    void keys.load({ force });
    activateRateLimitsPage();
    void limits.load({ force });
    void models.load();
    if (force) void loadRateLimitUsage();
  };

  onMount(() => loadAll());

  // Another tenant: what was ticked or typed belonged to the one before.
  createEffect(() => {
    void params.id;
    setSelected(new Set<string>());
    setKeyFilter('');
    setLimitSubject('');
    setLimitModel('');
  });

  const startTab: DetailTab = search.tab === 'limits' || search.tab === 'access' ? search.tab : 'keys';

  // A gateway with a single tenant lists its keys without naming the tenant on each row.
  const tenantKeys = createMemo(() => {
    const t = tenant();
    if (!t) return [];
    const single = keys.tenants().length === 0;
    return keys.allRows().filter((row) => (single ? true : row.tenantId === t.id));
  });

  const visibleKeys = createMemo(() => {
    const q = keyFilter().trim().toLowerCase();
    return tenantKeys()
      .filter((row) => keyStatus() === 'all' || row.active === true)
      .filter(
        (row) =>
          !q ||
          [row.label, row.keyPrefix, row.assignee, row.costCenter, row.role].join(' ').toLowerCase().includes(q),
      )
      .sort((a, b) => Number(b.active === true) - Number(a.active === true) || keyName(a).localeCompare(keyName(b)));
  });

  const effective = createMemo(() => {
    const cfg = limits.draft();
    const t = tenant();
    return cfg && t ? effectiveTenantTier(cfg, t) : null;
  });

  const rules = createMemo(() => {
    const cfg = limits.draft();
    const t = tenant();
    return cfg && t ? tenantRules(cfg, t, tenantKeys().map(keyId)) : [];
  });

  /** The key's own all-models rule, when it has one. */
  const keyRule = (id: string) =>
    rules().find((r) => r.scope.toLowerCase() === 'api_key' && ruleSubject(r).toLowerCase() === id.toLowerCase()) ?? null;
  const keyModelRuleCount = (id: string) =>
    rules().filter((r) => r.scope.toLowerCase() === 'api_key_model' && ruleSubject(r).toLowerCase() === id.toLowerCase()).length;

  const usage = () => {
    const t = tenant();
    return t ? tenantUsageRow(activity.usageReport()?.byTenant, t) : null;
  };

  /** Opens the rule for this subject and model, staging a new one first when there is none. */
  const openLimit = (subject: { kind: 'tenant'; slug: string } | { kind: 'key'; keyId: string }, modelId = '') => {
    const cfg = limits.draft();
    if (!cfg) return;
    const address = ruleAddress(subject, modelId);
    const t = tenant();
    // A tenant rule written against the tenant's id is the same rule as one against its slug.
    const existing = cfg.rules.find((r) => {
      if (r.scope.toLowerCase() !== address.scope) return false;
      if (subject.kind === 'key' || !t) return rateLimitRuleIdentity(r.scope, r.target) === rateLimitRuleIdentity(address.scope, address.target);
      const [who, model = ''] = r.target.split('|');
      return (who.toLowerCase() === t.id.toLowerCase() || who.toLowerCase() === t.slug.toLowerCase()) && model.toLowerCase() === modelId.trim().toLowerCase();
    });
    if (!existing) {
      if (!limits.editable()) return;
      // A tenant rule with rpm 0 keeps the plan's rate, so a new one starts from the rate in force.
      const rpm = subject.kind === 'tenant' && !modelId ? (effective()?.tier.rpm ?? 60) : 60;
      const burst = subject.kind === 'tenant' && !modelId ? (effective()?.tier.burst ?? 0) : 0;
      limits.addRule({ ...address, rpm, burst });
    }
    const target = existing ?? address;
    drawers.openRuleDrawer(rateLimitRuleIdentity(target.scope, target.target));
  };

  const addLimit = () => {
    const t = tenant();
    if (!t) return;
    const subject = limitSubject();
    openLimit(subject ? { kind: 'key', keyId: subject } : { kind: 'tenant', slug: t.slug }, limitModel());
    setLimitSubject('');
    setLimitModel('');
  };

  const subjectOptions = createMemo(() => [
    { value: '', label: 'The whole tenant' },
    ...tenantKeys()
      .filter((row) => row.active === true)
      .map((row) => ({ value: keyId(row), label: `Key: ${keyName(row)}` }))
      .sort((a, b) => a.label.localeCompare(b.label)),
  ]);

  const modelOptions = createMemo(() => [
    { value: '', label: 'All models' },
    ...models.modelIds().map((id) => ({ value: id, label: id })),
  ]);

  const planOptions = createMemo(() => {
    const plans = Object.keys(limits.saved()?.plans ?? {});
    const current = editDraft().planSlug;
    return [
      { value: '', label: 'Default tier' },
      ...(current && !plans.includes(current) ? [{ value: current, label: `${current} (not configured)` }] : []),
      ...plans.map((slug) => ({ value: slug, label: slug })),
    ];
  });

  const moveOptions = createMemo(() => [
    { value: '', label: 'Choose a tenant…' },
    ...store
      .tenants()
      .filter((t) => t.id !== tenant()?.id && t.isActive)
      .map((t) => ({ value: t.id, label: t.name && t.name !== t.slug ? `${t.name} (${t.slug})` : t.slug })),
  ]);

  const toggleSelected = (id: string) =>
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  const openEdit = () => {
    const t = tenant();
    if (!t) return;
    setEditDraft({ name: t.name, planSlug: t.planSlug, costCenter: t.costCenter });
    setEditOpen(true);
  };

  const submitEdit = async () => {
    const t = tenant();
    if (!t) return;
    setBusy(true);
    try {
      await updateTenant(t.id, editDraft());
      setEditOpen(false);
    } catch {
      // Reported as a toast by the store; the drawer keeps what was typed.
    } finally {
      setBusy(false);
    }
  };

  const confirmMove = async () => {
    if (!moveTarget() || selected().size === 0) return;
    setBusy(true);
    try {
      await moveKeysToTenant(moveTarget(), [...selected()]);
      setSelected(new Set<string>());
      setMoveOpen(false);
      setMoveTarget('');
    } catch {
      // Reported as a toast by the store.
    } finally {
      setBusy(false);
    }
  };

  const openCreateKey = () => {
    setKeyDraft({ ...DEFAULT_NEW_KEY, tenantId: keys.tenants().length ? (tenant()?.id ?? '') : '' });
    setCreatedSecret('');
    setCreatedAck(false);
    setCreateOpen(true);
  };

  const closeCreateKey = () => {
    if (createdSecret() && !createdAck()) return;
    setCreateOpen(false);
  };

  const submitCreateKey = async () => {
    setBusy(true);
    try {
      setCreatedSecret(await createKey(keyDraft()));
      void store.load({ force: true });
    } catch {
      // Reported as a toast by the store.
    } finally {
      setBusy(false);
    }
  };

  return (
    <section class="page" id="panel-tenant">
      <A class="tn-back" href="/tenants">
        <span class="icon"><IconArrowLeft /></span> Tenants
      </A>

      <Show when={store.loadError()}>
        <p class="notice error" role="alert">{store.loadError()}</p>
      </Show>
      <Show when={store.loading() && !store.loaded()}>
        <p class="loading-hint">Loading tenant…</p>
      </Show>
      <Show when={store.loaded() && !tenant()}>
        <p class="notice warn">
          {store.operator() === false
            ? 'Opening a tenant needs an operator key.'
            : 'There is no tenant with this id. It may have been opened from an old link.'}
        </p>
      </Show>

      <Show when={tenant()}>
        {(t) => (
          <>
            <header class="page-header">
              <div class="titles">
                
                <h2 class="tn-title">
                  {t().name || t().slug}
                  <Show when={t().isOperatorTenant}>
                    <span class="tag accent" title="Admin keys of this tenant manage every tenant.">operator</span>
                  </Show>
                  <Show when={!t().isActive}>
                    <span class="tag muted">inactive</span>
                  </Show>
                </h2>
                <p class="page-sub tn-meta">
                  <code>{t().slug}</code>
                  <Show when={t().costCenter}><span>cost center {t().costCenter}</span></Show>
                  <span>created {formatTime(t().createdAt)}</span>
                </p>
              </div>
              <div class="page-actions">
                <Button variant="secondary" onClick={() => loadAll(true)} disabled={store.loading()}>
                  <span class="icon"><IconRefresh /></span> Refresh
                </Button>
                <Button variant="secondary" onClick={openEdit}>
                  <span class="icon"><IconEdit /></span> Edit tenant
                </Button>
              </div>
            </header>

            <dl class="tn-stats tn-stats-lead card">
              <div class="tn-stat">
                <dt>Shared limit · all keys together</dt>
                <dd>
                  <Show when={effective()} fallback="—">
                    {(e) => <TenantLimitFigures tier={e().tier} />}
                  </Show>
                </dd>
                <Show when={effective()}>
                  {(e) => <span class="tn-sub">from {tierSourceLabel(e())}</span>}
                </Show>
              </div>
              <div class="tn-stat">
                <dt>Active keys</dt>
                <dd>{formatNum(t().activeKeyCount)}</dd>
                <Show when={t().keyCount !== t().activeKeyCount}>
                  <span class="tn-sub">{formatNum(t().keyCount)} counting revoked ones</span>
                </Show>
              </div>
              <div class="tn-stat">
                <dt>Requests · last {formatNum(activity.usageReport()?.windowMinutes ?? 60)} min</dt>
                <dd>{usage() ? formatNum(usage()!.requests) : activity.usageReport() ? '0' : '—'}</dd>
                <Show when={usage()}>
                  <span class="tn-sub">busiest minute {formatNum(usage()!.peakRequestsInOneMinute)}</span>
                </Show>
              </div>
              <div class="tn-stat">
                <dt>Refused by a limit</dt>
                <dd classList={{ 'is-warn': (usage()?.rejected ?? 0) > 0 }}>
                  {usage() ? formatNum(usage()!.rejected) : activity.usageReport() ? '0' : '—'}
                </dd>
              </div>
            </dl>

            <Tabs
              class="tn-tabs"
              // Prefixed: the rail's own tabs already use the ids `tab-keys` and `tab-ratelimits`.
              defaultId={`tenant-${startTab}`}
              onChange={(id) => {
                const tab = id.replace('tenant-', '');
                setSearch({ tab: tab === 'keys' ? undefined : tab }, { replace: true });
              }}
              tabs={[
                {
                  id: 'tenant-keys',
                  label: `Keys (${formatNum(t().activeKeyCount)})`,
                  content: () => (
                    <>
                      <div class="toolbar">
                        <label class="field-inline" for="tn-key-status">Show</label>
                        <Select
                          id="tn-key-status"
                          class="inline-select"
                          options={KEY_FILTERS}
                          value={keyStatus()}
                          onChange={setKeyStatus}
                        />
                        <label class="filter-field">
                          <span class="icon"><IconSearch /></span>
                          <input
                            placeholder="label, prefix, assignee…"
                            aria-label="Filter this tenant's keys"
                            value={keyFilter()}
                            onInput={(e) => setKeyFilter(e.currentTarget.value)}
                          />
                        </label>
                        <span class="spacer" />
                        <Button
                          variant="secondary"
                          onClick={() => setMoveOpen(true)}
                          disabled={selected().size === 0 || moveOptions().length < 2}
                        >
                          Move to another tenant ({selected().size})
                        </Button>
                        <Button onClick={openCreateKey}>
                          <span class="icon"><IconPlus /></span> Create key
                        </Button>
                      </div>

                      <div class="table-wrap">
                        <table class="data-table t-tenant-keys">
                          <thead>
                            <tr>
                              <th class="cell-select" />
                              <th class="tn-col-key">Key</th>
                              <th class="tn-col-role">Role</th>
                              <th class="tn-col-status">Status</th>
                              <th class="tn-col-own">Own limit</th>
                              <th class="tn-col-used">Last used</th>
                              <th class="num tn-col-mtd">MTD req</th>
                              <th class="cell-actions"><span class="sr-only">Actions</span></th>
                            </tr>
                          </thead>
                          <tbody>
                            <For each={visibleKeys()}>
                              {(row) => {
                                const id = keyId(row);
                                const active = () => row.active === true;
                                const own = () => keyRule(id);
                                const perModel = () => keyModelRuleCount(id);
                                return (
                                  <tr classList={{ off: !active() }}>
                                    <td class="cell-select">
                                      <Show when={active()}>
                                        <input
                                          type="checkbox"
                                          checked={selected().has(id)}
                                          onChange={() => toggleSelected(id)}
                                          aria-label={`Select ${keyName(row)}`}
                                        />
                                      </Show>
                                    </td>
                                    <td class="tn-col-key">
                                      <span class="tn-name">{keyName(row)}</span>
                                      <span class="tn-slug">
                                        <code>{String(row.keyPrefix ?? '')}</code>
                                        <Show when={row.assignee}> · {String(row.assignee)}</Show>
                                      </span>
                                    </td>
                                    <td>
                                      <span class="tag role-tag" classList={{ admin: row.role === 'Admin' }}>{String(row.role ?? '—')}</span>
                                    </td>
                                    <td>
                                      <span class="status-chip" classList={{ ok: active(), fail: row.isRevoked === true && !row.isArchived, muted: row.isArchived === true }}>
                                        {row.isArchived ? 'archived' : row.isRevoked ? 'revoked' : 'active'}
                                      </span>
                                    </td>
                                    <td class="tn-col-own">
                                      <Show
                                        when={own()}
                                        fallback={<span class="tn-sub">{perModel() > 0 ? `${perModel()} per-model rule(s)` : 'shares the tenant limit'}</span>}
                                      >
                                        {(rule) => (
                                          <>
                                            <span class="tn-nums" classList={{ 'tn-rule-off': !rule().enabled }}>
                                              <span><b>{formatNum(rule().rpm)}</b><i>rpm</i></span>
                                              <span>
                                                <b>{rule().maxConcurrentStreams > 0 ? formatNum(rule().maxConcurrentStreams) : '∞'}</b>
                                                <i>streams</i>
                                              </span>
                                              <Show when={(rule().tpm ?? 0) > 0}>
                                                <span><b>{formatNum(rule().tpm ?? 0)}</b><i>tpm</i></span>
                                              </Show>
                                            </span>
                                            <Show when={!rule().enabled || perModel() > 0}>
                                              <span class="tn-sub">
                                                {[!rule().enabled ? 'switched off' : '', perModel() > 0 ? `+ ${perModel()} per-model` : '']
                                                  .filter(Boolean)
                                                  .join(' · ')}
                                              </span>
                                            </Show>
                                          </>
                                        )}
                                      </Show>
                                    </td>
                                    <td class="cell-time">{formatTime(row.lastUsedAt as string | undefined)}</td>
                                    <td class="num">{formatNum(row.mtdRequests as number | undefined)}</td>
                                    <td class="cell-actions">
                                      <span class="row-actions">
                                        <Show when={active()}>
                                          <button
                                            type="button"
                                            class="icon-btn"
                                            title={own() ? 'Open this key’s limit' : 'Limit this key…'}
                                            aria-label="Limit this key"
                                            disabled={!own() && !limits.editable()}
                                            onClick={() => openLimit({ kind: 'key', keyId: id })}
                                          >
                                            <IconGauge />
                                          </button>
                                          <Show when={canGrantKey(row)}>
                                            <button type="button" class="icon-btn" title="Model access" aria-label="Model access" onClick={() => void openKeyAccessDrawer(row)}>
                                              <IconCpu />
                                            </button>
                                          </Show>
                                        </Show>
                                        <button type="button" class="icon-btn" title="View usage" aria-label="View usage" onClick={() => navigate(`/usage?key=${encodeURIComponent(id)}`)}>
                                          <IconBarChart />
                                        </button>
                                      </span>
                                    </td>
                                  </tr>
                                );
                              }}
                            </For>
                          </tbody>
                        </table>
                      </div>
                      <Show when={visibleKeys().length === 0}>
                        <div class="empty-state">
                          <span class="empty-state-icon icon"><IconKey /></span>
                          <p>{tenantKeys().length === 0 ? 'This tenant has no keys yet.' : 'No key matches these filters.'}</p>
                        </div>
                      </Show>
                      
                    </>
                  ),
                },
                {
                  id: 'tenant-limits',
                  label: `Limits (${formatNum(rules().length)})`,
                  content: () => (
                    <div class="rl-page tn-limits">
                      <Show when={limits.loadError()}>
                        <p class="notice error" role="alert">{limits.loadError()}</p>
                      </Show>
                      <Show when={limits.readOnlyReason()}>
                        <p class="notice warn" role="status">
                          <span class="tag muted">read-only</span> {limits.readOnlyReason()}
                        </p>
                      </Show>
                      <Show when={limits.draft() && !limits.draft()!.enabled}>
                        <p class="notice warn" role="status">
                          Rate limits are switched off for the whole gateway, so nothing below is enforced.{' '}
                          <A class="rl-link" href="/ratelimits">Open Rate limits</A>
                        </p>
                      </Show>

                      <div class="card tn-shared">
                        <div class="tn-shared-main">
                          <span class="eyebrow">Shared limit</span>
                          <Show when={effective()} fallback={<p class="loading-hint">Loading limits…</p>}>
                            {(e) => (
                              <>
                                <TenantLimitFigures tier={e().tier} />
                                
                              </>
                            )}
                          </Show>
                        </div>
                        <div class="tn-shared-actions">
                          <Button variant="secondary" size="sm" onClick={openEdit}>Change plan</Button>
                          <Button
                            variant="secondary"
                            size="sm"
                            disabled={!effective()?.rule && !limits.editable()}
                            onClick={() => openLimit({ kind: 'tenant', slug: t().slug })}
                          >
                            {effective()?.rule ? 'Open tenant rule' : 'Give this tenant its own limit'}
                          </Button>
                        </div>
                      </div>

                      <div class="rl-section">
                        <div class="rl-section-head">
                          <div>
                            <h3>Rules for this tenant and its keys</h3>
                            
                          </div>
                        </div>

                        <Show when={limits.editable()}>
                          <div class="card rl-new-rule">
                            <h4 class="rl-h4">Add a limit</h4>
                            <div class="filter-row">
                              <Field label="Applies to">
                                <Select aria-label="Applies to" options={subjectOptions()} value={limitSubject()} onChange={setLimitSubject} />
                              </Field>
                              <Field label="On">
                                <Select aria-label="On model" options={modelOptions()} value={limitModel()} onChange={setLimitModel} />
                              </Field>
                              <div class="field">
                                <label>&nbsp;</label>
                                <Button onClick={addLimit}>
                                  <span class="icon"><IconPlus /></span> Add limit
                                </Button>
                              </div>
                            </div>
                          </div>
                        </Show>

                        <RateLimitRulesTable
                          rules={rules()}
                          empty="No rule names this tenant or its keys, so the shared limit above is all that applies."
                        />
                        
                      </div>
                    </div>
                  ),
                },
                {
                  id: 'tenant-access',
                  label: 'Model access',
                  content: () => (
                    <TenantModelAccess tenantId={keys.tenants().length ? t().id : ''} tenantName={t().name || t().slug} />
                  ),
                },
              ]}
            />

            <RateLimitSaveBar />
          </>
        )}
      </Show>

      <RateLimitRuleDrawer />

      <Drawer open={editOpen()} title="Edit tenant" onClose={() => setEditOpen(false)}>
        <p class="hint">Slug: <code>{tenant()?.slug}</code>. It cannot be changed.</p>
        <Field label="Name">
          <input value={editDraft().name} onInput={(e) => setEditDraft((d) => ({ ...d, name: e.currentTarget.value }))} />
        </Field>
        <Field label="Plan">
          <Select options={planOptions()} value={editDraft().planSlug} onChange={(v) => setEditDraft((d) => ({ ...d, planSlug: v }))} />
        </Field>
        <Field label="Cost center">
          <input
            value={editDraft().costCenter}
            placeholder="FinOps grouping (optional)"
            onInput={(e) => setEditDraft((d) => ({ ...d, costCenter: e.currentTarget.value }))}
          />
        </Field>
        <div class="modal-actions">
          <Button variant="secondary" onClick={() => setEditOpen(false)}>Cancel</Button>
          <Button onClick={() => void submitEdit()} disabled={busy() || !editDraft().name.trim()}>
            {busy() ? 'Saving…' : 'Save'}
          </Button>
        </div>
      </Drawer>

      <Dialog open={moveOpen()} title={`Move ${selected().size} key(s) to another tenant?`} onClose={() => setMoveOpen(false)}>
        <p>
          A moved key starts drawing on the other tenant's shared limit and model access at once. A rule on the key
          itself stays with the key.
        </p>
        <Field label="Move to">
          <Select aria-label="Move to" options={moveOptions()} value={moveTarget()} onChange={setMoveTarget} />
        </Field>
        <div class="modal-actions">
          <Button variant="secondary" onClick={() => setMoveOpen(false)}>Cancel</Button>
          <Button onClick={() => void confirmMove()} disabled={busy() || !moveTarget()}>
            {busy() ? 'Moving…' : 'Move keys'}
          </Button>
        </div>
      </Dialog>

      <Drawer open={createOpen()} title={`Create API key in ${tenant()?.name || tenant()?.slug || 'this tenant'}`} onClose={closeCreateKey}>
        <Show when={!createdSecret()} fallback={
          <div>
            <p class="notice warn"><strong>Copy now — this secret is shown only once.</strong></p>
            <code class="secret-display">{createdSecret()}</code>
            <Button onClick={() => navigator.clipboard?.writeText(createdSecret())}>Copy secret</Button>
            <label class="checkbox-label">
              <input type="checkbox" checked={createdAck()} onChange={(e) => setCreatedAck(e.currentTarget.checked)} />
              I have saved this secret
            </label>
          </div>
        }>
          <Field label="Role">
            <Select options={ROLES} value={keyDraft().role} onChange={(v) => setKeyDraft((d) => ({ ...d, role: v }))} />
          </Field>
          <Field label="Label">
            <input value={keyDraft().label} onInput={(e) => setKeyDraft((d) => ({ ...d, label: e.currentTarget.value }))} placeholder="e.g. prod-chatbot" />
          </Field>
          <Field label="Assignee">
            <input value={keyDraft().assignee} onInput={(e) => setKeyDraft((d) => ({ ...d, assignee: e.currentTarget.value }))} placeholder="Person or team name" />
          </Field>
          <Field label="Cost center">
            <input value={keyDraft().costCenter} onInput={(e) => setKeyDraft((d) => ({ ...d, costCenter: e.currentTarget.value }))} placeholder="FinOps grouping (optional)" />
          </Field>
          <Field label="Description">
            <textarea rows={2} value={keyDraft().description} onInput={(e) => setKeyDraft((d) => ({ ...d, description: e.currentTarget.value }))} placeholder="Optional notes" />
          </Field>
          <Button onClick={() => void submitCreateKey()} disabled={busy()}>Create key</Button>
        </Show>
      </Drawer>

      <KeyModelAccessDrawer />
    </section>
  );
}
