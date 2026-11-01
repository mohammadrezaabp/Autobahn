import { createSignal } from 'solid-js';
import { apiClient, handleApiError, pushToast } from './auth';
import { AUTOBAHN_FLEET_PATH } from './autobahn_fleet';

export { AUTOBAHN_FLEET_PATH, closeAutobahnFleetSession } from './autobahn_fleet';

/** A monitoring tool the gateway serves under /admin (Grafana, Prometheus). */
export interface PanelLink {
  id: string;
  label: string;
  path: string;
  reachable: boolean;
}

const SESSION_URL = '/admin/api/panels/session';

const [panels, setPanels] = createSignal<PanelLink[]>([]);
const [autobahn_fleet, setAutobahnFleet] = createSignal(false);

export function usePanels() {
  return { panels, autobahn_fleet };
}

export function normalizePanelsResponse(data: unknown): PanelLink[] {
  const rows = (data as { panels?: unknown } | null)?.panels;
  if (!Array.isArray(rows)) return [];
  return rows
    .filter((row): row is Record<string, unknown> => !!row && typeof row === 'object')
    .filter((row) => typeof row.id === 'string' && typeof row.path === 'string' && row.path.startsWith('/admin/'))
    .map((row) => ({
      id: row.id as string,
      label: typeof row.label === 'string' && row.label ? row.label : (row.id as string),
      path: row.path as string,
      reachable: row.reachable !== false,
    }));
}

/**
 * Which panels this gateway serves. A gateway without the feature answers 404 and a key without
 * the Operator role 403; both simply mean there is nothing to show.
 */
export async function loadPanels(): Promise<void> {
  try {
    setPanels(normalizePanelsResponse(await apiClient.apiJson('/admin/api/panels')));
  } catch {
    setPanels([]);
  }
}

/**
 * Whether fleet is deployed beside this gateway. Asked of its own health route, without the
 * console's key: the gateway has nothing to say about it, and the key is not fleet's to see here.
 * With no hub on the host the edge answers 503, and with no edge the gateway answers 404.
 */
export async function loadAutobahnFleet(
  probe: (url: string, init: RequestInit) => Promise<Response> = (url, init) => fetch(url, init),
): Promise<void> {
  try {
    setAutobahnFleet((await probe(`${AUTOBAHN_FLEET_PATH}healthz`, { cache: 'no-store' })).ok);
  } catch {
    setAutobahnFleet(false);
  }
}

export function clearPanels(): void {
  setPanels([]);
  setAutobahnFleet(false);
}

/**
 * Opens a panel in a new tab.
 *
 * The console sends its key as a header, which a tab opened on Grafana cannot do, so the gateway
 * is asked for a short-lived cookie first. The tab is opened before that call rather than after
 * it: a window opened once an await has passed is a blocked pop-up in some browsers.
 */
export async function openPanel(
  panel: PanelLink,
  open: (url: string, target: string) => Window | null = (url, target) => window.open(url, target),
): Promise<boolean> {
  const tab = open('', '_blank');
  if (!tab) {
    pushToast(`The browser blocked the ${panel.label} tab. Allow pop-ups for this site.`, 'error');
    return false;
  }
  try {
    await apiClient.apiFetch(SESSION_URL, { method: 'POST' });
    tab.location.replace(panel.path);
    return true;
  } catch (e) {
    tab.close();
    handleApiError(e, 'panels');
    return false;
  }
}

/** Ends the panel session on sign-out. Call it before the key is cleared. */
export function closePanelSession(): void {
  if (panels().length === 0) return;
  void apiClient.apiFetch(SESSION_URL, { method: 'DELETE' }).catch(() => {});
  setPanels([]);
}
