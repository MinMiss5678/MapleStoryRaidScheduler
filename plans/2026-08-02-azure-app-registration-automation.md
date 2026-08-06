# Azure App Registration 自動化腳本

> 輕量 plan（動手前的 spec）：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟；穩定規則再收進 `docs/`。

## 背景

`docs/microsoft-mail-oauth2-setup.md` 記錄的 Azure App Registration 設定全靠手動點 Azure Portal（新增 App、
設 Redirect URI、加 API 權限、改 `signInAudience`+`requestedAccessTokenVersion` 兩步順序），加上手動組 PKCE
授權網址、手動 curl 換 token。之後如果 refresh token 失效要重做一次，或要在另一台機器上重新設定，整套手動
流程很容易漏步驟或再踩一次已知的坑。

## 目標

把「開瀏覽器登入同意」以外的所有步驟寫成一支腳本：Azure App Registration 建立/設定 + PKCE 產生 + 授權網址
組裝 + code 換 token + refresh token 存檔，一次執行到底（中間跳出來要求貼 code 的那一步是唯一的人工
介入點，OAuth2 設計上就需要真人同意，不能也不該自動化）。

## 範圍

- 用 **Azure CLI（`az`）** 建立/設定 App Registration：
  ```bash
  az ad app create \
    --display-name "seq-mail-alert" \
    --sign-in-audience AzureADandPersonalMicrosoftAccount \
    --is-fallback-public-client true \
    --public-client-redirect-uris "http://localhost:8400" \
    --requested-access-token-version 2 \
    --required-resource-accesses @graph-permissions.json
  ```
  已查證 `--sign-in-audience` 跟 `--requested-access-token-version` **同一個指令**就能一起設定——手動走 Portal
  Manifest 編輯器時撞到的「要先存 token 版本、才能存帳戶類型」那個兩步驟順序限制，是 Portal UI 編輯器的
  限制，`az` CLI/底層 API 一次呼叫就能兩個一起帶，不會卡住。
- `graph-permissions.json`：Microsoft Graph 的 `Mail.Send` + `offline_access`（Delegated），對應的權限 GUID
  待實作時用 `az ad sp show --id 00000003-0000-0000-c000-000000000000 --query "oauth2PermissionScopes[?value=='Mail.Send' || value=='offline_access']"` 查出來寫死進腳本，不要憑記憶猜 GUID。
- PKCE 產生（沿用 `docs/microsoft-mail-oauth2-setup.md` 裡驗證過的 `openssl rand -hex 32` + sha256 手法，寫成
  腳本內建函式，不是每次手動打指令）。
- 腳本印出組好的授權網址，等使用者貼網址列的 `code` 回來（唯一的人工步驟）。
- 貼回 `code` 後，腳本自動用 `consumers` 端點換 token，`refresh_token` 寫進 `secrets/microsoft_mail_refresh_token.txt`。
- 腳本最後可選：直接呼叫一次 `/api/internal/alert-mail`（或直接測 Graph `sendMail`）驗證整條路通。

## 非範圍（YAGNI）

- **不自動化瀏覽器登入同意本身**：OAuth2 的 Authorization Code flow 設計上就是要真人同意，跳過這步等於繞過
  使用者授權本身的意義，不會做、也不應該做。
- **不做 refresh token 輪替後的自動存檔**：跟 `plans/2026-07-31-error-alerting.md` 的既有決策一致，維持現況
  （失效才重跑這支腳本）。
- **不做常駐服務**：這是一次性/低頻率使用的 setup script，不是背景常駐程式（呼應之前否決 `email-oauth2-proxy`
  那類常駐代理工具的理由——多一個要顧的服務不划算）。

## 決策

- 腳本語言：**bash**，跟這個 repo 既有的 `db/create-migration.sh`、`loadtest-multiround.sh` 一致，不用另外引入
  PowerShell 或其他語言。
- 放置位置：`k8s/setup-microsoft-mail-oauth.sh`（比照 `k8s/setup-secrets.ps1` 這類「一次性環境設定」腳本的
  存放慣例）或專案根目錄，待實作時依實際使用頻率決定。

## 驗收

- [ ] 腳本能在乾淨環境（未建立過 App Registration）從頭跑到「印出授權網址等待貼 code」
- [ ] 貼入 `code` 後，腳本自動換到 `refresh_token` 並寫入 `secrets/microsoft_mail_refresh_token.txt`
- [ ] 跑完後用既有的 `MicrosoftMailService`（或腳本內建的測試呼叫）驗證真的能寄出一封信
- [ ] 重跑腳本（模擬 refresh token 失效重新設定的情境）不會因為 App 已存在而報錯或產生重複 App Registration

## 工時估

- 研究/確認 `az ad app create` 確切參數 + Graph 權限 GUID 查詢方式：約 30 分鐘（部分已在這份 plan 裡驗證過）
- 腳本本體（PKCE + az CLI 呼叫 + 授權網址組裝 + 手動貼 code 的互動 + token 交換 + 存檔）：約 1-1.5 小時
- 測試（乾淨環境跑一次、重跑一次驗證冪等性）：約 30 分鐘
- 小計：約 2-2.5 小時

## 未解問題

- `az ad app create` 重複執行時的冪等性——用同樣 `--display-name` 會不會建立第二個同名 App（而非更新既有的）？
  待實作時查證，可能需要先 `az ad app list --display-name ... --query "[0].appId"` 判斷是否已存在，存在則走
  `az ad app update` 而非 `create`。
