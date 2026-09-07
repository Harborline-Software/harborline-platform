# HLP-0006 — Own the Harborline App Switch seam

Status: draft
Date: 2026-08-10


> **Demoted to `draft` by control ticket 063 phase 1 (2026-08-20).** This record is no longer
> binding. It was authored while the source-era identity was retained deliberately, and its
> reasoning is preserved with current vocabulary because it explains why the retention was correct at the
> time. What changed is not the reasoning but the premise: the earlier package names (ticket 063) cannot be published, since
> the name is held on NuGet and npm by other parties, so retention became a permanent block on
> distribution rather than a temporary compatibility measure. The filename is frozen deliberately —
> an ADR number is its identity and citations point at the path — so this is edited in place rather
> than superseded by a new number.

## Context

The legacy Platform decision `HLP-0010` deferred a standalone Switch module because the earlier
review did not establish enough independent behavior to justify another owner. Harborline App now has
thirteen controlled production usages across three callers, and its Switch carries behavior that
cannot be delegated to an undifferentiated form-control layer: stable switch semantics, Form Field
metadata, controlled and uncontrolled state, form participation, coarse-pointer targeting,
canonical size normalization, and logical RTL thumb motion.

## Decision

`hlp.ui.switch` is the projection-neutral owner for this behavior. React and Blazor implement the
same revision-1 interface and fixtures behind `ui.switch.framework`; neither projection is the
authority for the other. Harborline App React at commit
`a5036ff8b500e0d857d7e0a698408e7cf7f6cf31` remains the source-scope authority, while the frozen
contract corrects its state-dependent accessible-name fallback and physical RTL translation.

This decision supersedes the archived legacy `HLP-0010` deferral. It does not promote
`SwitchField`, Toggle Switch compatibility facades, or domain-specific boolean fields into this
module.

## Alternatives evaluated

1. Keep Switch deferred and expose it through a broad forms adapter. This reduces catalog rows but
   leaves no owner for keyboard, form-value, touch-target, stable-name, and RTL parity. Deleting the
   adapter would redistribute those rules across every consumer.
2. Give Switch its own deep seam and keep Form Field, touch sizing, localization, and token policy
   as dependencies. This keeps the public interface small while concentrating the behavior that
   must remain equivalent across React and Blazor.

The second alternative is accepted because Harborline App production use now proves the seam and the
deletion test shows materially redistributed complexity.

## Consequences

- A Switch must have a stable name supplied by a visible label, explicit accessible name, or Form
  Field label; localized On/Off state text never becomes that name.
- The Blazor projection is a parity implementation, not promotion of the shallow legacy candidate.
- Compatibility aliases may normalize into the canonical interface but may not duplicate callback
  delivery or create a second state model.
- Future Switch-field composition depends on this module instead of absorbing it.
