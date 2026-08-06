import { test, expect } from '@playwright/test';
import { loginAs } from './helpers/auth';

// leader-led（計畫 §7）：報名 = 只把「角色 + 時段」放進候選池，**不再觸發自動排團**。
// 新玩家 P-New(2001) 走報名表單 → 送出成功 → 導回首頁；但因 auto-assign 已移除，
// /scheduleResult 顯示「我的場次 (0)」（要等隊長 Pull 邀請才入隊）。
// 驗：報名寫入整串仍通（表單 → proxy → API → DB），且確認不再自動分配（不入隊）。
// 前置：compose.e2e backend + db/seed-e2e.sql（含 2001 有角色 c2001、未入隊）。
test('新玩家報名成功、但 leader-led 下不再自動排入隊伍', async ({ page }) => {
  await loginAs(page, { discordId: 2001, name: 'P-New', role: 'user' });

  await page.goto('/register');

  // Step 1：一鍵填「平日晚上」→ 下一步
  await page.getByRole('button', { name: /平日晚上/ }).click();
  await page.getByRole('button', { name: '下一步' }).click();

  // Step 2：新增報名項目 → 選 boss + 角色 + 場數 → 送出（先選 boss，選角色會重新分組）
  await page.getByRole('button', { name: '新增報名項目' }).click();
  await page.locator('select').nth(1).selectOption({ index: 1 });      // boss E2E王
  await page.locator('select').first().selectOption({ index: 1 });     // 角色 CNew
  await page.getByRole('button', { name: '7', exact: true }).click();  // 7 場
  await page.getByRole('button', { name: '送出報名' }).click();

  // 報名成功 → toast +  router.push("/")；等導回首頁確認寫入已 commit
  await page.waitForURL(u => new URL(u).pathname === '/');

  // leader-led：報名不再自動排團 → 尚未被任何隊長邀請 → 我的場次 (0)
  await page.goto('/scheduleResult');
  await expect(page.getByRole('button', { name: /我的場次 \(0\)/ })).toBeVisible();
});
