#!/usr/bin/env python3
"""T-1036: check recipes by default; --execute requires a controller-granted heavy lease.

Replays historical surviving mutants one at a time, with each named behavioral test.
A nonzero dotnet exit is evidence only when its TRX names a failing target test.
This is supplemental negative-control evidence, never a replacement for Stryker.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def offset(source, position):
    lines = source.splitlines(keepends=True)
    return sum(len(line) for line in lines[:position["line"] - 1]) + position["column"] - 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute", action="store_true", help="run only after the controller grants the heavy validation lease")
    parser.add_argument("--ids", help="optional comma-separated historical IDs; default is all 34")
    parser.add_argument("--output", type=Path, help="required fresh receipt directory for --execute")
    args = parser.parse_args()
    tests = Path(__file__).resolve().parent
    root = tests.parents[3]
    source_directory = tests.parent / "hlp.foundation.notification-delivery"
    plan = json.loads((tests / "evidence/t1036-negative-control-plan.json").read_text())
    raw_report = (tests / "evidence/t1036-historical-before.json").read_bytes()
    if sha256(raw_report) != plan["reportSha256"]:
        raise ValueError("Historical report hash differs from the audited artifact")
    report = json.loads(raw_report)
    sources = {}
    mutants = {}
    for name, file in report["files"].items():
        basename = name.replace("\\", "/").rsplit("/", 1)[-1]
        if basename not in {"DeliveryChannels.cs", "InboxChannel.cs"}:
            raise ValueError("Report contains an out-of-scope source")
        target = source_directory / basename
        original = target.read_bytes()
        source = original.decode().replace("\r\n", "\n")
        if source != file["source"].replace("\r\n", "\n"):
            raise ValueError(f"{target}: source changed; refresh recipes instead of guessing offsets")
        sources[basename] = (target, original, source)
        for mutant in file["mutants"]:
            if mutant["status"] == "Survived":
                mutants[int(mutant["id"])] = (basename, mutant)
    controls = plan["controls"]
    if {row["historicalId"] for row in controls} != set(mutants):
        raise ValueError("Plan does not map every observed survivor exactly")
    if len(controls) != len(mutants):
        raise ValueError("Plan repeats a survivor")
    if args.ids:
        selected = {int(value) for value in args.ids.split(",")}
        if not selected or not selected.issubset(mutants):
            raise ValueError("Unknown historical ID selection")
        controls = [row for row in controls if row["historicalId"] in selected]
    recipes = []
    test_sources = "\n".join(path.read_text() for path in tests.glob("*.cs"))
    for row in controls:
        basename, mutant = mutants[row["historicalId"]]
        if row["file"] != basename or row["line"] != mutant["location"]["start"]["line"] or row["mutator"] != mutant["mutatorName"]:
            raise ValueError("Plan location or mutator differs from the report")
        source = sources[basename][2]
        start, end = offset(source, mutant["location"]["start"]), offset(source, mutant["location"]["end"])
        if not source[start:end] or row["test"].rsplit(".", 1)[-1] + "(" not in test_sources:
            raise ValueError("Empty mutation span or missing named test")
        mutated = source[:start] + mutant["replacement"] + source[end:]
        if mutated == source:
            raise ValueError("Mutation recipe leaves source unchanged")
        recipes.append((row, sources[basename][0], mutated))
    print(f"Checked {len(recipes)} real survivor recipes and named tests; no .NET execution yet.")
    if not args.execute:
        return 0
    if not args.output:
        parser.error("--execute requires --output with a fresh receipt directory")
    owned = [str(tests.relative_to(root)), str(source_directory.relative_to(root))]
    untracked = subprocess.check_output(["git", "ls-files", "--others", "--exclude-standard", "--", *owned], cwd=root, text=True).strip()
    if untracked:
        raise ValueError("Stage or commit every owned candidate file before execution: " + untracked)
    if subprocess.run(["git", "diff", "--quiet"], cwd=root).returncode != 0:
        raise ValueError("Stage or commit all working-tree changes before execution; receipts must bind the exact candidate")
    candidate_tree = subprocess.check_output(["git", "write-tree"], cwd=root, text=True).strip()
    tracked = subprocess.check_output(["git", "ls-files", "--", *owned], cwd=root, text=True).splitlines()
    manifest = {name: sha256((root / name).read_bytes()) for name in tracked}
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    project = tests / "Harborline.Foundation.NotificationDelivery.Tests.csproj"
    receipt = {"platformHead": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip(),
               "candidateIndexTree": candidate_tree, "ownedFileSha256": manifest,
               "candidateDiffSha256": sha256(subprocess.check_output(["git", "diff", "HEAD", "--", str(tests)], cwd=root)),
               "historicalReportSha256": plan["reportSha256"], "controls": [], "status": "running"}

    def save():
        (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")

    def run(label, test_filter):
        directory = output / label
        directory.mkdir()
        command = ["dotnet", "test", str(project), "-c", "Debug", "--nologo", "--filter", test_filter,
                   "--logger", "trx;LogFileName=tests.trx", "--results-directory", str(directory)]
        with (directory / "output.log").open("w") as log:
            result = subprocess.run(command, cwd=root, stdout=log, stderr=subprocess.STDOUT, timeout=600)
        trx = directory / "tests.trx"
        outcomes = [] if not trx.exists() else [element.attrib for element in ET.parse(trx).getroot().iter()
                                                if element.tag.rsplit("}", 1)[-1] == "UnitTestResult"]
        return {"command": command, "exitCode": result.returncode, "results": outcomes}

    try:
        receipt["before"] = run("before", "FullyQualifiedName~Harborline.Foundation.NotificationDelivery.Tests")
        save()
        if receipt["before"]["exitCode"] != 0 or not receipt["before"]["results"] or any(
                result.get("outcome") != "Passed" for result in receipt["before"]["results"]):
            raise RuntimeError("Clean candidate is not green; negative controls were not started")
        for row, target, mutated in recipes:
            print(f"Running historical mutant {row['historicalId']} against {row['test']}", flush=True)
            try:
                target.write_text(mutated)
                result = run(f"mutant-{row['historicalId']}", "FullyQualifiedName=" + row["test"])
            finally:
                target.write_bytes(sources[row["file"]][1])
            failed_target = any(entry.get("outcome") == "Failed" and entry.get("testName", "").startswith(row["test"])
                                for entry in result["results"])
            result.update({"historicalId": row["historicalId"], "test": row["test"],
                           "status": "behavioral-failure-observed" if result["exitCode"] != 0 and failed_target
                           else "not-proven"})
            receipt["controls"].append(result)
            save()
        receipt["after"] = run("after", "FullyQualifiedName~Harborline.Foundation.NotificationDelivery.Tests")
        all_failed_as_expected = all(row["status"] == "behavioral-failure-observed" for row in receipt["controls"])
        restored_green = receipt["after"]["exitCode"] == 0 and bool(receipt["after"]["results"]) and all(
            result.get("outcome") == "Passed" for result in receipt["after"]["results"])
        receipt["status"] = "passed" if all_failed_as_expected and restored_green else "not-proven"
        save()
        return 0 if receipt["status"] == "passed" else 1
    except BaseException as error:
        receipt["status"] = "interrupted-or-blocked"
        receipt["error"] = str(error)
        save()
        raise
    finally:
        for target, original, _ in sources.values():
            target.write_bytes(original)


if __name__ == "__main__":
    sys.exit(main())
