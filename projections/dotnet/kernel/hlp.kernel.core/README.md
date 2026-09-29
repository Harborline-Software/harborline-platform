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

## Package closure (T-979)

`KernelPackageClosure.Resolve(roots, packages, active)` is a pure resolver: the host supplies the root keys, the package manifests it holds and each active key's version, and gets back the closure in install order or a `KernelClosureRefusalException` carrying a `Code` and the dependency `Path` from the root. The walk is transitive over the manifests themselves, never over an author-claimed dependency list. `harborline.platform` is the implicit first root of every closure (DES-0029 ck-2, kernel-floor first), and every dependency precedes its dependents; ties break by ordinal key, so input order never changes the result. A closure holds one version per key, which must be the key's active version (`kernel.closure.version-conflict`). A pin is a minimum-inclusive SemVer 2.0.0 floor checked against the active version on every edge, so a diamond checks each pin on the shared package. The refusals are `kernel.closure.dependency-missing`, `kernel.closure.dependency-inactive`, `kernel.closure.dependency-below-pin`, `kernel.closure.cycle` (the path ends at the repeated key), `kernel.closure.version-conflict` and `kernel.closure.version-invalid`. Prior art: NuGet dependency resolution (one version per package, a bare version is a `>=` floor) and NU1108 for cycles.

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
