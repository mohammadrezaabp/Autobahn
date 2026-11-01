import { describe, expect, it } from 'vitest';
import type { AgentStatus, StoreModel } from '../types';
import { deployable, gpuUse, holders, modelArg, parseArgs, servedNameOf, serverName, suggestPort, takenPorts, usualImage } from './deploy';

function model(over: Partial<StoreModel> = {}): StoreModel {
  return {
    path: '/data/huggingface/hub/models--Qwen--Qwen3.8-27B', name: 'Qwen/Qwen3.8-27B', kind: 'hub',
    repoId: 'Qwen/Qwen3.8-27B', repoType: 'model', sizeBytes: 1, fileCount: 1, modifiedAt: 1, incomplete: false,
    revisions: ['abc'], inUseBy: [], downloading: false, ...over,
  };
}

const status = {
  gpu: {
    gpus: [
      { index: 2, processes: [{ servedName: 'chat', kind: 'model' }, { servedName: 'chat', kind: 'model' }] },
      { index: 3, processes: [{ servedName: '', kind: 'other' }] },
      { index: 4, processes: [] },
    ],
  },
  servers: [{ port: 8000, hostPorts: [2238] }, { port: null, hostPorts: [] }],
} as unknown as AgentStatus;

describe('a server for a model in the store', () => {
  it('is offered for a whole model the agent can mount', () => {
    expect(deployable(model())).toBe(true);
    expect(deployable(model({ kind: 'flat', repoId: '' }))).toBe(true);
    expect(deployable(model({ incomplete: true }))).toBe(false);
    expect(deployable(model({ downloading: true }))).toBe(false);
    expect(deployable(model({ kind: 'stray' }))).toBe(false);
    expect(deployable(model({ repoType: 'dataset' }))).toBe(false);
  });

  it('names the model as the inventory does', () => {
    expect(modelArg(model())).toBe('Qwen/Qwen3.8-27B');
    expect(modelArg(model({ kind: 'flat', path: '/data/huggingface/hub2/M', name: 'M' }))).toBe('/data/huggingface/hub2/M');
    expect(servedNameOf(model())).toBe('Qwen3.8-27B');
  });

  it('makes a name the agent accepts', () => {
    expect(serverName('Qwen3.8-27B')).toBe('qwen3-8-27b');
    expect(serverName('  --Easy_Chat!! ')).toBe('easy-chat');
    expect(serverName('x'.repeat(39) + '-yz')).toBe('x'.repeat(39));
  });

  it('reads one flag to a line and keeps its value whole', () => {
    expect(parseArgs('--max-model-len 65536\n\n  --trust-remote-code  \n--hf-overrides {"a": "b c"}')).toEqual([
      '--max-model-len', '65536', '--trust-remote-code', '--hf-overrides', '{"a": "b c"}',
    ]);
    expect(parseArgs(' \n')).toEqual([]);
  });

  it('suggests the lowest port nothing has', () => {
    const taken = takenPorts([{ name: 'a', state: 'ready', port: 2300 }, { name: 'b', state: 'absent' }], status);
    expect(taken).toEqual([2238, 2300, 8000]);
    expect(suggestPort([2300, 2399], taken)).toBe(2301);
    expect(suggestPort([2300, 2300], taken)).toBeNull();
  });

  it('says what is on a GPU', () => {
    expect(gpuUse(status, 2)).toBe('chat');
    expect(gpuUse(status, 3)).toBe('another process');
    expect(gpuUse(status, 4)).toBe('');
    expect(gpuUse(null, 4)).toBe('');
  });

  it('finds the servers of ours that hold a chosen GPU', () => {
    const servers = [{ name: 'a', state: 'ready', gpus: [6, 7] }, { name: 'b', state: 'ready', gpus: [4] },
      { name: 'gone', state: 'absent', gpus: [4] }, { name: 'c', state: 'stopped' }];
    expect(holders(servers, [4, 7]).map((s) => s.name)).toEqual(['a', 'b']);
    expect(holders(servers, [])).toEqual([]);
  });

  it('offers the image most servers there run', () => {
    expect(usualImage([{ name: 'a', state: 'ready', image: 'v:2' }, { name: 'b', state: 'ready', image: 'v:1' },
      { name: 'c', state: 'ready', image: 'v:1' }, { name: 'd', state: 'absent' }])).toBe('v:1');
    expect(usualImage([])).toBe('');
  });
});
