import { Show, lazy, type Component } from 'solid-js';
import type { RouteDefinition } from '@solidjs/router';
import { Navigate } from '@solidjs/router';
import { TENANT_HOME, isTenantAdmin } from '../stores/session';

const OverviewPage = lazy(() => import('../pages/overview/OverviewPage'));
const UsagePage = lazy(() => import('../pages/usage/UsagePage'));
const RoutingPage = lazy(() => import('../pages/routing/RoutingPage'));
const ProvidersPage = lazy(() => import('../pages/providers/ProvidersPage'));
const ProviderDetailPage = lazy(() => import('../pages/providers/ProviderDetailPage'));
const KeysPage = lazy(() => import('../pages/keys/KeysPage'));
const TenantsPage = lazy(() => import('../pages/tenants/TenantsPage'));
const TenantDetailPage = lazy(() => import('../pages/tenants/TenantDetailPage'));
const RateLimitsPage = lazy(() => import('../pages/ratelimits/RateLimitsPage'));
const LogsPage = lazy(() => import('../pages/logs/LogsPage'));
const ErrorsPage = lazy(() => import('../pages/errors/ErrorsPage'));
const SettingsPage = lazy(() => import('../pages/settings/SettingsPage'));
const LimitsPage = lazy(() => import('../pages/limits/LimitsPage'));
const FleetPage = lazy(() => import('../pages/fleet/FleetPage'));

/**
 * A page whose data only an operator key may read. A tenant's own admin key is sent to its first
 * page instead of being shown a screen of refusals.
 */
function operatorOnly(Page: Component): Component {
  return () => (
    <Show when={!isTenantAdmin()} fallback={<Navigate href={TENANT_HOME} />}>
      <Page />
    </Show>
  );
}

export const routes: RouteDefinition[] = [
  { path: '/', component: () => <Navigate href="/dashboard" /> },
  { path: '/dashboard', component: operatorOnly(OverviewPage) },
  { path: '/usage', component: UsagePage },
  { path: '/routing', component: operatorOnly(RoutingPage) },
  { path: '/providers', component: operatorOnly(ProvidersPage) },
  { path: '/providers/:id', component: operatorOnly(ProviderDetailPage) },
  { path: '/tenants', component: operatorOnly(TenantsPage) },
  { path: '/tenants/:id', component: operatorOnly(TenantDetailPage) },
  { path: '/keys', component: KeysPage },
  { path: '/ratelimits', component: operatorOnly(RateLimitsPage) },
  { path: '/limits', component: LimitsPage },
  { path: '/logs', component: operatorOnly(LogsPage) },
  { path: '/errors', component: operatorOnly(ErrorsPage) },
  { path: '/settings', component: operatorOnly(SettingsPage) },
  { path: '/fleet', component: () => <Navigate href="/fleet/gpus" /> },
  { path: '/fleet/:section', component: operatorOnly(FleetPage) },
  { path: '/models', component: () => <Navigate href="/routing?sub=models" /> },
  { path: '/backends', component: () => <Navigate href="/routing?sub=backends" /> },
  { path: '/*', component: () => <Navigate href="/dashboard" /> },
];

export function tabFromPath(path: string): string {
  // The first segment names the section: `/tenants/<id>` is still the Tenants tab.
  const p = path.replace(/^\//, '').split('/')[0] || 'dashboard';
  if (p === 'fleet') return `fleet-${path.split('/')[2] || 'gpus'}`;
  if (p === 'models' || p === 'backends') return 'routing';
  return p;
}
