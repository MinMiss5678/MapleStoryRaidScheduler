# 死碼清理：重複鎖測試 + RegistrationLock 命名 misnomer

> 輕量 plan。定位：period-less 收尾殘留。**不改行為**，純清死碼 + 正名。

## 掃描結論（已查證）

| 項 | 性質 | 處置 |
|---|---|---|
| `RegistrationLockIntegrationTests` | **真死碼**：是 `TeamSlotEditLockIntegrationTests` 的子集（少 timeout 測），名字沿用退場的 auto-assign 概念 | **刪除** |
| `RegistrationLock` / `IRegistrationLock` | **stale 命名**：現在只做 teamslot edit lock（classId 1002），registration/auto-assign 早退場 → misnomer、誤導 | **改名** |
| `TeamLeaderService.cs:163` 註解「比照 TeamSlotAutoAssignService 慣例」 | stale 註解（該 service 退場） | 改註解 |
| classId 1001 auto-assign 鎖 | 已清（`RegistrationLock` 無此方法） | 無事 |
| MQ / Redis Streams relay | 已清（grep 零） | 無事 |
| `MigrationDbContextModelSnapshot`（含退場 `BossTemplate*` + LfgIntent 舊 nullability） | stale **design-time** 快照（非 runtime 死碼；影響 `create-migration.sh` diff） | **另議**（見下，不放本 plan） |

## 範圍

### A. 刪重複測試（真死碼）
- 刪 `Test.Integration/RegistrationLockIntegrationTests.cs`。
- 先確認 `TeamSlotEditLockIntegrationTests` 覆蓋齊：`_SameTeamSlot_BlocksConcurrent`、`_DifferentTeamSlots_DoNotBlock`、`_TimesOut_WhenHeldByAnotherTransaction`（＝前者兩個方法 + timeout）→ 是嚴格超集，刪之無損覆蓋。

### B. RegistrationLock → 正名（misnomer，rename）
現在唯一方法是 `AcquireTeamSlotEditLockAsync`（classId 1002）。建議改名對齊用途：
- `IRegistrationLock` → **`ITeamSlotEditLock`**；`RegistrationLock` → **`TeamSlotEditLock`**（名稱待你定；也可 `IAdvisoryLock`）。
- 連帶改的檔（grep 過）：
  - `Domain/Repositories/IRegistrationLock.cs`（介面 + 檔名）
  - `Infrastructure/Repositories/RegistrationLock.cs`（類別 + 檔名 + ctor）
  - `Infrastructure/Services/TeamLeaderService.cs`（欄位 `_registrationLock`、ctor 參數）
  - `Presentation.WebApi/Extensions/ServiceCollectionExtensions.cs`（DI 註冊）
  - `Test/TeamLeaderServiceTests.cs`（`Mock<IRegistrationLock>`）
  - `Test.Integration/TeamSlotEditLockIntegrationTests.cs`（`new RegistrationLock(...)`）
  - `Domain/Exceptions/AdvisoryLockTimeoutException.cs`（doc 註解引用）
- ⚠️ 這是 rename 不是刪碼；若嫌動太多檔、可只做 A，B 留著當「已知 stale、面試可講」。

### C. 修 stale 註解
- `TeamLeaderService.cs:163`「比照 TeamSlotAutoAssignService 慣例」→ 拿掉退場 service 名（改「比照既有 weekday/time 換算慣例」或直接刪）。

### 非範圍（另議）
- **`MigrationDbContextModelSnapshot`**：仍模型化退場的 `BossTemplate*`、且 LfgIntent BossId 還 nullable。它是 EF **design-time** 快照，`db/create-migration.sh` 用它做 diff → stale 會讓下次自動產 migration 冒出假 DROP/ALTER。**但清它要跑 EF 工具重生快照**（對齊現行 DbModels），是獨立且較大的工作 → 另開 plan，不混進這次純刪碼。

## 驗收
- [ ] `dotnet build` 綠、`dotnet test`（單元 + 整合 Docker）綠——鎖互斥/逾時仍由 `TeamSlotEditLockIntegrationTests` 覆蓋。
- [ ] `grep -rn "RegistrationLock"` 若做 B 應歸零（除本 plan）；只做 A 則剩正常引用。
- [ ] 無行為改變（純刪重複測試 + 改名/註解）。

## 工時
- 只做 A：~10 分。A+B+C：~40 分（rename 靠 IDE/Rider 重構最穩，避免漏改）。

## 已定案
- 範圍：A + B + C 全做。
- 正名：`IRegistrationLock` → **`ITeamSlotEditLock`**、`RegistrationLock` → **`TeamSlotEditLock`**。
- 確認：`RegistrationLockIntegrationTests` 是整合測（非壓測）、且被 `TeamSlotEditLockIntegrationTests` 完整覆蓋 → 刪之零覆蓋損失。k6 壓測（`k6/confirm-accept-load.js`）不受影響。
