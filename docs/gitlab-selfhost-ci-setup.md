# 自架 GitLab CI/CD 設定筆記（Testcontainers + dind）

自架 GitLab CE + Runner，跑本專案的 CI（build → 單元 → 整合測試打真 Postgres）。
重點記**坑**——照抄指令不難，難在踩到的網路/認證問題。

> **現況**：實際 CI 已改用 **gitlab.com 官方托管**（自架 GitLab CE 閒置吃 ~4GB，本機扛不住）→ 見 `e2e-testing-setup.md` 的「CI on gitlab.com」。本文留作**自架學習 / dind 原理參考**（親手搞懂 runner 註冊、dind、network_mode、覆蓋率合併、閘控的底層）。

## 架構

```
docker-compose（C:\Users\jerem\gitlab-selfhost\）
├─ gitlab      GitLab CE：Git server + Web UI + CI 調度（~4GB RAM）
└─ runner      GitLab Runner：Docker executor + privileged（跑 dind）

pipeline job（runner 生的容器）
└─ services: docker:dind  ← Testcontainers 連這個 daemon 起 postgres
```

**dind 鏈**：Runner 掛 host `docker.sock`（生 job/dind 容器）→ job privileged + `docker:dind` service → Testcontainers 連 dind 起 Postgres。

## 前置

- Docker Desktop 開著（容器全跑在上面，**關 Docker = GitLab 掛**）
- Docker 分到 **≥ 4GB RAM**（Settings → Resources），否則 GitLab 一直 `starting`

---

## Step 1 — 起 GitLab CE + Runner

`C:\Users\jerem\gitlab-selfhost\docker-compose.yml`（放 repo 外，保持 app repo 乾淨）。重點設定：
- `external_url 'http://localhost:8929'`、Web `8929`、Git SSH `2224`（避開 host 22 / app 3000·5432）
- Runner 掛 `/var/run/docker.sock`
- 三個 volume 持久化（`config`/`data`/`logs`）

```bash
cd /c/Users/jerem/gitlab-selfhost && docker compose up -d
```

- 第一次拉映像 ~3GB、開機 3~5 分鐘
- 等 healthy：`docker ps --filter name=gitlab --format "{{.Names}}\t{{.Status}}"` → `(healthy)`

> **坑：healthy 後仍 502** = puma 還在暖機，等 1~2 分鐘。查 `docker exec gitlab gitlab-ctl status`（puma/workhorse/nginx 要 `run:`）。

## Step 2 — 登入

```bash
docker exec gitlab grep Password /etc/gitlab/initial_root_password   # 24h 後清掉
```
`http://localhost:8929` → 帳號 `root` + 該密碼。

> **坑：不要 Register**——用內建 `root` admin，別自建帳號。登入後先改密碼（初始密碼會過期）。

## Step 3 — 建 project + 推 repo

1. UI：**Create blank project**，名稱 `MapleStoryRaidScheduler`，**取消勾 README**（要推現有 repo）。
2. 加 remote（保留 GitHub origin，另加 `gitlab`）：
   ```bash
   git remote add gitlab http://localhost:8929/root/maplestoryraidscheduler.git
   git push gitlab main
   ```

> **坑：push 失敗** `Unencrypted HTTP is not supported` + `/dev/tty: No such device`
> 原因：① Git Credential Manager 拒絕未加密 HTTP ② 非互動 shell 沒 TTY 沒法問帳密。
> **解**：在**真終端機**（PowerShell）跑 push → GCM 彈視窗認證即可；
> 或把 Personal Access Token（scope `write_repository`）塞進 URL：
> `http://root:<TOKEN>@localhost:8929/root/maplestoryraidscheduler.git`（token 會明文存 `.git/config`）。

## Step 4 — 註冊 Runner（GitLab 17+ 新流程）

舊的 registration token 已廢；改成先在 UI 建 runner 拿 **auth token**。

1. UI：project → **Settings → CI/CD → Runners → New project runner** → 勾 **Run untagged jobs** → Create → 複製 `glrt-xxxx`。
2. 註冊（**在真終端機一整行**，別讓它折行）：
   ```bash
   docker exec gitlab-runner gitlab-runner register --non-interactive --url http://gitlab:8929 --token glrt-xxxx --executor docker --docker-image alpine:latest --docker-privileged --docker-volumes /certs/client
   ```
   - `--url http://gitlab:8929`：容器內用**服務名** `gitlab`，不是 localhost
   - `--docker-privileged` + `--docker-volumes /certs/client`：**為 dind 準備**
3. 確認：`docker exec gitlab-runner gitlab-runner list`、UI runner 變綠。

> **坑：長指令被終端機折行** → `--executor` 掉到下一行 → `PANIC: Invalid executor` → 自己 unregister。
> **解**：真終端機貼「一整行」；或直接寫 config.toml `docker cp` 進去。
> 生效 config 在容器內 `/etc/gitlab-runner/config.toml`（Windows Git Bash 讀路徑要 `MSYS_NO_PATHCONV=1`）。
> 手動在 host 寫的 config.toml **記得刪**（含明文 token）。

## Step 5 — `.gitlab-ci.yml`（repo 根）

3 個 job：`build`（整個 solution）、`unit-test`（純邏輯）、`integration-test`（dind + Testcontainers）。整合 job 關鍵 env：

```yaml
services: [{ name: docker:27-dind, alias: docker }]
variables:
  DOCKER_HOST: "tcp://docker:2376"
  DOCKER_TLS_CERTDIR: "/certs"
  DOCKER_CERT_PATH: "/certs/client"
  DOCKER_TLS_VERIFY: "1"
  TESTCONTAINERS_HOST_OVERRIDE: "docker"   # 透過 dind host 連容器 port，不是 localhost
  TESTCONTAINERS_RYUK_DISABLED: "true"     # dind 裡 Ryuk 常起不來，關掉
```

commit + push（真終端機）觸發 pipeline：project → **Build → Pipelines**。

> **坑（實際，兩段——job 端連不到 GitLab 的兩條不同 URL）**
> ① **git clone 報 `Failed to connect to localhost:8929`**：clone URL 來自 GitLab `external_url`（localhost）；job 容器裡 localhost=它自己。
> ② **artifact 上傳報 `lookup gitlab: no such host`**：artifact/API 用 runner 的 `url`（原 `gitlab:8929`），job 容器不在 compose 網路 → 解析不到 `gitlab`。
> **解（已套用 → green）**：config.toml `[[runners]]` 兩個 URL **都**指 `host.docker.internal`（Docker Desktop 下 runner 與 job 容器都連得到 host 發布的 8929）：
> ```toml
> url = "http://host.docker.internal:8929"        # runner 輪詢 + job 端 API（artifact）
> clone_url = "http://host.docker.internal:8929"  # job 端 git clone（蓋掉 external_url 的 localhost）
> ```
> 再 `docker restart gitlab-runner`。
> **教訓**：clone 走 `external_url`、artifact/API 走 runner `url`——**兩條不同的 URL，都要 job 端連得到**。只修一條會在另一條再爆一次。

---

## Step 6 — 把 pipeline 變成閘（CI 閘控）

1. `main` 預設已是 protected（GitLab 自動保護 default branch）。
2. **Settings → Merge requests → Merge checks** → 勾 **Pipelines must succeed**。
   - **Skipped pipelines are considered successful：不要勾**——勾了的話 commit 帶 `[skip ci]` 就能繞過閘（測試沒跑也能 merge）。
3. 效果：開 MR → pipeline 紅 → Merge 鈕鎖住、顯示「Pipeline must succeed」。

> **邊界：這閘只管 Merge Request**。直接 `git push gitlab main` 不會被擋（pipeline 會跑但不 gate）→ 要走 branch + MR 才體驗得到閘。

**驗證（Break & Fix，已實測）**：開 branch 加一個故意失敗的測試（`Assert.Equal(1,2)`）→ push → 開 MR → `unit-test` job 紅 → 按鈕變「Merge when all merge checks pass」（延後合併，pipeline 永遠不綠 → 永遠不合）→ 確認 merge 被擋 → 關 MR、刪 branch。

## Step 7 — 合併覆蓋率（unit + integration）

兩個測試專案覆蓋不同層（unit=邏輯、integration=持久層），各產一份 cobertura，合併取**聯集**才是真實總覆蓋。

- 兩個 test job：`dotnet test ... --collect:"XPlat Code Coverage" --results-directory ./coverage-xxx` → cobertura 存成 artifact。
- `coverage` job：`needs` 抓兩份 artifact → `reportgenerator -reports:"...;..." -reporttypes:"TextSummary;Cobertura"` 合併 → `cat Summary.txt`。
- `coverage: '/Line coverage: \d+(?:\.\d+)?%/'`：從 log 抓總覆蓋率顯示在 pipeline/MR。
- `artifacts:reports:coverage_report`（cobertura）：MR diff 標行覆蓋。

實測：合併後 **Line 53.1% / Branch 70.3%**（單元單獨看會低估，因為持久層要靠整合測試點亮）。

### 看覆蓋率的四個地方

| 地方 | 看什麼 | 怎麼到 |
|---|---|---|
| **Pipeline / Job 徽章** | 一個 `%`（最快，免進 log） | Pipeline 頁 `coverage` job 旁的數字 |
| **coverage job 的 log** | Line + Branch + 每個 class 分項（找哪裡低） | 點 `coverage` job → log 尾巴 `Summary.txt` |
| **Artifact** | 完整檔案 `Summary.txt` + `Cobertura.xml` | `coverage` job → Download artifacts |
| **Merge Request** | 覆蓋率 % + 跟 target 的增減、diff 上標行覆蓋 | 開 MR 就看得到（來自 `coverage_report` cobertura） |

- **徽章一個 job 只能顯示一個數字**（line 或 branch，二選一）。目前 `coverage: '/Line coverage.../'` → 顯示 **line 53.1%**；要改 branch 就把正則換成 `/Branch coverage: \d+(?:\.\d+)?%/`（→ 70.3%）。慣例是 line，branch 一直在 log 裡看得到。
- 想要**可點的 HTML 報告**（一路點進每個檔看哪行紅/綠）：`-reporttypes` 加 `Html`，下載 artifact 開 `index.html`。

## 日常開關

```bash
docker compose -f /c/Users/jerem/gitlab-selfhost/docker-compose.yml stop    # 保留資料、釋放 RAM
docker compose -f /c/Users/jerem/gitlab-selfhost/docker-compose.yml start
```

⚠️ **別用 `down -v`**——會連 volume 一起刪，GitLab 資料全沒。

## 進度

- ✅ Step 1–4：GitLab + Runner 起好、repo 推上、runner 註冊（Docker executor + privileged + dind 就緒）
- ✅ Step 5：`.gitlab-ci.yml` push 觸發，套 `clone_url` 後 pipeline **全綠（~2m23s）**——build + unit-test + integration-test（dind + Testcontainers 打真 Postgres）都過。
- ✅ Step 6：`main` protected + **Pipelines must succeed**；用故意失敗的紅 MR **實測 merge 被擋**。
- ✅ Step 7：`coverage` job（ReportGenerator 合併 unit+integration cobertura）**綠**——合併後 Line 53.1% / Branch 70.3%，pipeline/MR 顯示覆蓋率。

## 未決

- `url` + `clone_url` 綁 `host.docker.internal`（Docker Desktop 專屬）；搬到 Linux CI runner 需改回 `gitlab:8929` + 讓 job 容器上 compose 網路（`network_mode`）。
- 覆蓋率門檻閘（低於 X% 擋 merge）尚未設，目前只顯示不強制。
