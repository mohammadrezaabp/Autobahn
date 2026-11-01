import type { DownloadJob, DownloadsNode, StoreModel, StoreNode } from '../types';
import { duration } from './format';
import {
  copySource, deleteBlock, diskBreakdown, filterRows, fit, jobRows, progress, resumable, secondsLeft, storeRows, totalBytes,
} from './store';

const GB = 1024 ** 3;

function model(over: Partial<StoreModel> = {}): StoreModel {
  return {
    path: '/data/huggingface/hub/models--Qwen--Qwen3-0.6B', name: 'Qwen/Qwen3-0.6B', kind: 'hub',
    repoId: 'Qwen/Qwen3-0.6B', repoType: 'model', sizeBytes: 2 * GB, fileCount: 10, modifiedAt: 1, incomplete: false,
    revisions: ['abc'], inUseBy: [], downloading: false, ...over,
  };
}

function flat(name: string, sizeBytes: number, inUseBy: string[] = []): StoreModel {
  return model({ path: `/data/huggingface/hub2/${name}`, name, kind: 'flat', repoId: '', sizeBytes, inUseBy });
}

function node(id: string, models: StoreModel[] | null, over: Partial<NonNullable<StoreNode['store']>> = {}): StoreNode {
  if (models === null) return { id, ok: false, error: { code: 'agent_unreachable', message: 'down' }, store: null };
  return {
    id, ok: true, error: null,
    store: {
      name: id, disk: { path: '/data', totalBytes: 1000 * GB, usedBytes: 700 * GB, freeBytes: 300 * GB },
      minFreeBytes: 200 * GB, scannedAt: 1, scanning: false, allowDelete: true, models, ...over,
    },
  };
}

function job(over: Partial<DownloadJob> = {}): DownloadJob {
  return {
    id: 'a', repo: 'Qwen/Qwen3-0.6B', revision: 'main', state: 'running', createdAt: 100, startedAt: 101,
    finishedAt: null, totalBytes: 100, doneBytes: 25, files: 3, bytesPerSecond: 5, sha: '', path: '',
    errorCode: '', message: '', requestedBy: 'key …0001', ...over,
  };
}

describe('storeRows', () => {
  const nodes = [
    node('gpu1', [model(), flat('DeepSeek-V4.1-Flash', 600 * GB, ['DeepSeek-V4.1-Flash'])]),
    node('gpu2', [model({ sizeBytes: 3 * GB }), model({ name: 'org/only-here', repoId: 'org/only-here', sizeBytes: GB })]),
    node('gpu3', null),
  ];

  it('lists every host, largest first', () => {
    expect(storeRows(nodes).map((r) => `${r.node}:${r.model.name}`)).toEqual([
      'gpu1:DeepSeek-V4.1-Flash', 'gpu2:Qwen/Qwen3-0.6B', 'gpu1:Qwen/Qwen3-0.6B', 'gpu2:org/only-here',
    ]);
  });
  it('says which other hosts hold the same model', () => {
    const rows = storeRows(nodes);
    expect(rows.find((r) => r.node === 'gpu1' && r.model.kind === 'hub')!.alsoOn).toEqual(['gpu2']);
    expect(rows.find((r) => r.model.name === 'org/only-here')!.alsoOn).toEqual([]);
  });
  it('offers a download only to hosts that answered and lack the model', () => {
    const rows = storeRows(nodes);
    expect(rows.find((r) => r.model.name === 'org/only-here')!.missingOn).toEqual(['gpu1']);
    expect(rows.find((r) => r.node === 'gpu1' && r.model.kind === 'hub')!.missingOn).toEqual([]);
  });
  it('never offers to download a plain directory: Hugging Face does not know it by that name', () => {
    expect(storeRows(nodes).find((r) => r.model.kind === 'flat')!.missingOn).toEqual([]);
  });
});

describe('copySource', () => {
  const held = model({ sizeBytes: 3 * GB });
  const nodes = (over: Partial<StoreModel> = {}, target: { copyFrom?: string[] } = { copyFrom: ['gpu2'] }) => [
    node('gpu1', [], target),
    node('gpu2', [{ ...held, ...over }, flat('Qwen/Qwen3-0.6B', GB)]),
  ];

  it('finds the complete copy on a host the target is set up to copy from', () => {
    expect(copySource(nodes(), 'gpu1', 'Qwen/Qwen3-0.6B')).toEqual({ node: 'gpu2', model: held });
    expect(copySource(nodes(), 'gpu1', 'org/other')).toBeNull();
  });
  it('is nothing when the target has no such host, as with an agent that cannot copy yet', () => {
    expect(copySource(nodes({}, { copyFrom: [] }), 'gpu1', 'Qwen/Qwen3-0.6B')).toBeNull();
    expect(copySource(nodes({}, {}), 'gpu1', 'Qwen/Qwen3-0.6B')).toBeNull();
    expect(copySource(nodes(), 'gpu2', 'Qwen/Qwen3-0.6B')).toBeNull();
  });
  it('never offers half a model, or one that is not in the hub cache', () => {
    expect(copySource(nodes({ incomplete: true }), 'gpu1', 'Qwen/Qwen3-0.6B')).toBeNull();
    expect(copySource(nodes({ downloading: true }), 'gpu1', 'Qwen/Qwen3-0.6B')).toBeNull();
    expect(copySource(nodes({ kind: 'stray' }), 'gpu1', 'Qwen/Qwen3-0.6B')).toBeNull();
  });
});

describe('resumable', () => {
  it('is a hub cache download that stopped part way and is not running now', () => {
    expect(resumable(model({ incomplete: true }))).toBe(true);
    expect(resumable(model())).toBe(false);
    expect(resumable(model({ incomplete: true, downloading: true }))).toBe(false);
    expect(resumable(model({ incomplete: true, kind: 'stray' }))).toBe(false);
    expect(resumable({ ...flat('x', GB), incomplete: true })).toBe(false);
  });
});

describe('filterRows', () => {
  const rows = storeRows([
    node('gpu1', [model(), flat('DeepSeek-V4.1-Flash', 600 * GB, ['deepseek-flash'])]),
    node('gpu2', [model({ sizeBytes: 3 * GB })]),
  ]);
  const all = { node: '', kind: '', unusedOnly: false, text: '' };

  it('answers "how much can be freed on gpu1"', () => {
    const unused = filterRows(rows, { ...all, node: 'gpu1', unusedOnly: true });
    expect(unused.map((r) => r.model.name)).toEqual(['Qwen/Qwen3-0.6B']);
    expect(totalBytes(unused)).toBe(2 * GB);
  });
  it('finds a model by its name or by the name it is served under', () => {
    expect(filterRows(rows, { ...all, text: 'QWEN' })).toHaveLength(2);
    expect(filterRows(rows, { ...all, text: 'deepseek-flash' })).toHaveLength(1);
    expect(filterRows(rows, { ...all, kind: 'flat' })).toHaveLength(1);
  });
});

describe('diskBreakdown', () => {
  it('splits the filesystem into served, not served, other data and free', () => {
    const answer = diskBreakdown(node('gpu1', [model({ sizeBytes: 100 * GB }), flat('x', 400 * GB, ['x'])]));
    expect(answer).toEqual({ total: 1000 * GB, inUse: 400 * GB, unused: 100 * GB, other: 200 * GB, free: 300 * GB });
  });
  it('does not go negative when the models add up to more than the filesystem uses', () => {
    expect(diskBreakdown(node('gpu1', [flat('x', 900 * GB)]))!.other).toBe(0);
  });
  it('is nothing for a host that did not answer', () => {
    expect(diskBreakdown(node('gpu3', null))).toBeNull();
  });
});

describe('deleteBlock', () => {
  const on = { allowDelete: true };

  it('lets an operator delete a model nothing uses', () => {
    expect(deleteBlock(model(), on, true)).toBe('');
  });
  it('names the server that uses a model', () => {
    expect(deleteBlock(flat('x', 1, ['deepseek-flash']), on, true)).toContain('deepseek-flash');
  });
  it('refuses for a viewer, a host with deletes off and a download in progress', () => {
    expect(deleteBlock(model(), on, false)).not.toBe('');
    expect(deleteBlock(model(), { allowDelete: false }, true)).toContain('NODE_AGENT_ALLOW_DELETE');
    expect(deleteBlock(model({ downloading: true }), on, true)).toContain('Cancel');
  });
});

describe('fit', () => {
  it('refuses a download that would eat into the reserve', () => {
    expect(fit(150 * GB, 0, 300 * GB, 200 * GB)).toEqual({ needed: 150 * GB, freeAfter: 150 * GB, fits: false });
    expect(fit(100 * GB, 0, 300 * GB, 200 * GB).fits).toBe(true);
  });
  it('counts what is already on the host, so a download can be continued', () => {
    expect(fit(150 * GB, 60 * GB, 300 * GB, 200 * GB)).toMatchObject({ needed: 90 * GB, fits: true });
    expect(fit(10, 50, 300, 200).needed).toBe(0);
  });
});

describe('jobRows', () => {
  const nodes: DownloadsNode[] = [
    {
      id: 'gpu1', ok: true, error: null,
      downloads: [
        job({ id: 'new', state: 'failed', createdAt: 300 }),
        job({ id: 'old', state: 'cancelled', createdAt: 200 }),
        job({ id: 'done', repo: 'org/done', state: 'completed', createdAt: 250 }),
      ],
    },
    { id: 'gpu2', ok: true, error: null, downloads: [job({ id: 'live', createdAt: 150 })] },
  ];

  it('puts what is moving first, then newest first', () => {
    expect(jobRows(nodes).map((r) => r.job.id)).toEqual(['live', 'new', 'done', 'old']);
  });
  it('offers "start again" only on the newest stopped job of a model on that host', () => {
    expect(jobRows(nodes).filter((r) => r.canRestart).map((r) => r.job.id)).toEqual(['new']);
  });
});

describe('progress', () => {
  it('is unknown until the size is', () => {
    expect(progress(job({ totalBytes: null }))).toBeNull();
    expect(progress(job())).toBe(0.25);
    expect(progress(job({ state: 'completed', totalBytes: null }))).toBe(1);
  });
  it('estimates the time left only while bytes are moving', () => {
    expect(secondsLeft(job())).toBe(15);
    expect(secondsLeft(job({ bytesPerSecond: 0 }))).toBeNull();
    expect(secondsLeft(job({ state: 'queued' }))).toBeNull();
  });
  it('says a length of time in words', () => {
    expect(duration(15)).toBe('15 s');
    expect(duration(400)).toBe('7 min');
    expect(duration(2 * 3600 + 15 * 60)).toBe('2 h 15 min');
    expect(duration(null)).toBe('–');
  });
});
