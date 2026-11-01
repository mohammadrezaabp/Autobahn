import { For, Show, createEffect, createMemo, createSignal, onCleanup, onMount } from 'solid-js';
import { Button, Dialog, Field, Select } from '../../components/primitives';
import { IconChevronRight, IconFileText, IconPlus, IconRefresh, IconSearch } from '../../components/icons';
import {
  fetchRateLimitHelp,
  type RateLimitHelpLang,
} from '../../domain/rateLimitHelp';
import { scopeInfo } from '../../domain/rateLimitEdit';
import { formatNum } from '../../domain/format';
import { ruleModel } from '../../domain/tenantLimits';
import { useKeysStore } from '../../stores/keys';
import { useModelOptions } from '../../stores/modelOptions';
import {
  RATE_LIMIT_SCOPES,
  activateRateLimitsPage,
  disposeRateLimitsPage,
  rateLimitRuleIdentity,
  useAddRuleIntent,
  useRateLimitDrawersStore,
  useRateLimitsStore,
} from '../../stores/rateLimits';
import { useTenantsStore } from '../../stores/tenants';
import { RateLimitActivitySection } from './RateLimitActivitySection';
import { RateLimitRuleDrawer } from './RateLimitRuleDrawer';
import { RateLimitRulesTable, useRuleSubjectLabel } from './RateLimitRulesTable';
import { RateLimitSaveBar } from './RateLimitSaveBar';
import { RateLimitTierDrawer } from './RateLimitTierDrawer';

export default function RateLimitsPage() {
  const store = useRateLimitsStore();
  const drawers = useRateLimitDrawersStore();
  const intent = useAddRuleIntent();
  const keys = useKeysStore();
  const tenants = useTenantsStore();
  const models = useModelOptions();
  const subjectLabel = useRuleSubjectLabel();
  const [helpOpen, setHelpOpen] = createSignal(false);
  const [helpContent, setHelpContent] = createSignal<RateLimitHelpLang | null>(null);
  const [helpLang, setHelpLang] = createSignal<'en' | 'fa'>('en');
  const [helpLoading, setHelpLoading] = createSignal(false);
  const [newScope, setNewScope] = createSignal('model');
  const [newSubject, setNewSubject] = createSignal('');
  const [newModel, setNewModel] = createSignal('');
  const [newRpm, setNewRpm] = createSignal('60');
  const [newBurst, setNewBurst] = createSignal('0');
  const [ruleFilter, setRuleFilter] = createSignal('');

  const onBeforeUnload = (e: BeforeUnloadEvent) => {
    if (!store.workInProgress()) return;
    e.preventDefault();
    e.returnValue = 'Rate-limit changes have not been saved yet.';
  };

  onMount(() => {
    window.addEventListener('beforeunload', onBeforeUnload);
    activateRateLimitsPage();
    void store.load();
    // Names for the rules' targets, and the lists the new-rule form picks from.
    void keys.load();
    void tenants.load();
    void models.load();
    const pending = intent.addRuleIntent();
    if (pending) {
      const [subject, model = ''] = pending.target.split('|');
      setNewScope(pending.scope);
      setNewSubject(subject);
      setNewModel(model);
      intent.setAddRuleIntent(null);
    }
  });

  onCleanup(() => {
    window.removeEventListener('beforeunload', onBeforeUnload);
    disposeRateLimitsPage();
  });

  createEffect(() => {
    if (!helpOpen()) return;
    const lang = helpLang();
    setHelpLoading(true);
    void fetchRateLimitHelp(lang)
      .then((content) => setHelpContent(content))
      .finally(() => setHelpLoading(false));
  });

  const readNumber = (value: string, fallback = 0) => {
    const n = Number(value);
    return Number.isFinite(n) ? n : fallback;
  };

  // What the chosen scope asks for: a tenant or a key, a model, both, or nothing at all.
  const scopeKind = () => {
    const info = scopeInfo(newScope());
    return {
      singleton: info.singleton === true,
      subject: info.id.startsWith('tenant') ? ('tenant' as const) : info.id.startsWith('api_key') ? ('key' as const) : null,
      model: info.id === 'model' || info.id.endsWith('_model'),
    };
  };

  const subjectOptions = createMemo(() => {
    const kind = scopeKind().subject;
    if (kind === 'tenant') {
      return tenants.tenants().map((t) => ({ value: t.slug, label: t.name && t.name !== t.slug ? `${t.name} (${t.slug})` : t.slug }));
    }
    if (kind === 'key') {
      return keys
        .allRows()
        .filter((row) => row.active === true)
        .map((row) => ({
          value: String(row.id ?? ''),
          label: [String(row.label || row.keyPrefix || row.id), String(row.tenantSlug ?? '')].filter(Boolean).join(' · '),
        }))
        .sort((a, b) => a.label.localeCompare(b.label));
    }
    return [];
  });

  /** A value the list does not hold (a revoked key, a tenant by id) is still offered, so it is not lost. */
  const withCurrent = (options: Array<{ value: string; label: string }>, current: string, placeholder: string) => [
    { value: '', label: placeholder },
    ...(current && !options.some((o) => o.value === current) ? [{ value: current, label: current }] : []),
    ...options,
  ];

  const newTarget = () => {
    const kind = scopeKind();
    if (kind.singleton) return '*';
    const subject = newSubject().trim();
    const model = newModel().trim();
    if (kind.subject && kind.model) return subject && model ? `${subject}|${model}` : '';
    return kind.subject ? subject : model;
  };

  const changeScope = (scope: string) => {
    const before = scopeKind().subject;
    setNewScope(scope);
    if (scopeKind().subject !== before) setNewSubject('');
    if (!scopeKind().model) setNewModel('');
  };

  const addRule = () => {
    const target = newTarget();
    if (!target) return;
    const identity = rateLimitRuleIdentity(newScope(), target);
    // One rule per scope and target: a second one would be refused on save, so open the first.
    if (!store.draft()?.rules.some((r) => rateLimitRuleIdentity(r.scope, r.target) === identity)) {
      store.addRule({
        scope: newScope(),
        target,
        rpm: readNumber(newRpm(), 60),
        burst: readNumber(newBurst(), 0),
      });
    }
    drawers.openRuleDrawer(identity);
    setNewSubject('');
    setNewModel('');
    setNewRpm('60');
    setNewBurst('0');
  };

  const visibleRules = createMemo(() => {
    const rules = store.draft()?.rules ?? [];
    const q = ruleFilter().trim().toLowerCase();
    if (!q) return rules;
    return rules.filter((rule) => {
      const who = subjectLabel(rule);
      return [who.name, who.sub, rule.target, scopeInfo(rule.scope).short, ruleModel(rule)]
        .join(' ')
        .toLowerCase()
        .includes(q);
    });
  });

  const planRows = () => {
    const cfg = store.draft();
    if (!cfg) return [];
    const plans = Object.keys(cfg.plans).map((k) => k.toLowerCase());
    const using = (slug: string) =>
      tenants.tenants().filter((t) => {
        const plan = t.planSlug.toLowerCase();
        return slug ? plan === slug.toLowerCase() : !plans.includes(plan);
      }).length;
    const who = (slug: string, fallback: string) =>
      tenants.operator() ? `${formatNum(using(slug))} tenant(s)` : fallback;
    const rows: Array<{ key: string; name: string; who: string; tier: typeof cfg.default; kind: 'default' | 'plan'; slug: string }> = [
      { key: 'default', name: 'Default', who: who('', 'Every tenant without a plan'), tier: cfg.default, kind: 'default', slug: '' },
    ];
    for (const [slug, tier] of Object.entries(cfg.plans)) {
      rows.push({ key: 'plan:' + slug, name: slug, who: who(slug, 'Tenants on this plan'), tier, kind: 'plan', slug });
    }
    return rows;
  };

  return (
    <section class="page" id="panel-ratelimits">
      <header class="page-header">
        <div class="titles">
          {null}
          <h2>Rate limits</h2>
          {null}
        </div>
      </header>

    <div class="rate-limits-panel rl-page">
      <Show when={store.loadError()}>
        <p class="notice error" role="alert">
          {store.loadError()}{' '}
          <button type="button" class="rl-link" onClick={() => void store.load({ force: true })}>
            Retry
          </button>
        </p>
      </Show>

      <Show when={store.loading() && !store.draft()}>
        <p class="loading-hint">Loading rate limits…</p>
      </Show>

      <Show when={store.draft()}>
        {(cfg) => (
          <>
            <Show when={store.readOnlyReason()}>
              <p class="notice warn" role="status">
                <span class="tag muted">read-only</span> {store.readOnlyReason()} Rules can still be inspected.
              </p>
            </Show>

            <div class="card rl-status">
              <label class="rl-switch" title="Enforce rate limits">
                <input
                  type="checkbox"
                  role="switch"
                  checked={cfg().enabled}
                  disabled={store.locked()}
                  aria-label="Enforce rate limits"
                  onChange={(e) => store.setEnabled(e.currentTarget.checked)}
                />
                <span class="rl-switch-track" />
              </label>
              <div class="rl-status-text">
                <div class={`rl-status-title${cfg().enabled ? '' : ' off'}`}>
                  {cfg().enabled ? 'Rate limits enforced' : 'Rate limits disabled'}
                </div>
                <div class="rl-status-sub">
                  <label class="rl-switch sm" title="Adapt model limits to load">
                    <input
                      type="checkbox"
                      role="switch"
                      checked={cfg().adaptiveEnabled}
                      disabled={store.locked()}
                      aria-label="Adapt model limits to load"
                      onChange={(e) => store.setAdaptiveEnabled(e.currentTarget.checked)}
                    />
                    <span class="rl-switch-track" />
                    <span>Adaptive load shedding</span>
                  </label>
                </div>
              </div>
              <div class="rl-status-actions">
                <Button variant="ghost" size="sm" onClick={() => setHelpOpen(true)}>
                  <span class="icon"><IconFileText /></span> Rate limits, explained
                </Button>
                <Button variant="ghost" size="sm" onClick={() => void store.load({ force: true })} disabled={store.loading()}>
                  <span class="icon"><IconRefresh /></span> Reload
                </Button>
              </div>
            </div>

            <div class="rl-section" id="rl-rules">
              <div class="rl-section-head">
                <div>
                  <h3>Rules</h3>
                  {null}
                </div>
              </div>

              <Show when={store.editable()}>
                <div class="card rl-new-rule">
                  <h4 class="rl-h4">Add rule</h4>
                  <div class="filter-row">
                    <Field label="Scope">
                      <Select
                        aria-label="Scope"
                        options={RATE_LIMIT_SCOPES.map((s) => ({ value: s.value, label: s.label }))}
                        value={newScope()}
                        onChange={changeScope}
                      />
                    </Field>
                    <Show when={scopeKind().subject}>
                      <Field label={scopeKind().subject === 'tenant' ? 'Tenant' : 'API key'}>
                        <Show
                          when={subjectOptions().length > 0}
                          fallback={
                            <input
                              type="text"
                              aria-label="Rule subject"
                              placeholder={scopeKind().subject === 'tenant' ? 'Tenant slug' : 'API key id'}
                              value={newSubject()}
                              onInput={(e) => setNewSubject(e.currentTarget.value)}
                            />
                          }
                        >
                          <Select
                            aria-label="Rule subject"
                            options={withCurrent(subjectOptions(), newSubject(), scopeKind().subject === 'tenant' ? 'Choose a tenant…' : 'Choose a key…')}
                            value={newSubject()}
                            onChange={setNewSubject}
                          />
                        </Show>
                      </Field>
                    </Show>
                    <Show when={scopeKind().model}>
                      <Field label="Model">
                        <Show
                          when={models.modelIds().length > 0}
                          fallback={
                            <input
                              type="text"
                              aria-label="Rule model"
                              placeholder="Model id"
                              value={newModel()}
                              onInput={(e) => setNewModel(e.currentTarget.value)}
                            />
                          }
                        >
                          <Select
                            aria-label="Rule model"
                            options={withCurrent(models.modelIds().map((id) => ({ value: id, label: id })), newModel(), 'Choose a model…')}
                            value={newModel()}
                            onChange={setNewModel}
                          />
                        </Show>
                      </Field>
                    </Show>
                    <Field label="RPM">
                      <input type="number" min="0" value={newRpm()} onInput={(e) => setNewRpm(e.currentTarget.value)} />
                    </Field>
                    <Field label="Burst">
                      <input type="number" min="0" value={newBurst()} onInput={(e) => setNewBurst(e.currentTarget.value)} />
                    </Field>
                    <div class="field">
                      <label>&nbsp;</label>
                      <Button onClick={addRule} disabled={!newTarget()}>
                        <span class="icon"><IconPlus /></span> Add rule
                      </Button>
                    </div>
                  </div>
                </div>
              </Show>

              <Show when={cfg().rules.length > 6}>
                <div class="toolbar rl-toolbar">
                  <label class="filter-field rl-filter">
                    <span class="icon"><IconSearch /></span>
                    <input
                      placeholder="key, tenant, model…"
                      aria-label="Filter rules"
                      value={ruleFilter()}
                      onInput={(e) => setRuleFilter(e.currentTarget.value)}
                    />
                  </label>
                  <span class="hint flush">
                    {visibleRules().length === cfg().rules.length
                      ? `${formatNum(cfg().rules.length)} rules`
                      : `${formatNum(visibleRules().length)} of ${formatNum(cfg().rules.length)} rules`}
                  </span>
                </div>
              </Show>

              <RateLimitRulesTable
                rules={visibleRules()}
                empty={
                  cfg().rules.length === 0
                    ? 'No rules yet. Tenants use their tier until you add a scoped rule.'
                    : 'No rule matches this filter.'
                }
              />
            </div>

            <RateLimitActivitySection />

            <div class="rl-section" id="rl-baselines">
              <div class="rl-section-head">
                <div>
                  <h3>Tenant tiers</h3>
                  {null}
                </div>
                <Show when={store.editable()}>
                  <Button variant="ghost" size="sm" onClick={() => drawers.openTierDrawer('plan')}>
                    <span class="icon"><IconPlus /></span> Add plan
                  </Button>
                </Show>
              </div>
              <div class="table-wrap">
                <table class="data-table t-rl-tiers">
                  <thead>
                    <tr>
                      <th>Tier</th>
                      <th>Applies to</th>
                      <th class="num">rpm</th>
                      <th class="num">burst</th>
                      <th class="num">streams</th>
                      <th class="num">tpm</th>
                      <th class="cell-actions"><span class="sr-only">Open</span></th>
                    </tr>
                  </thead>
                  <tbody>
                    <For each={planRows()}>
                      {(row) => (
                        <tr class="rl-base-row">
                          <th scope="row" class="rl-base-name">{row.name}</th>
                          <td>{row.who}</td>
                          <td class="num">{formatNum(row.tier.rpm)}</td>
                          <td class="num">{formatNum(row.tier.burst)}</td>
                          <td class="num">
                            {row.tier.maxConcurrentStreams > 0 ? formatNum(row.tier.maxConcurrentStreams) : '∞'}
                          </td>
                          <td class="num">{(row.tier.tpm ?? 0) > 0 ? formatNum(row.tier.tpm ?? 0) : '∞'}</td>
                          <td class="cell-actions">
                            <button
                              type="button"
                              class="icon-btn"
                              aria-label={`Open ${row.name}`}
                              onClick={() => drawers.openTierDrawer(row.kind, row.slug)}
                            >
                              <span class="icon"><IconChevronRight /></span>
                            </button>
                          </td>
                        </tr>
                      )}
                    </For>
                  </tbody>
                </table>
              </div>
            </div>

            <RateLimitSaveBar />
          </>
        )}
      </Show>

      <RateLimitTierDrawer />
      <RateLimitRuleDrawer />

      <Dialog
        open={helpOpen()}
        title={helpContent()?.ui?.guide ?? 'Rate limits, explained'}
        onClose={() => setHelpOpen(false)}
      >
        <div class="rl-help-toolbar">
          <Button variant={helpLang() === 'en' ? 'primary' : 'ghost'} size="sm" onClick={() => setHelpLang('en')}>
            EN
          </Button>
          <Button variant={helpLang() === 'fa' ? 'primary' : 'ghost'} size="sm" onClick={() => setHelpLang('fa')}>
            FA
          </Button>
        </div>
        <Show when={helpLoading()}>
          <p class="loading-hint">Loading guide…</p>
        </Show>
        <Show when={!helpLoading() && !helpContent()}>
          <p class="hint">
            Could not load the in-console guide.{' '}
            <a href="/admin/admin-rate-limit-help.json" target="_blank" rel="noopener noreferrer">
              Open help data
            </a>
          </p>
        </Show>
        <Show when={helpContent()}>
          {(help) => (
            <div class={`rl-help-body${helpLang() === 'fa' ? ' rtl' : ''}`}>
              <p class="hint">{help().ui?.guideSub}</p>
              <nav class="rl-help-nav" aria-label="Guide contents">
                <For each={help().sections ?? []}>
                  {(section) => (
                    <a class="rl-link" href={`#rl-help-${section.id}`}>
                      {section.title}
                    </a>
                  )}
                </For>
              </nav>
              <For each={help().sections ?? []}>
                {(section) => (
                  <section id={`rl-help-${section.id}`} class="rl-help-section">
                    <h3>{section.title}</h3>
                    <Show when={section.intro}>
                      <p>{section.intro}</p>
                    </Show>
                    <For each={section.items ?? []}>
                      {(item) => (
                        <div class="rl-help-item">
                          <Show when={item.term}>
                            <h4>{item.term}</h4>
                          </Show>
                          <Show when={item.text}>
                            <p>{item.text}</p>
                          </Show>
                          <Show when={item.example}>
                            <p class="hint">
                              <strong>{help().ui?.example ?? 'Example:'}</strong> {item.example}
                            </p>
                          </Show>
                        </div>
                      )}
                    </For>
                  </section>
                )}
              </For>
            </div>
          )}
        </Show>
      </Dialog>
    </div>
    </section>
  );
}
