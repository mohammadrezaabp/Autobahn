import { For, Show, createEffect, createSignal, onCleanup, onMount } from 'solid-js';
import { useNavigate, useSearchParams } from '@solidjs/router';
import { Button, Dialog, Tabs } from '../../components/primitives';
import { IconActivity, IconCheckCircle, IconRefresh } from '../../components/icons';
import { formatTime } from '../../domain/format';
import { activateCorsPanel, disposeCorsPanel, loadCors, saveCors, useCorsStore } from '../../stores/cors';
import {
  activateSettingsPage,
  disposeSettingsPage,
  reloadConfigFromDisk,
  useSettingsStore,
  type SettingsSubTab,
} from '../../stores/settings';

export default function SettingsPage() {
  const store = useSettingsStore();
  const cors = useCorsStore();
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const [reloadConfirm, setReloadConfirm] = createSignal(false);
  const [reloadBusy, setReloadBusy] = createSignal(false);

  // Before the tabs are created, not in onMount: Tabs reads its starting tab once, so a
  // `?sub=cors` link applied after the first render would still open on Runtime.
  const sub = params.sub as string | undefined;
  if (sub === 'cors' || sub === 'observability' || sub === 'runtime') {
    store.setSubTab(sub);
  }
  // Rate limits and model access were tabs here once; links to them still land on the right page.
  if (sub === 'ratelimits') navigate('/ratelimits', { replace: true });
  if (sub === 'access') navigate('/tenants', { replace: true });

  onMount(() => activateSettingsPage());
  onCleanup(() => {
    disposeSettingsPage();
    disposeCorsPanel();
  });

  createEffect(() => {
    void store.load();
  });

  createEffect(() => {
    if (store.subTab() === 'cors') activateCorsPanel();
    else disposeCorsPanel();
  });

  const status = () => store.configStatus();

  const runReload = async () => {
    setReloadBusy(true);
    try {
      await reloadConfigFromDisk();
      setReloadConfirm(false);
    } finally {
      setReloadBusy(false);
    }
  };

  return (
    <section class="page" id="panel-settings">
      <header class="page-header">
        <div class="titles">
          
          <h2>Settings</h2>
          
        </div>
        <Button variant="secondary" onClick={() => void store.load({ force: true })} disabled={store.phase() === 'loading'}>
          <span class="icon"><IconRefresh /></span> Reload all
        </Button>
      </header>

      <Tabs
        defaultId={store.subTab()}
        onChange={(id) => store.setSubTab(id as SettingsSubTab)}
        tabs={[
          {
            id: 'runtime',
            label: 'Runtime',
            content: () => (
              <div class="card">
                <span class="eyebrow">Config file</span>
                <h3>Runtime configuration</h3>
                <Show when={status()}>
                  {(s) => (
                    <p class="hint">
                      Hot reload <strong>{s().hotReloadEnabled ? 'on' : 'off'}</strong> ·
                      File watch <strong>{s().watchEnabled ? 'on' : 'off'}</strong> ·{' '}
                      <strong>{String(s().modelCount ?? (s().models as unknown[])?.length ?? '—')}</strong> models in file
                      <Show when={s().lastReload}> · last reload {formatTime(s().lastReload)}</Show>
                    </p>
                  )}
                </Show>
                
                <div class="toolbar mb-0">
                  <Button onClick={() => setReloadConfirm(true)} disabled={store.phase() === 'loading'}>
                    <span class="icon"><IconRefresh /></span> Reload config from disk
                  </Button>
                  <Button variant="secondary" onClick={() => void store.load({ force: true })} disabled={store.phase() === 'loading'}>
                    Refresh status
                  </Button>
                </div>
              </div>
            ),
          },
          {
            id: 'cors',
            label: 'CORS',
            content: () => (
              <div class="card">
                <span class="eyebrow">Browser origins</span>
                <h3>CORS allowed origins</h3>
                
                <Show when={cors.loadError()}>
                  <p class="notice error">{cors.loadError()}</p>
                </Show>
                <Show when={cors.loading() && cors.origins() == null}>
                  <p class="loading-hint">Loading CORS settings…</p>
                </Show>
                <Show when={cors.origins() != null}>
                  <For each={cors.origins() ?? []}>
                    {(origin, index) => (
                      <div class="repeat-row">
                        <label>
                          Origin
                          <input
                            type="text"
                            value={origin}
                            placeholder="https://example.com"
                            onInput={(e) => cors.updateRow(index(), e.currentTarget.value)}
                          />
                        </label>
                        <Button variant="secondary" size="sm" onClick={() => cors.removeRow(index())}>Remove</Button>
                      </div>
                    )}
                  </For>
                  <div class="toolbar">
                    <Button variant="secondary" size="sm" onClick={() => cors.addRow()}>Add origin</Button>
                    <span class="spacer" />
                    <Button variant="secondary" onClick={() => void loadCors()} disabled={cors.loading()}>
                      <span class="icon"><IconRefresh /></span> Reload
                    </Button>
                    <Button size="sm" onClick={() => void saveCors()} disabled={cors.loading()}>Save CORS</Button>
                  </div>
                  <Show when={cors.fieldError()}>
                    <p class="field-error">{cors.fieldError()}</p>
                  </Show>
                </Show>
              </div>
            ),
          },
          {
            id: 'observability',
            label: 'Observability',
            content: () => (
              <div class="card">
                <span class="eyebrow">Endpoints</span>
                <h3>Observability</h3>
                
                <div class="link-grid">
                  <a class="action secondary" href="/metrics" target="_blank" rel="noopener">
                    <span class="icon"><IconActivity /></span> Prometheus /metrics
                  </a>
                  <a class="action secondary" href="/health" target="_blank" rel="noopener">
                    <span class="icon"><IconActivity /></span> Health
                  </a>
                  <a class="action secondary" href="/health/live" target="_blank" rel="noopener">
                    <span class="icon"><IconActivity /></span> Liveness
                  </a>
                  <a class="action secondary" href="/health/ready" target="_blank" rel="noopener">
                    <span class="icon"><IconCheckCircle /></span> Readiness
                  </a>
                </div>
              </div>
            ),
          },
        ]}
      />

      <Dialog open={reloadConfirm()} title="Reload config from disk?" onClose={() => setReloadConfirm(false)}>
        <p>Reload models and configuration from disk? Running traffic may briefly see registry changes.</p>
        <div class="modal-actions">
          <Button variant="secondary" onClick={() => setReloadConfirm(false)}>Cancel</Button>
          <Button onClick={runReload} disabled={reloadBusy()}>{reloadBusy() ? 'Reloading…' : 'Reload'}</Button>
        </div>
      </Dialog>
    </section>
  );
}
