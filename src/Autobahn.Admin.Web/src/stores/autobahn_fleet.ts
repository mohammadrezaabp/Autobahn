/** Compute API session managed by the shared Autobahn workspace. */
export const AUTOBAHN_FLEET_PATH = '/admin/fleet/';

/**
 * Signs the browser out of fleet. Its session is a cookie of its own and outlives this console's
 * key, so without this a tab opened after switching to a tenant's key still acted as the operator
 * who opened fleet before. The header is the one fleet asks of its own console's writes.
 */
export function closeAutobahnFleetSession(
  send: (url: string, init: RequestInit) => Promise<Response> = (url, init) => fetch(url, init),
): void {
  void send(`${AUTOBAHN_FLEET_PATH}api/session`, {
    method: 'DELETE',
    credentials: 'same-origin',
    headers: { 'X-AutobahnFleet-Request': '1' },
  }).catch(() => {});
}
