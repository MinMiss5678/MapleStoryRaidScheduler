import { test, expect } from '@playwright/test';
import { loginAs } from './helpers/auth';

// period-less（報名 UX 大改）：報名頁改成「我的資料」profile——設常設可用時段 + 勾參戰角色，取代每期報名。
// 驗寫入整串：profile 表單 → /api/Profile(PUT) → DB → 重載後仍在。
// 前置：compose.e2e backend + db/seed-e2e.sql（含 2001 有角色 c2001）。
test('玩家設定 profile：常設時段 + 參戰角色 → 儲存 → 重載仍在', async ({ page }) => {
  await loginAs(page, { discordId: 2001, name: 'P-New', role: 'user' });

  await page.goto('/register');
  await expect(page.getByRole('heading', { name: '我的資料' })).toBeVisible();

  // 常設時段：選「平日晚上」預設 → 套用到平日（一~五）
  await page.getByRole('button', { name: '平日晚上' }).click();
  await page.getByRole('button', { name: /套用到平日/ }).click();
  // 週一列出現 19:00–22:00
  const monRow = page.locator('li').filter({ hasText: '週一' });
  await expect(monRow.getByText('19:00–22:00')).toBeVisible();

  // 勾參戰角色（CNew）
  const charRow = page.locator('li').filter({ hasText: 'CNew' });
  await charRow.click();
  await expect(charRow.locator('input[type="checkbox"]')).toBeChecked();

  // 儲存
  await Promise.all([
    page.waitForResponse(r => r.url().includes('/Profile') && r.request().method() === 'PUT' && r.ok()),
    page.getByRole('button', { name: '儲存' }).click(),
  ]);

  // 重載 → 常設時段與參戰角色都還在（讀 /api/Profile 回填）
  await page.reload();
  await expect(page.locator('li').filter({ hasText: '週一' }).getByText('19:00–22:00')).toBeVisible();
  await expect(page.locator('li').filter({ hasText: 'CNew' }).locator('input[type="checkbox"]')).toBeChecked();
});
