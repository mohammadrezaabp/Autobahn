const STORAGE_KEY = 'Autobahn-interface-theme';

export type ThemeMode = 'light' | 'dark' | 'system';

export function getStoredTheme(): ThemeMode {
  const stored = localStorage.getItem(STORAGE_KEY);
  return stored === 'light' || stored === 'dark' || stored === 'system' ? stored : 'light';
}

function updateAppearance(mode: ThemeMode): void {
  const dark = mode === 'dark' || (mode === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
  document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  document.querySelector<HTMLMetaElement>('meta[name="theme-color"]')?.setAttribute('content', dark ? '#17221d' : '#f5f2eb');
}

export function applyTheme(mode: ThemeMode): void {
  localStorage.setItem(STORAGE_KEY, mode);
  updateAppearance(mode);
}

export function initTheme(): void {
  updateAppearance(getStoredTheme());
  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
    if (getStoredTheme() === 'system') updateAppearance('system');
  });
  window.addEventListener('storage', (event) => {
    if (event.key === STORAGE_KEY || event.key === null) updateAppearance(getStoredTheme());
  });
}
