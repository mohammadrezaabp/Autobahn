import { beforeEach, describe, expect, it, vi } from 'vitest';

const closeAutobahnFleetSession = vi.hoisted(() => vi.fn());
vi.mock('./autobahn_fleet', () => ({ closeAutobahnFleetSession }));

import { clearSession, persistApiKey } from './auth';

describe('the fleet session follows the key', () => {
  beforeEach(() => {
    persistApiKey('');
    closeAutobahnFleetSession.mockReset();
  });

  it('endsItWhenAKeySignsIn', () => {
    persistApiKey('sk-Autobahn-tenant');
    expect(closeAutobahnFleetSession).toHaveBeenCalledTimes(1);
  });

  it('endsItWhenTheKeyChangesOrSignsOut', () => {
    persistApiKey('sk-Autobahn-operator');
    persistApiKey('sk-Autobahn-tenant');
    clearSession();
    expect(closeAutobahnFleetSession).toHaveBeenCalledTimes(3);
  });

  it('keepsItWhenTheSameKeyIsSavedAgain', () => {
    persistApiKey('sk-Autobahn-operator');
    persistApiKey(' sk-Autobahn-operator ');
    expect(closeAutobahnFleetSession).toHaveBeenCalledTimes(1);
  });
});
