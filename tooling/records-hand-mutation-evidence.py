#!/usr/bin/env python3
"""Focused behavioral hand mutations; compile/restore failures never count as kills."""
import argparse
import hashlib
import json
import os
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--pr', type=int, required=True)
parser.add_argument('--cases', choices=['tooling/records-hand-mutation-cases.json', 'tooling/records-publication-hand-mutation-cases.json'], default='tooling/records-hand-mutation-cases.json')
parser.add_argument('--expected-head')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
cases = [c for c in json.loads((root / args.cases).read_text()) if c['pr'] == args.pr]
if not cases or len({c['head'] for c in cases}) != 1:
    raise ValueError('Exactly one frozen head must be selected')
head = cases[0]['head']
if args.expected_head is not None and head != args.expected_head:
    raise ValueError('Case manifest does not match the requested frozen head')
work = Path(os.environ['RUNNER_TEMP']) / f'hand-pr-{args.pr}'
out = root / 'hand-mutation-evidence' / str(args.pr)
out.mkdir(parents=True, exist_ok=True)
subprocess.run(['git', 'worktree', 'add', '--detach', str(work), head], cwd=root, check=True)
if subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=work, text=True).strip() != head:
    raise ValueError('Checkout is not the frozen head')
results = []
def execute(case, phase):
    dest = out / case['id'] / phase
    dest.mkdir(parents=True, exist_ok=True)
    command = ['dotnet', 'test', case['project'], '--configuration', 'Release', '--filter', case['filter'],
        '--logger', 'trx;LogFileName=result.trx', '--results-directory', str(dest)]
    with (dest / 'stdout.log').open('w') as log:
        result = subprocess.run(command, cwd=work, stdout=log, stderr=subprocess.STDOUT)
    trx = dest / 'result.trx'
    if not trx.is_file():
        raise ValueError(f'{case["id"]}/{phase}: no TRX; build/restore failure is not a kill')
    tree = ET.parse(trx)
    tests = [dict(t.attrib) for t in tree.getroot().iter() if t.tag.endswith('UnitTestResult')]
    failed = [t['testName'] for t in tests if t.get('outcome') == 'Failed']
    if not tests:
        raise ValueError(f'{case["id"]}/{phase}: no tests executed')
    return {'command': command, 'exitCode': result.returncode, 'tests': tests, 'failedTestNames': failed}
try:
    for case in cases:
        path = work / case['path']
        original = path.read_text()
        if original.count(case['find']) != 1:
            raise ValueError(f'{case["id"]}: mutation must match exactly once')
        baseline = execute(case, 'baseline')
        if baseline['exitCode'] != 0 or baseline['failedTestNames']:
            raise ValueError(f'{case["id"]}: baseline did not pass')
        path.write_text(original.replace(case['find'], case['replace'], 1))
        diff = subprocess.check_output(['git', 'diff', '--', case['path']], cwd=work, text=True)
        (out / case['id'] / 'mutation.diff').write_text(diff)
        try:
            mutated = execute(case, 'mutated')
        finally:
            path.write_text(original)
        if mutated['exitCode'] == 0 or not mutated['failedTestNames']:
            raise ValueError(f'{case["id"]}: mutation survived')
        for name in case['expectedFailures']:
            if not any(name in actual for actual in mutated['failedTestNames']):
                raise ValueError(f'{case["id"]}: expected killing test not found: {name}')
        restored = execute(case, 'restored')
        if restored['exitCode'] != 0 or restored['failedTestNames']:
            raise ValueError(f'{case["id"]}: restored baseline did not pass')
        measured = {'case': case, 'originalSourceSha256': hashlib.sha256(original.encode()).hexdigest(),
            'diffSha256': hashlib.sha256(diff.encode()).hexdigest(), 'baseline': baseline,
            'mutated': mutated, 'restored': restored, 'status': 'Killed'}
        results.append(measured)
        (out / 'proof.json').write_text(json.dumps({'pr': args.pr, 'head': head, 'results': results}, indent=2) + '\n')
        print(json.dumps({'case': case['id'], 'status': 'Killed', 'failedTestNames': mutated['failedTestNames']}), flush=True)
finally:
    changed = subprocess.check_output(['git', 'diff', '--name-only'], cwd=work, text=True)
    if changed.strip():
        raise ValueError('Mutation checkout was not restored')
