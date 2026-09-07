# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Harborline product engineers and design-system maintainers who build and maintain Harborline App across multiple business domains, including property management and accounting/ERP workflows.

The immediate consumers are Harborline App and its framework-specific application lanes. The repository does not establish a public or external distribution audience.

## Product Purpose

Harborline Platform hosts the shared design system and reusable platform modules used by Harborline App. It gives the multi-domain application a common set of contracts, behaviors, components, and supporting capabilities across TypeScript, .NET, React, Blazor, and other justified projections.

Success means Harborline App can reuse stable platform capabilities across business domains without each framework or domain reimplementing or redefining them, while the repository can prove that each projection satisfies the same product contract.

## Positioning

The platform treats projection-neutral module interfaces as the authority and framework implementations as adapters. Equivalence is established through shared conformance fixtures and executable evidence rather than matching component names or relying on one framework implementation as the source of truth.

## Operating Context

- Harborline App spans multiple business domains, including property management and accounting/ERP.
- Platform capabilities are maintained as cohesive modules with stable, projection-neutral interfaces.
- React and Blazor provide application-facing UI projections; TypeScript and .NET support broader contracts and platform capabilities.
- Private galleries expose neutral UI scenarios through both React Storybook and Blazor Blazing Story for verification.
- Native tests, shared conformance fixtures, disposable package consumers, compatibility records, and repository gates form the maintenance and release-proof workflow.

## Capabilities and Constraints

- The repository owns reusable module interfaces and their justified projections. Its current UI surface contains 73 modules; the README records React projections for all 73 and Blazor projections for 62.
- Module specs under `specs/modules/ui/`, conformance fixtures under `conformance/`, projection implementations, styles, and gallery scenarios collectively provide sufficient evidence to produce and verify a projection.
- The domain language in `CONTEXT.md` is authoritative: module, interface, projection, seam, adapter, independent consumer, application projection lane, compatibility projection, and product evidence have precise meanings.
- Retained aggregate package IDs and assembly names are compatibility identities only. Ticket 063 records the identity cutover from the earlier repository name.
- Package distribution is not authorized by this repository. npm and NuGet outputs are private/local shadow artifacts used for consumer verification.
- Migration planning, source discovery, prioritization, work packets, and cross-repository orchestration remain outside this product repository.
- The repository must not use the prohibited source classes listed in `repository.yaml`, including external sibling source and build output.

## Brand Commitments

- Product names are **Harborline App** for the multi-domain application and **Harborline Platform** for its shared design-system platform.
- Preserve the established `Harborline`, `hlp.*`, and `--hl-*` terminology where it is already part of module, package, or token contracts.
- Retained compatibility names are compatibility commitments, not current Harborline brand direction and not evidence of distribution authority.

## Evidence on Hand

- `README.md`: repository scope, UI-module counts, projection coverage, verification commands, and distribution boundary.
- `CONTEXT.md`: authoritative platform domain language.
- `repository.yaml`: repository authority, phase, compatibility, licensing, distribution, and prohibited-source constraints.
- `catalog/modules.yaml` and `catalog/projections.yaml`: module ownership, depth, projection, compatibility, and artifact records.
- `catalog/ui-theme-registry.json`: public semantic theme-token mappings and cross-projection visual-parity thresholds.
- `specs/modules/`: projection-neutral interfaces and quality profiles.
- `conformance/`: shared behavioral and quality fixtures.
- `gallery/scenarios/`: neutral UI scenarios used across React and Blazor galleries.
- `compatibility/aggregate/`: current compatibility evidence and rollback records.
- No testimonials, public customer claims, pricing, benchmarks, or authorization for public package distribution are established; future work must not invent them.

## Product Principles

1. **One product contract, many projections.** Framework lanes implement shared module truth; they do not define separate products.
2. **Prove parity with executable evidence.** Shared fixtures, native tests, package consumers, galleries, and compatibility records are the basis for confidence.
3. **Keep modules deep and independently useful.** A module should centralize meaningful behavior so consumers need less platform-specific knowledge.
4. **Preserve authority boundaries.** Compatibility identities, source authority, package identity, and distribution rights change only through explicit documented cutovers.
5. **Support the breadth of Harborline App without domain drift.** Shared platform behavior should remain coherent as the application spans property management, accounting/ERP, and additional business domains.

## Accessibility & Inclusion

UI projections must preserve equivalent accessible behavior across frameworks. Existing gallery evidence covers automated Axe scans, accessible naming, keyboard and focus-visible behavior, forced colors, reduced motion, 200% reflow, right-to-left content, Arabic localization, pseudolocale expansion, mixed-direction content, theme contrast, and runtime theme switching.
