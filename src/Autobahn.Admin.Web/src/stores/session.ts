import { createSignal } from 'solid-js';

/** Who the console is signed in as: the key's own tenant, and whether it manages every tenant. */
export interface AdminSession {
  operator: boolean;
  tenantId: string;
  tenantSlug: string;
  tenantName: string;
}

const STORAGE_KEY = 'Autobahn-admin-session';

/** The path the console sends its sign-in check to. Any admin key may read it. */
export const SESSION_URL = '/admin/api/session';

/** The page a key that is not an operator lands on: the first one it may open. */
export const TENANT_HOME = '/usage';

export function normalizeSession(data: unknown): AdminSession | null {
  if (!data || typeof data !== 'object') return null;
  const s = data as Record<string, unknown>;
  if (typeof s.operator !== 'boolean') return null;
  const slug = String(s.tenantSlug ?? '');
  return {
    operator: s.operator,
    tenantId: String(s.tenantId ?? ''),
    tenantSlug: slug,
    tenantName: String(s.tenantName ?? '') || slug,
  };
}

function readStored(): AdminSession | null {
  try {
    return normalizeSession(JSON.parse(localStorage.getItem(STORAGE_KEY) || 'null'));
  } catch {
    return null;
  }
}

// Kept beside the key, so a reload draws the right menu before the gateway has answered.
const [session, setSessionSignal] = createSignal<AdminSession | null>(readStored());

export function useSession() {
  return session;
}

/** False only once the gateway has said so: an unknown key is shown what an operator is shown. */
export function isTenantAdmin(): boolean {
  return session()?.operator === false;
}

export function setSession(next: AdminSession | null): void {
  setSessionSignal(next);
  if (next) localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
  else localStorage.removeItem(STORAGE_KEY);
}
