#!/usr/bin/env python3
"""Extracts libical's recurrence corpus cases that fall inside the bounded subset into libical-cases.json (T-1175).

Source: libical v4.0.7, src/test/icalrecur_test.txt, which R-0146 call 5 read as a differential oracle. Only facts are
recorded: each rule, its DTSTART date and time of day and the dates of its INSTANCES, with the line number they come from. The file is
MPL-2.0 or LGPL-2.1 and is not copied. The expected dates come from libical, never from the Harborline producer
(T-724 ruling 121).

    curl -sSfL -o icalrecur_test.txt https://raw.githubusercontent.com/libical/libical/v4.0.7/src/test/icalrecur_test.txt
    python3 extract-libical.py icalrecur_test.txt > libical-cases.json

Excluded: START-AT cases (they test where iteration begins), and any rule outside DES-0057 recurrence-expansion-ck-1,
judged by the same rules as RruleSubset: FREQ DAILY, WEEKLY, MONTHLY or YEARLY; only INTERVAL, COUNT, UNTIL, BYDAY,
BYMONTHDAY (1 to 28) and BYMONTH; no part twice; not COUNT with UNTIL; a BYDAY ordinal of 1 to 5 either way, and only
under MONTHLY or YEARLY; no BYMONTHDAY under WEEKLY. Cases with more than 1 000 instances are excluded (the cap).
"""
import hashlib
import json
import re
import sys

PARTS = {"FREQ", "INTERVAL", "COUNT", "UNTIL", "BYDAY", "BYMONTHDAY", "BYMONTH"}


def in_subset(rule):
    seen = {}
    for token in rule.split(";"):
        if "=" not in token:
            return False
        key, value = token.split("=", 1)
        key = key.upper()
        if key not in PARTS or key in seen:
            return False
        seen[key] = value.upper()
    if seen.get("FREQ") not in {"DAILY", "WEEKLY", "MONTHLY", "YEARLY"}:
        return False
    if "COUNT" in seen and "UNTIL" in seen:
        return False
    if not re.fullmatch(r"\d{8}(T\d{6}Z)?", seen.get("UNTIL", "20000101")):
        return False
    for day in seen.get("BYMONTHDAY", "1").split(","):
        if not re.fullmatch(r"\d+", day) or not 1 <= int(day) <= 28:
            return False
    if "BYMONTHDAY" in seen and seen["FREQ"] == "WEEKLY":
        return False
    for entry in seen.get("BYDAY", "MO").split(","):
        match = re.fullmatch(r"([+-]?\d+)?(MO|TU|WE|TH|FR|SA|SU)", entry)
        if not match:
            return False
        if match.group(1):
            if not 1 <= abs(int(match.group(1))) <= 5 or seen["FREQ"] in {"DAILY", "WEEKLY"}:
                return False
    for month in seen.get("BYMONTH", "1").split(","):
        if not re.fullmatch(r"\d+", month) or not 1 <= int(month) <= 12:
            return False
    return True


def date(stamp):
    return f"{stamp[0:4]}-{stamp[4:6]}-{stamp[6:8]}"


path = sys.argv[1]
raw = open(path, "rb").read()
lines = raw.decode("utf-8").splitlines()
cases, block = [], {}
for number, line in enumerate(lines, start=1):
    if line.startswith("RRULE:"):
        block = {"rule": line[6:], "line": number}
    elif line.startswith("DTSTART:"):
        block["dtstart"] = line[8:]
    elif line.startswith("START-AT:"):
        block["startAt"] = True
    elif line.startswith("INSTANCES:"):
        instances = [stamp for stamp in line[10:].split(",") if stamp]
        if not block.get("startAt") and in_subset(block["rule"]) and 0 < len(instances) <= 1000 \
                and re.fullmatch(r"\d{8}(T\d{6})?", block["dtstart"]):
            case = {"line": block["line"], "rule": block["rule"], "start": date(block["dtstart"])}
            if "T" in block["dtstart"]:
                # A timed DTSTART: the test runs it as a timed series read in UTC, so a UTC UNTIL bounds it at the
                # instant, as libical does.
                stamp = block["dtstart"].split("T")[1]
                case["startTime"] = f"{stamp[0:2]}:{stamp[2:4]}:{stamp[4:6]}"
            case["dates"] = [date(stamp) for stamp in instances]
            cases.append(case)
        block = {}

print(json.dumps({
    "engine": "libical v4.0.7 src/test/icalrecur_test.txt",
    "sha256": hashlib.sha256(raw).hexdigest(),
    "recordedBy": "Corpus/extract-libical.py",
    "cases": cases,
}, indent=2))
