import { createSignal, Show } from 'solid-js';
import type { JSX } from 'solid-js';
import { Button } from '../components/primitives';
import { IconEye, IconEyeOff } from '../components/icons';
import {
  dismissGlobalError,
  signedIn,
  useAuthSignals,
  verifyConnection,
  handleApiError,
} from '../stores/auth';

export interface AuthGateProps {
  children: JSX.Element;
}

export function AuthGate(props: AuthGateProps) {
  const { globalError, authLoading } = useAuthSignals();
  const [gateKey, setGateKey] = createSignal('');
  const [showKey, setShowKey] = createSignal(false);
  const [localError, setLocalError] = createSignal('');

  const saveKey = async () => {
    setLocalError('');
    dismissGlobalError();
    try {
      const ok = await verifyConnection(gateKey());
      if (!ok) setLocalError('Could not connect with that key.');
    } catch (e) {
      handleApiError(e);
      const err = e as { message?: string };
      setLocalError(err.message ?? 'Authentication failed.');
    }
  };

  return (
    <Show when={signedIn()} fallback={
      <div class="ab-login ab-login-compact" role="main">
        <div class="ab-login-form-area">
          <div class="ab-brand">
            <span class="ab-monogram" aria-hidden="true"><i /><i /><i /></span>
            <strong>autobahn<span class="ab-brand-dot">.</span></strong>
          </div>
          <form class="auth-gate" onSubmit={(event) => { event.preventDefault(); void saveKey(); }} aria-labelledby="ab-signin-heading">
            <h2 id="ab-signin-heading">Sign in</h2>
            <label for="gate-apiKey">Admin API key</label>
            <div class="key-row">
              <input
                id="gate-apiKey"
                type={showKey() ? 'text' : 'password'}
                value={gateKey()}
                onInput={(e) => setGateKey(e.currentTarget.value)}
                placeholder="Paste your admin key"
                autocomplete="off"
                autocapitalize="none"
                spellcheck={false}
                required
              />
              <button type="button" class="icon-btn" onClick={() => setShowKey((v) => !v)} aria-label={showKey() ? 'Hide key' : 'Show key'}>
                <span class="icon">{showKey() ? <IconEyeOff /> : <IconEye />}</span>
              </button>
            </div>
            <Button type="submit" disabled={authLoading()}>
              <span>{authLoading() ? 'Connecting…' : 'Connect'}</span>
              <span aria-hidden="true">↗</span>
            </Button>
            <Show when={localError() || globalError()?.message}>
              <p class="field-error" role="alert">{localError() || globalError()?.message}</p>
            </Show>
          </form>
        </div>
      </div>
    }>
      {props.children}
    </Show>
  );
}
