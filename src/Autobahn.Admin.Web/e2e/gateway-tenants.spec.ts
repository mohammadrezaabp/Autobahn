import { expect, test, type Page } from '@playwright/test';
import { openPrimaryTab, signInThroughGate } from './fixtures/gateway-auth';

/** The bootstrap tenant: the only one a fresh gateway has, and the one whose admin keys are operators. */
const operatorRow = (page: Page) =>
  page.locator('.t-tenants tbody tr').filter({ has: page.locator('.tag', { hasText: 'operator' }) });

test.beforeEach(async ({ page }) => {
  await signInThroughGate(page);
  await openPrimaryTab(page, 'Tenants', '#panel-tenants');
});

test('lists the operator tenant with its shared limit', async ({ page }) => {
  const row = operatorRow(page);
  await expect(row).toBeVisible();
  await expect(row.locator('.tag', { hasText: 'operator' })).toBeVisible();
  await expect(row.locator('.tn-nums')).toContainText('rpm');
});

test('creates a tenant, gives it a limit of its own and reads it back', async ({ page }) => {
  const slug = `e2e-${Date.now().toString(36)}`;
  await page.getByRole('button', { name: 'New tenant' }).click();
  const drawer = page.getByRole('dialog');
  await drawer.getByPlaceholder('e.g. demo-app').fill(slug);
  await drawer.getByRole('button', { name: 'Create tenant' }).click();

  // Creating a tenant opens it.
  await expect(page.locator('#panel-tenant h2')).toContainText(slug);
  await page.getByRole('tab', { name: /^Limits/ }).click();
  await expect(page.locator('.tn-shared')).toContainText('default tier');

  await page.getByRole('button', { name: 'Give this tenant its own limit' }).click();
  const rule = page.getByRole('dialog');
  await rule.locator('.rl-bignums input').first().fill('42');
  await rule.getByRole('button', { name: 'Apply to draft' }).click();
  await page.locator('.rl-savebar').getByRole('button', { name: /Save/ }).click();
  await expect(page.locator('.toast.success', { hasText: /saved/i })).toBeVisible({ timeout: 20_000 });

  await expect(page.locator('.tn-shared')).toContainText('its own tenant rule');
  await expect(page.locator('.tn-shared .tn-nums')).toContainText('42');
  await expect(page.locator('.tn-limits .rl-rules-table tbody tr')).toHaveCount(1);
});

test('model access of a tenant is edited on its own tab', async ({ page }) => {
  await operatorRow(page).click();
  await page.getByRole('tab', { name: 'Model access' }).click();
  await expect(page.getByRole('radio', { name: /All self-hosted models/ })).toBeChecked();
  await expect(page.getByRole('button', { name: 'Save model access' })).toBeVisible();
});
