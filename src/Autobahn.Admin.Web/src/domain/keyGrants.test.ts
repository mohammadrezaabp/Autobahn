import { describe, expect, it } from 'vitest';
import {
  buildGrantChoices,
  buildReplaceGrantsPayload,
  canGrantKey,
  describeKeyAccess,
  normalizeGrantModels,
  normalizePrefix,
  toggleGrantSelection,
} from './keyGrants';

describe('keyGrants', () => {
  it('canGrantKey_rejectsAdminAndRevoked', () => {
    expect(canGrantKey({ role: 'Admin' })).toBe(false);
    expect(canGrantKey({ role: 'Inference', isRevoked: true })).toBe(false);
    expect(canGrantKey({ role: 'Inference', active: true })).toBe(true);
  });

  it('toggleGrantSelection_addsAndRemoves', () => {
    expect(toggleGrantSelection(['a'], 'b')).toEqual(['a', 'b']);
    expect(toggleGrantSelection(['a', 'b'], 'a')).toEqual(['b']);
  });

  it('buildReplaceGrantsPayload_copiesIds', () => {
    expect(buildReplaceGrantsPayload(['m1'])).toEqual({ modelIds: ['m1'] });
  });
});

describe('buildReplaceGrantsPayload', () => {
  it('leavesOutAModelTheRegistryNoLongerHas', () => {
    const registry = [{ id: 'M1', serving: true, healthy: null }];
    expect(buildReplaceGrantsPayload(['m1', 'gone'], registry)).toEqual({ modelIds: ['m1'] });
  });

  it('sendsEverythingWhenTheRegistryIsNotKnown', () => {
    expect(buildReplaceGrantsPayload(['m1', 'gone'], null)).toEqual({ modelIds: ['m1', 'gone'] });
  });

  it('carriesPatternsAndHeldBackModels_andDropsAHeldBackModelThatIsGone', () => {
    const registry = [{ id: 'qwen/big', serving: true, healthy: null }];
    expect(buildReplaceGrantsPayload([], registry, ['qwen/*'], ['qwen/big', 'gone'])).toEqual({
      modelIds: [],
      patterns: ['qwen/*'],
      deniedModelIds: ['qwen/big'],
    });
  });
});

describe('paid providers', () => {
  const registry = [
    { id: 'gemma', serving: true, healthy: true },
    { id: 'qwen/paid', serving: true, healthy: true, provider: 'openrouter' },
  ];

  it('normalizeGrantModels_readsTheProviderOffTheListItem', () => {
    expect(normalizeGrantModels([{ model: { id: 'a' }, provider: 'openrouter' }, { model: { id: 'b' }, provider: 'self-hosted' }]))
      .toEqual([
        { id: 'a', serving: true, healthy: null, provider: 'openrouter' },
        { id: 'b', serving: true, healthy: null },
      ]);
  });

  it('aPaidModelCannotBeTickedWhileItsProviderIsOff_butCanBeUnticked', () => {
    expect(buildGrantChoices(registry, [], null, ['openrouter'])).toEqual([
      { id: 'gemma', note: '', disabled: false },
      { id: 'qwen/paid', note: 'provider is off', disabled: true, paid: true },
    ]);
    expect(buildGrantChoices(registry, ['qwen/paid'], null, ['openrouter'])[1].disabled).toBe(false);
    expect(buildGrantChoices(registry, [], null, [])[1]).toEqual({ id: 'qwen/paid', note: '', disabled: false, paid: true });
  });

  it('normalizePrefix_acceptsOnlyATrailingStar', () => {
    expect(normalizePrefix(' qwen/ ')).toEqual({ pattern: 'qwen/*' });
    expect(normalizePrefix('qwen/*')).toEqual({ pattern: 'qwen/*' });
    expect(normalizePrefix('*').error).toBeTruthy();
    expect(normalizePrefix('q*en/').error).toBeTruthy();
  });

  it('describeKeyAccess_saysWhatTheKeyCanCallAfterTheSave', () => {
    const openRouter = { provider: 'openrouter', displayName: 'OpenRouter' };
    expect(describeKeyAccess([], [], [], null)).toContain('refused on every model');
    expect(describeKeyAccess(['a', 'b'], ['qwen/*'], ['qwen/big'], [{ ...openRouter, open: true }]))
      .toBe('After saving, this key can call 2 chosen models and every model starting with qwen/, except 1 held back.');
    expect(describeKeyAccess([], ['openrouter:*'], [], [{ ...openRouter, open: true }]))
      .toBe('After saving, this key can call every OpenRouter model.');
    expect(describeKeyAccess(['a'], [], [], [{ ...openRouter, open: false }]))
      .toContain('OpenRouter stays closed for it');
  });
});

describe('normalizeGrantModels', () => {
  it('readsStateAndHealthAndSortsById', () => {
    expect(
      normalizeGrantModels(
        [{ model: { id: 'b', state: 'Stopped' } }, { id: 'a' }, { model: { id: 'A' } }, null],
        [{ modelId: 'A', isHealthy: false }],
      ),
    ).toEqual([
      { id: 'a', serving: true, healthy: false },
      { id: 'b', serving: false, healthy: null },
    ]);
  });

  it('returnsNothingForAnUnexpectedShape', () => {
    expect(normalizeGrantModels({ error: 'forbidden' })).toEqual([]);
  });
});

describe('buildGrantChoices', () => {
  const registry = [
    { id: 'serving', serving: true, healthy: true },
    { id: 'stopped', serving: false, healthy: null },
  ];

  it('offersAStoppedModelOnlyWhileTheKeyHoldsIt', () => {
    expect(buildGrantChoices(registry, [], null).map((c) => c.id)).toEqual(['serving']);
    expect(buildGrantChoices(registry, ['stopped'], null)).toContainEqual({ id: 'stopped', note: 'stopped', disabled: false });
  });

  it('doesNotLetAModelOutsideTheTenantBeTickedButLetsItBeUnticked', () => {
    expect(buildGrantChoices(registry, [], ['other'])).toEqual([
      { id: 'serving', note: 'not allowed for the tenant', disabled: true },
    ]);
    expect(buildGrantChoices(registry, ['serving'], ['other'])[0].disabled).toBe(false);
  });
});
