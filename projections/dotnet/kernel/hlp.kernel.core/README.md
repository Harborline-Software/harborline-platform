# Harborline.Kernel.Core

This internal package owns the producer floor shared by kernel-facing modules: one injected
authoritative clock, one-command atomic commit and rollback, and the three compiled shapes that
exist before a catalogue seed is installed.

It contains no host persistence, member interpreter, package installer, or HTTP adapter.

## Clock and compiled bootstrap floor

`KernelClock.GetUtcNow()` assigns the recorded instant from the host's injected authoritative
clock. A recorded instant is never requestable. `ResolveEffectiveFromAsync` admits an omitted,
equal, or future effective instant; an explicit past effective instant requires
`IKernelBackdateCapability` and otherwise refuses with `kernel.backdate-capability-required`.

The Definition Package, Record Type and Field shapes are a compiled, self-describing floor. Each
shape carries its stable key and the complete DES-0004 section 1 member list (key, kind,
requiredness, cardinality and reference target), so no catalogue seed is read to describe it. A
package may not replace a floor shape by either its key or identity, case-insensitively.

## Configuration recovery (T-587)

`KernelProfile` is the kernel profile record: it declares the `configuration-recovery` capability in
compiled code, because recovery of the thing that installs packages cannot itself be a package.
`ConfigurationRecovery.RecoverAsync` reads one `KernelProfileSnapshot` through `IKernelProfileReader`
and nothing else: no catalogue, pack or released definition takes part in establishing authority. It
refuses before any write when the host admits no `IKernelConfigurationRecoveryCapability` for the
actor and tenant, when the request carries no reason or authority snapshot, when the profile's tenant
differs, or when the effective generation is missing or its content digest does not match the pointer;
each refusal names the state. A prepared generation left by a crash is abandoned, an effective pointer
bound to a committed evidence intent is confirmed and never moved, and unpublished evidence intents are
published. The record, its reason and the authority snapshot commit as one set through
`KernelTransactionBoundary`; the host's `IKernelTransactionPort` makes the terminal states durable.
