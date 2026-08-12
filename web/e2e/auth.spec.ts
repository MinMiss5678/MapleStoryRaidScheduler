import { test, expect } from '@playwright/test';
import { loginAs } from './helpers/auth';

// Phase 2：驗證 test-login 接縫——繞過 Discord OAuth 也能以登入態進站。
// 需要全 stack（前端 + 後端非 Production + DB）。

test('以玩家身分登入後，首頁顯示 Dashboard（總覽）', async ({ page }) => {
  await loginAs(page, { discordId: 999001, name: 'E2E玩家', role: 'user' });
  await page.goto('/');
  // 未登入是 Landing（用 Discord 登入）；登入後 layout 依 jwtToken cookie 給 role=user → Dashboard
  await expect(page.getByRole('heading', { name: '總覽' })).toBeVisible();
});

test('以管理員身分登入後，可進系統設定頁', async ({ page }) => {
  await loginAs(page, { discordId: 999002, name: 'E2E管理員', role: 'admin' });
  await page.goto('/admin/config');
  // 管理員 session → 不會被導回登入 / 401；停在 admin 頁
  await expect(page).toHaveURL(/\/admin\/config/);
});
