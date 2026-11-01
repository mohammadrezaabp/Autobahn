import { For, Show, createEffect, createSignal, onCleanup, onMount, type JSX } from 'solid-js';
import { useLocation, useNavigate } from '@solidjs/router';
import { Alert, Button, ToastContainer } from '../components/primitives';
import {
  IconActivity,
  IconKey,
  IconLogOut,
  IconMonitor,
  IconMoon,
  IconSun,
} from '../components/icons';
import { tabFromPath } from './router';
import { applyTheme, getStoredTheme, type ThemeMode } from './theme';
import {
  clearSession,
  dismissGlobalError,
  keyPrefix,
  signedIn,
  useAuthSignals,
  verifyConnection,
  handleApiError,
  setVerifyConnection,
  requestConnectionRecheck,
} from '../stores/auth';
import { setActiveTab, setErrorsAutoRefresh, setLogsAutoRefresh, useConnectionLifecycle, useConnectionSnapshot } from '../stores/connection';
import { subscribeHealthPolling, useHealthSignals } from '../stores/health';
import { useSummary } from '../stores/summary';
import { clearPanels, closePanelSession, loadPanels, openPanel, usePanels } from '../stores/panels';
import { resetTenants } from '../stores/tenants';
import { resetOwnTenantLimits } from '../stores/tenantLimits';
import { isTenantAdmin, useSession } from '../stores/session';
import { liveBadgeView } from './liveBadge';
import { formatNum } from '../domain/format';

/** `tenant`: also for a tenant's own admin key. `tenantOnly`: for such a key and nobody else. */
const NAV: Array<{ label: string; items: Array<{ key: string; label: string; path: string; tenant?: boolean; tenantOnly?: boolean; compute?: boolean }> }> = [
  { label: 'Workspace', items: [
    { key: 'dashboard', label: 'Overview', path: '/dashboard' },
    { key: 'usage', label: 'Usage & cost', path: '/usage', tenant: true },
  ] },
  { label: 'Gateway', items: [
    { key: 'routing', label: 'Routing', path: '/routing' },
    { key: 'providers', label: 'Providers', path: '/providers' },
    { key: 'ratelimits', label: 'Rate limits', path: '/ratelimits' },
  ] },
  { label: 'Compute', items: [
    { key: 'fleet-gpus', label: 'GPUs', path: '/fleet/gpus', compute: true },
    { key: 'fleet-models', label: 'Models', path: '/fleet/models', compute: true },
    { key: 'fleet-downloads', label: 'Downloads', path: '/fleet/downloads', compute: true },
    { key: 'fleet-activity', label: 'Activity', path: '/fleet/activity', compute: true },
  ] },
  { label: 'Access', items: [
    { key: 'tenants', label: 'Tenants', path: '/tenants' },
    { key: 'keys', label: 'API keys', path: '/keys', tenant: true },
    { key: 'limits', label: 'Limits', path: '/limits', tenant: true, tenantOnly: true },
  ] },
  { label: 'Operations', items: [
    { key: 'logs', label: 'Logs', path: '/logs' },
    { key: 'errors', label: 'Errors', path: '/errors' },
    { key: 'settings', label: 'Settings', path: '/settings' },
  ] },
];

export function Shell(props: { children: JSX.Element }) {
  const location = useLocation();
  const navigate = useNavigate();
  const conn = useConnectionSnapshot();
  const { summary } = useSummary();
  const { toasts, globalError, apiKey } = useAuthSignals();
  const [theme, setTheme] = createSignal<ThemeMode>(getStoredTheme());
  const [showChangeKey, setShowChangeKey] = createSignal(false);
  const [headerKey, setHeaderKey] = createSignal('');
  const [showKey, setShowKey] = createSignal(false);
  const [appVersion, setAppVersion] = createSignal('');
  const [sidebarOpen, setSidebarOpen] = createSignal(false);
  const health = useHealthSignals();
  const { panels } = usePanels();
  const session = useSession();
  const nav = () => NAV.map(group => ({ ...group, items: group.items.filter(item =>
    (isTenantAdmin() ? item.tenant : !item.tenantOnly) && (!item.compute || session()?.operator === true)
  ) })).filter(group => group.items.length > 0);

  setVerifyConnection(verifyConnection);
  useConnectionLifecycle();

  createEffect(() => {
    const tab = tabFromPath(location.pathname);
    setSidebarOpen(false);
    setActiveTab(tab);
    setLogsAutoRefresh(tab === 'logs');
    if (tab !== 'errors') setErrorsAutoRefresh(false);
  });

  onMount(() => {
    applyTheme(theme());
    // A key kept from an earlier visit: ask again who it is, since its tenant may have changed.
    requestConnectionRecheck();
    void fetch('/', { cache: 'no-store' })
      .then((r) => (r.ok ? r.json() : null))
      .then((body) => {
        const version = body?.version;
        if (typeof version === 'string' && version) setAppVersion(`v${version}`);
      })
      .catch(() => {});
  });

  onCleanup(subscribeHealthPolling());

  // Re-read on every key change: whether the panels are offered depends on the key's role.
  createEffect(() => {
    if (apiKey()) {
      void loadPanels();
    } else clearPanels();
    // Another key may be an operator where this one was not, or the other way round.
    resetTenants();
    resetOwnTenantLimits();
  });

  const setThemeMode = (mode: ThemeMode) => {
    setTheme(mode);
    applyTheme(mode);
  };

  const saveHeaderKey = async () => {
    try {
      await verifyConnection(headerKey());
      setShowChangeKey(false);
      setHeaderKey('');
    } catch (e) {
      handleApiError(e);
    }
  };

  const s = () => summary();
  const totalErrors = () => formatNum(s()?.totalErrors ?? 0);
  const activeRequests = () => formatNum(s()?.activeRequests ?? 0);
  const activeStreams = () => formatNum(s()?.activeStreams ?? 0);
  const live = () => liveBadgeView(conn());
  const healthLive = () => health.healthLive();
  const healthReady = () => health.healthReady();

  return (
    <>
      <ToastContainer toasts={toasts()} />
      <Show when={signedIn()}>
        <div class="app-shell ab-application">
          <Show when={sidebarOpen()}><button type="button" class="ab-sidebar-backdrop" aria-label="Close navigation" onClick={() => setSidebarOpen(false)} /></Show>
          <aside id="workspace-navigation" class="ab-sidebar" classList={{ 'is-open': sidebarOpen() }} onKeyDown={event => { if (event.key === 'Escape') setSidebarOpen(false); }}>
            <div class="ab-sidebar-brand">
              <div class="ab-brand">
                <span class="ab-monogram" aria-hidden="true"><i /><i /><i /></span>
                <strong>autobahn<span class="ab-brand-dot">.</span></strong>
              </div>
              <button type="button" class="icon-btn ab-menu-toggle" aria-label="Close navigation" onClick={() => setSidebarOpen(false)}>×</button>
            </div>
            <nav class="ab-sidebar-nav" aria-label="Main">
              <For each={nav()}>{group => (
                <section class="ab-nav-group" aria-label={group.label}>
                  <h2>{group.label}</h2>
                  <For each={group.items}>{item => <a id={`tab-${item.key}`} href={`#${item.path}`} aria-current={tabFromPath(location.pathname) === item.key ? 'page' : undefined} classList={{ active: tabFromPath(location.pathname) === item.key }}>{item.label}</a>}</For>
                </section>
              )}</For>
              <Show when={panels().length > 0}>
                <section class="ab-nav-group" aria-label="Monitoring"><h2>Monitoring</h2>
                  <For each={panels()}>{panel => <button type="button" onClick={() => void openPanel(panel)}>{panel.label} ↗</button>}</For>
                </section>
              </Show>
            </nav>
            <div class="ab-sidebar-footer">
                <span class="ab-version">{appVersion()}</span>
                <div class="theme-switch" role="group" aria-label="Theme">
                  <button type="button" class={theme() === 'light' ? 'active' : ''} aria-pressed={theme() === 'light'} onClick={() => setThemeMode('light')} title="Light"><span class="icon"><IconSun /></span></button>
                  <button type="button" class={theme() === 'dark' ? 'active' : ''} aria-pressed={theme() === 'dark'} onClick={() => setThemeMode('dark')} title="Dark"><span class="icon"><IconMoon /></span></button>
                  <button type="button" class={theme() === 'system' ? 'active' : ''} aria-pressed={theme() === 'system'} onClick={() => setThemeMode('system')} title="Match system"><span class="icon"><IconMonitor /></span></button>
                </div>
            </div>
          </aside>

          <div class="app-main">
            <header class="topbar">
              <button type="button" class="icon-btn ab-menu-toggle" aria-label="Open navigation" aria-controls="workspace-navigation" aria-expanded={sidebarOpen()} onClick={() => setSidebarOpen(true)}>☰</button>
              <div class="vitals">
                <span class={live().className} title={live().title} role="status">
                  <span class="pulse-dot" classList={{ live: live().dotClass === 'live' }} />
                  <span>{live().text}</span>
                </span>
                <Show when={conn().status === 'ok' || conn().status === ''}>
                  <span class="vital-chip is-ok"><span class="pulse-dot" /> Connected</span>
                </Show>
                <Show when={conn().status === 'fail'}>
                  <span class="vital-chip is-fail">Invalid key</span>
                </Show>
                <Show when={conn().degraded && conn().status !== 'fail'}>
                  <span class="vital-chip is-warn">Session check failed</span>
                </Show>
                <Show when={healthLive() !== null}>
                  <span class={`vital-chip ${healthLive() ? 'is-ok' : 'is-fail'}`}>
                    <span class="pulse-dot" classList={{ live: healthLive() === true }} />
                    {healthLive() ? 'Live' : 'Live down'}
                  </span>
                </Show>
                <Show when={healthReady() !== null}>
                  <span class={`vital-chip ${healthReady() ? 'is-ok' : 'is-fail'}`}>
                    <span class="vc-label">Ready</span>
                    <span class="vc-value">{healthReady() ? 'ok' : 'no'}</span>
                  </span>
                </Show>
                <Show when={Number(s()?.activeRequests ?? 0) > 0}>
                  <span class="vital-chip is-live">
                    <span class="pulse-dot live" />
                    <span class="vc-value">{activeRequests()}</span> <span class="vc-label">in flight</span>
                  </span>
                </Show>
                <Show when={Number(s()?.activeStreams ?? 0) > 0}>
                  <span class="vital-chip is-live">
                    <span class="pulse-dot live" />
                    <span class="vc-value">{activeStreams()}</span> <span class="vc-label">streams</span>
                  </span>
                </Show>
                <Show when={Number(s()?.totalErrors ?? 0) > 0}>
                  <button type="button" class="vital-chip is-fail is-actionable" onClick={() => navigate('/errors')}>
                    <span class="vc-value">{totalErrors()}</span> <span class="vc-label">errors</span>
                  </button>
                </Show>
              </div>
              <div class="topbar-right">
                <Show when={isTenantAdmin()}>
                  <span class="key-tag" title="This key manages one tenant.">
                    <span class="vc-label">tenant</span> <code>{session()?.tenantName}</code>
                  </span>
                </Show>
                <span class="key-tag">
                  <span class="vc-label">key</span> <code>{keyPrefix()}</code>
                </span>
                <button type="button" class="icon-btn" title="Change key" aria-label="Change key" onClick={() => setShowChangeKey((v) => !v)}>
                  <span class="icon"><IconKey /></span>
                </button>
                <Button variant="ghost" size="sm" onClick={() => { closePanelSession(); clearSession(); navigate('/dashboard'); }}>
                  <span class="icon"><IconLogOut /></span> Sign out
                </Button>
              </div>
            </header>

            <Show when={showChangeKey()}>
              <div class="card change-key-panel">
                <label for="header-apiKey">Admin API key</label>
                <div class="key-row">
                  <input
                    id="header-apiKey"
                    type={showKey() ? 'text' : 'password'}
                    value={headerKey()}
                    onInput={(e) => setHeaderKey(e.currentTarget.value)}
                    placeholder="Paste the new admin key"
                    onKeyDown={(e) => e.key === 'Enter' && (e.preventDefault(), saveHeaderKey())}
                    autocomplete="off"
                  />
                  <button type="button" class="icon-btn" onClick={() => setShowKey((v) => !v)} aria-label="Toggle key visibility">
                    <span class="icon"><IconActivity /></span>
                  </button>
                  <Button onClick={saveHeaderKey}>Save</Button>
                </div>
              </div>
            </Show>

            <Show when={globalError()}>
              <Alert
                title={globalError()!.title}
                message={globalError()!.message}
                detail={globalError()!.detail}
                onDismiss={dismissGlobalError}
              />
            </Show>

            <main class="page-content" role="main" data-solid-root>
              {props.children}
            </main>
          </div>
        </div>
      </Show>
    </>
  );
}
