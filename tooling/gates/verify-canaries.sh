#!/usr/bin/env bash
# assertGateCanFail, applied to the scanners themselves.
#
# Each scanner ships a --canary. That proves it passes; it does not prove it can FAIL. This
# perturbs the module-discovery prefix every UI scanner shares and requires the canary to notice.
# Same shape as harborline-api/eng/verify-analyzer-canary.sh, which exists because a canary that
# failed for the wrong reason proved nothing.
#
# A scanner that does not discover on that prefix cannot be reached by this perturbation, and
# reporting it DEAD -- "canary passed with discovery broken" -- claims something that never happened.
# Those are SKIPped by name and counted in the summary, never dropped silently: a bounded check that
# does not say what it left out reads as coverage it does not have.
set -uo pipefail
cd "$(dirname "$0")"
status=0
skipped=0
checked=0
for tool in scan-*.mjs; do
  if ! grep -q "startsWith('hlp\.ui\.')" "$tool"; then
    echo "SKIP $tool — does not discover on the hlp.ui. prefix; this perturbation cannot reach it"
    skipped=$((skipped + 1))
    continue
  fi
  checked=$((checked + 1))
  node "$tool" --canary >/dev/null 2>&1 || { echo "FAIL $tool — canary is red before perturbation"; status=1; continue; }
  cp "$tool" "$tool.bak"
  sed -i "s/startsWith('hlp\.ui\.')/startsWith('hlp.zz.')/" "$tool"
  node "$tool" --canary >/dev/null 2>&1
  rc=$?
  mv "$tool.bak" "$tool"
  if [ $rc -eq 0 ]; then echo "DEAD $tool — canary passed with discovery broken"; status=1
  else echo "OK   $tool — canary green, and red when discovery is broken"; fi
done
echo "-- $checked perturbed, $skipped skipped"
exit $status
