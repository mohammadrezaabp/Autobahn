import { beforeEach, describe, expect, it, vi } from 'vitest';

vi.mock('./auth', () => ({
  apiClient: { apiJson: vi.fn() },
  handleApiError: vi.fn(),
  pushToast: vi.fn(),
}));

import { apiClient, handleApiError } from './auth';
import {
  addPrefix,
  closeDrawer,
  openKeyAccessDrawer,
  saveGrants,
  setProviderOpen,
  toggleModel,
  useKeyGrantsStore,
} from './keyGrants';

interface Gateway {
  models?: unknown;
  backends?: unknown;
  tenant?: unknown;
  key?: unknown;
  providers?: unknown;
}

/** Answers by path, since the drawer reads the registry, the tenant and the key together. */
function gateway(answers: Gateway) {
  vi.mocked(apiClient.apiJson).mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'PUT') return {};
    const answer = url === '/admin/api/models' ? answers.models
      : url === '/admin/api/backends' ? answers.backends ?? []
      : url === '/admin/api/tenant/model-grants' ? answers.tenant ?? { modelIds: [], usesDefaultAccess: true }
      : url.endsWith('/provider-access') ? answers.providers ?? {}
      : answers.key ?? { modelIds: [] };
    if (answer instanceof Error) throw answer;
    return answer;
  });
}

const putBody = () => {
  const call = vi.mocked(apiClient.apiJson).mock.calls.find(([, init]) => init?.method === 'PUT');
  return JSON.parse(String(call?.[1]?.body));
};

const openRouter = (open: boolean, tenantOpen: boolean) => ({
  providers: [
    { provider: 'self-hosted', displayName: 'Self-hosted', paid: false, open: true, tenantOpen: true },
    { provider: 'openrouter', displayName: 'OpenRouter', paid: true, open, tenantOpen },
  ],
});

describe('keyGrants store, paid providers', () => {
  beforeEach(() => {
    vi.mocked(apiClient.apiJson).mockReset();
    vi.mocked(handleApiError).mockReset();
    closeDrawer();
  });

  it('keepsTheKeysPatternsAndHeldBackModelsThroughASave', async () => {
    gateway({
      models: [{ id: 'm1' }, { id: 'qwen/big' }],
      key: { modelIds: ['m1'], patterns: ['qwen/*'], deniedModelIds: ['qwen/big'] },
    });

    await openKeyAccessDrawer({ id: 'key-1' });
    await saveGrants();

    expect(putBody()).toEqual({ modelIds: ['m1'], patterns: ['qwen/*'], deniedModelIds: ['qwen/big'] });
  });

  it('writesAChangedSwitchBeforeTheList_andLeavesAnUnchangedOneAlone', async () => {
    gateway({ models: [{ model: { id: 'qwen/paid' }, provider: 'openrouter' }], providers: openRouter(false, true) });

    await openKeyAccessDrawer({ id: 'key-1' });
    expect(useKeyGrantsStore().choices()[0]).toMatchObject({ note: 'provider is off', disabled: true });

    setProviderOpen('openrouter', true);
    expect(useKeyGrantsStore().choices()[0]).toMatchObject({ note: '', disabled: false, paid: true });
    expect(addPrefix('qwen/')).toBeNull();
    await saveGrants();

    const puts = vi.mocked(apiClient.apiJson).mock.calls.filter(([, init]) => init?.method === 'PUT');
    expect(puts.map(([url]) => url)).toEqual([
      '/admin/api/keys/key-1/provider-access/openrouter',
      '/admin/api/keys/key-1/model-grants',
    ]);
    expect(JSON.parse(String(puts[0][1]?.body))).toEqual({ open: true });
    expect(JSON.parse(String(puts[1][1]?.body))).toEqual({ modelIds: [], patterns: ['qwen/*'] });
  });

  it('showsAKeyAsClosedWhileItsTenantIs_andWritesNoSwitch', async () => {
    gateway({ models: [{ id: 'm1' }], providers: openRouter(true, false) });

    await openKeyAccessDrawer({ id: 'key-1' });
    expect(useKeyGrantsStore().isProviderOpen('openrouter')).toBe(false);
    await saveGrants();

    const puts = vi.mocked(apiClient.apiJson).mock.calls.filter(([, init]) => init?.method === 'PUT');
    expect(puts.map(([url]) => url)).toEqual(['/admin/api/keys/key-1/model-grants']);
  });
});

describe('keyGrants store', () => {
  beforeEach(() => {
    vi.mocked(apiClient.apiJson).mockReset();
    vi.mocked(handleApiError).mockReset();
    closeDrawer();
  });

  it('openKeyAccessDrawer_loadsRegistryAndGrants', async () => {
    gateway({ models: [{ id: 'gpt-4' }], key: { modelIds: ['gpt-4'] } });

    await openKeyAccessDrawer({ id: 'key-1', label: 'Bot', keyPrefix: 'sk-x' });

    const store = useKeyGrantsStore();
    expect(store.keyId()).toBe('key-1');
    expect(store.selected()).toEqual(['gpt-4']);
    expect(store.choices().map((c) => c.id)).toEqual(['gpt-4']);
  });

  it('openKeyAccessDrawer_readsTheRegistryAgainOnEveryOpen', async () => {
    gateway({ models: [{ id: 'm1' }] });
    await openKeyAccessDrawer({ id: 'key-1' });
    closeDrawer();

    gateway({ models: [{ id: 'm1' }, { id: 'm2' }] });
    await openKeyAccessDrawer({ id: 'key-1' });

    expect(useKeyGrantsStore().choices().map((c) => c.id)).toEqual(['m1', 'm2']);
  });

  it('openKeyAccessDrawer_offersServingModelsAndMarksTheRest', async () => {
    gateway({
      models: [
        { model: { id: 'up', state: 'serving' } },
        { model: { id: 'down', state: 'serving' } },
        { model: { id: 'stopped-held', state: 'stopped' } },
        { model: { id: 'stopped-other', state: 'stopped' } },
        { model: { id: 'closed', state: 'serving' } },
      ],
      backends: [{ modelId: 'up', isHealthy: true }, { modelId: 'down', isHealthy: false }],
      tenant: { modelIds: ['up', 'down', 'stopped-held'], usesDefaultAccess: false },
      key: { modelIds: ['stopped-held', 'gone'] },
    });

    await openKeyAccessDrawer({ id: 'key-1', tenantId: 't-1' });

    expect(useKeyGrantsStore().choices()).toEqual([
      { id: 'closed', note: 'not allowed for the tenant', disabled: true },
      { id: 'down', note: 'down', disabled: false },
      { id: 'gone', note: 'removed', disabled: false },
      { id: 'stopped-held', note: 'stopped', disabled: false },
      { id: 'up', note: '', disabled: false },
    ]);
  });

  it('openKeyAccessDrawer_showsTheKeysOwnGrantsWhenTheRegistryIsRefused', async () => {
    gateway({ models: new Error('403 Forbidden'), key: { modelIds: ['m1'] } });

    await openKeyAccessDrawer({ id: 'key-1' });

    expect(useKeyGrantsStore().choices()).toEqual([{ id: 'm1', note: '', disabled: false }]);
    expect(handleApiError).toHaveBeenCalled();
  });

  it('saveGrants_putsModelIds', async () => {
    gateway({ models: [{ id: 'm1' }] });

    await openKeyAccessDrawer({ id: 'key-2', keyPrefix: 'sk-y' });
    toggleModel('m1');
    const ok = await saveGrants();

    expect(ok).toBe(true);
    expect(apiClient.apiJson).toHaveBeenCalledWith(
      '/admin/api/keys/key-2/model-grants',
      expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ modelIds: ['m1'] }),
      }),
    );
  });

  it('saveGrants_leavesOutAModelThatIsNoLongerRegistered', async () => {
    gateway({ models: [{ id: 'm1' }], key: { modelIds: ['m1', 'gone'] } });

    await openKeyAccessDrawer({ id: 'key-3' });
    await saveGrants();

    expect(putBody()).toEqual({ modelIds: ['m1'] });
  });

  it('saveGrants_sendsEveryGrantBackWhenTheRegistryIsNotKnown', async () => {
    gateway({ models: new Error('403 Forbidden'), key: { modelIds: ['m1', 'm2'] } });

    await openKeyAccessDrawer({ id: 'key-4' });
    toggleModel('m2');
    await saveGrants();

    expect(putBody()).toEqual({ modelIds: ['m1'] });
  });
});
