import { test, expect } from '@playwright/test';
import { loginAs } from './helpers/auth';

// Phase 5：管理員重排——admin 在 /admin/schedule 對 E2E王3（獨立王）自動排團 → 重排完成。
// 前置：seed-e2e（E2E王3 有範本 E2E範本3 + 報名池玩家 P-Pool(5001)）。用獨立王隔離。
test('管理員自動排團（重排）', async ({ page }) => {
  await loginAs(page, { discordId: 999003, name: 'E2E管理員', role: 'admin' });
  await page.goto('/admin/schedule');

  await page.getByRole('button', { name: 'E2E王3' }).click();
  // 明確選範本（避免 auto-select 競態；頁上唯一的 <select> 就是排團模式）
  await page.locator('select').selectOption({ label: 'E2E範本3' });
  await page.getByRole('button', { name: /開始自動排團/ }).click();

  await expect(page.getByText('重排完成')).toBeVisible();
});
