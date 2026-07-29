import { test, expect } from '@playwright/test';
import { loginAs } from './helpers/auth';

// Phase C：管理員存檔時，隊伍已被別的流程異動或消失（TeamSlot 併發控制計畫）。
// 前置：seed-e2e 的 E2E王4 只有 P-Dummy2(ch4002) 一人在自動隊裡（最後一人）。
// 用獨立王/隊（E2E王4），不可跟補位測試共用的 E2E王2 混用——這裡會把隊的最後一人
// 移除觸發連帶砍團，若跟 fill.spec.ts 共用同一隊，平行測試會互踩（見 seed-e2e.sql 註解）。
// 情境：admin 頁面載入後（本地快取還顯示 P-Dummy2 在隊），直接用 API 把 P-Dummy2 移除
//       →（自動隊清空觸發連帶砍團，見 TeamSlotCharacterRepository.DeleteCharacterAsync）
//       → admin 拿著舊畫面對同一隊按「移除成員」→ 儲存 → 應該落到統一衝突回報，
//       前端顯示衝突提示，而不是假裝成功或原生錯誤。
test('管理員存檔時隊伍已消失 → 顯示衝突提示', async ({ page }) => {
  await loginAs(page, { discordId: 999004, name: 'E2E管理員2', role: 'admin' });

  const bosses = await (await page.request.get('/api/Boss/GetAll')).json();
  const boss4 = bosses.find((b: { name: string }) => b.name === 'E2E王4');
  expect(boss4).toBeTruthy();

  const teamSlots = await (await page.request.get(`/api/teamSlot?bossId=${boss4.id}`)).json();
  const team4 = teamSlots[0];
  const lastMember = team4.characters.find((c: { characterId: string | null }) => c.characterId !== null);
  expect(lastMember).toBeTruthy();

  await page.goto('/admin/schedule');
  await page.getByRole('button', { name: 'E2E王4' }).click();
  // 頁面把 P-Dummy2 載進本地畫面（快照 A）
  await expect(page.getByText('CDummy2')).toBeVisible();

  // 背後：直接呼叫 API 移除最後一人 → 觸發連帶砍團（admin 的畫面此時渾然不知，仍是快照 A）
  const removeRes = await page.request.put('/api/teamSlot', {
    headers: { 'X-Idempotency-Key': crypto.randomUUID() },
    data: {
      bossId: boss4.id,
      deleteTeamSlotIds: [],
      teamSlots: [
        {
          id: team4.id,
          bossId: boss4.id,
          periodId: team4.periodId ?? 0,
          slotDateTime: team4.slotDateTime,
          source: team4.source ?? 'auto',
          characters: [],
          deleteTeamSlotCharacterIds: [lastMember.id],
        },
      ],
    },
  });
  expect(removeRes.ok()).toBeTruthy();

  // admin 拿著舊畫面（快照 A，還顯示 CDummy2）操作：不動 E2E王4 這隊，
  // 只是新增一個手動隊伍觸發「有變更」→ 存檔時 E2E王4（快照 A 的舊資料）仍會一併送出
  await page.getByRole('button', { name: '新增隊伍' }).click();
  await page.getByRole('button', { name: '儲存排團' }).click();

  // 應該顯示衝突提示，不是「排團已儲存！」的一般成功訊息
  await expect(page.getByText(/隊因被異動或消失而略過/)).toBeVisible();
  await expect(page.getByText(/隊有衝突，點此查看/)).toBeVisible();
});
