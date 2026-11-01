import { For, Show } from 'solid-js';
import { IconChevronRight } from '../../components/icons';
import { formatEnforcingNow, scopeInfo } from '../../domain/rateLimitEdit';
import { formatNum } from '../../domain/format';
import { ruleModel, ruleSubject } from '../../domain/tenantLimits';
import { useKeysStore } from '../../stores/keys';
import {
  rateLimitRuleIdentity,
  useRateLimitActivityStore,
  useRateLimitDrawersStore,
  useRateLimitsStore,
  type RateLimitRule,
} from '../../stores/rateLimits';
import { useTenantsStore } from '../../stores/tenants';

/**
 * Who a rule limits, in the words an operator knows them by: a key by its label and tenant, a
 * tenant by its name. The stored target (a key id, a tenant slug) stays available as the tooltip.
 */
export function useRuleSubjectLabel() {
  const keys = useKeysStore();
  const tenants = useTenantsStore();
  return (rule: Pick<RateLimitRule, 'scope' | 'target'>): { name: string; sub: string; raw: string } => {
    const info = scopeInfo(rule.scope);
    const subject = ruleSubject(rule);
    if (info.singleton) return { name: info.name, sub: '', raw: '' };
    if (info.id === 'model') return { name: 'Everyone', sub: '', raw: '' };
    if (info.id === 'api_key' || info.id === 'api_key_model') {
      const key = keys.keyById(subject);
      if (!key) return { name: subject, sub: '', raw: subject };
      const name = String(key.label || key.keyPrefix || subject);
      const prefix = key.label ? String(key.keyPrefix ?? '') : '';
      const tenant = String(key.tenantSlug ?? '');
      return { name, sub: [tenant, prefix].filter(Boolean).join(' · '), raw: subject };
    }
    const wanted = subject.toLowerCase();
    const tenant = tenants.tenants().find((t) => t.id.toLowerCase() === wanted || t.slug.toLowerCase() === wanted);
    return tenant
      ? { name: tenant.name || tenant.slug, sub: tenant.name && tenant.name !== tenant.slug ? tenant.slug : '', raw: subject }
      : { name: subject, sub: '', raw: subject };
  };
}

/**
 * A usage-report subject (`<tenant id>`, `<key id>`, either followed by `|<model>`) with the id
 * replaced by the name it stands for. Anything that is not a known id is returned as it came.
 */
export function useSubjectNamer() {
  const keys = useKeysStore();
  const tenants = useTenantsStore();
  return (subject: string): string => {
    const [who, ...rest] = String(subject ?? '').split('|');
    const wanted = who.toLowerCase();
    const key = keys.keyById(who);
    const tenant = key ? null : tenants.tenants().find((t) => t.id.toLowerCase() === wanted);
    const name = key ? String(key.label || key.keyPrefix || who) : tenant ? tenant.name || tenant.slug : who;
    return [name, ...rest].join(' · ');
  };
}

export function RateLimitRulesTable(props: { rules: RateLimitRule[]; empty: string }) {
  const store = useRateLimitsStore();
  const drawers = useRateLimitDrawersStore();
  const activity = useRateLimitActivityStore();
  const subjectLabel = useRuleSubjectLabel();

  const limitActivity = (identity: string) => {
    const row = activity.usageReport()?.limits.find((l) => l.limitId === identity) ?? null;
    if (!row) return { text: '—', sub: '' };
    const refused = row.refusedByRate + row.refusedByStreams;
    const sub =
      row.peakUtilization != null
        ? `peak ${Math.round(row.peakUtilization * 100)}%`
        : '';
    return {
      text: `${formatNum(row.charged)} passed · ${formatNum(refused)} refused`,
      sub,
    };
  };

  return (
    <div class="table-wrap rl-rules card">
      <table class="data-table rl-rules-table">
        <thead>
          <tr>
            <th class="rl-col-on">On</th>
            <th class="rl-col-who">Who</th>
            <th class="rl-col-model">Model</th>
            <th class="rl-col-limit">
              Limit
              <span class="rl-limit-units"><span>rpm</span><span>burst</span><span>streams</span></span>
            </th>
            <th class="rl-col-now" title="What production applies right now from the saved configuration.">
              Enforcing now
              <span class="rl-th-sub">saved configuration</span>
            </th>
            <th class="rl-col-traffic">Limit activity</th>
            <th class="rl-col-chev"><span class="sr-only">Open</span></th>
          </tr>
        </thead>
        <tbody>
          <For each={props.rules}>
            {(rule) => {
              const identity = rateLimitRuleIdentity(rule.scope, rule.target);
              const info = scopeInfo(rule.scope);
              const who = () => subjectLabel(rule);
              const enf = () =>
                formatEnforcingNow(store.scheduleStatusFor(rule.scope, rule.target), !!store.saved()?.enabled);
              const act = () => limitActivity(identity);
              const modelLabel = ruleModel(rule) || 'All models';
              return (
                <tr
                  class={`rl-row${rule.enabled ? '' : ' off'}`}
                  onClick={() => drawers.openRuleDrawer(identity)}
                >
                  <td class="rl-col-on" onClick={(e) => e.stopPropagation()}>
                    <label class="rl-switch sm">
                      <input
                        type="checkbox"
                        role="switch"
                        checked={rule.enabled}
                        disabled={store.locked()}
                        aria-label={`Enable rule ${rule.target}`}
                        onChange={(e) => store.setRuleEnabled(identity, e.currentTarget.checked)}
                      />
                      <span class="rl-switch-track" />
                    </label>
                  </td>
                  <td class="rl-col-who" title={who().raw || undefined}>
                    <span class="rl-target">{who().name}</span>
                    <span class="rl-scope">
                      {info.short}
                      <Show when={who().sub}> · {who().sub}</Show>
                    </span>
                  </td>
                  <td class="rl-col-model">
                    <span class="rl-model" classList={{ muted: !ruleModel(rule) }}>{modelLabel}</span>
                  </td>
                  <td class="rl-col-limit">
                    <span class="rl-limit-nums">
                      <span><b>{formatNum(rule.rpm)}</b><i>rpm</i></span>
                      <span><b>{formatNum(rule.burst)}</b><i>burst</i></span>
                      <span>
                        <b>{rule.maxConcurrentStreams > 0 ? formatNum(rule.maxConcurrentStreams) : '∞'}</b>
                        <i>streams</i>
                      </span>
                    </span>
                    <Show when={(rule.tpm ?? 0) > 0}>
                      <span class="rl-act-sub rl-tpm">{formatNum(rule.tpm ?? 0)} tokens/min</span>
                    </Show>
                    <Show when={(rule.schedule || []).length > 0}>
                      <span class="hint">{(rule.schedule || []).length} window(s)</span>
                    </Show>
                  </td>
                  <td class="rl-col-now" title={enf().text}>
                    <span class="rl-enf-text">{enf().text}</span>
                    <For each={enf().tags}>
                      {(tag) => <span class="tag accent">{tag}</span>}
                    </For>
                  </td>
                  <td class="rl-col-traffic">
                    <span>{act().text}</span>
                    <Show when={act().sub}>
                      <span class="rl-act-sub">{act().sub}</span>
                    </Show>
                  </td>
                  <td class="rl-col-chev">
                    <button
                      type="button"
                      class="icon-btn"
                      aria-label="Open rule"
                      onClick={(e) => {
                        e.stopPropagation();
                        drawers.openRuleDrawer(identity);
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
      <Show when={props.rules.length === 0}>
        <p class="hint rl-no-match flush">{props.empty}</p>
      </Show>
    </div>
  );
}
