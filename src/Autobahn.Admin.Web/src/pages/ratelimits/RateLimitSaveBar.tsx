import { For, Show } from 'solid-js';
import { Button } from '../../components/primitives';
import { useRateLimitDrawersStore, useRateLimitsStore } from '../../stores/rateLimits';

/**
 * The sticky bar that appears while rate-limit edits are staged. Every page that edits the
 * rate-limit draft shows it, because the draft is one document saved as a whole.
 */
export function RateLimitSaveBar() {
  const store = useRateLimitsStore();
  const drawers = useRateLimitDrawersStore();
  const dirtyView = () => drawers.dirtyView();

  return (
    <Show when={dirtyView().show}>
      <div class="rl-savebar" role="region" aria-label="Unsaved rate-limit changes">
        <Show when={drawers.reviewOpen()}>
          <div class="rl-review" id="rl-review">
            <ul class="rl-diff" aria-label="Unsaved changes">
              <For each={dirtyView().items}>
                {(item) => (
                  <li class="rl-diff-item">
                    <span class={item.kindCls}>{item.kind}</span>
                    <span class="rl-diff-what">
                      <b>{item.subject}</b>
                      <Show when={item.change}>
                        <span class="rl-diff-change">{item.change}</span>
                      </Show>
                    </span>
                    <Show when={store.editable()}>
                      <button type="button" class="rl-link rl-undo" onClick={() => drawers.undoDirtyChange(item.id)}>
                        Undo
                      </button>
                    </Show>
                  </li>
                )}
              </For>
            </ul>
          </div>
        </Show>
        <div class="rl-savebar-row">
          <span class="rl-dot accent" aria-hidden="true" />
          <div class="rl-savebar-text" role="status">
            <span>{dirtyView().countText}</span>
            <small>{dirtyView().detail}</small>
          </div>
          <span class="spacer" />
          <Button variant="ghost" onClick={() => store.discard()} disabled={store.saving() || store.locked()}>
            Discard
          </Button>
          <Button
            variant="ghost"
            onClick={() => drawers.setReviewOpen(!drawers.reviewOpen())}
            disabled={store.saving()}
            aria-expanded={drawers.reviewOpen()}
            aria-controls="rl-review"
          >
            {drawers.reviewOpen() ? 'Hide review' : 'Review changes'}
          </Button>
          <Button
            class="rl-primary"
            onClick={() => void drawers.requestSave()}
            disabled={store.locked() || store.saving() || !store.dirty()}
          >
            {store.saving() ? 'Saving…' : dirtyView().saveLabel}
          </Button>
        </div>
      </div>
    </Show>
  );
}
