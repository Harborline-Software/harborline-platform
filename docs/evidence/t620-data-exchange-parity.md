# T-620 gate blocker: Data Exchange lane parity

The full host phase-4 run at `f31decbb10349c4cb30a398a5750e343a8054b15`
reached gallery verification: 555 passed, two failed, zero flaky. The two failures were
`data-exchange.authoring` and `data-exchange.stale-review`. Both exceeded the unchanged
0.34 worst-tile limit with 0.390625 at tile 592,512. An independent clean-main run at
`bd4269cfc4b8d7514d4737e0fc229b8899e9ab72` reproduced the same failures.

The Blazor editor emitted whitespace between inline controls that React does not emit,
changing wrapping. Its Format selector also omitted the empty choice, and its gallery
draft omitted three delivery values present in React. The fix keeps adjacent markup
contiguous, adds the empty Format option, and aligns the gallery's authored draft values.
No engine, pack, seed, threshold, exemption, or flake registration changes are included.

## Native regression evidence

The workspace-write Codex lane ran this command before and after the producer correction:

```text
dotnet test projections/blazor/ui/hlp.ui.button.tests/Harborline.UIAdapters.Blazor.Tests.csproj --no-restore -p:UseSharedCompilation=false -nodeReuse:false --filter FullyQualifiedName~DataExchangeAuthoringEditorTests
```

RED exited 1: six failed, three passed. Five sibling-boundary cases found a TextNode where
the next control should be. The empty-format case found no selected empty option.
GREEN exited 0:

```text
Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9, Duration: 315 ms - Harborline.UIAdapters.Blazor.Tests.dll (net11.0)
```

## Packed browser verification

Fresh .NET and all three npm producer artifacts were built in serial workspace-write lanes.
Sandbox gallery preparation failed with NU1301 (NuGet TLS credentials), before any browser
test ran. After Chris approved host verification in chat, the focused host command exited 0:

```text
GALLERY_GREP=data-exchange\.(authoring|stale-review) is accessible and visually conformant$
GALLERY_SHARDS=1
PW_WORKERS=2
node tooling/run-gallery-gate.mjs --packages-ready

"status": "CHECKED"
"browserOutcomes": {
  "expected": 2,
  "unexpected": 0,
  "flaky": 0,
  "skipped": 0
}
data-exchange.stale-review  ratio=0.000000  worstTile=0.0000  at=0,0
data-exchange.authoring     ratio=0.000000  worstTile=0.0000  at=0,0
```

The browser assertions also verify all three delivery values and both Format options in
each lane. Local raw evidence remains under `artifacts/t620-local/`: `data-exchange-red.log`,
`data-exchange-green.log`, `phase4-gate-2.log`, `gallery-host-run-2.json`,
`parity-gallery-host-1.log`, `gallery-focused-host-1.json`, and `parity-measure.tsv`.
This focused result is not a full platform gate pass.
