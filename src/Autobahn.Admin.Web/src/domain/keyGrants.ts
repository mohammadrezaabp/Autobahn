export interface ModelGrantsResponse {
  modelIds: string[];
  /** A key's allow entries that end in '*': a prefix, or every model of a provider. */
  patterns?: string[];
  /** Models held back from a key even when a pattern allows them. */
  deniedModelIds?: string[];
  usesDefaultAccess?: boolean;
}

export interface ReplaceModelGrantsRequest {
  modelIds: string[];
  patterns?: string[];
  deniedModelIds?: string[];
}

export const SELF_HOSTED = 'self-hosted';

/** One provider's switch, for a tenant or for a key. */
export interface ProviderAccessItem {
  provider: string;
  displayName: string;
  /** Bills per request; its switches start closed. */
  paid: boolean;
  open: boolean;
  /** A key's switch cannot be turned on while the tenant's is off. */
  tenantOpen: boolean;
  openKeyCount?: number;
  /** USD this level (the key, or the tenant) may spend at the provider in a month; null for no cap of its own. */
  monthlyCapUsd?: number | null;
  spentThisMonthUsd?: number;
  /** The tenant's cap, which a key's cannot exceed. */
  tenantMonthlyCapUsd?: number | null;
  tenantSpentThisMonthUsd?: number;
  /** False for a provider whose API the gateway does not forward yet. */
  forwardable?: boolean;
}

/**
 * What a key's cap field holds, as a number: blank is no cap of its own. An amount above the
 * tenant's cap is refused here as the gateway refuses it, so the reason is shown beside the field.
 */
export function parseKeyCap(text: string, tenantCap: number | null | undefined): { value: number | null; error: string } {
  const trimmed = text.trim();
  if (!trimmed) return { value: null, error: '' };
  const n = Number(trimmed);
  if (!Number.isFinite(n) || n < 0) return { value: null, error: 'Enter an amount in USD, or leave it empty.' };
  const value = Math.round(n * 100) / 100;
  if (tenantCap != null && value > tenantCap) {
    return { value: null, error: `A key's cap cannot exceed its tenant's cap of $${tenantCap.toFixed(2)}.` };
  }
  return { value, error: '' };
}

export interface AccessCheckResult {
  allowed: boolean;
  reason?: string | null;
  message: string;
  modelId?: string | null;
  provider?: string | null;
  rule?: string | null;
}

/** A registered model, as the key's model picker needs it. */
export interface GrantModel {
  id: string;
  /** In service: not stopped by an operator. */
  serving: boolean;
  /** The backend's last health verdict; null when it is not known. */
  healthy: boolean | null;
  /** Set only for a model of a paid provider. */
  provider?: string;
}

export type GrantChoiceNote = '' | 'down' | 'stopped' | 'removed' | 'not allowed for the tenant' | 'provider is off';

/** One row of the key's model picker. */
export interface GrantChoice {
  id: string;
  note: GrantChoiceNote;
  /** Cannot be ticked. A ticked row is never disabled, so it can always be unticked. */
  disabled: boolean;
  /** Served by a provider that bills per request. */
  paid?: boolean;
}

export function canGrantKey(row: Record<string, unknown>): boolean {
  if (String(row.role ?? '') === 'Admin') return false;
  if (row.isRevoked || row.isArchived) return false;
  return true;
}

const lower = (id: string) => id.toLowerCase();

/** The registry and backend lists, joined into what the picker shows. */
export function normalizeGrantModels(models: unknown, backends?: unknown): GrantModel[] {
  const health = new Map<string, boolean>();
  const backendRows = Array.isArray(backends) ? backends : (backends as { items?: unknown } | null)?.items;
  if (Array.isArray(backendRows)) {
    for (const row of backendRows as Array<Record<string, unknown> | null>) {
      if (row && typeof row.modelId === 'string' && typeof row.isHealthy === 'boolean') {
        health.set(lower(row.modelId), row.isHealthy);
      }
    }
  }
  if (!Array.isArray(models)) return [];
  const seen = new Set<string>();
  const out: GrantModel[] = [];
  for (const item of models as Array<Record<string, unknown> | null>) {
    const m = ((item?.model as Record<string, unknown> | undefined) ?? item ?? {}) as Record<string, unknown>;
    const id = String(m.id ?? '');
    if (!id || seen.has(lower(id))) continue;
    seen.add(lower(id));
    // The list item carries the provider the access rules read; an older gateway sends none.
    const provider = String(item?.provider ?? '').toLowerCase();
    out.push({
      id,
      // A route written before the state field existed is serving.
      serving: String(m.state ?? 'serving').toLowerCase() !== 'stopped',
      healthy: health.get(lower(id)) ?? null,
      ...(provider && provider !== SELF_HOSTED ? { provider } : {}),
    });
  }
  return out.sort((a, b) => a.id.localeCompare(b.id));
}

/**
 * The rows of the picker: every serving model, and whatever else the key already holds.
 *
 * A stopped model is offered only while the key holds it, so it can be taken away but not handed
 * out. A model the tenant may not call cannot be ticked: the gateway refuses that write.
 *
 * @param registry null when the registry could not be read; the key's own grants are then all
 *   there is to show.
 * @param tenantAllowed null when the tenant may call every model.
 * @param closedProviders paid providers this key is not opened for. Their models cannot be
 *   ticked: the gateway would refuse the key at the provider before it read the list.
 */
export function buildGrantChoices(
  registry: GrantModel[] | null,
  selected: string[],
  tenantAllowed: string[] | null,
  closedProviders: string[] = [],
): GrantChoice[] {
  const held = new Set(selected.map(lower));
  const allowed = tenantAllowed ? new Set(tenantAllowed.map(lower)) : null;
  const choices: GrantChoice[] = [];
  const known = new Set<string>();

  for (const model of registry ?? []) {
    known.add(lower(model.id));
    const isHeld = held.has(lower(model.id));
    if (!model.serving && !isHeld) continue;
    const outsideTenant = allowed !== null && !allowed.has(lower(model.id));
    const providerOff = !!model.provider && closedProviders.includes(model.provider);
    const note: GrantChoiceNote = !model.serving
      ? 'stopped'
      : outsideTenant
        ? 'not allowed for the tenant'
        : providerOff
          ? 'provider is off'
          : model.healthy === false
            ? 'down'
            : '';
    choices.push({
      id: model.id,
      note,
      disabled: (outsideTenant || providerOff) && !isHeld,
      ...(model.provider ? { paid: true } : {}),
    });
  }

  for (const id of selected) {
    if (known.has(lower(id))) continue;
    known.add(lower(id));
    choices.push({ id, note: registry ? 'removed' : '', disabled: false });
  }

  return choices.sort((a, b) => a.id.localeCompare(b.id));
}

/**
 * The body of the write. A grant on a model that is no longer registered is left out: the gateway
 * refuses the whole write over it, so it would block every later change to the key.
 */
export function buildReplaceGrantsPayload(
  selected: string[],
  registry?: GrantModel[] | null,
  patterns: string[] = [],
  denied: string[] = [],
): ReplaceModelGrantsRequest {
  const known = registry ? new Set(registry.map((m) => lower(m.id))) : null;
  const keep = (ids: string[]) => (known ? ids.filter((id) => known.has(lower(id))) : [...ids]);
  const heldBack = keep(denied);
  return {
    modelIds: keep(selected),
    // Left out when empty: the gateway reads a missing list as an empty one.
    ...(patterns.length ? { patterns: [...patterns] } : {}),
    ...(heldBack.length ? { deniedModelIds: heldBack } : {}),
  };
}

/** The entry for every model of a provider, including ones registered later. */
export const providerPattern = (provider: string) => `${provider}:*`;

/**
 * Turns what an operator typed into a prefix entry: 'qwen/' and 'qwen/*' both give 'qwen/*'.
 * Only a trailing '*' is accepted, and it needs something in front of it.
 */
export function normalizePrefix(text: string): { pattern?: string; error?: string } {
  const stem = text.trim().replace(/\*$/, '');
  if (!stem) return { error: 'Type the start of a model id, such as qwen/' };
  if (stem.includes('*')) return { error: "Only one '*' is accepted, at the end." };
  return { pattern: `${stem}*` };
}

/**
 * What the key can call once the editor is saved, in one sentence.
 *
 * @param providers the paid providers and whether the key will be opened for each; null when the
 *   gateway did not report them.
 */
export function describeKeyAccess(
  selected: string[],
  patterns: string[],
  denied: string[],
  providers: Array<{ provider: string; displayName: string; open: boolean }> | null,
): string {
  const parts: string[] = [];
  if (selected.length) parts.push(selected.length === 1 ? '1 chosen model' : `${selected.length} chosen models`);
  for (const pattern of patterns) {
    const named = providers?.find((p) => providerPattern(p.provider) === pattern.toLowerCase());
    parts.push(named ? `every ${named.displayName} model` : `every model starting with ${pattern.slice(0, -1)}`);
  }
  if (!parts.length) return 'After saving, this key is refused on every model that needs a key.';

  const list = parts.length > 1 ? `${parts.slice(0, -1).join(', ')} and ${parts[parts.length - 1]}` : parts[0];
  const except = denied.length ? `, except ${denied.length} held back` : '';
  const closed = (providers ?? []).filter((p) => !p.open).map((p) => p.displayName);
  const paid = closed.length ? ` ${closed.join(' and ')} stays closed for it, so paid models are refused.` : '';
  return `After saving, this key can call ${list}${except}.${paid}`;
}

export function toggleGrantSelection(selected: string[], modelId: string): string[] {
  const set = new Set(selected);
  if (set.has(modelId)) set.delete(modelId);
  else set.add(modelId);
  return [...set];
}
