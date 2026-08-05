import { test, expect } from '@playwright/test';
import { loginAs } from './helpers/auth';

// Phase 4a：核心流程「看到隊伍」讀取半段——seeded 玩家在 /scheduleResult 看到自己的隊。
// 前置：compose.e2e 的 backend + db/seed-e2e.sql 已灌（P1 = discordId 1002 在一支自動隊、角色 c1002）。
// 驗的是整串讀取路徑：前端 → /api proxy → 後端 → DB → 呈現（非 mock）。
test('seeded 玩家在排團結果看到自己的隊伍', async ({ page }) => {
  await loginAs(page, { discordId: 1002, name: 'P1', role: 'user' });
  await page.goto('/scheduleResult');

  await expect(page.getByRole('heading', { name: '排團結果' })).toBeVisible();
  // 預設「我的場次」→ getByDiscordId(1002) → 保留隊；P1 只在 1 隊
  await expect(page.getByRole('button', { name: /我的場次 \(1\)/ })).toBeVisible();
  // 隊伍卡出現該王、且沒有「尚未被排入」空狀態
  await expect(page.getByText('E2E王').first()).toBeVisible();
  await expect(page.getByText('您本週尚未被排入任何團隊')).toHaveCount(0);
});
