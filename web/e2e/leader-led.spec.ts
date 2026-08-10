import { test, expect } from '@playwright/test';
import { loginAs } from './helpers/auth';

// leader-led（隊長主導組隊）Push 全流程，純 UI：
//   隊長開隊 → 玩家「尋隊」申請 → 隊長「帶隊」審核核准 → 玩家「隊伍列表」看到已加入。
// 覆蓋頁：/teams/new（開隊 builder）、/me/led-teams（帶隊 hub）、/teams/open（尋隊+申請）、
//         /teams/[id]/applications（審核）、/me/teams（隊伍列表・已加入）。
// 隔離：專屬隊長 6001（test-login 自建 Player）+ 申請者 P-LL(6002,角色 c6002，seed)。
// 冪等：每次跑用唯一時段 + 唯一隊伍說明，所有互動 scope 到該說明的卡片 → CI retry 彼此獨立
//       （避免同 slot 撞跨隊重疊約束、多隊撞 strict-mode）。
// 前置：compose.e2e backend（非 Production→test-login 有效）+ db/seed-e2e.sql。
test('隊長開隊 → 玩家申請 → 隊長核准 → 玩家看到已加入', async ({ page }) => {
  test.slow(); // 一支測跨 4 次登入 + 多次導覽，平行負載下給足 timeout 餘裕（×3）
  page.on('dialog', d => d.accept()); // 申請成功等操作用 alert 提示

  // 唯一化：時段落在當期（period = today+10 ~ today+17）內、且每次不同 → 免撞重疊約束
  const salt = Date.now();
  const pad = (n: number) => String(n).padStart(2, '0');
  const slot = new Date();
  slot.setDate(slot.getDate() + 10 + (salt % 7));            // today+10 ~ +16（皆在期內）
  const slotLocal = `${slot.getFullYear()}-${pad(slot.getMonth() + 1)}-${pad(slot.getDate())}T${pad(salt % 24)}:${pad(salt % 60)}`;
  const desc = `LL-e2e-${salt}`;                             // 唯一隊伍說明，用來鎖定卡片

  // ── 1) 隊長開隊 ──
  await loginAs(page, { discordId: 6001, name: 'P-LL-Leader', role: 'user' });
  await page.goto('/teams/new');
  await page.locator('select').selectOption({ label: 'E2E王' });
  await page.locator('input[type="datetime-local"]').fill(slotLocal);
  await page.locator('textarea').fill(desc);                // 隊伍說明（唯一）
  await page.getByRole('button', { name: '開隊', exact: true }).click();

  // 開隊成功 → 導向帶隊 hub，看到剛開的隊（用唯一說明鎖定）
  await page.waitForURL(u => new URL(u).pathname === '/me/led-teams');
  await expect(page.locator('li').filter({ hasText: desc })).toBeVisible();

  // ── 2) 玩家 P-LL 尋隊申請（scope 到唯一說明的開放隊卡片）──
  await loginAs(page, { discordId: 6002, name: 'P-LL', role: 'user' });
  await page.goto('/teams/open');
  const openCard = page.locator('li').filter({ hasText: desc });
  await expect(openCard).toBeVisible();
  await openCard.locator('select').selectOption('c6002');    // option value = characterId
  // 等申請 POST 真的成交再往下（避免隊長端搶在 commit 前看不到申請）
  await Promise.all([
    page.waitForResponse(r => r.url().includes('/Applications') && r.request().method() === 'POST' && r.ok()),
    openCard.getByRole('button', { name: '申請' }).click(),
  ]);

  // ── 3) 隊長審核核准（從該隊卡片進審核佇列）──
  await loginAs(page, { discordId: 6001, name: 'P-LL-Leader', role: 'user' });
  await page.goto('/me/led-teams');
  const hubCard = page.locator('li').filter({ hasText: desc });
  await expect(hubCard).toBeVisible();                        // 確保 hub 載入完成再點
  await hubCard.getByRole('link', { name: /審核申請/ }).click();
  await page.waitForURL(/\/teams\/\d+\/applications/);
  await expect(page.getByText('C-LL')).toBeVisible();        // 申請者角色名
  await page.getByRole('button', { name: '核准' }).click();
  await expect(page.getByText('目前沒有待審核的申請')).toBeVisible();

  // ── 4) 玩家隊伍列表看到已加入 ──
  await loginAs(page, { discordId: 6002, name: 'P-LL', role: 'user' });
  await page.goto('/me/teams');
  await expect(page.getByText('已加入')).toBeVisible();
  await expect(page.getByText('E2E王').first()).toBeVisible();

  // ── 5) 玩家自助退隊 → 位子重開、已加入變空 ──
  // window.confirm 由頂端 dialog handler 自動接受
  await Promise.all([
    page.waitForResponse(r => /\/teamSlot\/\d+\/Leave$/i.test(new URL(r.url()).pathname) && r.request().method() === 'POST' && r.ok()),
    page.getByRole('button', { name: '退隊' }).click(),
  ]);
  await expect(page.getByText('你目前還沒有已確認的隊伍')).toBeVisible();
});
