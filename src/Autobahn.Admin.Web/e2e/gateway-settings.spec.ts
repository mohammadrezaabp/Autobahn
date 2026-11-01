import { expect, test } from '@playwright/test';
import { openPrimaryTab, signInThroughGate } from './fixtures/gateway-auth';

test.beforeEach(async ({ page }) => {
  await signInThroughGate(page);
  await openPrimaryTab(page, 'Settings', '#panel-settings');
});

test('keeps only gateway-wide tabs', async ({ page }) => {
  const tabs = page.locator('#panel-settings .sub-nav').getByRole('tab');
  await expect(tabs).toHaveText(['Runtime', 'CORS', 'Observability']);
});

test('confirms config reload from disk', async ({ page }) => {
  await page.getByRole('button', { name: 'Reload config from disk' }).click();
  await expect(page.getByRole('heading', { name: 'Reload config from disk?' })).toBeVisible();
  await page.getByRole('button', { name: 'Reload', exact: true }).click();
  await expect(page.locator('.toast.success')).toContainText(/reload/i, { timeout: 20_000 });
});
