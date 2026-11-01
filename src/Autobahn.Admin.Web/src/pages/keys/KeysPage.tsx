import { For, Show, createMemo, createSignal, onCleanup, onMount } from 'solid-js';
import { A, useNavigate, useSearchParams } from '@solidjs/router';
import { Button, Dialog, Drawer, Field, Select } from '../../components/primitives';
import {
  IconArchive,
  IconArchiveOut,
  IconBarChart,
  IconChevronDown,
  IconChevronUp,
  IconCpu,
  IconEdit,
  IconGauge,
  IconPlus,
  IconRefresh,
  IconSearch,
  IconShieldOff,
  IconTrash,
} from '../../components/icons';
import { canGrantKey } from '../../domain/keyGrants';
import { formatNum, formatTime } from '../../domain/format';
import type { KeyStatusFilter } from '../../domain/filters';
import { openKeyAccessDrawer } from '../../stores/keyGrants';
import { KeyModelAccessDrawer } from '../../components/KeyModelAccessDrawer';
import { queueAddRuleIntent } from '../../stores/rateLimits';
import {
  DEFAULT_KEY_EDIT,
  DEFAULT_NEW_KEY,
  activateKeysPage,
  bulkRevoke,
  createKey,
  deleteKey,
  disposeKeysPage,
  revokeKey,
  updateKey,
  useKeysStore,
  type KeyEditDraft,
  type NewKeyDraft,
} from '../../stores/keys';

const FILTERS = [
  { value: 'active', label: 'Active' },
  { value: 'revoked', label: 'Revoked' },
  { value: 'archived', label: 'Archived' },
  { value: 'all', label: 'All' },
];

const ROLES = [
  { value: 'Inference', label: 'Inference' },
  { value: 'Admin', label: 'Admin' },
];

function SortHeader(props: { label: string; sortKey: string; current: string; dir: number; num?: boolean; onSort: (k: string) => void }) {
  const active = () => props.current === props.sortKey;
  return (
    <th classList={{ num: props.num }} aria-sort={active() ? (props.dir === 1 ? 'ascending' : 'descending') : 'none'}>
      <button type="button" class="th-sort" onClick={() => props.onSort(props.sortKey)}>
        {props.label}
        <Show when={active()}>
          <span class="sort-icon">{props.dir === 1 ? <IconChevronUp /> : <IconChevronDown />}</span>
        </Show>
      </button>
    </th>
  );
}

export default function KeysPage() {
  const store = useKeysStore();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [createOpen, setCreateOpen] = createSignal(false);
  const [draft, setDraft] = createSignal<NewKeyDraft>({ ...DEFAULT_NEW_KEY });
  const [editDraft, setEditDraft] = createSignal<KeyEditDraft>({ ...DEFAULT_KEY_EDIT });
  const [editOpen, setEditOpen] = createSignal(false);
  const [createdSecret, setCreatedSecret] = createSignal('');
  const [createdAck, setCreatedAck] = createSignal(false);
  const [revokeId, setRevokeId] = createSignal<string | null>(null);
  const [bulkRevokeOpen, setBulkRevokeOpen] = createSignal(false);
  const [deleteKeyRow, setDeleteKeyRow] = createSignal<Record<string, unknown> | null>(null);
  const [deleteConfirmText, setDeleteConfirmText] = createSignal('');
  const [busy, setBusy] = createSignal(false);

  onMount(() => {
    activateKeysPage();
    // A link from a tenant's page arrives with that tenant chosen. Set after the load, which
    // clears a filter naming a tenant it does not know yet.
    const wanted = typeof params.tenant === 'string' ? params.tenant : '';
    void store.load().then(() => {
      if (wanted && store.tenants().some((t) => t.id === wanted)) store.setTenantFilter(wanted);
    });
  });
  onCleanup(() => disposeKeysPage());

  const keyId = (row: Record<string, unknown>) => String(row.id ?? row.keyId ?? '');

  // Only an operator is given more than one tenant; everyone else keeps the single-tenant page.
  const multiTenant = () => store.tenants().length > 1;
  const tenantFilterOptions = createMemo(() => [
    { value: '', label: 'All tenants' },
    ...store.tenants().map((t) => ({ value: t.id, label: t.name || t.slug })),
  ]);
  const tenantCreateOptions = createMemo(() => [
    { value: '', label: 'My own tenant' },
    ...store.tenants().map((t) => ({ value: t.id, label: t.name || t.slug })),
  ]);

  const openCreate = () => {
    setDraft({ ...DEFAULT_NEW_KEY, tenantId: store.tenantFilter() });
    setCreatedSecret('');
    setCreatedAck(false);
    setCreateOpen(true);
  };

  const closeCreate = () => {
    if (createdSecret() && !createdAck()) return;
    setCreateOpen(false);
  };

  const openEdit = (row: Record<string, unknown>) => {
    setEditDraft({
      id: keyId(row),
      keyPrefix: String(row.keyPrefix ?? ''),
      label: String(row.label ?? ''),
      assignee: String(row.assignee ?? ''),
      description: String(row.description ?? ''),
      costCenter: String(row.costCenter ?? ''),
    });
    setEditOpen(true);
  };

  const submitCreate = async () => {
    setBusy(true);
    try {
      const secret = await createKey(draft());
      setCreatedSecret(secret);
    } finally {
      setBusy(false);
    }
  };

  const submitEdit = async () => {
    setBusy(true);
    try {
      await updateKey(editDraft());
      setEditOpen(false);
    } finally {
      setBusy(false);
    }
  };

  const confirmRevoke = async () => {
    const id = revokeId();
    if (!id) return;
    setBusy(true);
    try {
      await revokeKey(id);
      setRevokeId(null);
    } finally {
      setBusy(false);
    }
  };

  const confirmBulkRevoke = async () => {
    setBusy(true);
    try {
      await bulkRevoke([...store.selectedIds()]);
      setBulkRevokeOpen(false);
    } finally {
      setBusy(false);
    }
  };

  const deleteMatches = () => {
    const row = deleteKeyRow();
    if (!row) return false;
    return deleteConfirmText().trim() === String(row.keyPrefix ?? '');
  };

  const confirmDelete = async () => {
    const row = deleteKeyRow();
    if (!row || !deleteMatches()) return;
    setBusy(true);
    try {
      await deleteKey(keyId(row), String(row.keyPrefix ?? ''));
      setDeleteKeyRow(null);
      setDeleteConfirmText('');
    } finally {
      setBusy(false);
    }
  };

  const goUsage = (id: string) => navigate(`/usage?key=${encodeURIComponent(id)}`);
  const goRateLimit = (id: string) => {
    queueAddRuleIntent('api_key', id);
    navigate('/ratelimits');
  };

  return (
    <section class="page" id="panel-keys" data-solid-root="keys">
      <div class="page-header">
        <div class="titles">
          
          <h2>API keys</h2>
        </div>
        <div class="page-actions">
          <Button onClick={openCreate}>
            <span class="icon"><IconPlus /></span> Create key
          </Button>
        </div>
      </div>

      <div class="toolbar">
        <label class="field-inline" for="keys-status-filter">Show</label>
        <Select
          id="keys-status-filter"
          class="inline-select"
          options={FILTERS}
          value={store.statusFilter()}
          onChange={(v) => store.setStatusFilter(v as KeyStatusFilter)}
        />
        <Show when={multiTenant()}>
          <Select
            id="keys-tenant-filter"
            aria-label="Tenant"
            class="inline-select"
            options={tenantFilterOptions()}
            value={store.tenantFilter()}
            onChange={(v) => store.setTenantFilter(v)}
          />
        </Show>
        <label class="filter-field">
          <span class="icon"><IconSearch /></span>
          <input
            placeholder="prefix, label, assignee, cost center…"
            aria-label="Filter keys"
            value={store.textFilter()}
            onInput={(e) => store.setTextFilter(e.currentTarget.value)}
          />
        </label>
        <span class="spacer" />
        <Button variant="danger" onClick={() => setBulkRevokeOpen(true)} disabled={store.selectedIds().size === 0}>
          <span class="icon"><IconShieldOff /></span> Revoke selected ({store.selectedIds().size})
        </Button>
        <Button variant="secondary" onClick={() => store.load({ force: true })}>
          <span class="icon"><IconRefresh /></span> Refresh
        </Button>
      </div>

      

      <div class="table-wrap">
        <table class="data-table t-keys" classList={{ 'has-tenant': multiTenant() }}>
          <colgroup>
            <col /><col /><Show when={multiTenant()}><col /></Show><col />
            <col /><col /><col />
            <col /><col /><col />
            <col /><col />
          </colgroup>
          <thead>
            <tr>
              <th class="cell-select" />
              <SortHeader label="Prefix" sortKey="keyPrefix" current={store.sortSpec().key} dir={store.sortSpec().dir} onSort={store.toggleSort} />
              <Show when={multiTenant()}>
                <SortHeader label="Tenant" sortKey="tenantSlug" current={store.sortSpec().key} dir={store.sortSpec().dir} onSort={store.toggleSort} />
              </Show>
              <SortHeader label="Role" sortKey="role" current={store.sortSpec().key} dir={store.sortSpec().dir} onSort={store.toggleSort} />
              <th>Label</th>
              <th>Status</th>
              <SortHeader label="Last used" sortKey="lastUsedAt" current={store.sortSpec().key} dir={store.sortSpec().dir} onSort={store.toggleSort} />
              <SortHeader num label="MTD req" sortKey="mtdRequests" current={store.sortSpec().key} dir={store.sortSpec().dir} onSort={store.toggleSort} />
              <SortHeader num label="MTD cost" sortKey="mtdCost" current={store.sortSpec().key} dir={store.sortSpec().dir} onSort={store.toggleSort} />
              <th>Assignee</th>
              <SortHeader label="Created" sortKey="createdAt" current={store.sortSpec().key} dir={store.sortSpec().dir} onSort={store.toggleSort} />
              <th class="cell-actions"><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            <For each={store.capped()}>
              {(row) => {
                const id = () => keyId(row);
                const active = () => row.active === true;
                return (
                  <tr>
                    <td class="cell-select">
                      <Show when={active()}>
                        <input
                          type="checkbox"
                          checked={store.selectedIds().has(id())}
                          onChange={() => store.toggleSelected(id())}
                          aria-label={`Select ${row.keyPrefix}`}
                        />
                      </Show>
                    </td>
                    <td class="cell-code">
                      <span class="cell-code-inner"><code title={String(row.keyPrefix ?? '')}>{String(row.keyPrefix ?? '')}</code></span>
                    </td>
                    <Show when={multiTenant()}>
                      <td class="col-truncate" title={String(row.tenantName ?? '')}>
                        <A class="cell-link" href={`/tenants/${encodeURIComponent(String(row.tenantId ?? ''))}`}>{String(row.tenantSlug ?? '—')}</A>
                      </td>
                    </Show>
                    <td><span class="tag role-tag" classList={{ admin: row.role === 'Admin' }}>{String(row.role ?? '—')}</span></td>
                    <td class="col-truncate" title={String(row.label ?? '')}>{String(row.label ?? '—')}</td>
                    <td>
                      <span class="status-chip" classList={{ ok: active(), fail: row.isRevoked === true && !row.isArchived, muted: row.isArchived === true }}>
                        {row.isArchived ? 'archived' : row.isRevoked ? 'revoked' : 'active'}
                      </span>
                    </td>
                    <td class="cell-time">{formatTime(row.lastUsedAt as string | undefined)}</td>
                    <td class="num">{formatNum(row.mtdRequests as number | undefined)}</td>
                    <td class="num">{row.mtdCost != null ? `$${Number(row.mtdCost).toFixed(2)}` : '—'}</td>
                    <td class="col-truncate" title={String(row.assignee ?? '')}>{String(row.assignee ?? '—')}</td>
                    <td class="cell-time">{formatTime(row.createdAt as string | undefined)}</td>
                    <td class="cell-actions">
                      <span class="row-actions">
                        <Show when={active()}>
                          <button type="button" class="icon-btn" title="Edit key" aria-label="Edit key" onClick={() => openEdit(row)}><IconEdit /></button>
                          <Show when={canGrantKey(row)}>
                            <button type="button" class="icon-btn" title="Model access" aria-label="Model access" onClick={() => void openKeyAccessDrawer(row)}><IconCpu /></button>
                          </Show>
                        </Show>
                        <button type="button" class="icon-btn" title="View usage" aria-label="View usage" onClick={() => goUsage(id())}><IconBarChart /></button>
                        <Show when={active()}>
                          <button type="button" class="icon-btn" title="Limit this key…" aria-label="Limit this key" onClick={() => goRateLimit(id())}><IconGauge /></button>
                          <button type="button" class="icon-btn danger" title="Revoke key" aria-label="Revoke key" onClick={() => setRevokeId(id())}><IconShieldOff /></button>
                        </Show>
                        <Show when={row.canArchive === true && !row.isArchived}>
                          <button type="button" class="icon-btn" title="Archive key" aria-label="Archive key" onClick={() => store.archiveKey(id())}><IconArchive /></button>
                        </Show>
                        <Show when={row.canUnarchive === true}>
                          <button type="button" class="icon-btn" title="Restore key to the list" aria-label="Restore key to the list" onClick={() => store.unarchiveKey(id())}><IconArchiveOut /></button>
                        </Show>
                        <Show when={row.canDelete === true}>
                          <button type="button" class="icon-btn danger" title="Delete key permanently" aria-label="Delete key permanently" onClick={() => { setDeleteKeyRow(row); setDeleteConfirmText(''); }}><IconTrash /></button>
                        </Show>
                      </span>
                    </td>
                  </tr>
                );
              }}
            </For>
          </tbody>
        </table>
      </div>

      <Drawer open={createOpen()} title="Create API key" onClose={closeCreate}>
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
          <Show when={multiTenant()}>
            <Field label="Tenant">
              <Select options={tenantCreateOptions()} value={draft().tenantId} onChange={(v) => setDraft((d) => ({ ...d, tenantId: v }))} />
            </Field>
          </Show>
          <Field label="Role">
            <Select options={ROLES} value={draft().role} onChange={(v) => setDraft((d) => ({ ...d, role: v }))} />
          </Field>
          <Field label="Label">
            <input value={draft().label} onInput={(e) => setDraft((d) => ({ ...d, label: e.currentTarget.value }))} placeholder="e.g. prod-chatbot" />
          </Field>
          <Field label="Assignee">
            <input value={draft().assignee} onInput={(e) => setDraft((d) => ({ ...d, assignee: e.currentTarget.value }))} placeholder="Person or team name" />
          </Field>
          <Field label="Cost center">
            <input value={draft().costCenter} onInput={(e) => setDraft((d) => ({ ...d, costCenter: e.currentTarget.value }))} placeholder="FinOps grouping (optional)" />
          </Field>
          <Field label="Description">
            <textarea rows={2} value={draft().description} onInput={(e) => setDraft((d) => ({ ...d, description: e.currentTarget.value }))} placeholder="Optional notes" />
          </Field>
          <Button onClick={submitCreate} disabled={busy()}>Create key</Button>
        </Show>
      </Drawer>

      <Drawer open={editOpen()} title="Edit API key" onClose={() => setEditOpen(false)}>
        <p class="hint">Prefix: <code>{editDraft().keyPrefix}</code></p>
        <Field label="Label">
          <input value={editDraft().label} onInput={(e) => setEditDraft((d) => ({ ...d, label: e.currentTarget.value }))} />
        </Field>
        <Field label="Assignee">
          <input value={editDraft().assignee} onInput={(e) => setEditDraft((d) => ({ ...d, assignee: e.currentTarget.value }))} />
        </Field>
        <Field label="Cost center">
          <input value={editDraft().costCenter} onInput={(e) => setEditDraft((d) => ({ ...d, costCenter: e.currentTarget.value }))} />
        </Field>
        <Field label="Description">
          <textarea rows={2} value={editDraft().description} onInput={(e) => setEditDraft((d) => ({ ...d, description: e.currentTarget.value }))} />
        </Field>
        <Button onClick={submitEdit} disabled={busy()}>Save</Button>
      </Drawer>

      <KeyModelAccessDrawer />

      <Dialog open={!!revokeId()} title="Revoke API key?" onClose={() => setRevokeId(null)}>
        <p>This will revoke the key immediately. This cannot be undone.</p>
        <div class="modal-actions">
          <Button variant="secondary" onClick={() => setRevokeId(null)}>Cancel</Button>
          <Button variant="danger" onClick={confirmRevoke} disabled={busy()}>Revoke</Button>
        </div>
      </Dialog>

      <Dialog open={bulkRevokeOpen()} title="Revoke selected keys?" onClose={() => setBulkRevokeOpen(false)}>
        <p>Revoke {store.selectedIds().size} selected key(s)? This cannot be undone.</p>
        <div class="modal-actions">
          <Button variant="secondary" onClick={() => setBulkRevokeOpen(false)}>Cancel</Button>
          <Button variant="danger" onClick={confirmBulkRevoke} disabled={busy()}>Revoke all</Button>
        </div>
      </Dialog>

      <Dialog open={!!deleteKeyRow()} title="Delete API key permanently?" onClose={() => { setDeleteKeyRow(null); setDeleteConfirmText(''); }}>
        <p>Type the key prefix <code>{String(deleteKeyRow()?.keyPrefix ?? '')}</code> to confirm permanent deletion.</p>
        <Field label="Prefix confirmation">
          <input value={deleteConfirmText()} onInput={(e) => setDeleteConfirmText(e.currentTarget.value)} />
        </Field>
        <div class="modal-actions">
          <Button variant="secondary" onClick={() => { setDeleteKeyRow(null); setDeleteConfirmText(''); }}>Cancel</Button>
          <Button variant="danger" onClick={confirmDelete} disabled={!deleteMatches() || busy()}>Delete permanently</Button>
        </div>
      </Dialog>
    </section>
  );
}
