# Seq Alert 寄信設定筆記（Microsoft Graph OAuth2，個人帳號）

Seq Alert 觸發時打 `POST /api/internal/alert-mail`（`AlertMailController`），內部用 Microsoft Graph 的
`sendMail` 寄信。決策脈絡（為什麼不用 Gmail、為什麼選這條路）見 `plans/2026-07-31-error-alerting.md`；
這份是「怎麼設定 + 踩過的坑」參考，之後 refresh token 失效要重做時照這份操作。

## 前提：這是個人 Microsoft 帳號（Outlook/Hotmail）的做法

用的是 **Authorization Code flow + PKCE + Delegated `Mail.Send`/`offline_access`**，不需要 Microsoft 365
商務租戶或 admin consent。如果手上是商務帳號，改裝 `Seq.App.Mail.Microsoft365`（Application 權限）更簡單，
不用走這份文件。

## 架構

```
Seq Alert（@Level = 'Error'）
  → Seq.App.HttpRequest（POST，帶 X-Alert-Secret）
    → POST /api/internal/alert-mail（AlertMailController，[AllowAnonymous] + 共用密鑰驗證）
      → IMicrosoftMailService.SendMailAsync
        1. 用 refresh_token 換 access_token（POST /consumers/oauth2/v2.0/token）
        2. 呼叫 Microsoft Graph POST /v1.0/me/sendMail
```

| 檔案 | 作用 |
|---|---|
| `Application/Options/MicrosoftMailOptions.cs` | 設定值（TenantId/ClientId/RefreshToken/ToAddress/WebhookSecret，皆有對應 `*File`） |
| `Infrastructure/Services/MicrosoftMailService.cs` | 換 token + 呼叫 Graph sendMail |
| `Presentation.WebApi/Controller/AlertMailController.cs` | 對外端點，共用密鑰保護 |
| `Presentation.WebApi/Middleware/IdempotencyMiddleware.cs` | `/api/internal/` 路徑排除（Seq 呼叫沒有 `X-Idempotency-Key`） |
| `secrets/microsoft_mail_refresh_token.txt` | 一次性登入拿到的 refresh token（gitignored） |
| `secrets/microsoft_mail_webhook_secret.txt` | Seq → `AlertMailController` 的共用密鑰（HTTP header `X-Alert-Secret`），`openssl rand -hex 32` 產生（gitignored、選填） |

## 一次性設定步驟

### 1. Azure App Registration

`entra.microsoft.com` → App registrations → New registration。

- Redirect URI：**Mobile and desktop applications** 類型，值填 `http://localhost:8400`（不要用 `http://localhost`，見下方「坑」）。
- API permissions → Microsoft Graph → **Delegated permissions** → 加 `Mail.Send` + `offline_access`（Add a permission 面板要先點 Microsoft Graph、再選 Delegated permissions，才會出現搜尋框，見下方「坑」）。
- 不用建立 Client Secret——這是公用用戶端（Public client，走 PKCE），換 token 不需要也不能帶密鑰。

### 2. 改成支援個人帳號（Manifest）

個人帳號要用 `consumers` 端點才能連到真正的信箱，這需要 App 支援個人帳號登入。**分兩步、順序不能反**：

1. Manifest 裡先設 `"requestedAccessTokenVersion": 2`，存檔。
2. 存檔成功後，再把 `"signInAudience"` 改成 `"AzureADandPersonalMicrosoftAccount"`，再存一次。

（一次改兩個會報 `Property api.requestedAccessTokenVersion is invalid`——Azure 要求先有 token v2 才准許開放個人帳號。）

### 3. 一次性登入拿 refresh token（PKCE，Authorization Code flow）

產生 PKCE 參數（**務必寫成檔案讀回，不要手動轉錄複製**——見下方「坑」）：

```bash
openssl rand -hex 32 > code_verifier.txt
CV=$(cat code_verifier.txt)
CC=$(printf '%s' "$CV" | openssl dgst -sha256 -binary | openssl base64 | tr '+/' '-_' | tr -d '=')
echo "$CC"
```

瀏覽器開這個網址（`{client_id}` 換成你的）：

```
https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize?client_id={client_id}&response_type=code&redirect_uri=http%3A%2F%2Flocalhost%3A8400&response_mode=query&scope=offline_access%20https%3A%2F%2Fgraph.microsoft.com%2FMail.Send&state=12345&code_challenge={CC}&code_challenge_method=S256
```

登入、同意後瀏覽器會導向 `http://localhost:8400/?code=...`（顯示「無法連上這個網站」是正常的，8400 本來就沒服務在監聽）。複製網址列的 `code` 參數，換 token：

```bash
curl -s -X POST "https://login.microsoftonline.com/consumers/oauth2/v2.0/token" \
  --data-urlencode "client_id={client_id}" \
  --data-urlencode "scope=https://graph.microsoft.com/Mail.Send offline_access" \
  --data-urlencode "code={拿到的 code}" \
  --data-urlencode "redirect_uri=http://localhost:8400" \
  --data-urlencode "grant_type=authorization_code" \
  --data-urlencode "code_verifier=$(cat code_verifier.txt)"
```

回應裡的 `refresh_token` 存進 `secrets/microsoft_mail_refresh_token.txt`。

## 踩過的坑

- **`http://localhost`（無 port）撞到本機 IIS**：Windows 本機 port 80 常有 IIS 在監聽，OAuth 的長查詢字串會被 IIS 的 request filtering 擋下（`404.15`）。改用 `http://localhost:8400`（或任何沒被佔用的 port）繞開，Redirect URI 要跟 Azure 註冊的完全一致。
- **PKCE code_verifier 手動轉錄一定會出錯**：測試時手動把產生的字串複製貼進對話/指令，連續兩次都在轉錄時漏字（64 碼變 63 碼），導致 `AADSTS501481: Code_Verifier does not match`。**務必寫成檔案、用 `$(cat file)` 讀回**，不要手動打字或口頭轉述。
- **個人帳號必須用 `consumers` 端點，不能用租戶 GUID**：個人 Microsoft 帳號雖然會自動掛一個 Entra ID 租戶（給 App 註冊用），但**實際信箱在 Microsoft 消費者版 Outlook.com 基礎設施**，不在那個自動租戶下。全程用租戶 GUID 會在呼叫 `sendMail` 時得到 `MailboxNotEnabledForRESTAPI`（「信箱未啟用 REST API」）——換成 `consumers` 就正常。
- **改 `signInAudience` 支援個人帳號前要先設 `requestedAccessTokenVersion: 2`**：順序反了會報 `Property api.requestedAccessTokenVersion is invalid`，見上方步驟 2。
- **App 沒開放個人帳號登入時，`consumers` 端點回 `unauthorized_client`**：「單一租戶」的 App 不能用 `consumers`/`common`，要先完成上方步驟 2。
- **手動用 curl 傳中文字串會亂碼**：Windows Bash 環境把多位元組 UTF-8 字元當 shell 參數傳遞時可能被轉碼，導致 Graph 收到的 `subject`/`body` 亂碼。**用檔案（heredoc）存 JSON payload、`curl --data-binary @file` 送**，避開 shell 參數轉碼。這只是手動測試的坑——正式 C# 程式碼用 `JsonContent.Create`（`System.Text.Json`）預設就是正確 UTF-8，不會踩到。
- **第一次收到的信會被 Gmail 當垃圾信**：新寄件人 + 短時間內寄多封內容相近的「測試信」容易被垃圾信過濾器抓。收到後在 Gmail 手動標記「並非垃圾郵件」，之後這個寄件人對這個帳號就會被信任；正式的錯誤通知信內容會依實際錯誤變化、頻率也低（只有真的出錯才寄），不太會重複觸發判定。
- **Refresh token 可能會輪替**：Microsoft 回應可能夾帶新的 `refresh_token`（RFC 6749 建議），目前程式碼**刻意不處理輪替存檔**——這是低風險的已知限制（見 `plans/2026-07-31-error-alerting.md`），真的失效時重跑一次上面「一次性登入」流程換新的即可，不影響主功能。

## 已知限制（刻意不做，YAGNI）

- 不處理 refresh token 輪替後的存檔（見上）。
- 不做 k8s Secret 自動更新機制——refresh token 失效要重跑一次性登入流程、手動更新 secret。
