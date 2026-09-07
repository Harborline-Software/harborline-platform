# HLP-0005 — Harborline App theme registry and visual parity baselines

Status: draft  
Date: 2026-08-09


> **Demoted to `draft` by control ticket 063 phase 1 (2026-08-20).** This record is no longer
> binding. It was authored while the source-era identity was retained deliberately, and its
> reasoning is preserved with current vocabulary because it explains why the retention was correct at the
> time. What changed is not the reasoning but the premise: the earlier package names (ticket 063) cannot be published, since
> the name is held on NuGet and npm by other parties, so retention became a permanent block on
> distribution rather than a temporary compatibility measure. The ADR NUMBER is frozen deliberately —
> a number is a record's identity — so this is edited in place rather than superseded by a new one.
> The slug was renamed by ticket 288 slice 2 (it named the source-era product, and the one citation
> that reads this path, `tooling/validate-repository.mjs`, moved with it); the record is still
> HLP-0005 and still about the Harborline App theme registry.

## Decision

`catalog/ui-theme-registry.json` is the additive Harborline public-token authority. It pins the
exact Harborline App source commit, path, and Git blob, maps each `--hl-*` token to its Harborline App semantic
source token, and records light and dark values. Components consume the public semantic tokens;
gallery-specific and retained Button names are aliases, not independent theme authorities.

Interactive control boundaries use `--hl-control-border`, mapped to Harborline App's high-contrast
`--color-text-faint`. The general `--hl-border` remains mapped to Harborline App's intentionally subtle
surface border. This distinction retains Harborline App appearance while preserving the 3:1 control
boundary proof.

React/Blazor live image comparison uses the registry's frozen pixelmatch threshold and maximum
changed-pixel ratio. The pinned Harborline App semantic-token snapshot is the appearance baseline; a
projection pair may not drift together by changing token values or tolerances outside a reviewed
registry revision. Repository validation binds the canvas, Button fixtures, and browser test to
the registry.

## Consequences

- New visual modules reuse the registered semantic names instead of minting component-global
  alternatives.
- A Harborline App theme change requires an explicit registry revision with source provenance.
- A threshold change is a compatibility decision, not an incidental test edit.
- Component-local custom properties may exist as aliases, but their fallback values must resolve
  to registered Harborline App semantics.
