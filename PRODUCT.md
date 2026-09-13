# Harborline Platform product

<!-- impeccable:product-schema 1 -->

## Platform

Reusable libraries with web UI projections and framework-neutral supporting behavior.

## Users

Product engineers compose Platform modules into Harborline applications and runtime services.
Design-system maintainers define the interaction and presentation behavior shared by React and
Blazor. Both need contracts they can rely on without learning another implementation's internals.

## Product Purpose

Harborline Platform makes reusable behavior explicit, composable and independently verifiable.
Module specifications define that behavior; language and framework projections implement it.
This lets Harborline support varied business models without each domain or framework redefining
common capabilities.

Success means a consumer can adopt a module with less implementation knowledge and maintenance
burden than owning the behavior itself. Another projection must preserve the contract while using
its framework's appropriate implementation techniques.

## Scope

Platform includes UI components, supporting state and localization behavior, contracts, and reusable
execution capabilities. The specifications define each module's boundary. Domain processes can
compose those capabilities through authored definitions; adding a business domain does not by
itself justify adding a compiled module.

The signed platform package supplies authoring definitions and has a different responsibility from
this reusable code foundation. Application composition belongs to consumers. Cross-repository
planning belongs outside this repository; specifications and executable checks belong beside the
implementations they govern.

## Product Principles

1. **Specify behavior independently of frameworks.** Consumers rely on the module contract;
   React or Blazor code does not become the authority for other projections.
2. **Make reuse earn its cost.** A module should hide meaningful complexity and serve an actual
   consumer. Generality follows demonstrated needs.
3. **Keep composition understandable.** Extend the model when existing capabilities cannot
   express a need clearly, while preserving predictable behavior for existing consumers.
4. **Assess quality against intent.** Conformance, accessibility and usability answer different
   questions. Equivalent outputs alone do not establish a useful experience.
5. **Preserve compatibility deliberately.** Changes to public behavior and identities need an
   explicit migration path and evidence that affected consumers still work.

## Accessibility & Inclusion

People must be able to use the resulting interfaces across input methods, languages, display
conditions and assistive technologies. Module quality profiles express the applicable requirements.
Automated checks and user evaluation provide complementary evidence; accessibility is part of the
contract rather than a property inferred from visual similarity.

## Product Identity

Use Harborline Platform for the reusable foundation and Harborline App for the human application.
Preserve established module, package and token identities according to their compatibility contracts.

For current inventory, implementation locations and verification commands, use the
[repository guide](README.md). This document defines purpose and design choices, not release status.
