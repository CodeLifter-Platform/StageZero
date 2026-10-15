#!/usr/bin/env python3
"""Merge coverlet cobertura files into one markdown summary for the CI job summary.

    dotnet test tests/X -c Release --collect:"XPlat Code Coverage" --results-directory coverage/X
    python3 .github/scripts/coverage-summary.py coverage | tee -a "$GITHUB_STEP_SUMMARY"

The same file is counted once even when several test projects exercise it (max hits per
line). Migrations and generated code are excluded. The number is a gap finder, not a
target: see Platform-Standards/process/testing.md.
"""
import collections
import glob
import sys
import xml.etree.ElementTree as ET

root = sys.argv[1] if len(sys.argv) > 1 else "coverage"
EXCLUDE = ("/Migrations/", ".g.cs", ".Designer.cs", "/obj/")

files = glob.glob(f"{root}/**/coverage.cobertura.xml", recursive=True)
if not files:
    print(f"no coverage.cobertura.xml under {root}")
    sys.exit(0)

hits = collections.defaultdict(dict)  # (assembly, file) -> {line: hits}
for path in files:
    for package in ET.parse(path).getroot().iter("package"):
        for cls in package.iter("class"):
            filename = cls.get("filename", "")
            if any(marker in filename for marker in EXCLUDE):
                continue
            lines = hits[(package.get("name"), filename)]
            for line in cls.iter("line"):
                number = int(line.get("number"))
                lines[number] = max(lines.get(number, 0), int(line.get("hits")))

per_assembly = collections.defaultdict(lambda: [0, 0])
per_file = []
for (assembly, filename), lines in hits.items():
    total = len(lines)
    covered = sum(1 for h in lines.values() if h > 0)
    per_assembly[assembly][0] += covered
    per_assembly[assembly][1] += total
    per_file.append((total - covered, covered, total, filename))

all_covered = sum(c for c, _ in per_assembly.values())
all_total = sum(t for _, t in per_assembly.values())
pct = lambda c, t: f"{100 * c / t:.1f}%" if t else "n/a"

print(f"## Line coverage: {pct(all_covered, all_total)} ({all_covered}/{all_total})\n")
print("| Assembly | Lines | Covered |")
print("|---|---|---|")
for assembly, (covered, total) in sorted(per_assembly.items()):
    print(f"| `{assembly}` | {covered}/{total} | {pct(covered, total)} |")

print("\n<details><summary>Least-covered files</summary>\n")
print("| Uncovered | File | Covered |")
print("|---|---|---|")
for missing, covered, total, filename in sorted(per_file, reverse=True)[:10]:
    if missing == 0:
        break
    short = filename.split("/src/", 1)[-1]
    print(f"| {missing} | `{short}` | {pct(covered, total)} |")
print("\n</details>")
