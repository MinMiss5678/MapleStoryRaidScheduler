---
description: commit 目前變更 → push → 開/更新 PR → 等 CI 綠 → rebase-merge 到 main
argument-hint: [commit 訊息]
allowed-tools: Bash(git*), Bash(gh*)
---

把目前分支的工作送上去並合併到 main。**照以下步驟；CI 沒綠、或 run 根本沒觸發，就絕不 merge。**

commit 訊息：$ARGUMENTS（若空，依變更內容擬一個繁體中文 conventional commit 訊息）

1. **前置**：`git rev-parse --abbrev-ref HEAD` 取分支；若在 `main` 就停下、請先開分支。`git status --short` 看變更——**忽略 session 前既有的 `.claude/skills/chrome-devtools/scripts/*` 未追蹤檔，別 commit 它們**。
2. **commit**：只 stage 本次相關檔（**別 `git add -A`** 掃進無關未追蹤檔）→ `git commit`（繁中訊息；多功能可依功能分多個 commit）。
3. **push**：`git push -u origin <branch>`。
4. **PR**：`gh pr list --head <branch>` 看有無既有 PR；沒有就 `gh pr create --base main --head <branch>`（標題/內文用 commit 訊息、繁中）。
5. **等 CI**：CI 只在 PR/push-main 觸發。抓 head sha 的 run：
   `gh api repos/{owner}/{repo}/actions/runs?head_sha=$(git rev-parse HEAD) --jq '.workflow_runs[0].id // empty'`
   - 若 ~30s 內沒 run 出現 → 查 https://www.githubstatus.com/（GitHub Actions 可能節流/當機）；**沒 run 就別 merge**，回報使用者。必要時 `gh pr close/reopen` 或空 commit nudge 觸發。
   - 有 run → `gh run watch <id> --exit-status`（背景阻塞等待）。紅了 `gh run view <id> --log-failed` 回報、**停在這不 merge**。
6. **merge**：CI 綠（exit 0）才 `gh pr merge <PR> --rebase`（**--rebase，不要 --squash**）。回報結果。

**鐵則**：CI 沒過、或 GitHub 沒觸發 run → **絕不 merge**，回報現況讓使用者決定。
