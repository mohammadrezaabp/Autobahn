import { Show, createSignal } from 'solid-js';
import { ApiError, api } from '../api';

export function SignIn(props: { problem: string; onSignedIn: () => void }) {
  const [key, setKey] = createSignal('');
  const [busy, setBusy] = createSignal(false);
  const [failure, setFailure] = createSignal('');

  async function submit(event: Event) {
    event.preventDefault();
    if (!key().trim() || busy()) return;
    setBusy(true);
    setFailure('');
    try {
      await api.signIn(key().trim());
      setKey('');
      props.onSignedIn();
    } catch (error) {
      setFailure(error instanceof ApiError ? error.message : 'Sign-in failed.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <form class="signin card ab-fleet-signin" onSubmit={submit}>
      <h1>Sign in</h1>
      <label class="field">
        <span>Autobahn admin key</span>
        <input
          type="password"
          autocomplete="off"
          spellcheck={false}
          placeholder="Paste your admin key"
          value={key()}
          onInput={(event) => setKey(event.currentTarget.value)}
          autofocus
        />
      </label>
      <Show when={failure() || props.problem}>
        <p class="alert alert-error" role="alert">{failure() || props.problem}</p>
      </Show>
      <button class="btn btn-primary" type="submit" disabled={busy() || !key().trim()}>
        {busy() ? 'Checking…' : 'Sign in'}
      </button>
    </form>
  );
}
