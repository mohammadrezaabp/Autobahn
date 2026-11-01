import { beforeEach, describe, expect, it, vi } from 'vitest';

const apiFetch = vi.hoisted(() => vi.fn());
vi.mock('./auth', () => ({
  apiClient: { apiFetch },
  handleApiError: vi.fn(),
  pushToast: vi.fn(),
}));

import { clearPanels, closeAutobahnFleetSession, loadAutobahnFleet, normalizePanelsResponse, openPanel, usePanels, type PanelLink } from './panels';

const grafana: PanelLink = { id: 'grafana', label: 'Grafana', path: '/admin/grafana/', reachable: true };

function fakeTab() {
  return { location: { replace: vi.fn() }, close: vi.fn() };
}

beforeEach(() => {
  apiFetch.mockReset();
});

describe('normalizePanelsResponse', () => {
  it('readsThePanelList', () => {
    expect(normalizePanelsResponse({ panels: [grafana] })).toEqual([grafana]);
  });

  it('treatsAMissingReachableFlagAsReachable', () => {
    const [panel] = normalizePanelsResponse({ panels: [{ id: 'prometheus', path: '/admin/prometheus/' }] });
    expect(panel).toEqual({ id: 'prometheus', label: 'prometheus', path: '/admin/prometheus/', reachable: true });
  });

  it('dropsRowsThatPointOutsideTheConsole', () => {
    expect(normalizePanelsResponse({ panels: [{ id: 'x', path: 'https://elsewhere.example/' }, null] })).toEqual([]);
  });

  it('returnsEmptyForUnexpectedShape', () => {
    expect(normalizePanelsResponse(null)).toEqual([]);
    expect(normalizePanelsResponse({ panels: 'grafana' })).toEqual([]);
  });
});

describe('openPanel', () => {
  it('opensTheSessionBeforeSendingTheTabToThePanel', async () => {
    const tab = fakeTab();
    apiFetch.mockResolvedValue(new Response(null, { status: 204 }));

    const opened = await openPanel(grafana, () => tab as unknown as Window);

    expect(opened).toBe(true);
    expect(apiFetch).toHaveBeenCalledWith('/admin/api/panels/session', { method: 'POST' });
    expect(tab.location.replace).toHaveBeenCalledWith('/admin/grafana/');
    expect(tab.close).not.toHaveBeenCalled();
  });

  it('closesTheTabWhenTheGatewayRefusesTheSession', async () => {
    const tab = fakeTab();
    apiFetch.mockRejectedValue(new Error('403 Forbidden'));

    const opened = await openPanel(grafana, () => tab as unknown as Window);

    expect(opened).toBe(false);
    expect(tab.location.replace).not.toHaveBeenCalled();
    expect(tab.close).toHaveBeenCalled();
  });

  it('doesNotAskForASessionWhenTheTabWasBlocked', async () => {
    expect(await openPanel(grafana, () => null)).toBe(false);
    expect(apiFetch).not.toHaveBeenCalled();
  });
});

describe('loadAutobahnFleet', () => {
  it('offersTheLinkWhenTheHubAnswers', async () => {
    const probe = vi.fn().mockResolvedValue(new Response('{}', { status: 200 }));

    await loadAutobahnFleet(probe);

    expect(probe).toHaveBeenCalledWith('/admin/fleet/healthz', { cache: 'no-store' });
    expect(usePanels().autobahn_fleet()).toBe(true);

    clearPanels();
    expect(usePanels().autobahn_fleet()).toBe(false);
  });

  it('hidesTheLinkWhenThereIsNoHubOrNoEdge', async () => {
    await loadAutobahnFleet(vi.fn().mockResolvedValue(new Response('{}', { status: 200 })));

    await loadAutobahnFleet(vi.fn().mockResolvedValue(new Response(null, { status: 503 })));
    expect(usePanels().autobahn_fleet()).toBe(false);

    await loadAutobahnFleet(vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));
    expect(usePanels().autobahn_fleet()).toBe(false);
  });
});

describe('closeAutobahnFleetSession', () => {
  it('signsTheBrowserOutOffleet', () => {
    const send = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));

    closeAutobahnFleetSession(send);

    expect(send).toHaveBeenCalledWith('/admin/fleet/api/session', {
      method: 'DELETE',
      credentials: 'same-origin',
      headers: { 'X-AutobahnFleet-Request': '1' },
    });
  });

  it('saysNothingWhenThereIsNofleet', async () => {
    closeAutobahnFleetSession(vi.fn().mockRejectedValue(new TypeError('Failed to fetch')));
    await Promise.resolve();
  });
});
