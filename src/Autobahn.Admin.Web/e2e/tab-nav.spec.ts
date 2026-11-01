import { expect, test } from '@playwright/test';
import { signInThroughGate } from './fixtures/gateway-auth';

const TABS = [
  { label: 'Overview', panel: '#panel-dashboard' },
  { label: 'API keys', panel: '#panel-keys' },
  { label: 'Tenants', panel: '#panel-tenants' },
  { label: 'Rate limits', panel: '#panel-ratelimits' },
  { label: 'Routing', panel: '#panel-routing' },
  { label: 'Usage & cost', panel: '#panel-usage' },
  { label: 'Errors', panel: '#panel-errors' },
  { label: 'Logs', panel: '#panel-logs' },
  { label: 'Settings', panel: '#panel-settings' },
];

test.beforeEach(async ({ page }) => {
  await signInThroughGate(page);
});

test('navigates across all primary sidebar sections', async ({ page }) => {
  for (const tab of TABS) {
    await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: tab.label, exact: true }).click();
    await expect(page.locator(tab.panel)).toBeVisible();
  }
});

test('old Settings links for rate limits and model access land on their new pages', async ({ page }) => {
  await page.goto('./#/settings?sub=ratelimits');
  await expect(page.locator('#panel-ratelimits')).toBeVisible();
  await page.goto('./#/settings?sub=access');
  await expect(page.locator('#panel-tenants')).toBeVisible();
});

test('preserves legacy hash redirects for routing sub-tabs', async ({ page }) => {
  await page.goto('./#/models');
  await expect(page.locator('#panel-routing')).toBeVisible();
  await page.goto('./#/backends');
  await expect(page.locator('#panel-routing')).toBeVisible();
});
