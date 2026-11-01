import type { Gpu, GpuProcess, NodeView } from '../types';
import { fleetSummary, gpuState, hostState, spread } from './fleet';
import { ago, bytes, gib, ratio } from './format';

function gpu(over: Partial<Gpu> = {}): Gpu {
  return {
    index: 0, uuid: 'GPU-0', name: 'NVIDIA H100', memoryUsedMiB: 4, memoryTotalMiB: 81559, utilizationPct: 0,
    temperatureC: 30, powerW: 70, powerLimitW: 350, eccUncorrected: 0, error: null, processes: [], ...over,
  };
}

function model(over: Partial<GpuProcess> = {}): GpuProcess {
  return {
    pid: 100, memoryMiB: 70000, kind: 'model', servedName: 'DeepSeek-V4.1-Flash', serverPid: 100, user: 'root',
    containerId: 'abc', command: '', source: 'nvidia-smi', ...over,
  };
}

function node(over: Partial<NodeView> = {}, gpus: Gpu[] = [gpu()], ok = true): NodeView {
  return {
    id: 'gpu2', address: '127.0.0.1:9455', reachable: true, error: null, lastOkAt: 1, lastPollAt: 1,
    status: {
      name: 'gpu2', hostname: 'sgpu02', agentVersion: '0.1.0', time: 1,
      disk: { path: '/data', totalBytes: 100, usedBytes: 40, freeBytes: 60 }, minFreeBytes: 10,
      gpu: {
        ok, error: ok ? null : { code: 'driver_mismatch', title: 't', message: 'm', hint: 'h' },
        warnings: [], source: ok ? 'nvidia-smi' : 'proc', driver: { kernelModule: '570', library: '580' }, gpus,
      },
      servers: [],
    },
    ...over,
  };
}

describe('gpuState', () => {
  it('is free with no process and little memory in use', () => {
    expect(gpuState(gpu())).toBe('free');
  });
  it('is serving when a model server has a process on it', () => {
    expect(gpuState(gpu({ memoryUsedMiB: 70000, processes: [model()] }))).toBe('serving');
  });
  it('is other when something that is not a model server holds memory', () => {
    expect(gpuState(gpu({ memoryUsedMiB: 9000 }))).toBe('other');
    expect(gpuState(gpu({ processes: [model({ kind: 'other' })] }))).toBe('other');
  });
  it('is assigned when there is no reading but Docker gave the card to a server', () => {
    expect(gpuState(gpu({ memoryUsedMiB: null, processes: [model({ source: 'assignment' })] }))).toBe('assigned');
  });
  it('is unreadable with no reading and nothing assigned', () => {
    expect(gpuState(gpu({ memoryUsedMiB: null }))).toBe('unreadable');
  });
  it('is error when the card reports one, whatever else is true', () => {
    expect(gpuState(gpu({ error: 'ECC', processes: [model()] }))).toBe('error');
  });
});

describe('hostState', () => {
  it('waits until the first poll', () => {
    expect(hostState(node({ reachable: null, status: null }))).toBe('waiting');
  });
  it('separates an unreachable agent from unreadable GPUs', () => {
    expect(hostState(node({ reachable: false }))).toBe('unreachable');
    expect(hostState(node({}, [gpu()], false))).toBe('gpu-error');
  });
  it('warns when one card has an error while the host answers', () => {
    expect(hostState(node({}, [gpu(), gpu({ index: 1, error: 'ECC' })]))).toBe('warning');
    expect(hostState(node())).toBe('healthy');
  });
});

describe('fleetSummary', () => {
  it('counts serving, free and unreadable cards across hosts', () => {
    const healthy = node({}, [gpu({ memoryUsedMiB: 70000, processes: [model()] }), gpu({ index: 1 })]);
    const mismatched = node(
      { id: 'gpu1' },
      [gpu({ memoryUsedMiB: null, memoryTotalMiB: null, processes: [model({ source: 'assignment' })] })],
      false,
    );
    const summary = fleetSummary([healthy, mismatched]);
    expect(summary).toMatchObject({ total: 3, serving: 1, free: 1, unreadable: 1, hostsWithProblem: ['gpu1'] });
    expect(summary.memoryUsedMiB).toBe(70004);
  });
  it('does not count the last known readings of a host that stopped answering', () => {
    const gone = node({ reachable: false }, [gpu({ memoryUsedMiB: 70000, processes: [model()] })]);
    expect(fleetSummary([gone])).toMatchObject({ total: 1, serving: 0, unreadable: 1, memoryUsedMiB: 0 });
  });
});

describe('spread', () => {
  it('counts the cards one model server runs on', () => {
    const gpus = [0, 1, 2].map((index) => gpu({ index, processes: [model()] }));
    gpus.push(gpu({ index: 3, processes: [model({ serverPid: 200 })] }));
    expect(spread(gpus[0].processes[0], gpus)).toBe(3);
    expect(spread(gpus[3].processes[0], gpus)).toBe(1);
  });
});

describe('format', () => {
  it('shows GPU memory in GiB', () => {
    expect(gib(81559)).toBe('79.6');
    expect(gib(null)).toBe('–');
  });
  it('picks a unit for bytes', () => {
    expect(bytes(1536)).toBe('1.5 KB');
    expect(bytes(24 * 1024 ** 4)).toBe('24.0 TB');
  });
  it('clamps a ratio and refuses a missing total', () => {
    expect(ratio(5, 4)).toBe(1);
    expect(ratio(1, null)).toBeNull();
  });
  it('says how long ago', () => {
    expect(ago(100, 130)).toBe('30 s ago');
    expect(ago(null, 130)).toBe('never');
  });
});
