import { test, expect, type Browser, type Page } from '@playwright/test';
import { signIn, waitForInteractive, type DemoRole } from './helpers';

/**
 * The main story of the app, with three people at once: a student orders, the cook works on the order,
 * and the TV at the counter shows the number. Every screen updates by itself (Blazor Server + SignalR).
 * Tagged @interactive: these tests need Blazor's JavaScript (they click interactive components).
 */
test.describe.configure({ mode: 'serial' });

async function open(browser: Browser, role: DemoRole | 'tv'): Promise<Page> {
  const context = await browser.newContext({ viewport: { width: 1366, height: 900 } });
  const page = await context.newPage();
  if (role === 'tv') {
    await page.goto('/board');
  } else {
    await signIn(page, role);
    await waitForInteractive(page);
  }

  return page;
}

/** Adds dishes on the menu and places the order for the first free pickup time. Returns the pickup number. */
async function placeOrder(student: Page, dishes: string[]): Promise<string> {
  await student.goto('/');
  await waitForInteractive(student);
  for (const dish of dishes) {
    await student.getByRole('button', { name: `Add ${dish}` }).click();
  }

  await student.getByRole('link', { name: 'Choose pickup time' }).first().click();
  await expect(student).toHaveURL(/\/checkout$/);
  await student.locator('button[role="radio"]:not([disabled])').first().click();
  await student.getByTestId('place-order').click();

  await expect(student).toHaveURL(/\/orders\/[0-9a-f-]{36}$/);
  return (await student.locator('.ticket-number').innerText()).trim();
}

test('order goes from the student to the kitchen and the TV, live @interactive', async ({ browser }) => {
  const student = await open(browser, 'student');
  const cook = await open(browser, 'cook');
  const tv = await open(browser, 'tv');

  const number = await placeOrder(student, ['Plov', 'Black tea with milk']);
  await expect(student.getByTestId('order-message')).toContainText('The kitchen has your order');

  // The new docket appears on the kitchen board without a reload.
  const docket = cook.locator(`article[data-order="${number}"]`);
  await expect(docket).toBeVisible();
  await expect(docket).toContainText('Plov');

  await docket.getByRole('button', { name: 'Start cooking' }).click();
  await expect(student.getByTestId('order-message')).toContainText('being cooked');
  await expect(tv.locator(`[data-list="preparing"] [data-number="${number}"]`)).toBeVisible();

  await docket.getByRole('button', { name: 'Mark ready' }).click();
  await expect(student.getByTestId('order-message')).toContainText(`say number ${number}`);
  await expect(tv.locator(`[data-list="ready"] [data-number="${number}"]`)).toBeVisible();

  await docket.getByRole('button', { name: 'Hand over' }).click();
  await expect(student.getByTestId('order-message')).toContainText('Enjoy your meal');
  await expect(tv.locator(`[data-number="${number}"]`)).toHaveCount(0);
});

test('student can cancel until cooking starts @interactive', async ({ browser }) => {
  const student = await open(browser, 'student');
  const number = await placeOrder(student, ['Samsa with beef']);

  await student.getByRole('button', { name: 'Cancel order' }).click();
  await student.getByRole('dialog').getByRole('button', { name: 'Cancel order' }).click();

  await expect(student.getByTestId('order-message')).toContainText('You cancelled this order');
  await expect(student.getByRole('button', { name: 'Cancel order' })).toHaveCount(0);
  expect(number).toMatch(/^\d+$/);
});

test('kitchen pause reaches every open menu at once @interactive', async ({ browser }) => {
  const student = await open(browser, 'student');
  const cook = await open(browser, 'cook');

  await cook.getByRole('button', { name: 'Pause new orders' }).click();
  await cook.getByRole('dialog').getByRole('button', { name: 'Pause orders' }).click();
  await expect(student.locator('.pause-banner')).toContainText('New orders are paused');

  await cook.getByRole('button', { name: 'Resume orders' }).click();
  await expect(student.locator('.pause-banner')).toHaveCount(0);
});

test('cart survives a page reload @interactive', async ({ browser }) => {
  const student = await open(browser, 'student');
  await student.getByRole('button', { name: 'Add Lagman' }).click();
  await expect(student.getByRole('group', { name: 'Quantity of Lagman' }).first()).toBeVisible();

  await student.reload();
  await waitForInteractive(student);

  await expect(student.getByRole('group', { name: 'Quantity of Lagman' }).first()).toBeVisible();
});
