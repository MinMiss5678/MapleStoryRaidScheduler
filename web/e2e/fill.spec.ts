import { test, expect } from '@playwright/test';
import { loginAs } from './helpers/auth';

// Phase 5：補位——未滿的隊有空缺 → 玩家用相符職業（輸出）補位 → 補位成功。
// 前置：seed-e2e（E2E王2 隊只有 P-Dummy 1 人、5 個「輸出」空缺；P-Fill(3001) 有 Hero 角色 c3001、未入隊）。
// 用獨立王 E2E王2，與報名/讀取測試隔離（避免平行互相干擾）。
test('玩家補位進未滿的隊伍', async ({ page }) => {
  page.on('dialog', d => d.accept()); // 接受「未報名確定補位」confirm

  await loginAs(page, { discordId: 3001, name: 'P-Fill', role: 'user' });
  await page.goto('/schedule');

  // 選 E2E王2（補位測試專用的獨立王）
  await page.getByRole('button', { name: 'E2E王2' }).click();
  // 第一個「補位」→ 下拉選自己的角色 CFill
  await page.getByRole('button', { name: '補位' }).first().click();
  await page.getByRole('button', { name: /CFill/ }).click();

  await expect(page.getByText('補位成功')).toBeVisible();
});
