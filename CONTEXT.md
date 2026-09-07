# Platform Domain Language

This file names the concepts used to keep Harborline Platform modules deep, independently
testable, and projection-neutral.

## Module

A cohesive capability behind one stable interface. A module owns decisions and hides enough
implementation detail that its consumers need less knowledge, not more.

## Interface

The projection-neutral behavioral contract owned by a module. Framework-specific props,
parameters, rendering details, and host mechanics do not become interface authority by default.

## Projection

A language- or framework-specific realization of a module interface, such as React or Blazor.
Projection parity means both realizations satisfy the same neutral fixtures; matching names alone
is not parity.

## Seam

A deliberate boundary where framework or host-specific behavior is translated into the module
contract. A seam has one interface owner and explicit adapters.

## Adapter

A projection-owned translator at a registered seam. An adapter may redistribute framework
complexity, but it must not redefine the module interface.

## Independent consumer

A production capability that depends on the module behavior and would have to own that behavior if
the module disappeared. Repeated calls, tests, galleries, and documentation do not create extra
consumer identities.

## Application projection lane

One framework realization of an application identity. React and Blazor lanes provide projection
evidence; they are not separate products and do not prove behavioral parity by themselves.

## Compatibility projection

A bounded facade or vocabulary retained to preserve an existing public contract while a deeper
module becomes authoritative. Compatibility status requires a rationale and an explicit revisit
trigger; it does not grant package distribution authority.

## Product evidence

Executable proof that supports the maintained result: module interfaces, neutral fixtures, native
and shared tests, package-consumer tests, gallery scenarios, provenance hashes, and compatibility
records. Planning, source discovery, prioritization, and work orchestration are control-plane
concerns and are not product evidence.
