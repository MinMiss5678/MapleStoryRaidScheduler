import { test, expect } from '@playwright/test';

// Phase 1 smoke：不需認證。首頁未登入時應顯示 Landing（含「用 Discord 登入」按鈕）。
// 證明 Playwright harness 通、baseURL/webServer 設定正確。
test('首頁未登入顯示 Landing 與登入按鈕', async ({ page }) => {
  await page.goto('/');
  await expect(page).toHaveTitle(/MapleStoryRaidScheduler/);
  await expect(page.getByRole('button', { name: '用 Discord 登入' })).toBeVisible();
});
