import { onCleanup, onMount } from 'solid-js';

/** Runs `tick` now, then every `everyMs` while the tab is visible, and again when it becomes visible. */
export function createPolling(tick: () => Promise<void>, everyMs: number): void {
  onMount(() => {
    // A hidden tab asks for nothing.
    const run = () => {
      if (!document.hidden) void tick();
    };
    run();
    const timer = setInterval(run, everyMs);
    document.addEventListener('visibilitychange', run);
    onCleanup(() => {
      clearInterval(timer);
      document.removeEventListener('visibilitychange', run);
    });
  });
}
