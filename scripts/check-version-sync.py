#!/usr/bin/env python3
"""版本一致性檢查：同一個版本寫在好幾個檔案時，確認它們都對齊。

設定在 scripts/version-sync.json。用法：
    python3 scripts/check-version-sync.py            # 以本腳本所在的 repo 為根目錄
    python3 scripts/check-version-sync.py --root DIR

只管「該一致的地方有沒有一致」，不管「有沒有漏洞、是不是最新」——那是 dependabot 的工作。
為什麼需要它：
  - dependabot 把 npm 與 Docker 當成不同的生態系，不會一起升級。#204 只升了 npm 的
    @playwright/test、#147 只升了 Dockerfile.e2e 的 image，兩個 PR 單獨合併都會讓 E2E 全倒
    （瀏覽器版本對不上），而錯誤訊息完全看不出原因。
  - 手動升大版本時容易漏改某一處（Node 升 24 時要改 5 個地方；PostgreSQL、Redis 分散在
    compose、k8s 與 Testcontainers fixture）。漏改通常不會出聲：測過的版本和實際執行的不一樣。

規則（壞了必須出聲，不可靜默放行）：
  - path 可用萬用字元；一個檔案都對不到 → 失敗
  - regex：檔案中每一處符合都要一致；一處都對不到 → 失敗（檔案寫法被改掉時）
  - json：依路徑取值；路徑不存在 → 失敗
  - compare：exact 比完整版本；major 只比第一段數字（24.19.2 與 24 一致）
"""
import argparse
import json
import re
import sys
from collections import Counter
from pathlib import Path


def read_text(path: Path) -> str:
    # utf-8-sig：有些檔案帶 BOM（例如 web/Dockerfile），BOM 會干擾行首的 ^ 比對
    return path.read_text(encoding="utf-8-sig")


def extract(root: Path, source: dict):
    """回傳 (found, errors)。found = [(位置, 版本字串)]。"""
    pattern = source["path"]
    files = sorted(p for p in root.glob(pattern) if p.is_file())
    if not files:
        return [], [f"{pattern}：找不到檔案"]

    found, errors = [], []
    for file in files:
        rel = file.relative_to(root).as_posix()
        text = read_text(file)

        if "json" in source:
            json_path = "/".join(source["json"])
            try:
                node = json.loads(text)
                for key in source["json"]:
                    node = node[key]
            except (json.JSONDecodeError, KeyError, TypeError, IndexError):
                errors.append(f"{rel}：抽不到版本（JSON 路徑 {json_path}）")
                continue
            # 同一個 JSON 檔可能有多個來源（如 lockfile 中的幾個套件），位置要帶上路徑才分得出來
            found.append((f"{rel} {json_path}", str(node)))
        else:
            matches = list(re.finditer(source["regex"], text, re.MULTILINE))
            if not matches:
                errors.append(f"{rel}：抽不到版本（正規式 {source['regex']}）")
                continue
            for match in matches:
                line = text.count("\n", 0, match.start()) + 1
                found.append((f"{rel}:{line}", match.group(1)))
    return found, errors


def compare_key(version: str, compare: str) -> str:
    version = version.strip().lstrip("v")
    return version.split(".")[0] if compare == "major" else version


def check_group(root: Path, group: dict) -> bool:
    name, compare = group["name"], group.get("compare", "exact")
    found, errors = [], []
    for source in group["sources"]:
        f, e = extract(root, source)
        found += f
        errors += e

    keys = [compare_key(v, compare) for _, v in found]
    counts = Counter(keys)
    scope = "只比大版本" if compare == "major" else "完整版本"

    if not errors and len(counts) == 1:
        print(f"✓ {name}：{keys[0]}（{len(found)} 處，{scope}）")
        return True

    print(f"✗ {name}：{'版本不一致' if len(counts) > 1 else '無法完成檢查'}（{scope}）")
    for message in errors:
        print(f"    {message}")

    # 多數決標出「不同」的位置；沒有唯一多數時（例如 1 對 1）無法判斷哪邊才對，全部都標
    ranked = counts.most_common()
    majority = ranked[0][0] if ranked and (len(ranked) == 1 or ranked[0][1] > ranked[1][1]) else None
    width = max((len(loc) for loc, _ in found), default=0)
    for (location, version), key in zip(found, keys):
        mark = "  ← 不同" if len(counts) > 1 and key != majority else ""
        print(f"    {location.ljust(width)}  {version}{mark}")
    return False


def main() -> int:
    parser = argparse.ArgumentParser(description="檢查成組的版本是否在各檔案中一致")
    parser.add_argument("--root", help="repo 根目錄（預設：本腳本所在 repo）")
    parser.add_argument("--config", help="設定檔（預設：<root>/scripts/version-sync.json）")
    args = parser.parse_args()

    # 預設以腳本位置定位 repo，而不是目前工作目錄 → 從任何目錄執行都一樣
    root = Path(args.root).resolve() if args.root else Path(__file__).resolve().parent.parent
    config_path = Path(args.config) if args.config else root / "scripts" / "version-sync.json"
    if not config_path.is_file():
        print(f"✗ 找不到設定檔：{config_path}")
        return 1

    groups = json.loads(read_text(config_path))["groups"]
    # 不提早結束：每一組都檢查並列出，一次看到所有問題
    results = [check_group(root, group) for group in groups]
    if all(results):
        return 0

    print(f"\n成組的版本必須一起改。設定與來源位置見 {config_path.relative_to(root).as_posix() if config_path.is_relative_to(root) else config_path}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
