import { expect, test, type APIRequestContext, type Page } from '@playwright/test';
import { ADMIN_E2E_KEY, signInThroughGate } from './fixtures/gateway-auth';

const asOperator = { Authorization: `Bearer ${ADMIN_E2E_KEY}` };

/** Records compute-session sign-outs without contacting an agent. */
async function stubAutobahnFleet(page: Page): Promise<string[]> {
  const signOuts: string[] = [];
  await page.route('**/admin/fleet/healthz', (route) => route.fulfill({ status: 200, body: '{"ok":true}' }));
  await page.route('**/admin/fleet/api/session', (route) => {
    signOuts.push(route.request().method());
    return route.fulfill({ status: 204 });
  });
  return signOuts;
}

/** A tenant of its own with an admin key, an application key and a limit on that key. */
async function seedTenant(request: APIRequestContext) {
  const slug = `e2e-team-${Date.now().toString(36)}`;
  const tenant = await request.post('/admin/api/tenants', { headers: asOperator, data: { slug, name: `Team ${slug}` } });
  expect(tenant.status()).toBe(201);
  const tenantId = (await tenant.json()).id as string;
  const inTenant = { ...asOperator, 'X-Autobahn-Tenant': tenantId };

  const admin = await request.post('/admin/api/keys', { headers: inTenant, data: { role: 'Admin', label: 'team-admin' } });
  expect(admin.status()).toBe(201);
  const app = await request.post('/admin/api/keys', { headers: inTenant, data: { role: 'Inference', label: 'team-app' } });
  expect(app.status()).toBe(201);

  const rule = await request.put('/admin/api/rate-limits/rules', {
    headers: asOperator,
    data: { scope: 'api_key', target: (await app.json()).id, rpm: 61, burst: 7, maxConcurrentStreams: 3, tpm: 250000 },
  });
  expect(rule.status()).toBe(200);

  return { slug, adminSecret: (await admin.json()).secret as string };
}

test("a tenant's own admin key signs in and sees only its tenant", async ({ page, request }) => {
  const { slug, adminSecret } = await seedTenant(request);
  await stubAutobahnFleet(page);

  await signInThroughGate(page, adminSecret);

  // It lands on the first page it may open, with the operator's pages left out of the menu.
  await expect(page.locator('#panel-usage, .page').first()).toBeVisible();
  await expect(page).toHaveURL(/#\/usage/);
  await expect(page.getByRole('navigation', { name: 'Main' }).getByRole('link')).toHaveText(['Usage & cost', 'API keys', 'Limits']);
  await expect(page.locator('.topbar .key-tag').first()).toContainText(`Team ${slug}`);
  // fleet runs the GPUs every tenant shares: not this key's to open.
  await expect(page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'GPUs', exact: true })).toHaveCount(0);

  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Limits' }).click();
  const limits = page.locator('#panel-limits');
  await expect(limits.locator('.tn-shared')).toContainText('3,000');
  await expect(limits.locator('.tn-shared .tn-nums')).toContainText('rpm');
  const row = limits.locator('tbody tr');
  await expect(row).toHaveCount(1);
  await expect(row).toContainText('team-app');
  await expect(row).toContainText('61');
  await expect(row).toContainText('250,000');

  await page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'API keys' }).click();
  await expect(page.locator('tbody tr', { hasText: 'team-app' })).toBeVisible();
  await expect(page.locator('tbody tr', { hasText: 'team-admin' })).toBeVisible();

  // An operator page typed into the address bar goes back to the tenant's own.
  await page.goto('./#/ratelimits');
  await expect(page).toHaveURL(/#\/usage/);
  await expect(page.locator('.alert, .notice.error')).toHaveCount(0);
});

test('an operator key still gets the whole console', async ({ page }) => {
  const signOuts = await stubAutobahnFleet(page);
  await signInThroughGate(page);
  await expect(page).toHaveURL(/#\/dashboard/);
  await expect(page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Rate limits' })).toBeVisible();
  await expect(page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'Limits', exact: true })).toHaveCount(0);
  await expect(page.getByRole('navigation', { name: 'Main' }).getByRole('link', { name: 'GPUs', exact: true })).toBeVisible();

  // Signing out ends the fleet session too, so the next key does not inherit it.
  signOuts.length = 0;
  await page.getByRole('button', { name: 'Sign out' }).click();
  await expect.poll(() => signOuts).toEqual(['DELETE']);
});
