import { test, expect } from '@playwright/test';
import { loginAs } from './helpers/auth';

// Phase 4b：核心流程「報名 → 自動排隊」寫入半段。
// 新玩家 P-New(2001) 走報名表單 → 後端 AutoAssign 排進隊 → /scheduleResult 看到自己入隊。
// 驗整串寫入路徑：報名表單 → proxy → API → 排隊演算法 → DB → 讀回呈現（非 mock）。
// 前置：compose.e2e backend + db/seed-e2e.sql（含 2001 有角色 ch2001、未入隊）。
test('新玩家報名後被自動排入隊伍', async ({ page }) => {
  await loginAs(page, { discordId: 2001, name: 'P-New', role: 'user' });

  await page.goto('/register');

  // Step 1：一鍵填「平日晚上」→ 下一步（有可用時段才會 enable）
  await page.getByRole('button', { name: /平日晚上/ }).click();
  await page.getByRole('button', { name: '下一步' }).click();

  // Step 2：新增報名項目 → 選角色 + boss + 場數 → 送出
  await page.getByRole('button', { name: '新增報名項目' }).click();
  // 先選 boss（此時角色/boss 兩個 select 都在）；選了角色會重新分組、角色 select 消失，故最後選角色
  await page.locator('select').nth(1).selectOption({ index: 1 });      // boss E2E王（唯一王）
  await page.locator('select').first().selectOption({ index: 1 });     // 角色 CNew（唯一角色）
  await page.getByRole('button', { name: '7', exact: true }).click();  // 7 場
  await page.getByRole('button', { name: '送出報名' }).click();

  // 成功 → toast「報名成功」+ router.push("/")；等導回首頁確認送出完成（含 AutoAssign 已 commit）
  await page.waitForURL(u => new URL(u).pathname === '/');

  // 去結果頁：2001 現在應被自動排進一支隊
  await page.goto('/scheduleResult');
  await expect(page.getByRole('button', { name: /我的場次 \(1\)/ })).toBeVisible();
  await expect(page.getByText('E2E王').first()).toBeVisible();
});
