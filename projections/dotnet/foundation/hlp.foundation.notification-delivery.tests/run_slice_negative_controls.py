#!/usr/bin/env python3
"""Replay T-527 slice mutations; restore each source and require named failing TRX tests.
Run from the repository root. Supplemental controls, not Stryker score evidence.
"""
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

TESTS = Path(__file__).resolve().parent
ROOT = TESTS.parents[3]
SOURCE = TESTS.parent / 'hlp.foundation.notification-delivery'
CHANNEL = SOURCE / 'DeliveryChannels.cs'
INBOX = SOURCE / 'InboxChannel.cs'
CONTROLS = [
    ('outcome-added', CHANNEL, 'Permanent = 3,', 'Permanent = 3,\n    /// <summary>Negative control.</summary>\n    Unexpected = 4,', 'Outcome_vocabulary_is_closed'),
    ('classifier-unset', CHANNEL, 'classifyResult is null', 'false', 'Unset_classifier_refuses'),
    ('diagnostic-replaced', CHANNEL, 'new DeliveryOutcome(classification, result.Diagnostic)', 'new DeliveryOutcome(classification, string.Empty)', 'Declared_classifier_preserves_diagnostic'),
    ('unknown-admitted', CHANNEL, '!Enum.IsDefined(classification)', 'false', 'Undefined_outcome_refuses'),
    ('inbox-inverted', CHANNEL, 'result.Succeeded ? DeliveryOutcomeClass.Delivered', '!result.Succeeded ? DeliveryOutcomeClass.Delivered', 'Inbox_classifies_write_completion'),
    ('mark-read-undismiss', INBOX, 'entry.ReadState == InboxReadState.Unread', 'true', 'Dismiss_changes_only_one_delivery'),
    ('dismiss-read', INBOX, 'ReadState = InboxReadState.Dismissed', 'ReadState = InboxReadState.Read', 'Dismiss_changes_only_one_delivery'),
    ('dismiss-tenant', INBOX, ' || entry.Tenant != tenant', '', 'Dismiss_refuses_foreign_or_unknown'),
    ('dismiss-recipient', INBOX, ' || entry.Recipient != recipient', '', 'Dismiss_refuses_foreign_or_unknown'),
    ('dismiss-cancellation', INBOX, 'cancellationToken.ThrowIfCancellationRequested();', '', 'Cancelled_dismissal_leaves_delivery_unchanged'),
]

def digest(data):
    return hashlib.sha256(data).hexdigest()

def main():
    receipts = []
    inputs = {str(p.relative_to(ROOT)): digest(p.read_bytes()) for p in [CHANNEL, INBOX, TESTS / 'ClassificationAndDismissTests.cs']}
    with tempfile.TemporaryDirectory(prefix='t527-controls-') as temp:
        for name, file, before, after, test in CONTROLS:
            original = file.read_bytes()
            source = original.decode()
            method = 'MarkReadAsync' if name == 'mark-read-undismiss' else 'DismissAsync'
            start = source.index(f'    public Task {method}(') if file == INBOX else 0
            end = source.index('    /// <inheritdoc />', start) if file == INBOX else len(source)
            prefix, body, suffix = source[:start], source[start:end], source[end:]
            if body.count(before) != 1:
                raise ValueError(f'{name}: expected one mutation span')
            try:
                file.write_text(prefix + body.replace(before, after, 1) + suffix)
                command = ['dotnet', 'test', str(TESTS / 'Harborline.Foundation.NotificationDelivery.Tests.csproj'), '--no-restore', '--nologo', '--filter', f'FullyQualifiedName~ClassificationAndDismissTests.{test}', '--logger', f'trx;LogFileName={name}.trx', '--results-directory', temp]
                run = subprocess.run(command, cwd=ROOT, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
                (Path(temp) / f'{name}.log').write_text(run.stdout)
                trx = Path(temp) / f'{name}.trx'
                if not trx.exists():
                    raise ValueError(f'{name}: no TRX; {run.stdout}')
                results = ET.parse(trx).getroot().findall('.//{*}UnitTestResult')
                failed = [r.attrib['testName'] for r in results if r.attrib['outcome'] == 'Failed']
                if run.returncode == 0 or not failed or any('notification-delivery-' not in t for t in failed):
                    raise ValueError(f'{name}: no named behavioral failure: {run.stdout}')
                receipts.append({'mutation': name, 'file': str(file.relative_to(ROOT)), 'before': before, 'after': after, 'killingTest': test, 'failedDisplayNames': failed, 'exitCode': run.returncode})
                print(f'{name}: killed by {test}', flush=True)
            finally:
                file.write_bytes(original)
    output = {'scope': 'T-527/channel-classification-and-dismiss supplemental negative controls; not Stryker evidence', 'oracle': 'DES-0055 ck-1, ck-7, ck-8 and ruling 7, clarified by control PR 1263', 'sourceSha256': inputs, 'controls': receipts}
    (TESTS / 'evidence/t527-negative-controls.json').write_text(json.dumps(output, indent=2) + '\n')

if __name__ == '__main__':
    main()
