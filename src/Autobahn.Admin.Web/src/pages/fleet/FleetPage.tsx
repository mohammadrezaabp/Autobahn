import { Match, Show, Switch, createEffect, createSignal, onCleanup } from 'solid-js';
import { useNavigate, useParams } from '@solidjs/router';
import { GpusPage } from '../../../../Autobahn.Fleet.Web/src/components/GpusPage';
import { StorePage } from '../../../../Autobahn.Fleet.Web/src/components/StorePage';
import { DownloadsPage } from '../../../../Autobahn.Fleet.Web/src/components/DownloadsPage';
import { ActivityPage } from '../../../../Autobahn.Fleet.Web/src/components/ActivityPage';
import { ApiError, api } from '../../../../Autobahn.Fleet.Web/src/api';
import type { Me } from '../../../../Autobahn.Fleet.Web/src/types';
import { clearSession, useAuthSignals } from '../../stores/auth';
import './fleet.css';

const TITLES: Record<string, string> = { gpus: 'GPUs', models: 'Models', downloads: 'Downloads', activity: 'Activity' };

export default function FleetPage() {
  const params = useParams<{ section: string }>();
  const navigate = useNavigate();
  const { apiKey } = useAuthSignals();
  const [identity, setIdentity] = createSignal<Me | null>(null);
  const [loading, setLoading] = createSignal(true);
  const [failure, setFailure] = createSignal('');
  const section = () => Object.hasOwn(TITLES, params.section) ? params.section : 'gpus';
  let request = 0;
  onCleanup(() => { request++; });

  async function load(key: string) {
    const current = ++request;
    setIdentity(null);
    setLoading(true);
    setFailure('');
    try {
      await api.signIn(key);
      const me = await api.me();
      if (current === request) setIdentity(me);
    } catch (error) {
      if (current !== request) return;
      if (error instanceof ApiError && error.status === 401) clearSession();
      else setFailure(error instanceof Error ? error.message : 'Compute is unavailable.');
    } finally {
      if (current === request) setLoading(false);
    }
  }

  createEffect(() => { const key = apiKey(); if (key) void load(key); });

  return (
    <section class="ab-fleet-workspace" aria-labelledby="compute-page-heading">
      <header class="page-header"><div class="titles"><h1 id="compute-page-heading">{TITLES[section()]}</h1></div></header>
      <Show when={loading()}><p class="muted" role="status">Loading…</p></Show>
      <Show when={failure()}>
        <div class="alert alert-error" role="alert">
          <p>{failure()}</p>
          <button type="button" class="btn" onClick={() => void load(apiKey())}>Retry</button>
        </div>
      </Show>
      <Show when={identity()} keyed>{me => (
        <Switch>
          <Match when={section() === 'models'}><StorePage me={me} onSignedOut={clearSession} onDownloadStarted={() => navigate('/fleet/downloads')} /></Match>
          <Match when={section() === 'downloads'}><DownloadsPage me={me} onSignedOut={clearSession} /></Match>
          <Match when={section() === 'activity'}><ActivityPage onSignedOut={clearSession} /></Match>
          <Match when={section() === 'gpus'}><GpusPage me={me} onSignedOut={clearSession} /></Match>
        </Switch>
      )}</Show>
    </section>
  );
}
