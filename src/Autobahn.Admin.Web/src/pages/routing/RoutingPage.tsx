import { For, Show, createSignal, onCleanup, onMount } from 'solid-js';
import { A, useSearchParams } from '@solidjs/router';
import { ModelDrawer } from '../../components/ModelDrawer';
import { Button, Dialog, Tabs } from '../../components/primitives';
import {
  IconCopy,
  IconCpu,
  IconEdit,
  IconExternalLink,
  IconPause,
  IconPlay,
  IconPlayCircle,
  IconPlus,
  IconRefresh,
  IconSearch,
  IconServer,
  IconTrash,
} from '../../components/icons';
import { formatNum } from '../../domain/format';
import { pushToast } from '../../stores/auth';
import { formatModelPrice, isPaidProvider, normalizeModelItem, providerName } from '../../domain/routingModels';
import {
  activateRoutingPage,
  disposeRoutingPage,
  isModelStopped,
  setModelState,
  useRoutingStore,
} from '../../stores/routing';
import {
  closeTestDialog,
  openEditModelDrawer,
  openNewModelDrawer,
  removeModel,
  rerunModelTest,
  testModel,
  useRoutingModelsStore,
} from '../../stores/routingModels';

export default function RoutingPage() {
  const store = useRoutingStore();
  const models = useRoutingModelsStore();
  const [params] = useSearchParams();
  const [confirmAction, setConfirmAction] = createSignal<{ id: string; action: 'stop' | 'start' } | null>(null);
  const [removeTarget, setRemoveTarget] = createSignal<string | null>(null);
  // A test sends a real request. For a paid model that is money, so it is said before it is sent.
  const [paidTest, setPaidTest] = createSignal<{ id: string; provider: string } | null>(null);
  const startTest = (id: string, provider: string) =>
    isPaidProvider(provider) ? setPaidTest({ id, provider }) : void testModel(id);
  const [busy, setBusy] = createSignal(false);

  const refreshModels = () => void store.loadModels({ force: true });

  const subFromRoute = () => {
    if (params.sub === 'backends') return 'backends' as const;
    if (params.sub === 'models') return 'models' as const;
    return store.subTab();
  };

  onMount(() => {
    activateRoutingPage();
    const sub = subFromRoute();
    store.setSubTab(sub);
    if (sub === 'models') void store.loadModels({ force: true });
    else void store.loadBackends({ force: true });
  });
  onCleanup(() => disposeRoutingPage());

  const runStateChange = async () => {
    const action = confirmAction();
    if (!action) return;
    setBusy(true);
    try {
      await setModelState(action.id, action.action);
      setConfirmAction(null);
    } finally {
      setBusy(false);
    }
  };

  const runRemove = async () => {
    const id = removeTarget();
    if (!id) return;
    setBusy(true);
    try {
      const ok = await removeModel(id, refreshModels);
      if (ok) setRemoveTarget(null);
    } finally {
      setBusy(false);
    }
  };

  const copyText = async (text: string, done: string) => {
    try {
      await navigator.clipboard.writeText(text);
      pushToast(done);
    } catch {
      pushToast('Could not access clipboard.', 'error');
    }
  };

  return (
    <section class="page" id="panel-routing">
      <header class="page-header">
        <div class="titles">
          <p class="eyebrow">Registry</p>
          <h1>Routing</h1>
        </div>
      </header>

      <Tabs
        defaultId={subFromRoute()}
        onChange={(id) => {
          store.setSubTab(id as 'models' | 'backends');
          if (id === 'models') void store.loadModels();
          else void store.loadBackends();
        }}
        tabs={[
          {
            id: 'models',
            label: 'Models',
            content: () => (
              <>
                <div class="toolbar">
                  <label class="filter-field narrow">
                    <span class="icon"><IconSearch /></span>
                    <input value={store.modelsFilter()} onInput={(e) => store.setModelsFilter(e.currentTarget.value)} placeholder="id, url, aliasâ€¦" aria-label="Search models" />
                  </label>
                  <span class="spacer" />
                  <a class="action secondary" href="/v1/models" target="_blank" rel="noopener">
                    <span class="icon"><IconExternalLink /></span> /v1/models
                  </a>
                  <A class="action secondary" href="/providers" title="Add a model from a paid provider's catalogue, with its price filled in">
                    Add from a provider
                  </A>
                  <Button variant="secondary" onClick={refreshModels}>
                    <span class="icon"><IconRefresh /></span> Refresh
                  </Button>
                  <Button variant="primary" onClick={() => openNewModelDrawer()}>
                    <span class="icon"><IconPlus /></span> Add model
                  </Button>
                </div>
                <Show when={store.filteredModels().length > 0}>
                  <div class="table-wrap">
                    <table class="data-table t-models">
                      <colgroup>
                        <col /><col /><col /><col />
                        <col /><col /><col /><col />
                        <col /><col />
                      </colgroup>
                      <thead>
                        <tr>
                          <th>ID</th><th>Upstream URL</th><th>Type</th><th>Aliases</th><th class="num">Context</th>
                          <th class="num">Price in/out (1M)</th><th>Access</th><th>Credential</th><th>State</th>
                          <th class="cell-actions"><span class="sr-only">Actions</span></th>
                        </tr>
                      </thead>
                      <tbody>
                        <For each={store.filteredModels()}>
                          {(m) => {
                            const row = normalizeModelItem(m);
                            const id = row.id;
                            const stopped = isModelStopped(m);
                            return (
                              <tr>
                                <td class="cell-code">
                                  <span class="cell-code-inner">
                                    <code title={id}>{id}</code>
                                    <button type="button" class="icon-btn" title="Copy ID" aria-label="Copy ID" onClick={() => void copyText(id, 'Model ID copied.')}>
                                      <span class="icon"><IconCopy /></span>
                                    </button>
                                  </span>
                                </td>
                                <td class="cell-code">
                                  <span class="cell-code-inner">
                                    <code title={row.url}>{row.url || 'â€”'}</code>
                                    <button type="button" class="icon-btn" title="Copy URL" aria-label="Copy URL" onClick={() => void copyText(row.url, 'URL copied.')}>
                                      <span class="icon"><IconCopy /></span>
                                    </button>
                                  </span>
                                </td>
                                <td>
                                  <span class="tag">{row.modelType ?? 'Text generation'}</span>
                                  <Show when={isPaidProvider(row.provider)}>
                                    {' '}
                                    <span class="tag level-warning" title={`Served by ${providerName(row.provider)}: each request is billed`}>
                                      Paid
                                    </span>
                                  </Show>
                                </td>
                                <td class="col-truncate" title={row.aliases.join(', ')}>{row.aliases.join(', ') || 'â€”'}</td>
                                <td class="num">{row.maxContextLength ? formatNum(row.maxContextLength) : 'â€”'}</td>
                                <td class="num">{formatModelPrice(row.pricing)}</td>
                                <td>
                                  <span class="status-chip" classList={{ warn: row.publicAccess, ok: !row.publicAccess }}>
                                    {row.publicAccess ? 'Public' : 'Key required'}
                                  </span>
                                </td>
                                <td>{row.hasUpstreamCredential ? <span class="tag">stored</span> : <span class="muted">â€”</span>}</td>
                                <td><span class="status-chip" classList={{ fail: stopped, ok: !stopped }}>{stopped ? 'Stopped' : 'Serving'}</span></td>
                                <td class="cell-actions">
                                  <span class="row-actions">
                                    <button type="button" class="icon-btn" title="Test model" aria-label="Test model" onClick={() => startTest(id, row.provider)}>
                                      <span class="icon"><IconPlay /></span>
                                    </button>
                                    <Show when={!stopped}>
                                      <button type="button" class="icon-btn" title="Stop model" aria-label="Stop model" onClick={() => setConfirmAction({ id, action: 'stop' })}>
                                        <span class="icon"><IconPause /></span>
                                      </button>
                                    </Show>
                                    <Show when={stopped}>
                                      <button type="button" class="icon-btn" title="Start model" aria-label="Start model" onClick={() => setConfirmAction({ id, action: 'start' })}>
                                        <span class="icon"><IconPlayCircle /></span>
                                      </button>
                                    </Show>
                                    <button type="button" class="icon-btn" title="Edit model" aria-label="Edit model" onClick={() => openEditModelDrawer(m)}>
                                      <span class="icon"><IconEdit /></span>
                                    </button>
                                    <button type="button" class="icon-btn danger" title="Remove model" aria-label="Remove model" onClick={() => setRemoveTarget(id)}>
                                      <span class="icon"><IconTrash /></span>
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
                </Show>
                <Show when={store.filteredModels().length === 0 && store.modelsPhase() !== 'loading'}>
                  <div class="empty-state">
                    <span class="empty-state-icon icon"><IconCpu /></span>
                    <p>{store.modelsFilter() ? 'No models match this filter.' : 'No models registered. Add one to start routing inference traffic.'}</p>
                    <Show when={!store.modelsFilter()}>
                      <Button onClick={() => openNewModelDrawer()}><span class="icon"><IconPlus /></span> Add your first model</Button>
                    </Show>
                  </div>
                </Show>
              </>
            ),
          },
          {
            id: 'backends',
            label: 'Backends & health',
            content: () => (
              <>
                <div class="toolbar">
                  <label class="filter-field">
                    <span class="icon"><IconSearch /></span>
                    <input value={store.backendsFilter()} onInput={(e) => store.setBackendsFilter(e.currentTarget.value)} placeholder="model, backend, urlâ€¦" aria-label="Filter backends" />
                  </label>
                  <span class="spacer" />
                  <Button variant="secondary" onClick={() => store.loadBackends({ force: true })}>
                    <span class="icon"><IconRefresh /></span> Refresh
                  </Button>
                </div>
                <Show when={store.filteredBackends().length > 0}>
                  <div class="table-wrap">
                    <table class="data-table t-backends">
                      <colgroup>
                        <col /><col /><col /><col />
                        <col /><col /><col />
                      </colgroup>
                      <thead>
                        <tr>
                          <th>Model</th><th>Backend</th><th>URL</th><th class="num">Context</th>
                          <th class="num" title="Requests this gateway is forwarding to the backend right now">In flight</th><th>Health</th>
                          <th class="cell-actions"><span class="sr-only">Actions</span></th>
                        </tr>
                      </thead>
                      <tbody>
                        <For each={store.filteredBackends()}>
                          {(b) => {
                            const stopped = String(b.state ?? 'serving').toLowerCase() === 'stopped';
                            const modelId = String(b.modelId ?? '');
                            const backend = String(b.backend ?? 'default');
                            const context = Number(b.maxContextLength ?? 0);
                            const healthy = !!b.isHealthy;
                            return (
                              <tr>
                                <td class="cell-code"><span class="cell-code-inner"><code title={modelId}>{modelId}</code></span></td>
                                <td class="cell-code">
                                  <span class="cell-code-inner"><code title={backend}>{backend}</code></span>
                                  <Show when={String(b.priority ?? 'primary') === 'standby'}>
                                    {' '}
                                    <span class="tag" title="Takes a request only when no primary backend of the model can">standby</span>
                                  </Show>
                                  <Show when={isPaidProvider(String(b.provider ?? ''))}>
                                    {' '}
                                    <span class="tag level-warning">Paid آ· {providerName(String(b.provider))}</span>
                                  </Show>
                                </td>
                                <td class="cell-code"><span class="cell-code-inner"><code title={String(b.url ?? '')}>{String(b.url ?? 'â€”')}</code></span></td>
                                <td class="num">{context > 0 ? context.toLocaleString() : 'â€”'}</td>
                                <td class="num">{Number(b.inFlight ?? 0).toLocaleString()}</td>
                                <td>
                                  <span class="cell-health-inner">
                                    <span class="health-dot" classList={{ 'dot-ok': healthy && !stopped, 'dot-fail': !healthy && !stopped, 'dot-idle': stopped }} />
                                    <span title={String(b.error ?? '')}>{stopped ? 'Stopped' : healthy ? 'Healthy' : 'Unhealthy'}</span>
                                  </span>
                                </td>
                                <td class="cell-actions">
                                  <Button
                                    variant="ghost"
                                    size="sm"
                                    title={stopped
                                      ? 'Send new requests to this backend again'
                                      : 'Send no new requests to this backend; the ones in flight finish'}
                                    onClick={() => void store.setBackendState(modelId, backend, stopped ? 'start' : 'stop')}
                                  >
                                    {stopped ? 'Start' : 'Stop'}
                                  </Button>
                                  <Button variant="ghost" size="sm" onClick={() => {
                                    store.setSubTab('models');
                                    const match = store.filteredModels().find((m) => String(m.id ?? (m.model as { id?: string })?.id) === modelId);
                                    if (match) openEditModelDrawer(match);
                                  }}>
                                    <span class="icon"><IconEdit /></span> Edit
                                  </Button>
                                </td>
                              </tr>
                            );
                          }}
                        </For>
                      </tbody>
                    </table>
                  </div>
                </Show>
                <Show when={store.filteredBackends().length === 0 && store.backendsPhase() !== 'loading'}>
                  <div class="empty-state">
                    <span class="empty-state-icon icon"><IconServer /></span>
                    <p>No backends â€” register models first.</p>
                  </div>
                </Show>
              </>
            ),
          },
        ]}
      />

      <ModelDrawer onSaved={refreshModels} />

      <Dialog
        open={!!confirmAction()}
        title={confirmAction()?.action === 'stop' ? 'Stop model?' : 'Start model?'}
        onClose={() => setConfirmAction(null)}
      >
        <Show when={confirmAction()?.action === 'stop'}>
          <p>
            â€œ{confirmAction()?.id}â€‌ stops serving: it disappears from /v1/models and requests for it are rejected.
            Its aliases, credential, pricing and grants are kept, so you can start it again.
          </p>
        </Show>
        <Show when={confirmAction()?.action === 'start'}>
          <p>Start â€œ{confirmAction()?.id}â€‌ and resume serving inference traffic.</p>
        </Show>
        <div class="modal-actions">
          <Button variant="secondary" onClick={() => setConfirmAction(null)}>Cancel</Button>
          <Button variant={confirmAction()?.action === 'stop' ? 'danger' : 'primary'} onClick={runStateChange} disabled={busy()}>
            {confirmAction()?.action === 'stop' ? 'Stop' : 'Start'}
          </Button>
        </div>
      </Dialog>

      <Dialog open={!!removeTarget()} title="Remove model?" onClose={() => setRemoveTarget(null)}>
        <p>Remove â€œ{removeTarget()}â€‌ from the registry? This cannot be undone.</p>
        <div class="modal-actions">
          <Button variant="secondary" onClick={() => setRemoveTarget(null)}>Cancel</Button>
          <Button variant="danger" onClick={runRemove} disabled={busy()}>Remove</Button>
        </div>
      </Dialog>

      <Dialog open={!!paidTest()} title="Send a paid test request?" onClose={() => setPaidTest(null)}>
        <p>
          â€œ{paidTest()?.id}â€‌ is served by {providerName(paidTest()?.provider ?? '')}. The test sends one real request
          with the provider key, and {providerName(paidTest()?.provider ?? '')} charges for it.
        </p>
        <div class="modal-actions">
          <Button variant="secondary" onClick={() => setPaidTest(null)}>Cancel</Button>
          <Button
            variant="primary"
            onClick={() => {
              const target = paidTest();
              setPaidTest(null);
              if (target) void testModel(target.id);
            }}
          >
            Send the test
          </Button>
        </div>
      </Dialog>

      <Dialog
        open={!!models.testDialog()}
        title="Model test"
        onClose={() => closeTestDialog()}
      >
        <Show when={models.testDialog()?.loading}>
          <p class="loading-hint">Testing modelâ€¦</p>
        </Show>
        <Show when={!models.testDialog()?.loading && models.testDialog()?.error}>
          <p class="hint error-text">{models.testDialog()?.error}</p>
        </Show>
        <Show when={models.testDialog()?.result}>
          {(result) => (
            <div>
              <Show when={result().supported === false}>
                <p>No automated health check exists for this model type.</p>
              </Show>
              <Show when={result().supported !== false}>
                <p class={result().ok ? 'hint' : 'hint error-text'}>
                  {result().ok ? 'Test succeeded.' : result().detail || 'Test failed.'}
                </p>
                <Show when={result().endpoint}><p>Endpoint: {result().endpoint}</p></Show>
                <Show when={result().latencyMs != null}><p>Latency: {result().latencyMs} ms</p></Show>
                <Show when={result().statusCode != null}><p>Status: {result().statusCode}</p></Show>
                <Show when={result().hint}><p class="hint">{result().hint}</p></Show>
              </Show>
            </div>
          )}
        </Show>
        <div class="modal-actions">
          <Button variant="secondary" onClick={() => closeTestDialog()}>Close</Button>
          <Button variant="primary" onClick={() => rerunModelTest()} disabled={models.testDialog()?.loading}>
            Rerun
          </Button>
        </div>
      </Dialog>
    </section>
  );
}
