# 多 node k3s HA demo：outbox 多 pod 派發

> 執行手冊 + 實測記錄。搭配 `k8s/ha-demo/` 的 manifests 與 `plans/2026-09-04-multi-pod-outbox-dispatch.md`。
> **已於 2026-09-05 在 3×EC2 t3.small（ap-northeast-1）真跑過**，結果見文末「實測結果」。

## 這個 demo 證什麼（誠實定位）

- **證**：`FOR UPDATE SKIP LOCKED` 讓 N 個 outbox 列被 M 個 dispatcher pod（**跨 ≥2 台真機器**）**恰處理一次、無選主**；硬殺整台 node 中途也**零遺漏**（容錯）。
- **不證**：吞吐更快——Discord DM 是 per-token rate limit 天花板，多 pod 是**正確性 + HA**，不是更快。
- **與正確性的關係**：exactly-once 是 **DB 層性質、與機器數無關**，本機整合測（`OutboxConcurrencyIntegrationTests`）已證。本 demo 只**加**「真跨機器 + node 故障」的敘事。
- **隔離**：獨立 `ha-demo` namespace + 自帶拋棄式 Postgres + **dry-run（不真送 DM、給 dummy token）** → 不碰 prod、不洗使用者、不吃 rate limit。

## 前置

- **2–3 台 EC2 t3.small**（≥2GB；**別用 prod 那台**）。1 server + 1~2 agent。
- 節點同 VPC，安全群組互通：`6443/TCP`（API）、`8472/UDP`（flannel VXLAN）、`10250/TCP`（kubelet）。
- Docker Hub 推送權限（image `minqq/presentation`）。
- kubectl：直接在 server 上用 `sudo k3s kubectl`（本手冊皆如此）；或把 `/etc/rancher/k3s/k3s.yaml` 複製到本機、server 改公網 IP（需對你的 IP 開 6443）。

## 步驟 0：重建並推送含 dry-run 的 image（**必做**）

`DryRunOutboxHandler` 是這輪新增的——prod image（`minqq/presentation:latest`）沒有它。用**獨立 `:hademo` tag**（不覆蓋 prod `:latest`）重建+推。

```bash
docker build -f Presentation/Dockerfile -t minqq/presentation:hademo .   # 專案根
docker push minqq/presentation:hademo
```

## 步驟 1：組 k3s 叢集（server + agent，用私有 IP）

```bash
# === server（機器 A，私有 IP 例 172.26.3.167）===
curl -sfL https://get.k3s.io | sudo sh -s - --write-kubeconfig-mode 644 --node-ip <A-private-ip>
sudo cat /var/lib/rancher/k3s/server/node-token          # 取 join token

# === 每台 agent（機器 B/C）===
curl -sfL https://get.k3s.io -o /tmp/k3s.sh
sudo env K3S_URL=https://<A-private-ip>:6443 K3S_TOKEN=<token> sh /tmp/k3s.sh --node-ip <本機-private-ip>
```

驗（server 上）：`sudo k3s kubectl get nodes -o wide` → 全部 **Ready**（能 join 就代表 SG 6443/8472 通）。

## 步驟 2：部署 demo

manifests 先 `scp` 到 server（或 git clone）。以下在 server 上：

```bash
K="sudo k3s kubectl"
$K apply -f ha-demo/00-namespace.yaml
$K apply -f ha-demo/10-postgres.yaml
$K -n ha-demo rollout status deploy/ha-demo-db
$K apply -f ha-demo/20-seed-job.yaml     # 種 1000 筆 HaDemo（改 N：改此檔 generate_series 上界）
$K -n ha-demo wait --for=condition=complete job/ha-demo-seed --timeout=120s
$K apply -f ha-demo/30-dispatcher.yaml   # 4 個 dispatcher，topologySpread 散到不同 node
$K -n ha-demo get pods -o wide           # 確認 dispatcher 跨 ≥2 node、DB 在哪台
```

## 步驟 3：驗證（恰一次 + 跨 node）

dispatcher 每 5s 撈一批（batch=20）。等清完再跑（k3s 上帶 `KUBECTL`）：

```bash
KUBECTL="sudo k3s kubectl" bash ha-demo/verify.sh 1000
```

期望：`distinct(n)=1000`（無遺漏）、`duplicate n=0`（恰一次）、node 分布多台皆有份。

## 步驟 4：Chaos（HA 重頭戲）

> **殺 node 前務必先看 placement**（無 PVC 的 demo DB 會隨 pod 重排漂移；殺到 DB 或 control-plane = 殺掉整場）：
> ```bash
> K="sudo k3s kubectl"
> $K -n ha-demo get pod -l app=ha-demo-db -o wide          # DB 在哪台 → 別殺它
> $K -n ha-demo get pod -l app=outbox-dispatcher -o wide   # 挑「有 dispatcher、非 DB、非 server」那台殺
> ```
> 想省事可把 DB 用 `nodeName`/`nodeSelector` 釘死在 server node，之後任一 agent 都能安全殺。

**A. graceful 殺 pod**（`kubectl delete pod`）：SIGTERM → dispatcher 的 `await using tx` 正常 dispose → **ROLLBACK 立即釋鎖** → 存活 pod 秒接手。

**B. 硬殺整台 node**（在該 agent 機器上 `sudo systemctl stop k3s-agent`）：見下「學到的事」——會留孤兒鎖、靠 timeout 自我修復。
- **想看到「卡住→自動歸零」的戲劇效果 → 晚點殺**（先 `verify` 到 rem 小、drain 快結束時才殺），孤兒鎖那批才會變成孤零零的尾巴、明顯卡住到 30s timeout；太早殺會被大量在途列淹沒、看不出停頓。
- 復原：`sudo systemctl start k3s-agent`。

驗（DB 是權威真相，勝過 log）：

```bash
sudo k3s kubectl -n ha-demo exec deploy/ha-demo-db -- \
  psql -U postgres -d hademo -tAc \
  'SELECT count(*) FILTER (WHERE "ProcessedAt" IS NULL) AS unprocessed FROM "OutboxMessage";'
```
期望 `unprocessed=0`＝**零遺漏**。

## 學到的事：硬殺 node ≠ graceful 刪 pod（實測挖到）

SKIP LOCKED 的「rollback 釋鎖被接手」**只對 graceful pod 刪除成立**。硬殺整台 node（斷電 / `stop k3s-agent`）不一樣：

- 被殺 pod 的 DB 連線**半開**（來不及送 FIN/RST）→ Postgres 以為連線還在、那個 backend 卡在 `idle in transaction`、**握著 `FOR UPDATE` 列鎖不放**。
- Postgres 靠 **TCP keepalive** 才會偵測死連線，但**預設 `tcp_keepalive_time=7200s`（2 小時）**→ 那批列被 SKIP LOCKED 跳過、**卡住約兩小時**（資料沒丟，是 pending 不是 lost）。
- 診斷：`pg_stat_activity` 出現 `idle in transaction`，`client_addr`＝死掉 pod 的 flannel IP。

**修復**：讓 DB 主動中止孤兒交易。本 demo 的 `10-postgres.yaml` 設 `idle_in_transaction_session_timeout=30000`（30s 自動中止 → 釋鎖 → 存活 pod 接手）→ **硬殺 node 也自我修復**。
手動等價：`SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE state='idle in transaction';`。

## 步驟 5：拆除（省成本）

```bash
sudo k3s kubectl delete ns ha-demo                 # 一鍵清 demo
# 各機器解除安裝：server → /usr/local/bin/k3s-uninstall.sh；agent → /usr/local/bin/k3s-agent-uninstall.sh
# 去 AWS console 終止 EC2（按小時計費，time-box 跑完就關）
```

## 實測結果（2026-09-05，3×EC2 t3.small，1 server + 2 agent，跨 3 node）

| 場景 | 設定 | 結果 |
|---|---|---|
| **Happy path** | N=1000、4 dispatcher 跨 3 node | `total=1000 / distinct=1000 / dup=0`＝**恰一次、無漏、無選主** |
| **硬殺 node（手動修）** | N=200000，殺 node mid-drain | 存活 pod 收到剩 **40 卡住**（2 孤兒 tx × batch20）→ 診斷 `idle in transaction` → 手動 `pg_terminate_backend` → 0；**200000/200000** |
| **硬殺 node（自我修復）** | N=200000，晚殺 agent2（3 pod），`timeout=30s` | 存活 pod 收到剩 **60 卡住 ~23s** → 孤兒 tx idle 滿 30s 被自動中止、釋鎖 → 跳 0；**零手動、200000/200000** |

一句話佐證：**多 pod × 跨 3 node × SKIP LOCKED = 恰一次、免選主；硬殺 node 也零遺漏（靠 `idle_in_transaction_session_timeout` 自我修復）。**

## 已知取捨

- **多 node 對正確性非必要**（DB 層性質）；純驗正確性用本機整合測或 `kubectl scale` 單機多 pod 即可。多 node 加的是「真分散式 + node 故障」敘事。
- demo DB 無 PVC、密碼明文 → **僅限拋棄式 demo 叢集**，勿套 prod。
- dry-run 只記 log 不真送 → 驗的是**搶列/派發協調**（SKIP LOCKED），非 Discord 送達；送達已由「dispatcher 完整跑」單筆真送驗過。
