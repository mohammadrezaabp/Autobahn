import { describe, expect, it } from 'vitest';
import type { Gpu, InventoryServer, ManagedServer, StoreModel, WaitingChange } from '../types';
import { askOf, newForm } from './deploy';
import { freed, inventoryPorts, runState, staysShared, strangers, unfiled, waitingOn, wantedOn } from './panel';

function file(name: string, over: Partial<InventoryServer> = {}): InventoryServer {
  return {
    name, path: `inventory/servers/${name}.toml`, content: '', error: '', host: 'gpu2', gpus: [3], shared: false,
    model: 'org/model', revision: '', image: 'vllm/vllm-openai:v1', port: 2301, servedName: name, replaces: [], args: [],
    hasEnv: false, ...over,
  };
}

const files = [
  file('clef', { shared: true, port: 2203 }),
  file('zerank', { shared: true, port: 2219, servedName: 'zerank-2-reranker' }),
  file('easy', { gpus: [6, 7], port: 2301 }),
  file('flash', { host: 'gpu1', gpus: [3], port: 2240 }),
];

describe('a GPU as the inventory wants it', () => {
  it('lists the files of its host that claim it', () => {
    expect(wantedOn(files, 'gpu2', 3).map((f) => f.name)).toEqual(['clef', 'zerank']);
    expect(wantedOn(files, 'gpu2', 7).map((f) => f.name)).toEqual(['easy']);
    expect(wantedOn(files, 'gpu2', 0)).toEqual([]);
  });

  it('says how each file runs, and which servers run without one', () => {
    const servers: ManagedServer[] = [
      { name: 'clef', state: 'ready', gpus: [3] },
      { name: 'old', state: 'ready', gpus: [3] },
      { name: 'gone', state: 'absent' },
      { name: 'flash', state: 'ready', gpus: [3] },
    ];
    expect(runState(files[0], servers)).toBe('ready');
    expect(runState(files[1], servers)).toBe('absent');
    // `flash` has a file, but on another host: here it is one without.
    expect(unfiled(servers, files, 'gpu2', 3).map((s) => s.name)).toEqual(['old', 'flash']);
  });

  it('names what no file accounts for, once', () => {
    const gpu = {
      index: 3,
      processes: [
        { pid: 1, kind: 'model', servedName: 'clef' },
        { pid: 2, kind: 'model', servedName: 'unlimited-ocr' },
        { pid: 3, kind: 'model', servedName: 'unlimited-ocr' },
        { pid: 4, kind: 'other', servedName: '' },
      ],
    } as unknown as Gpu;
    expect(strangers(gpu, wantedOn(files, 'gpu2', 3)).map((p) => p.pid)).toEqual([2, 4]);
  });

  it('knows what a removal frees and whether the GPU is still shared after it', () => {
    expect(freed(files, ['easy'])).toEqual([6, 7]);
    expect(freed(files, ['clef', 'easy'])).toEqual([3, 6, 7]);
    const wanted = wantedOn(files, 'gpu2', 3);
    expect(staysShared(wanted, ['clef'])).toBe(true);
    expect(staysShared(wanted, ['clef', 'zerank'])).toBe(false);
    expect(inventoryPorts(files, 'gpu2')).toEqual([2203, 2219, 2301]);
  });

  it('marks the GPUs a waiting change request touches', () => {
    const changes = [
      { iid: 12, node: 'gpu2', gpus: [3] },
      { iid: 13, node: 'gpu1', gpus: [3] },
    ] as unknown as WaitingChange[];
    expect(waitingOn(changes, 'gpu2', 3).map((c) => c.iid)).toEqual([12]);
    expect(waitingOn(changes, 'gpu2', 4)).toEqual([]);
  });
});

describe('the form a server is described in', () => {
  const model = {
    path: '/data/huggingface/hub/models--Qwen--Qwen3.8-27B', name: 'Qwen/Qwen3.8-27B', kind: 'hub', repoId: 'Qwen/Qwen3.8-27B',
  } as unknown as StoreModel;

  it('starts from the model and is a description once the host needs nothing more', () => {
    const form = newForm(model);
    expect([form.name, form.servedName, form.apiKey]).toEqual(['qwen3-8-27b', 'Qwen3.8-27B', true]);
    expect(askOf(form, model)).toBeNull();
    const filled = { ...form, gpus: [7, 6], port: '2301', image: ' vllm/vllm-openai:v1 ', args: '--max-model-len 8192', shared: true };
    expect(askOf(filled, model)).toEqual({
      name: 'qwen3-8-27b', model: 'Qwen/Qwen3.8-27B', revision: '', gpus: [6, 7], port: 2301, image: 'vllm/vllm-openai:v1',
      servedName: 'Qwen3.8-27B', args: ['--max-model-len', '8192'], apiKey: true, shared: true,
    });
    expect(askOf({ ...filled, port: '23.5' }, model)).toBeNull();
    expect('shared' in askOf({ ...filled, shared: false }, model)!).toBe(false);
  });
});
