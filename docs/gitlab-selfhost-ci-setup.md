# 自架 GitLab CI/CD 設定筆記（Testcontainers + dind）

自架 GitLab CE + Runner，跑本專案的 CI（build → 單元 → 整合測試打真 Postgres）。
重點記**坑**——照抄指令不難，難在踩到的網路/認證問題。

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

> **坑（實際）：job git clone 報 `Failed to connect to localhost:8929`**
> 原因：GitLab 給 CI 的 clone URL = `external_url`（`http://localhost:8929`）；job 容器裡 `localhost` = 它自己，連不到 GitLab。（不是「resolve gitlab」——URL 本身就是 localhost。）
> **解（已套用 → green）**：config.toml `[[runners]]` 加
> `clone_url = "http://host.docker.internal:8929"`（Docker Desktop 容器可經 `host.docker.internal` 連到 host 發布的 8929），再 `docker restart gitlab-runner`。
> `url`（runner↔GitLab API）維持 `gitlab:8929`；`clone_url` 只改 job 端 clone 網址。

---

## Step 6 — 把 pipeline 變成閘（CI 閘控）

1. `main` 預設已是 protected（GitLab 自動保護 default branch）。
2. **Settings → Merge requests → Merge checks** → 勾 **Pipelines must succeed**。
   - **Skipped pipelines are considered successful：不要勾**——勾了的話 commit 帶 `[skip ci]` 就能繞過閘（測試沒跑也能 merge）。
3. 效果：開 MR → pipeline 紅 → Merge 鈕鎖住、顯示「Pipeline must succeed」。

> **邊界：這閘只管 Merge Request**。直接 `git push gitlab main` 不會被擋（pipeline 會跑但不 gate）→ 要走 branch + MR 才體驗得到閘。

**驗證（Break & Fix，已實測）**：開 branch 加一個故意失敗的測試（`Assert.Equal(1,2)`）→ push → 開 MR → `unit-test` job 紅 → 按鈕變「Merge when all merge checks pass」（延後合併，pipeline 永遠不綠 → 永遠不合）→ 確認 merge 被擋 → 關 MR、刪 branch。

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

## 未決

- 覆蓋率合併（unit + integration 兩份 cobertura）尚未接進 pipeline。
- `clone_url` 用 `host.docker.internal`（綁 Docker Desktop）；若搬到 Linux CI runner 需改回 `network_mode` + `gitlab:8929`。
