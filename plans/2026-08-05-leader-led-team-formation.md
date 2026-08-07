# 隊長主導組隊 — 業務邏輯重規劃

> 狀態：**定稿**（§9 十九項決策全鎖定，2026-08-05）。分支起點：`fix/character-input-validation-on-dto`。

## 1. 為何轉向

**社群現實**：Discord 揪團是「隊長喊人 → 限定職業/攻擊 → 篩選後挑人」——重心在**隊長控制權、職業組成、人工篩選**。

**現況（grounded on code）**：
- **兩條不同的自動排團**：(1) 玩家報名觸發的 `TeamSlotAutoAssignService`——依 `同王 + 隊沒滿 + 時段重疊` 成隊，**完全不看職業/攻擊**；(2) admin 批次 `ScheduleService.AutoScheduleWithTemplateAsync`——**確實吃範本+職業分類**（職業分類 + 數量 + 優先級 + MinLevel/MinAttribute + IsOptional 嚴格湊隊）。
- 所以精確地說：**「照職業組成排團」今天唯一活著的地方是 admin 批次排團**，玩家/隊長端看不到；`BossTemplate`/`JobCategory` 對玩家報名路徑只剩前端補位提示。
- 無隊長歸屬、無入團審核——正是真實流程最核心的兩根柱子。

**結論**：自動排團不是有 bug，而是**站錯 C 位**。改為 **隊長制為主體、自動排團降級成「選用的初稿助手」**。

## 2. 目標流程（Pull + Push 都要）

1. **成員報名（不變）**：角色（職業/攻擊）+ 能打的時段。
2. **隊長開隊 = 數位化喊團**：選王 + 時段 + 限定條件（要哪些職業/分類、攻擊下限、各需幾人）。
3. **系統自動篩候選**：對報名池套 `時段重疊 + 職業符合 + 攻擊 ≥ 下限`。
4. 入隊兩路徑（**都需雙方同意**）：
   - **A. Pull（隊長邀）**：隊長看候選清單 → **邀請** → **玩家接受**才入隊。
   - **B. Push（玩家申請）**：玩家看自己夠格的隊 → 申請 → **隊長放行**才入隊。
5. **玩家決定自己的跨隊行程**（哪幾隊、時間連貫、不衝突）——這是只有玩家看得到的全域屬性，隊長只決定單一隊。故兩路徑都要玩家同意（見 §9.1）；系統於 `Confirmed` 時以 DB 約束擋時段重疊（見 §9.19）。

## 3. 領域模型改動

| 實體 | 改動 |
|---|---|
| `TeamSlot` | + `LeaderDiscordId`（隊長歸屬；現只有 `Source` 無 owner）。**`Source` 收斂成 `{ leader, admin }`、砍掉 `auto`**（新模型無 `auto` 產生者：報名不建隊、隊長自動排填的是 `leader` 隊、admin 草稿剩餘池是 `admin`）。「草稿 vs 已認領」**不進 `Source`**，用 `LeaderDiscordId is null`（未認領草稿）表達。遷移：CHECK → `IN ('leader','admin')` + 舊 `auto` 資料轉掉。<br>**+ `PeriodId` FK（`REFERENCES "Period" ON DELETE CASCADE`，NOT NULL）**：`SlotDateTime`＝打王時刻、`Period`＝這隊屬哪個週期，是**兩個不同概念**——`Period` 為**權威歸屬**、不從 `SlotDateTime` 反推（現況「entity 有 `PeriodId`、DB 無、靠時間 range 推導」是半套 phantom，一併轉正）。硬綁不變式：**`SlotDateTime` 必須落在所綁 `Period` 的 `[StartDate, EndDate]`**（app/domain 守；一隊只能排在自己那週、改時間不得跨週）。好處：刪 period 原子 cascade 帶走隊（推導版會留查不到的孤兒）＋`WHERE PeriodId=` 取代 range join。**Phase 1a migration 落地**（加欄 + FK + 回填既有隊的 PeriodId by SlotDateTime range），同步移除 domain/DTO 的 phantom 欄改讀真欄。 |
| `TeamSlotCharacter`（成員） | + `Status`（`Applied`〔Push 申請中〕/ `Invited`〔Pull 邀請中〕/ `Confirmed` / `Rejected`〔任一方拒絕/取消的終態〕）。**只有 `Confirmed` 占容量；`Applied`/`Invited` 皆不占**。**移除 `IsManual`**（見 §9.9）；不加 `Origin`（YAGNI，靠 `Status` + `TeamSlot.Source` 表達即可）。<br>**成員屬性＝承諾快照（明確設計，非快取）**：`AttackPower`、`CharacterName`、`Job`（＋`DiscordName`）存的是**凍結於 `Confirmed` 那刻**的值，**之後不隨 `Character`/`Player` 變動同步**。理由：(1) `AttackPower` 是自填可變、又是挑人門檻依據——roster 要記「以攻擊 X 被選/承諾」，像訂單品項凍結成交價，玩家事後改攻擊不得回溯改歷史；(2) `CharacterId REFERENCES Character ON DELETE SET NULL` + 另存 `CharacterName` → 角色被刪，roster 仍顯示「誰曾在此」（snapshot-survives-deletion，現況已有此設計）。狀態語意：`Applied`/`Invited` 期間顯示可即時值；一旦 `Confirmed` 就寫入快照定格。`DiscordName` 一併快照（KISS，roster＝當初談定的樣子）。**這是把現況「存了副本但沒講清是快照還是快取」的語意不清拍板成『凍結快照』**，並文件化「不同步、刪除存活」。<br>**空位不再是列、廢除哨兵**：現況「空位＝一列 `CharacterId=null` 佔位」＋ `DiscordId DEFAULT 0`／`DiscordName DEFAULT ''` 哨兵（見 `TeamSlotMemberDto`「null 表示空位」、merge／補位／`ScheduleService` 補空位）在新模型全退場——leader-led 無 auto-assign／merge／補位，隊形由 `TeamSlotRequirement` 定義。**每列 `TeamSlotCharacter` 都是真實成員**（`Applied`/`Invited`/`Confirmed`），`DiscordId` **NOT NULL**（無 0 哨兵）。**空位＝Σ需求 `Count` − 符合的 `Confirmed` 數**（推導，非佔位列）。`CharacterId` 維持 nullable **僅為 `ON DELETE SET NULL` 存活**、建列時不得為 null。`TeamSlot.FilledCount`/`HasRoom` 改由 `Confirmed` 計數（非 `CharacterId!=null`）。 |
| **隊伍條件（Level 2）** | 掛 **`TeamSlot` 實例**（非範本）。每個需求列 = **一組可接受職業（各帶自己的攻擊下限）** + `Count` + **`MinClearCount`**（本王通關數門檻，見下）。表：`TeamSlotRequirement`(`TeamSlotId`, `Count`, `MinClearCount`) + 子表 `TeamSlotRequirementJob`(`RequirementId`, `Job`, **`MinAttackPower`**) —— **攻擊下限下放到職業層級**（同攻擊下不同職業傷害期望不同）。**分類只是 UI「一鍵展開成職業」的方便鈕，儲存時展開成具體職業（快照）**——之後 admin 改分類不會回頭改到既有隊條件。「箭神(≥900) or 槍神(≥1000) 1位」= 列 `Count=1` + 職業 `{(箭神,900),(槍神,1000)}`。 |
| **通關次數（自填, Opt 1）** | 新表 `CharacterBossClear(CharacterId, BossId, ClearCount)`，玩家在角色頁自維護（同 `AttackPower` 信任模型、後端不查證）。派生**單一數字＝「本王總通關次數」**＝`Σ ClearCount WHERE BossId=本隊王 AND 屬該玩家的角色`（**同一隻王、跨該玩家不同角色相加**）。此數**兼作**：篩選 `MinClearCount`（「找打過的」=`≥1`）＋候選列顯示（老手參考）。反向「找沒打過的 carry」= 同欄位加 `MaxClearCount`，YAGNI 先不做。 |
| `Character.MapleBlessingLevel`（新增） | 楓葉祝福等級（自填 int，0=無；同攻擊信任模型）。**破例結構化**——楓葉祝福是隊員提供的隊伍 buff、**幾乎每隊必備**（見 #18 判準）。**顯示於候選列**（隊長挑 buffer 用），**不做 per-candidate 硬篩**（「有人開 20」是整隊只需 1 個的需求，硬篩會誤濾掉不帶 buff 的 DPS）；頂多隊層級軟提示「尚無達標 provider」。若楓葉祝福為帳號共通，日後可移 `Player`。 |
| `TeamSlot.Description`（新增） | nullable 自由文字「隊伍說明/公告」，吸收**所有非結構化招募需求**（buff 設定、楓葉祝福9/20、特殊要求…）。**不為個別 buff/技能開欄**（長尾無限、不可比）。界線＝「跨候選能否客觀比較」：能比→結構化篩選（職業＋base攻擊＋通關數）；不能比→丟這欄，靠人讀＋聊天協調。 |
| `Character.AttackPower` | **定義成「無BUFF（裸裝面板）攻擊」**（單一數，前端標清楚）。**不存有BUFF值**——有BUFF＝`base × 隊伍當下 buff 設定`（楓葉祝福9/20、活動道具…）的函數、非角色屬性、跨隊不可比。所有 `MinAttackPower` 門檻皆以 base 計；隊長心中的「buffed 門檻」自行換算成 base（base 是 buffed 的單調代理，把關效果相同且跨候選可比）。不加欄。 |
| `BossTemplate` | 保留為 **admin 全域「預設範本」**，隊長開隊時可**載入成自己那隊的條件再覆寫** → 「玩家自訂範本」的歸屬問題自然解決（改的是自己那隊，不是全域）。 |

**抉擇（KISS）**：入團申請用 `TeamSlotCharacter` 加 `Status` 表達，不另開 `JoinRequest` 表（少一張表、少一次 join）。

## 4. 應用 / 服務層

- **`TeamCandidateQuery`**：給定 `teamSlot(boss + time + requirements)` → 回符合的報名者（時段重疊 ＋ **`∃ job ∈ 集合: Character.Job = job 且 Character.AttackPower ≥ 該 job 的 MinAttackPower`** ＋ **本王通關數 ≥ 該列 `MinClearCount`**）。**重用 `SlotDateCalculator.IsTimeInAvailability`**。分類已在存檔時展開成職業，查詢不需再查 `JobCategory`。回傳的候選 DTO：角色名/職業/攻擊/時段 ＋ **本王總通關次數（＝篩選同一數字，跨該玩家角色加總；老手參考）** ＋ **楓葉祝福等級（挑 buffer 用，見 #18）**，**不含** discord 身分（見 §9.12）。
- **`TeamLeaderService`**：
  - `CreateTeamAsync(leader, boss, time, requirements)`
  - `InviteMemberAsync`（Pull：隊長邀 → `Invited`）＋ `AcceptInviteAsync` / `DeclineInviteAsync`（**玩家**接受→`Confirmed`／拒絕）
  - `AutoFillAsync(teamSlot)`（**依本隊條件對候選池一鍵自動排**；引擎＝`ScheduleService.FillTeamFromPool`〔見 §7〕，吃職業/攻擊；**產出的是一批邀請 `Invited`，仍需玩家接受**）
  - `ApplyAsync`（玩家申請 → `Applied`，Push）
  - `ApproveAsync` / `RejectAsync`（隊長審核，Push；approve→`Confirmed`）
- **權限/狀態機**：只有隊長能審核/挑人、只有本人能申請/退出；狀態轉移守 `HasRoom`（不超 `RequireMembers`）不變式，沿用聚合守法。
- **接手技術債（自 validation-layering 延後至此）**：現行 `TeamSlotService.UpdateAsync` 是 god method——同時做 建/改/刪隊＋加/改/刪成員，且用 `bool isAdmin` 當角色開關、授權判斷散在每個 mutation 點（`if(!isAdmin)…` ×5+）。leader-led 把授權主體從 `isAdmin` 換成「隊長／申請者／admin」時本就要重寫這些分支，屆時一併：(1) 建隊分支（`Id<=0`）抽成獨立 `CreateTeamAsync`（含其 FK 存在性檢查〔Boss/Period/Template〕），(2) 授權集中到狀態機/服務入口、不再散在迴圈內。**現在刻意不先重構**（避免替即將被換掉的 code 拋光）。另補：admin 加成員的 `TeamSlotCharacter.CharacterId` 目前無存在性檢查（壞 id→500），此處一併收（需 by-id 角色存在查詢）。見 `plans/2026-08-06-validation-layering.md` §5.2。

## 5. API（演化現有 `TeamSlotController`）

| Method | Path | 用途 |
|---|---|---|
| POST | `/TeamSlot` | 隊長開隊 + 條件 |
| GET | `/TeamSlot/{id}/Candidates` | Pull：符合的玩家清單 |
| POST | `/TeamSlot/{id}/Invitations` | Pull：隊長邀請候選（→`Invited`） |
| POST | `/TeamSlot/{id}/AutoFill` | 一鍵自動排＝批次邀請（→`Invited`，仍需玩家接受） |
| PUT | `/TeamSlot/{id}/Invitations/{memberId}` | **玩家** accept→`Confirmed` / decline |
| GET | `/TeamSlot/Open` | Push：玩家看自己夠格的開放隊 |
| POST | `/TeamSlot/{id}/Applications` | Push：玩家申請（→`Applied`） |
| PUT | `/TeamSlot/{id}/Applications/{memberId}` | Push：隊長 approve→`Confirmed` / reject |
| GET | `/Me/Invitations`、`/Me/Teams` | 玩家看自己的邀請/已入隊——**排自己跨隊行程**用（§9.19） |

## 6. 前端

- **隊長**：開隊頁（設條件＝每個需求列**勾一組可接受職業、各自填攻擊下限**〔選分類＝一鍵勾滿該群職業、也能單勾特定職業〕＋數量＋通關數門檻；可載入預設範本當起點）、候選清單挑人、申請審核佇列、成員管理。
- **玩家**：可申請的隊列表（依「我報名的角色」過濾夠格的）、申請/退出、狀態顯示。
- 現有補位頁（`PlayerRaidTeamCard` / `getMissingSlots`）演化成上述。

## 7. 自動排團的新定位（搬到「挑人時」）

- **報名不觸發自動排團**：`RegisterService.CreateAsync` **不再呼叫 `AutoAssignAsync`**；報名 = 只把角色+時段放進**候選池**。
- **自動排團搬家成隊長挑人時的「依條件一鍵自動排」動作**：隊長在自己那隊按「自動排」→ 系統對**本隊候選池**（已依 `時段重疊 + 職業 + 攻擊` 篩過）自動湊一份 roster → 隊長**微調/放行**才定案。
- **引擎來源＝admin 那支的 `ScheduleService.FillTeamFromPool`**（不是報名版 `AutoAssignAsync`）：它已會「依需求從池子把一隊補滿、湊不齊留空」，比報名版成熟。改動：`職業分類比對` → Level 2 的 `Job ∈ 集合`、`MinAttribute` → `MinAttackPower`。核心媒合（`SlotDateCalculator.IsTimeInAvailability`）留用。
- 影響：**register 單元測試 + `register.spec` E2E 要改**（報名不再產隊）。

## 8. 分期

> 順序原則：每階段對玩家有可見價值、能單獨 PR + 驗透；**先加新路徑、確認站穩後才拆舊碼**。Phase 1／3 顆粒過粗（big-bang），各再切三刀——尤其 migration 要能單獨落地驗（呼應 [[stop-revise-plan-when-problems-pile-up]]：上次 TeamSlot 充血一次做太多撞一堆坑）。**Phase 2 依賴 `23505→409`（validation-layering，已合併）**，故 Phase 2 前置已就緒。

- **Phase 1（MVP＝Pull 最小閉環）**——拆三刀：
  - **1a 資料層先落地（不改行為）**：migration（`TeamSlot.LeaderDiscordId`、`Source` 收斂 `{leader,admin}` + 轉舊 `auto`、`TeamSlotCharacter.Status`、`TeamSlotRequirement(+Job)`、`CharacterBossClear`、`Character.MapleBlessingLevel`、`TeamSlot.Description`）+ 領域實體 + repository。舊 `AutoAssignAsync` **暫留**、新欄先不接 UI。驗：migration 可逆測試 + repo 整合測試，風險隔離。
  - **1b 讀路徑**：隊長開隊 + 設條件（Level 2）+ 候選查詢 `TeamCandidateQuery`（時段+職業+攻擊+通關數；DTO 不回 discord 身分）。隊長能開隊、看候選，**尚不能入隊**——純讀、無併發、好驗。
  - **1c 寫路徑 + 破壞性收尾**：Pull 邀請/接受（`Invited`→`Confirmed`）+ confirm 併發（1002 鎖/xmin + 跨隊重疊 exclusion + 重複邀請 unique，見 §10）+ **這時才**移除 register→自動排團（改 `RegisterService` 單元測試 + `register.spec`）。併發與破壞性變更集中一階段一次驗透。
- **Phase 2**：**Push 申請 + 審核狀態機**（`Applied`→`Confirmed`）+ 通知（Discord/mail 沿用 outbox）。重複申請去重靠 `23505→409`（已就緒）。
- **Phase 3（升級挑人 + 拆舊）**——拆三刀，**3c 必須等 3a/3b 站穩才做**：
  - **3a 加 auto-fill 引擎**：以 `ScheduleService.FillTeamFromPool` 為底做隊長「依本隊條件一鍵自動排」（職業分類→Level 2 集合、`MinAttribute`→`MinAttackPower`；產出 `Invited` 仍需玩家接受）。純加功能。
  - **3b 降 admin 全期重排**：`AutoScheduleWithTemplateAsync` → 「草稿剩餘池」（只湊未進 `Confirmed` 的人、不覆蓋隊長隊、需人認領才落地）。改既有 admin 行為 → 動 `admin-rebuild`/`admin-conflict` E2E + `ScheduleServiceAutoScheduleTests`。
  - **3c 清死碼（最後、有退路才刪）**：`IsManual` 欄／`TeamSlot.ReschedulableMembers`／自動合併 `TeamSlotMergeService`（見 §9.9）+ 前端整併 + 範本預設載入。動 `TeamSlotMergeServiceMergeTests` + `TeamSlotAggregateTests`。順帶接手 **validation-layering §5.2 延後的 `TeamSlotService.UpdateAsync` god-method 重構**（見 §4「接手技術債」）。
- **Phase 4**：`JobCategory` 補 **admin CRUD**（取代手動 SQL；**不新增幹部/團長角色、不開放玩家**，維持 admin 集中）+ 好友同組 hint（Q4，**保留**：報名選填 group key，候選排序優先同 key）。範本仍為 admin 全域預設、隊長載入覆寫成自己那隊條件（非 admin 職責、屬隊長正常操作）。

## 9. 決策（2026-08-05 確認）

1. ✅ **兩路徑都要雙方同意**（**修正**：原「Pull 直接加」已推翻，見 §9.19）：Pull＝**隊長邀→玩家接受**（`Invited`→`Confirmed`）；Push＝玩家申請→隊長放行（`Applied`→`Confirmed`）。理由：玩家要能掌握自己跨隊行程，就必須對「加入」有決定權；直接加會讓手最快的隊長鎖住玩家、玩家無法自排。
2. ✅ **條件軟篩選 + 隊長可 override**（避免「湊不滿排不進」老問題）。
3. ✅ **自動排團搬到「挑人時」**：報名**不**觸發自動排團；`AutoAssignAsync` 演化成隊長按「依本隊條件一鍵自動排」的動作（對候選池、吃職業/攻擊），隊長再微調。保留媒合能力、換位置＋升級。
4. ✅ **入團用 `TeamSlotCharacter` 加 `Status`**（不另開表）：`Applied`〔Push 申請中〕/ `Invited`〔Pull 邀請中〕/ `Confirmed` / `Rejected`。**只有 `Confirmed` 占容量；`Applied`/`Invited` 皆不占**。（`Invited` 因 #1 改回邀請制而加回。）
5. ✅ **不分權**：不新增幹部/團長角色，`JobCategory`/範本維持 **admin 集中**（頂多補 admin CRUD）。
6. ✅ **好友同組保留**（Q4）：報名選填 group key，候選排序優先同 key。
7. ✅ **條件用 Level 2（隊長自訂 OR 組合）**：每個需求列＝一組可接受職業（分類一鍵展開成職業快照 ＋ 可單勾特定職業）＋數量＋攻擊下限。篩選＝`Job ∈ 集合`。
8. ✅ **admin 全期重排降級 → 草稿「建議名單」（B）**：`AutoScheduleWithTemplateAsync` 的全期權威重排 → 降成選用的「**草稿剩餘池**」，只對還沒進任何 `Confirmed` 隊的人**產「建議分組」（不是已存在的隊）**、**絕不覆蓋隊長隊**。底層 `FillTeamFromPool` 引擎**留用**作隊長 per-team auto-fill（見 §7）。
9. ✅ **不變式：存在的隊一定有召集人**。草稿是「建議名單」，需**有人自願認領、當召集人**才實現成真隊（`Source=leader`、補上 `LeaderDiscordId`）；**沒人認領就不成形**（沒團長＝沒團，符合現實）。→ 不存在無主隊；`admin` + `LeaderDiscordId=null` 僅是「未認領建議」的過渡表示。
10. ✅ **`TeamSlot.Source` 收斂成 `{ leader, admin }`、砍 `auto`**：新模型無 `auto` 產生者。「未認領建議 vs 已成隊」用 `LeaderDiscordId is null` 表達，不進 `Source`。遷移改 CHECK ＋轉舊 `auto` 資料。
11. ✅ **隱私：PII 最小化、承諾才揭露**。`discordId`／`discordName` 屬敏感（與 `sentry-discordid-hmac` 立場一致）。**未認領建議**只顯示組成（職業/攻擊/時段/角色名），**藏聯絡身分**；**認領後**才把 roster 的 `discordId`/`name` 給召集人；已成隊隊員彼此可見。前提：**認領＝真承諾（掛名負責）**，否則變免費去匿名按鈕。Pull 候選清單同原則（`discordName` 顯示與否見待定 §12）。
12. ⚠️ **[SUPERSEDED 2026-08-07 → 候選透明化]** 原決策：Pull 候選清單走「更嚴」，藏 `discordId`+`discordName`、按能力非身分挑。**已翻**：本產品為公會內固定開團、身分本就互通（角色名即知是誰）、非公會開放已 YAGNI（§12）→ 匿名收益薄、且壓抑「固定團」與造成「同人多角看不出」黑箱。改為**候選回 `DiscordName`（顯示名，raw discordId 仍留後端）**，隊長認得出老班底。保留能力欄；留意新人固化（日後軟機制）。細節見 `plans/2026-08-07-leave-team-and-candidate-dedup.md`。<br>_原文（保留）_：藏 `discordId` + `discordName`，只顯示角色名/職業/攻擊/時段——隊長按能力挑；身分於 pick（承諾）後才顯示。
14. ✅ **通關次數（Opt 1 自填）**：新表 `CharacterBossClear(CharacterId, BossId, ClearCount)`，玩家自維護（同 `AttackPower` 信任模型）。派生**單一數字「本王總通關次數」＝同一隻王、跨該玩家不同角色相加**，**兼作**篩選 `MinClearCount`（「找打過的」=`≥1`）＋候選列顯示（老手參考，非身分故可顯示）。反向 `MaxClearCount`（找沒打過的 carry）YAGNI 先不做。系統暫不追蹤實際通關（Opt 2 日後可疊加使其權威）。
15. ✅ **攻擊下限下放到職業層級**：`MinAttackPower` 從 `TeamSlotRequirement`（列）搬到 `TeamSlotRequirementJob`（每個可接受職業各一個下限）——同攻擊下不同職業傷害期望不同。篩選＝`∃ job∈集合: 候選.Job=job 且 攻擊≥該 job 下限`。`MinClearCount` 維持列層級（通關是本王經驗、與職業無關）。
17. ✅ **長尾需求走自由文字、不逐項開欄**：`TeamSlot` 加 nullable `Description`（隊伍說明）吸收特殊要求／其他 party buff…；**不為個別 buff/技能開欄**。界線（**修正**）：**結構化 = 可客觀比較 AND 近乎必備/高價值**；只可比但非必備（聖靈、各種 link…）→ 說明欄＋聊天。
18. ✅ **楓葉祝福破例結構化**：因**幾乎每隊必備**（過門檻）。`Character.MapleBlessingLevel`（自填），**候選列顯示**供隊長挑 buffer；**不做 per-candidate 硬篩**（整隊只需 1 provider，硬篩會誤濾 DPS）→ 顯示 ＋ 隊長手挑 ＋ 隊層級軟提示「尚無達標 provider」。base 彈性仍靠隊長 override、系統不算 buff 補償。帳號共通則日後可移 `Player`。
16. ✅ **攻擊採單一「無BUFF base」、不建模 buffed**：三輪追問（有/無BUFF→活動道具→楓葉祝福9 vs 20）證明「有BUFF攻擊」是 `base × 隊 buff 設定` 的函數、非角色屬性、跨隊不可比 → **不存**。只存 `Character.AttackPower`＝無BUFF base（唯一穩定客觀可比）；所有 `MinAttackPower` 以 base 計，限定 buffed 的隊長自行換算成 base 門檻。**推翻先前「雙值＋隊層級 basis」構想**（不加欄、無 `AttackBasis`）。**base floor 內含隊長對自身隊伍 buff（如楓葉祝福9/20）的判斷、由隊長自行下調**（強 buff 隊設低、弱 buff 隊設高）；系統只比 `候選base ≥ floor`、**不算 buff 補償**（否則要存 buff 等級＋換算公式，回到被拒的兔子洞）。楓葉祝福等級寫 `Description`（#17）給人看。
19. ✅ **玩家擁有跨隊行程＋併發/重疊防護**：跨隊行程（哪幾隊、連貫、不衝突）是只有玩家看得到的全域屬性 → **玩家決定**（故 #1 兩路徑皆需玩家同意）；隊長只決定單一隊。防護分層：
    - **跨隊時段重疊（硬規則）→ DB 約束**（同 `DiscordId`/角色 + 同 `SlotDateTime` 的 `Confirmed` 只能一筆；unique/exclusion constraint，原子擋，per-team 鎖管不到跨隊）。
    - **同隊容量（confirm/退出）→ 沿用 1002 `AcquireTeamSlotEditLockAsync`**（取鎖→重讀 `Confirmed` 數→檢查）；擋隊長雙送/退出-vs-approve race。
    - **重複申請/邀請 → DB unique**（同玩家同隊一筆有效 `Applied`/`Invited`）。
    - **1001（auto-assign per period）退場**（報名不再開隊）；其保護的「跨隊去重」需求改由上面 DB 約束承接。
13. ✅ **`IsManual` 廢除**：它唯一的邏輯消費者是「批次重排/合併保護」（`ScheduleService` 保留隊 + `TeamSlot.ReschedulableMembers()`）。新模型下補位沒了、報名 auto-assign 沒了、重排降 B（加法補漏、不改現有隊）、**自動合併 `TeamSlotMergeService` 退場**（本掛在 `AutoAssignAsync` 尾巴，報名不再產隊）→ 保護對象全消失，所有 `Confirmed` 皆隊長所置。**Phase 3 清掉 `IsManual` 欄 + `ReschedulableMembers` + 自動合併**。殘留需求「重跑 auto-fill 別蓋掉隊長手挑的人」由 `Status=Confirmed`／空位占用處理。
20. ✅ **滿員邀請：不失效、顯示候補，過期才終結**。因超額邀請是刻意允許的（#4/§10「邀8補6」）＋邀請不占容量（只 `Confirmed` 占），「已被 `Invited` 但隊已滿」是**預期正常狀態**、非異常。且**「滿員」是暫時的**（`Confirmed` 成員退出→空位重開→該邀請又可接受），故：
    - **不在滿員時自動拒絕/銷毀邀請**（那會誤殺仍可能生效的 standing offer）。
    - 站內「我的邀請」**照顯示**，但查詢帶出隊伍當前 `Confirmed`/`RequireMembers`，前端在滿員時把「接受」弱化成**「隊伍已滿・候補中」**——顯示誠實、真正把關交給 accept 的 **1002 鎖**（race 中按下也擋回「已滿」）。
    - **不加新 `Status`**：「滿員」是**推導**（`Confirmed ≥ RequireMembers`），非狀態；`{Applied,Invited,Confirmed,Rejected}` 不變。
    - **真正終結邀請的是「打王時間已過／週期結束」**（不可逆過期）→ 清理成 `Rejected`（或 expired），與「暫時滿員」分開；屬 Phase 2 polish。
21. ✅ **玩家側邀請可見範圍：組成看能力、身分等承諾**（§9.11/§9.12 隱私原則的玩家側鏡像）。玩家收到邀請時——
    - **看得到**：王 + `SlotDateTime`、這隊條件 + 自己被邀補的位（職業/攻擊下限/通關門檻）、目前組成的**能力輪廓**（`Confirmed` 成員的職業/攻擊/本王通關數/楓葉祝福 + 剩餘空位）→ 供知情決定（#1 玩家掌握跨隊行程、判斷隊夠不夠強）。
    - **看不到**：其他成員的 Discord 身分（`discordId`/`discordName`）——**接受（→`Confirmed`）後**才彼此揭露（§9.11「已成隊隊員彼此可見」）；與 §9.12「按能力非身分」對稱（玩家可能 decline 去別隊，不該先看光隊員身分）。
    - **例外**：邀請的**隊長名字顯示**（開隊/發邀＝已自我承諾曝光，§9.11）。
    - 影響：**邀請/候選查詢 DTO 形狀**（1b `TeamCandidateQuery` +「我的邀請」查詢回能力欄、不回隊員 discord 身分）；**不影響 1a migration**。
    - **取捨（日後可改）**：嚴格隱私（藏隊員身分到接受，**本案採用**）vs 社交友善（先給看 `Confirmed` 隊員名字助信任）。

## 10. 並發控制（新模型對照，實作參考）

三種機制各守不同不變式，互補：**悲觀鎖＝同隊容量、樂觀鎖＝單列狀態轉移、DB 約束＝跨隊重疊與去重**。

### 悲觀鎖 — advisory 1002（per `teamSlotId`，`AcquireTeamSlotEditLockAsync`）
守「同隊容量」不變式（`Confirmed ≤ RequireMembers`）。只用在**會動容量的操作**：

**根因**：`Confirmed` 計數是**多方併發共享**的——approve（隊長）＋ accept（任一/多個被邀玩家）都能 +1，還可能雙送。任一 confirm 都得序列化，否則兩個併發 confirm 各讀到「還有空位」舊值 → 超編。**鎖是 per-team、守容量計數，不是 per-actor。**

| 操作 | Status 轉移 | 為何要鎖 |
|---|---|---|
| 玩家接受邀請 | `Invited`→`Confirmed` | +1 容量；與其他 accept／approve 併發搶最後空位（隊長還可能邀8補6超額邀） |
| 隊長放行申請 | `Applied`→`Confirmed` | +1 容量；**與 accept 併發**（隊長 approve A 的同時玩家 B accept）→ 各讀到有空位 → 超編。**非** approve-vs-approve |
| 成員退出 | `Confirmed`→移除 | −1 容量；退出-vs-接受撞滿員要序列化 |
| auto-fill 批次邀請（選用） | →`Invited` | 邀請不占容量、輕量，防重複邀 |

作法：取鎖 → 重讀 `Confirmed` 數 → 檢查 `< RequireMembers` → 寫。**邀請/申請本身不占容量 → 不需這把鎖**。（替代方案：`TeamSlot` 加版本欄做樂觀 CAS，效果同；現有 code 用 advisory 鎖故沿用。）

### 樂觀鎖 — xmin（per `TeamSlotCharacter` 列，`WhereRaw xmin = @version::xid`）
守「單列狀態轉移」不被雙邊或 stale 蓋掉：
- **approve vs decline 撞同一列**：隊長放行同時玩家取消同一筆 → 只有一個轉移生效，輸方拿「已被處理」。
- **stale client**：隊長載入清單後隔一陣才動作、期間該列已變 → 擋 lost update。
- 玩家取消**自己**那筆不必搶整隊 1002 鎖，用 xmin 就夠。

### DB 約束（宣告式，鎖的盲區）
- **跨隊時段重疊** → unique/exclusion（同 `DiscordId`/角色 + 同 `SlotDateTime` 的 `Confirmed` 唯一）。per-team 鎖管不到跨隊，這是唯一可靠解。
- **重複申請/邀請** → unique（同玩家同隊一筆有效 `Applied`/`Invited`）。

### 退場 / 平移
- **1001（auto-assign per period）退場**：報名不再開隊；其「跨隊去重」責任交給 DB 重疊約束。
- 呼叫端平移：**1002/xmin 從 `FillSlotAsync`／`UpdateAsync` 換成 accept／approve／leave**；補位（Fill）與 admin 全期重排（`UpdateAsync`）皆退場（見 §7、§9.8）。

## 11. 通知（Phase 2 設計）

**管道＝Outbox（原子、跨行程、可靠）+ 主走 Discord DM**（玩家本來就是 Discord 使用者；mail 多數玩家沒填、且屬 `AlertMail` 系統告警用途，非玩家面）。

### 觸發事件（雙向）
| 事件 | 通知對象 |
|---|---|
| 隊長邀請（→`Invited`，Pull） | 被邀玩家 |
| 玩家接受/拒絕（→`Confirmed`/`Rejected`） | 隊長 |
| 玩家申請（→`Applied`，Push） | 隊長 |
| 隊長核准/拒絕（→`Confirmed`/`Rejected`） | 申請玩家 |

### 機制（沿用現有 outbox）
1. 狀態改動的**同一交易**內寫一列 `OutboxMessage`（新 `Type`，如 `MemberInvited`；payload 帶 `teamSlotId / targetDiscordId / BossName / SlotDateTime`）→ 通知不因崩潰遺失。
2. **bot 行程**註冊對應 `IOutboxHandler` → 讀到 → 送 Discord（跨行程：寫在 API、送在 bot，正是 outbox 存在的理由）。
3. `OutboxDispatcher` 既有保證：at-least-once + 冪等 + 多 pod `FOR UPDATE SKIP LOCKED`。

### Discord DM（需新增 per-user 能力）
現有 `IDiscordService.SendMessageAsync(message)` **只發固定頻道**。要私訊本人需加：
```csharp
Task SendDirectMessageAsync(ulong discordId, string message);
// impl：GetGuildAsync(GuildId) → GetMemberAsync(discordId) → member.SendMessageAsync(msg)
```
前置已就緒：`DiscordOptions.GuildId` 有、bot intents 已含 `GuildMembers`。**為何 DM 而非頻道 @提及**：@頻道會把「誰被邀」公開，違反 §9.11/§9.12「承諾前不揭露身分」；DM 只有本人看到。

### ⚠️ handler 失敗分流（否則毒訊息）
outbox 失敗會重試 5 次。handler 要分辨：
- **永久失敗吞掉、不重試**：`UnauthorizedException`（對方關 DM，403）、`NotFoundException`（已退公會）→ log 後**不 rethrow**（讓 outbox 標 processed）。
- **暫時失敗才 throw**：網路、429 限流 → 讓 outbox 重送。

### 站內清單為權威、DM 為加料
DM 可能永久送不到（玩家關 DM）→ **站內「我的邀請/我的隊」清單（`/Me/Invitations`、§5）是權威真相**，玩家登入即見；**DM 只是主動提醒**，非唯一觸達。即使 DM 全滅，功能不漏。

## 12. 非公會開放：前提與濫用面（未來，YAGNI）

> 現況：**登入就要公會身分**——`AuthAppService.LoginAsync` 由公會身分組經 `DiscordRoleMapping` 解析系統角色，解析不到即登入失敗（§見 code）。故「已登入者必在公會」是**被 auth 硬性保證**的，本節僅在**未來真的開放非公會路人**時才需處理。

### 若開放非公會使用者，破的不只通知
公會今天免費提供**信任/治理邊界**：管理員把關誰能進/踢人、社群問責、Discord 自身檢舉封鎖。去掉它要自己承接：
- **DM 不可達**：bot 只能私訊同公會的人 → 非公會者 `GetMemberAsync` 丟 `NotFoundException`（§11 已當永久失敗吞掉、不重試）→ 靠站內清單 fallback（web push 是「不靠 Discord 也能推」的未來選項，代價大、iOS 受限，YAGNI）。
- **角色**：`DiscordRoleMapping` 靠公會身分組 → 非公會者需給預設角色。

### leader-led 模型本身已擋掉硬性「卡位」
- **雙方同意（§9.1）**：陌生人無法硬塞進隊；隊長 approve/邀請才入隊 → 隊長天生守門。
- **隊長篩選**：組成由隊長控制，惡意申請頂多塞審核佇列、進不了隊。
- **跨隊重疊 DB 約束（§10）**：一人同時段只能 Confirmed 一隊 → 擋「一人卡爆多隊同時段」。

### 殘留濫用 + 反制順序（開放路人時才做）
| 殘留 | 反制（依優先） |
|---|---|
| throwaway 帳號 sybil、狂申請/狂邀洗佇列 | **1. 速率限制**（每人每期申請上限、每隊長邀請上限）+ **檢舉→admin ban** |
| Confirm 後放鴿子（占位不出席） | **2. no-show 信譽追蹤**（影響候選排序/門檻） |
| 點對點騷擾（不想跟 X 同隊/被 X 申請/被邀） | **3. 玩家層黑名單/封鎖**（互不媒合）——**最重、最後做**，等騷擾真的發生再上 |

**結論**：維持公會為中心（它免費做治理、整套 auth/角色/通知本就假設它）。黑名單/反濫用**先不做**；真開放非公會時才照「速率限制 → 信譽 → 黑名單」順序加。
