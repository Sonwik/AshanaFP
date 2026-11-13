import { test, expect } from '@playwright/test';
import { horizontalOverflow, signIn, type DemoRole } from './helpers';

/** Every page must fit the screen: no sideways scrolling on a phone, a tablet or a desktop. */
const pages: Array<{ role?: DemoRole; paths: string[] }> = [
  { paths: ['/', '/help', '/account/login', '/account/register', '/board'] },
  { role: 'student', paths: ['/', '/orders', '/checkout', '/account'] },
  { role: 'cook', paths: ['/kitchen', '/kitchen/prep', '/kitchen/stock'] },
  { role: 'admin', paths: ['/admin', '/admin/menu', '/admin/settings', '/admin/users'] },
];

for (const group of pages) {
  test(`${group.role ?? 'visitor'} pages fit the screen`, async ({ page }) => {
    if (group.role) {
      await signIn(page, group.role);
    }

    for (const path of group.paths) {
      await page.goto(path);
      await page.waitForLoadState('networkidle');
      expect(await horizontalOverflow(page), `${path} scrolls sideways`).toBeLessThanOrEqual(0);
    }
  });
}

test('navigation adapts to the screen size', async ({ page, isMobile }) => {
  await signIn(page, 'student');

  // Phones get a bottom tab bar within thumb reach; wide screens get links in the top bar.
  const bottom = page.locator('nav.bottomnav');
  const top = page.locator('nav.topnav');
  if (isMobile) {
    await expect(bottom).toBeVisible();
    await expect(top).toBeHidden();
  } else {
    await expect(top).toBeVisible();
    await expect(bottom).toBeHidden();
  }
});
