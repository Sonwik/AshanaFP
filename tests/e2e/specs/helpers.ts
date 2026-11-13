import { expect, type Page } from '@playwright/test';

export type DemoRole = 'student' | 'cook' | 'admin';

/** Where each role lands after signing in. */
export const homeOf: Record<DemoRole, RegExp> = {
  student: /\/$/,
  cook: /\/kitchen$/,
  admin: /\/admin$/,
};

/** One-click demo sign-in, exactly like a person would do it on the sign-in page. */
export async function signIn(page: Page, role: DemoRole): Promise<void> {
  await page.goto('/account/login');
  await page.getByTestId(`demo-${role}`).click();
  await expect(page).toHaveURL(homeOf[role]);
}

/** Waits until Blazor has connected the page (interactive components respond to clicks). */
export async function waitForInteractive(page: Page): Promise<void> {
  await page.waitForFunction(() => (window as unknown as { Blazor?: unknown }).Blazor !== undefined);
  await page.waitForLoadState('networkidle');
}

/** Horizontal overflow in pixels (0 means the layout fits the screen). */
export function horizontalOverflow(page: Page): Promise<number> {
  return page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
}
