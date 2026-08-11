import { test, expect } from '@playwright/test';
import { loginAs } from './helpers/auth';

// period-less §8 Phase 2b-write：玩家自助管理「可用時段例外」。
// 驗寫入整串：表單 → /api/AvailabilityOverride(POST) → DB → GET 列出；再刪除 → 清空。
// 隔離：test-login 自建玩家 8001，獨立日期。
test('玩家新增可用時段例外 → 列出 → 刪除', async ({ page }) => {
  page.on('dialog', d => d.accept());
  await loginAs(page, { discordId: 8001, name: 'P-Override', role: 'user' });

  await page.goto('/me/availability');
  await expect(page.getByText('還沒有任何例外。你的可用時段完全依常設。')).toBeVisible();

  // 新增：某日 19:00–22:00 標「不行」（預設）
  await page.locator('input[type="date"]').fill('2026-09-03');
  await page.locator('input[type="time"]').first().fill('19:00');
  await page.locator('input[type="time"]').last().fill('22:00');
  await Promise.all([
    page.waitForResponse(r => r.url().includes('/AvailabilityOverride') && r.request().method() === 'POST' && r.ok()),
    page.getByRole('button', { name: '新增' }).click(),
  ]);

  // 列出：出現該筆（日期 + 不行 徽章 + 時段）
  const row = page.locator('li').filter({ hasText: '2026-09-03' });
  await expect(row).toBeVisible();
  await expect(row.getByText('不行')).toBeVisible();
  await expect(row.getByText('19:00–22:00')).toBeVisible();

  // 刪除 → 回到空狀態
  await Promise.all([
    page.waitForResponse(r => /\/AvailabilityOverride\/\d+$/.test(new URL(r.url()).pathname) && r.request().method() === 'DELETE' && r.ok()),
    row.getByRole('button').last().click(),
  ]);
  await expect(page.getByText('還沒有任何例外。你的可用時段完全依常設。')).toBeVisible();
});
