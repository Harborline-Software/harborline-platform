#!/usr/bin/env python3
"""Records python-dateutil's expansion of each rule below into dateutil-cases.json (T-1175/occurrence-engine).

The expected dates in that file come from python-dateutil, never from the Harborline producer (T-724 ruling 121).
Run with python-dateutil 4.0.0 (R-0146 call 5 measured that version):  python3 record-dateutil.py > dateutil-cases.json
Each rule is inside the bounded subset (DES-0057 recurrence-expansion-ck-1). A rule with COUNT or UNTIL is listed
whole; any other rule is read between its start and end, both inclusive.
"""
import json
from datetime import datetime

import dateutil
from dateutil.rrule import rrulestr

CASES = [
    # DAILY
    ("FREQ=DAILY;COUNT=5", "2026-01-01", "2026-12-31"),
    ("FREQ=DAILY;INTERVAL=3;COUNT=6", "2026-02-25", "2026-12-31"),
    ("FREQ=DAILY;BYDAY=MO,WE,FR;COUNT=8", "2026-01-01", "2026-12-31"),
    ("FREQ=DAILY;BYMONTHDAY=1,15", "2026-01-01", "2026-06-30"),
    ("FREQ=DAILY;BYMONTH=2;COUNT=30", "2026-01-01", "2028-12-31"),
    ("FREQ=DAILY;UNTIL=20260310", "2026-03-01", "2026-12-31"),
    ("freq=daily;interval=2;count=4", "2026-01-30", "2026-12-31"),
    # WEEKLY
    ("FREQ=WEEKLY;COUNT=5", "2026-01-07", "2026-12-31"),
    ("FREQ=WEEKLY;INTERVAL=2;BYDAY=TU,TH;COUNT=8", "2026-01-01", "2026-12-31"),
    ("FREQ=WEEKLY;BYDAY=SU;COUNT=4", "2026-01-07", "2026-12-31"),
    ("FREQ=WEEKLY;BYMONTH=3;BYDAY=MO", "2026-01-01", "2027-03-31"),
    ("FREQ=WEEKLY;INTERVAL=3;UNTIL=20260601", "2026-01-05", "2026-12-31"),
    # MONTHLY
    ("FREQ=MONTHLY;COUNT=6", "2026-01-31", "2027-12-31"),
    ("FREQ=MONTHLY;COUNT=6", "2026-01-30", "2027-12-31"),
    ("FREQ=MONTHLY;BYMONTHDAY=28;COUNT=4", "2026-01-01", "2027-12-31"),
    ("FREQ=MONTHLY;BYMONTHDAY=1,15;COUNT=6", "2026-01-10", "2027-12-31"),
    ("FREQ=MONTHLY;BYDAY=1MO;COUNT=6", "2026-01-01", "2027-12-31"),
    ("FREQ=MONTHLY;BYDAY=-1FR;COUNT=6", "2026-01-01", "2027-12-31"),
    ("FREQ=MONTHLY;BYDAY=2TU,4TU;COUNT=6", "2026-01-01", "2027-12-31"),
    ("FREQ=MONTHLY;BYDAY=5MO;COUNT=4", "2026-01-01", "2028-12-31"),
    ("FREQ=MONTHLY;INTERVAL=3;BYMONTHDAY=10;COUNT=4", "2026-01-10", "2028-12-31"),
    ("FREQ=MONTHLY;BYDAY=MO,TU;COUNT=6", "2026-02-01", "2027-12-31"),
    ("FREQ=MONTHLY;BYMONTHDAY=13;BYDAY=FR;COUNT=4", "2026-01-01", "2030-12-31"),
    ("FREQ=MONTHLY;BYMONTH=1,7;BYDAY=1MO;COUNT=4", "2026-01-01", "2028-12-31"),
    ("FREQ=MONTHLY;UNTIL=20261231", "2026-08-31", "2027-12-31"),
    # YEARLY
    ("FREQ=YEARLY;COUNT=3", "2024-02-29", "2040-12-31"),
    ("FREQ=YEARLY;BYDAY=1MO;COUNT=3", "2026-01-05", "2030-12-31"),
    ("FREQ=YEARLY;BYDAY=-1SU;COUNT=3", "2026-01-01", "2030-12-31"),
    ("FREQ=YEARLY;BYMONTH=3;BYMONTHDAY=15;COUNT=3", "2026-01-01", "2030-12-31"),
    ("FREQ=YEARLY;BYMONTH=1;BYDAY=MO;COUNT=6", "2026-01-01", "2030-12-31"),
    ("FREQ=YEARLY;BYMONTH=11;BYDAY=4TH;COUNT=4", "2026-01-01", "2030-12-31"),
    ("FREQ=YEARLY;INTERVAL=2;COUNT=3", "2026-06-15", "2040-12-31"),
    ("FREQ=YEARLY;BYMONTH=2;BYDAY=-1MO;COUNT=3", "2026-01-01", "2030-12-31"),
    ("FREQ=YEARLY;BYMONTH=6,12;BYMONTHDAY=1;COUNT=5", "2026-01-01", "2030-12-31"),
    ("FREQ=YEARLY;UNTIL=20300101", "2026-05-05", "2040-12-31"),
]


def day(text):
    return datetime.strptime(text, "%Y-%m-%d")


cases = []
for rule, start, end in CASES:
    parsed = rrulestr(rule, dtstart=day(start))
    bounded = "COUNT=" in rule.upper() or "UNTIL=" in rule.upper()
    dates = list(parsed) if bounded else parsed.between(day(start), day(end), inc=True)
    dates = [d for d in dates if d <= day(end)]
    cases.append({"rule": rule, "start": start, "end": end, "dates": [d.strftime("%Y-%m-%d") for d in dates]})

print(json.dumps({
    "engine": f"python-dateutil {dateutil.__version__}",
    "recordedBy": "Corpus/record-dateutil.py",
    "cases": cases,
}, indent=2))
