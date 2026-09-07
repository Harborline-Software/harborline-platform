# HLP-0004: Aggregate compatibility and the Button style bridge

Status: draft

The compatibility baseline is the exact `refoundation/baseline-2` earlier source commit
`a5036ff8b500e0d857d7e0a698408e7cf7f6cf31` and the three artifacts identified by SHA-256 in
`compatibility/aggregate/legacy/`. Public TypeScript declarations and public .NET type/member
metadata are machine-inventoried. Every added, changed, and removed item must match exactly one
named classification rule. The allowed dispositions are extracted, compatibility facade,
deferred, and approved removal; an unclassified difference or a stale rule fails the gate.

The Button extraction is compatible at its bounded public surfaces. React retains every legacy
Button property and adds only the optional `intent` alias. The 31 Button Foundation API items are
unchanged. The Blazor Button, base host properties, assembly identity, and public disposal contract
are unchanged. The new Blazor locale provider is an extracted addition. The narrower React locale
surface and every unrelated aggregate export are deferred, not silently removed. No removal is
approved by this decision.

The canonical Blazor styling interface is the internal, Button-sized `IButtonStyleBridge` with its
`CanonicalButtonStyleBridge` implementation. It resolves the extracted package's canonical
classes and public tokens. Legacy Bootstrap, Fluent UI, and Material providers remain supported at
the compatibility boundary by passing their produced class strings through the public `Class`
host parameter (and consumer styles through `Style`). All 900 rich provider combinations are
captured from the exact baseline and exercised through that public seam. The aggregate
styling interface from the earlier source (ticket 063), provider packages, theme service, and dependency-injection graph are not
restored as the canonical interface.

The original decision kept aggregate source and package identity/contract authority with the earlier
repository while public items remained deferred; ticket 063 records the later identity ruling. The original capture
proved the exact legacy npm and NuGet artifacts installable at that time, but only the original npm
bytes were retained. The durable record therefore makes no current executable rollback claim for
the unavailable NuGet artifacts. No repository has distribution authority for packages under the earlier name, because those names
will not be available from npm or NuGet (ticket 063). A future authority decision must either reduce the deferred count
to zero or approve a new package partition with explicit consumer migration evidence. This decision
authorizes no remote, stable release, package rename, assembly rename, identity-authority change,
or distribution-authority assignment.

> **Demoted to `draft` by control ticket 063 phase 1 (2026-08-20).** This record is no longer
> binding. It was authored while the source-era identity was retained deliberately, and its
> reasoning is preserved with current vocabulary because it explains why the retention was correct at the
> time. What changed is not the reasoning but the premise: the earlier package names (ticket 063) cannot be published, since
> the name is held on NuGet and npm by other parties, so retention became a permanent block on
> distribution rather than a temporary compatibility measure. The filename is frozen deliberately —
> an ADR number is its identity and citations point at the path — so this is edited in place rather
> than superseded by a new number.
