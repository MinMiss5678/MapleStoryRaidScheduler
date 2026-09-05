#!/usr/bin/env bash
# 彙總所有 dispatcher pod 的 HADEMO_CLAIM log（跨 node）→ 斷言 SKIP LOCKED 正確性：
#   distinct(n) == N（無遺漏，硬性）；duplicate n == 0（恰一次；chaos 殺 pod 後可能 >0＝at-least-once，屬預期 → WARN 不 FAIL）。
# 另印 node 分布，佐證「真跨機器」。用法：./verify.sh [N]（預設 1000）
set -euo pipefail
NS=ha-demo
N="${1:-1000}"
# k3s 上沒有獨立 kubectl → 用 KUBECTL="sudo k3s kubectl" 覆寫。
KUBECTL="${KUBECTL:-kubectl}"

logs="$($KUBECTL -n "$NS" logs -l app=outbox-dispatcher --tail=-1 2>/dev/null | grep 'HADEMO_CLAIM' || true)"
if [ -z "$logs" ]; then echo "沒有 HADEMO_CLAIM log（dispatcher 還沒跑完？稍等再試）"; exit 1; fi

total="$(printf '%s\n' "$logs" | grep -c 'HADEMO_CLAIM' || true)"
distinct="$(printf '%s\n' "$logs" | grep -oE 'n=[0-9]+' | sort -u | wc -l | tr -d ' ')"
dups="$(printf '%s\n' "$logs" | grep -oE 'n=[0-9]+' | sort | uniq -d | wc -l | tr -d ' ')"

echo "seeded N=$N | processed(total)=$total | distinct(n)=$distinct | duplicate n=$dups"
echo "--- node 分布（真跨機器佐證）---"
printf '%s\n' "$logs" | grep -oE 'node=[^ ]+' | sort | uniq -c
echo "--- pod 分布 ---"
printf '%s\n' "$logs" | grep -oE 'pod=[^ ]+' | sort | uniq -c

fail=0
if [ "$distinct" -eq "$N" ]; then echo "PASS: 無遺漏（distinct == N）"; else echo "FAIL: 有遺漏（distinct=$distinct < N=$N）"; fail=1; fi
if [ "$dups" -eq 0 ]; then echo "PASS: 無重複（恰一次）"; else echo "WARN: 有重複 n=$dups（chaos 殺 pod 後的 at-least-once 重送，屬預期；未殺 pod 卻出現才是問題）"; fi
exit $fail
