# 文件索引

這個資料夾的檔案分兩類：**現況文件**（描述系統目前長什麼樣子，要跟著程式碼更新）跟
**歷史參考**（描述已經淘汰的做法，內容不會再更新，留著純粹當學習筆記）。

## 現況文件

| 檔案 | 內容 | 什麼時候看 |
|---|---|---|
| [`architecture.md`](architecture.md) | 系統架構總覽、關鍵設計決策、領域設計、ERD、部署拓樸 | 想知道「這個系統整體長什麼樣子、為什麼這樣設計」 |
| [`business-rules.md`](business-rules.md) | 業務規則清單（不變量、驗收條件），跟 `architecture.md` 是一體兩面：這裡列**規則**，機制怎麼實作看 `architecture.md` | 想知道「系統必須遵守什麼規則」，或改動前確認會不會破壞既有不變量 |
| [`e2e-testing-setup.md`](e2e-testing-setup.md) | Playwright E2E 測試怎麼跑、seed 資料模型、踩過的坑 | 要跑/寫 E2E 測試 |
| [`cd-deploy-setup.md`](cd-deploy-setup.md) | CD 部署（GitHub Actions `deploy.yml` → k8s 滾動更新）的完整流程、回滾、migration 失敗恢復 | 要部署到 production，或 migration 出事要恢復 |
| [`deployment.md`](deployment.md) | 手動部署（本機 `deploy.ps1` / `rollout.ps1`，不走 CI）的操作指令 | 要手動操作 k8s（首次部署、改密碼、重置 DB） |

## 歷史參考（內容已淘汰，僅供學習）

| 檔案 | 為什麼還留著 |
|---|---|
| [`gitlab-selfhost-ci-setup.md`](gitlab-selfhost-ci-setup.md) | 自架 GitLab CE + dind 的設定筆記；CI 已遷 GitHub Actions，這份記錄的是 runner 註冊、dind、network_mode 這些底層原理，當年親手踩過的坑 |

## 維護原則

- **同一件事只在一個地方講清楚，其他地方用連結指過去**：`architecture.md` 的「部署」章節只放摘要 + 連結，細節留給 `cd-deploy-setup.md`/`deployment.md`/`e2e-testing-setup.md`。曾經因為 `architecture.md` 自己重複展開 CI/CD 細節，跟專門文件各自漂移、甚至同一份文件內兩段互相矛盾（CI 從 GitLab 遷到 GitHub Actions 時沒有同步改完整）。
- **文件過期就要嘛更新、要嘛明確標示**：不確定的話，看那份文件是不是在用「現在式」語氣描述一個已經不存在的系統——是的話要嘛更新，要嘛搬進「歷史參考」並加註淘汰原因。
