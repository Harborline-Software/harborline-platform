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

The [module specifications](specs/modules/) define Platform's parts and their behavioral contracts.
The [module catalog](catalog/modules.yaml) identifies ownership and dependencies, and the
[projection catalog](catalog/projections.yaml) identifies implementations and declared status.
These records are the place to inspect current scope; the [README](README.md#specifications-and-inventory)
shows how to calculate inventory without maintaining counts in prose.

UI specifications include rendered components and supporting behavior. React, Blazor, TypeScript
and .NET implementations need not have matching directory layouts. Conformance is assessed against
the applicable contract, not inferred from a directory name or registration count.

[CONTEXT.md](CONTEXT.md) owns the platform vocabulary. Repository metadata and applicable release
workflows record source, compatibility and distribution constraints. A package produced for a test
is not by itself evidence of an authorized release. Migration planning and cross-repository work
coordination remain outside this product repository; maintainable contracts and their executable
checks remain beside the source.

## Brand Commitments

- Product names are **Harborline App** for the multi-domain application and **Harborline Platform** for its shared design-system platform.
- Preserve the established `Harborline`, `hlp.*`, and `--hl-*` terminology where it is already part of module, package, or token contracts.
- Retained compatibility names are compatibility commitments, not current Harborline brand direction and not evidence of distribution authority.

## Where to assess the product

- [Specifications](specs/modules/) describe intended behavior and quality requirements.
- [Catalogs](catalog/) record module ownership, implementation locations and declared statuses.
- [Conformance](conformance/) and [gallery scenarios](gallery/scenarios/) define shared checks.
- [Verification commands](package.json) identify the checks available in the current checkout.
- [Repository metadata](repository.yaml) records authority and compatibility decisions.

Assess results against the revision and environment they tested. A fixture's existence is not a
passing result; recorded parity is not proof of usability or complete application integration.
Public claims about adoption, performance and readiness require evidence for the specific claim.

## Product Principles

1. **One product contract, many projections.** Framework lanes implement shared module truth; they do not define separate products.
2. **Prove parity with executable evidence.** Shared fixtures, native tests, package consumers, galleries, and compatibility records are the basis for confidence.
3. **Keep modules deep and independently useful.** A module should centralize meaningful behavior so consumers need less platform-specific knowledge.
4. **Preserve authority boundaries.** Compatibility identities, source authority, package identity, and distribution rights change only through explicit documented cutovers.
5. **Support the breadth of Harborline App without domain drift.** Shared platform behavior should remain coherent as the application spans property management, accounting/ERP, and additional business domains.

## Accessibility & Inclusion

UI projections must preserve equivalent accessible behavior across frameworks. Each module's quality
profile and conformance fixtures define the applicable requirements, including interaction,
localization, visual adaptation and assistive-technology behavior. Evaluate those requirements
through the relevant checks and user testing; do not infer accessibility from projection parity.
