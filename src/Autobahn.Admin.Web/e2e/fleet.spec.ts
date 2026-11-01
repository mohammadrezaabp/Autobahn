import { test, expect } from '@playwright/test';
import { signInThroughGate } from './fixtures/gateway-auth';

test('compute pages share gateway navigation and sign-in, and sign-out clears the session', async ({ page, context }) => {
  await signInThroughGate(page);
  const navigation = page.getByRole('navigation', { name: 'Main' });
  const originalPages = context.pages().length;
  await expect(navigation.getByRole('heading')).toHaveText(['Workspace', 'Gateway', 'Compute', 'Access', 'Operations']);
  await expect(page.getByText('Your models, traffic and compute.')).toHaveCount(0);
  await navigation.getByRole('link', { name: 'GPUs', exact: true }).click();
  await expect(page).toHaveURL(/#\/fleet\/gpus$/);
  await expect(page.getByRole('heading', { name: 'No hosts configured' })).toBeVisible();
  for (const label of ['Models', 'Downloads', 'Activity']) {
    await navigation.getByRole('link', { name: label, exact: true }).click();
    await expect(page.getByRole('heading', { name: label, exact: true })).toBeVisible();
    await expect(navigation.getByRole('link', { name: label, exact: true })).toHaveAttribute('aria-current', 'page');
  }
  expect(context.pages()).toHaveLength(originalPages);
  const me = await page.request.get('/admin/fleet/api/me');
  expect(me.status()).toBe(200);
  expect((await me.json()).canWrite).toBe(true);
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Connect', exact: true })).toBeVisible();
  await expect.poll(async () => (await page.request.get('/admin/fleet/api/nodes')).status()).toBe(401);
});

test('saved compute URLs redirect to the shared workspace', async ({ page }) => {
  await signInThroughGate(page);
  await page.goto('/admin/fleet/index.html#models');
  await expect(page).toHaveURL(/\/admin\/index\.html#\/fleet\/models$/);
  await expect(page.getByRole('heading', { name: 'Models', exact: true })).toBeVisible();
});

test('sidebar opens and closes on a narrow screen', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('./');
  await page.getByLabel('Admin API key').fill((await import('./fixtures/gateway-auth')).ADMIN_E2E_KEY);
  await page.getByRole('button', { name: 'Connect' }).click();
  const menu = page.getByRole('button', { name: 'Open navigation' });
  await menu.click();
  const navigation = page.getByRole('navigation', { name: 'Main' });
  await expect(navigation).toBeVisible();
  await navigation.getByRole('link', { name: 'Models', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Models', exact: true })).toBeVisible();
  await expect(menu).toHaveAttribute('aria-expanded', 'false');
});
