import { test, expect, type Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import { signIn, type DemoRole } from './helpers';

/**
 * Automatic accessibility audit with axe-core (WCAG 2.1 A and AA rules).
 * It cannot replace testing with a screen reader, but it catches missing labels, low contrast,
 * broken ARIA and similar mistakes on every run.
 */
async function audit(page: Page, path: string): Promise<void> {
  await page.goto(path);
  await page.waitForLoadState('networkidle');

  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
  const serious = results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  const report = serious.map((v) => `${v.id}: ${v.help} (${v.nodes.length}x) e.g. ${v.nodes[0]?.target.join(' ')}`).join('\n');

  expect(serious, `${path}\n${report}`).toEqual([]);
}

const audits: Array<{ role?: DemoRole; paths: string[] }> = [
  { paths: ['/', '/help', '/account/login', '/account/register', '/board'] },
  { role: 'student', paths: ['/', '/orders', '/checkout', '/account'] },
  { role: 'cook', paths: ['/kitchen', '/kitchen/prep', '/kitchen/stock'] },
  { role: 'admin', paths: ['/admin', '/admin/menu', '/admin/settings', '/admin/users'] },
];

for (const group of audits) {
  test(`${group.role ?? 'visitor'} pages have no serious accessibility problems`, async ({ page }) => {
    if (group.role) {
      await signIn(page, group.role);
    }

    for (const path of group.paths) {
      await audit(page, path);
    }
  });
}

test('dark theme has enough contrast too', async ({ page }) => {
  await page.emulateMedia({ colorScheme: 'dark' });
  await audit(page, '/');
  await audit(page, '/account/login');
});
