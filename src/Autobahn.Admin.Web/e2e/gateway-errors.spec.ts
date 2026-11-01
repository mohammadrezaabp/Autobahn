import { expect, test } from '@playwright/test';
import { openPrimaryTab, signInThroughGate } from './fixtures/gateway-auth';

test.beforeEach(async ({ page }) => {
  await signInThroughGate(page);
  await openPrimaryTab(page, 'Errors', '#panel-errors');
});

test('renders the errors table or its empty state', async ({ page }) => {
  // The table is only drawn when there is something in it; a quiet gateway shows the empty state.
  await expect(page.locator('#panel-errors .t-errors, #panel-errors .empty-state').first()).toBeVisible();
  await expect(page.getByText(/last seen|no errors/i).first()).toBeVisible();
});
