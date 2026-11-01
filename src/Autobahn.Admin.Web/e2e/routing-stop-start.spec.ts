import { expect, test } from '@playwright/test';
import { ADMIN_E2E_KEY, openPrimaryTab, signInThroughGate } from './fixtures/gateway-auth';

test.beforeEach(async ({ page }) => {
  await signInThroughGate(page);
  await openPrimaryTab(page, 'Routing', '#panel-routing');
});

test('stops a serving model after confirmation', async ({ page, request }) => {
  const prepared = await request.post('/admin/api/models/local-mock/start', { headers: { Authorization: `Bearer ${ADMIN_E2E_KEY}` } });
  expect(prepared.ok()).toBe(true);
  expect(await prepared.json()).toMatchObject({ success: true });
  await page.reload();
  const rows = page.locator('#panel-routing tbody tr');
  await expect(rows.first()).toBeVisible();

  const row = rows.first();
  const stopBtn = row.getByRole('button', { name: 'Stop model' });
  await expect(stopBtn).toBeVisible();

  await stopBtn.click();
  await expect(page.getByRole('heading', { name: 'Stop model?' })).toBeVisible();
  await page.getByRole('button', { name: 'Stop', exact: true }).click();
  await expect(page.locator('.toast')).toContainText(/stop/i);
});

test('starts a stopped model after confirmation', async ({ page, request }) => {
  const prepared = await request.post('/admin/api/models/local-mock/stop', { headers: { Authorization: `Bearer ${ADMIN_E2E_KEY}` } });
  expect(prepared.ok()).toBe(true);
  expect(await prepared.json()).toMatchObject({ success: true });
  await page.reload();
  const rows = page.locator('#panel-routing tbody tr');
  await expect(rows.first()).toBeVisible();

  const row = rows.first();
  const startBtn = row.getByRole('button', { name: 'Start model' });
  await expect(startBtn).toBeVisible();

  await startBtn.click();
  await expect(page.getByRole('heading', { name: 'Start model?' })).toBeVisible();
  await page.getByRole('button', { name: 'Start', exact: true }).click();
  await expect(page.locator('.toast')).toContainText(/start/i);
});
