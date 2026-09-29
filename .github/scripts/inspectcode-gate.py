#!/usr/bin/env python3
"""ReSharper InspectCode 品質門檻 + PR 品質摘要。

jb inspectcode 不論有沒有問題都 exit 0，所以由這支腳本解析 SARIF 決定成敗：
- 門檻：只擋 GATED_RULES 清單內的規則（明確列舉，不看 severity）。
  ReSharper 自己的解析誤報（如 .CSharpErrors）或新版多出的規則不會讓 CI 突然變紅；
  真正的編譯錯誤由 build job 負責。
- 摘要：落在本次 PR「新增行」上的所有問題（含不擋的風格/建議），依規則分組，
  連同改動行數寫進 GitHub job summary，用來觀察每個 PR（多為 AI 產出）的品質。

用法：inspectcode-gate.py <sarif> [base-ref]
"""
import json
import os
import re
import subprocess
import sys
from collections import Counter

# 要擋的規則：死碼、易出 bug 的寫法、結構問題（風格類不擋）。
# 誤判請在程式碼用 `// ReSharper disable once <規則>` 並寫明理由。
GATED_RULES = {
    "UnusedType.Global",            # 沒人用的型別（全方案分析）
    "UnusedMember.Local",           # 沒用到的 private 成員
    "UnusedParameter.Local",        # 沒用到的參數
    "UnusedVariable",               # 沒用到的區域變數
    "RedundantAssignment",          # 指派後沒被讀取
    "VariableHidesOuterVariable",   # 遮蔽外層變數
    "PossibleMultipleEnumeration",  # IEnumerable 重複列舉
    "CheckNamespace",               # namespace 與資料夾不符
    "UseAwaitUsing",                # IAsyncDisposable 應 await using
}


def load_results(sarif_path):
    with open(sarif_path, encoding="utf-8-sig") as f:
        run = json.load(f)["runs"][0]
    results = []
    for r in run.get("results", []):
        if r.get("suppressions"):
            continue
        locs = r.get("locations") or []
        if not locs:
            continue
        phys = locs[0]["physicalLocation"]
        results.append({
            "rule": r["ruleId"],
            "file": phys["artifactLocation"]["uri"],
            "line": phys.get("region", {}).get("startLine", 1),
            "message": r["message"]["text"],
        })
    return results


def added_lines(base_ref):
    """回傳 ({檔案: 新增行號集合}, 新增行數, 刪除行數)。"""
    diff = subprocess.run(
        ["git", "diff", "-U0", f"{base_ref}...HEAD"],
        capture_output=True, text=True, encoding="utf-8", check=True,
    ).stdout
    added, current, plus, minus = {}, None, 0, 0
    for line in diff.splitlines():
        if line.startswith("+++ "):
            current = line[6:] if line.startswith("+++ b/") else None
        elif line.startswith("@@") and current:
            m = re.search(r"\+(\d+)(?:,(\d+))?", line)
            start, count = int(m.group(1)), int(m.group(2) or 1)
            added.setdefault(current, set()).update(range(start, start + count))
        elif line.startswith("+") and not line.startswith("+++"):
            plus += 1
        elif line.startswith("-") and not line.startswith("---"):
            minus += 1
    return added, plus, minus


def write_summary(results, gated, base_ref):
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    lines = ["## ReSharper InspectCode", ""]
    lines.append(f"門檻規則命中：**{len(gated)}**" + (" ❌" if gated else " ✅"))
    if base_ref:
        added, plus, minus = added_lines(base_ref)
        on_new = [r for r in results if r["line"] in added.get(r["file"], ())]
        lines += ["", f"本次改動：+{plus} / -{minus} 行；新增行上的問題：**{len(on_new)}**", ""]
        if on_new:
            lines += ["| 規則 | 筆數 | 擋？ |", "|---|---|---|"]
            for rule, n in Counter(r["rule"] for r in on_new).most_common():
                lines.append(f"| `{rule}` | {n} | {'✅' if rule in GATED_RULES else ''} |")
    text = "\n".join(lines) + "\n"
    if summary_path:
        with open(summary_path, "a", encoding="utf-8") as f:
            f.write(text)
    else:
        print(text)


def main():
    sys.stdout.reconfigure(encoding="utf-8")  # 本機 Windows 主控台預設 cp950，印不出 ✅/❌
    sarif_path = sys.argv[1]
    base_ref = sys.argv[2] if len(sys.argv) > 2 and sys.argv[2] else None
    results = load_results(sarif_path)
    gated = [r for r in results if r["rule"] in GATED_RULES]
    for r in gated:
        # GitHub annotation：直接標在 PR 的檔案行上
        print(f"::error file={r['file']},line={r['line']},title={r['rule']}::{r['message']}")
    write_summary(results, gated, base_ref)
    sys.exit(1 if gated else 0)


if __name__ == "__main__":
    main()
