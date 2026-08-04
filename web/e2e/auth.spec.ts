import { test, expect } from '@playwright/test';
import { loginAs } from './helpers/auth';

// Phase 2：驗證 test-login 接縫——繞過 Discord OAuth 也能以登入態進站。
// 需要全 stack（前端 + 後端非 Production + DB）。

test('以玩家身分登入後，首頁顯示 Dashboard（本週總覽）', async ({ page }) => {
  await loginAs(page, { discordId: 999001, name: 'E2E玩家', role: 'user' });
  await page.goto('/');
  // 未登入是 Landing（用 Discord 登入）；登入後 layout 依 jwtToken cookie 給 role=user → Dashboard
  await expect(page.getByRole('heading', { name: '本週總覽' })).toBeVisible();

  // 回歸防護：報名截止 banner 要跟後端一致。seed 的 period 在未來（today+10），後端截止在未來、
  // 報名開著 → banner 不該顯示「已截止」。防止「前端用日曆週重算、後端用週期回推」再度分歧的舊 bug
  // （見 plans/2026-08-05-registration-deadline-banner-inconsistency.md）。併進本測試不另開登入避免增並發。
  await expect(page.getByText('已截止')).toHaveCount(0);
});

test('以管理員身分登入後，可進排團管理頁', async ({ page }) => {
  await loginAs(page, { discordId: 999002, name: 'E2E管理員', role: 'admin' });
  await page.goto('/admin/schedule');
  // 管理員 session → 不會被導回登入 / 401；停在 admin 頁
  await expect(page).toHaveURL(/\/admin\/schedule/);
});
