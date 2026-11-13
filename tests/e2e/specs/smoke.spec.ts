import { test, expect } from '@playwright/test';
import { signIn } from './helpers';

test.describe('first visit', () => {
  test('menu is rendered on the server with dishes and prices', async ({ page }) => {
    await page.goto('/');

    await expect(page).toHaveTitle('Menu | Ashana');
    await expect(page.getByRole('heading', { level: 3, name: 'Plov' })).toBeVisible();
    await expect(page.locator('.dish-price').first()).toContainText('₸');
  });

  test('Blazor script is served so pages become interactive @interactive', async ({ request }) => {
    const response = await request.get('/_framework/blazor.web.js');

    expect(response.status()).toBe(200);
  });

  test('help page explains how ordering works', async ({ page }) => {
    await page.goto('/help');

    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(page.locator('main')).toContainText('pickup');
  });

  test('unknown pages show a friendly 404', async ({ page }) => {
    const response = await page.goto('/this/page/does/not/exist');

    expect(response?.status()).toBe(404);
    await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible();
  });
});

test.describe('roles', () => {
  test('student signs in with one click and sees the menu', async ({ page }) => {
    await signIn(page, 'student');

    await expect(page.getByRole('heading', { level: 1 })).toContainText('Aruzhan');
  });

  test('cook lands on the kitchen board', async ({ page }) => {
    await signIn(page, 'cook');

    await expect(page.locator('[data-column]')).toHaveCount(3);
    await expect(page.getByRole('button', { name: 'Start cooking' }).first()).toBeVisible();
  });

  test('admin lands on the dashboard', async ({ page }) => {
    await signIn(page, 'admin');

    await expect(page.getByRole('heading', { level: 1, name: 'Dashboard' })).toBeVisible();
    await expect(page.getByText('Revenue', { exact: true })).toBeVisible();
  });

  test('students are kept out of the kitchen', async ({ page }) => {
    await signIn(page, 'student');
    await page.goto('/kitchen');

    await expect(page).toHaveURL(/\/account\/denied/);
  });

  test('visitors are asked to sign in and come back afterwards', async ({ page }) => {
    await page.goto('/orders');
    await expect(page).toHaveURL(/\/account\/login\?ReturnUrl=%2Forders/);

    await page.getByLabel('Email').fill('student@ashana.test');
    await page.getByLabel('Password').fill('Ashana2026');
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();

    await expect(page).toHaveURL(/\/orders$/);
    await expect(page.getByRole('heading', { level: 1, name: 'My orders' })).toBeVisible();
  });

  test('wrong password shows a clear error', async ({ page }) => {
    await page.goto('/account/login');
    await page.getByLabel('Email').fill('student2@ashana.test');
    await page.getByLabel('Password').fill('not-the-password-1');
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();

    await expect(page.getByRole('alert')).toContainText('Wrong email or password');
  });
});

test.describe('pickup screen and app shell', () => {
  test('TV board connects live and shows numbers, never names', async ({ page }) => {
    await page.goto('/board');

    await expect(page.locator('[data-status]')).toHaveAttribute('data-status', 'live');
    await expect(page.locator('[data-list="preparing"] li').first()).toBeVisible();
    await expect(page.locator('#board')).not.toContainText('Madina');
  });

  test('app can be installed and explains when the network is gone', async ({ page, context }) => {
    await page.goto('/');
    const manifest = await page.locator('link[rel="manifest"]').getAttribute('href');
    expect(manifest).toBeTruthy();
    await page.evaluate(async () => {
      await navigator.serviceWorker.ready;
    });

    await context.setOffline(true);
    await page.goto('/help').catch(() => undefined); // the service worker answers instead of the network

    await expect(page.getByRole('heading', { name: 'You are offline' })).toBeVisible();
    await context.setOffline(false);
  });
});
