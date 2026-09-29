#!/usr/bin/env python3
"""NuGet 已知漏洞門檻（含間接相依）。

補 Dependabot 的盲點：專案沒有 packages.lock.json 時，GitHub 相依圖只看得到直接參照，
Testcontainers 間接帶進來的 SSH.NET 漏洞它就看不到。`dotnet list package --vulnerable
--include-transitive` 會把整棵相依樹還原後比對 NuGet 漏洞資料庫，但它有沒有漏洞都 exit 0，
所以由這支腳本解析 JSON 輸出決定成敗。

用法：dotnet list <sln> package --vulnerable --include-transitive --format json | nuget-vulnerable-gate.py
"""
import json
import re
import sys


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    data = json.load(sys.stdin)
    hits = []
    for project in data.get("projects", []):
        name = re.split(r"[\\/]", project["path"])[-1]
        for framework in project.get("frameworks", []):
            for kind, label in (("topLevelPackages", "直接"), ("transitivePackages", "間接")):
                for pkg in framework.get(kind, []):
                    for vuln in pkg.get("vulnerabilities", []):
                        hits.append((name, label, pkg["id"], pkg["resolvedVersion"],
                                     vuln["severity"], vuln["advisoryurl"]))

    if not hits:
        print("✅ 沒有已知漏洞的 NuGet 套件（含間接相依）")
        return 0

    print(f"❌ 發現 {len(hits)} 筆已知漏洞：")
    for name, label, pkg, ver, sev, url in sorted(set(hits)):
        print(f"::error title=Vulnerable NuGet package::{name}：{label}相依 {pkg} {ver}（{sev}）{url}")
    print("修法：升級直接參照的套件，讓它帶進已修補的版本；必要時在 csproj 直接參照修補版覆蓋間接相依。")
    return 1


if __name__ == "__main__":
    sys.exit(main())
