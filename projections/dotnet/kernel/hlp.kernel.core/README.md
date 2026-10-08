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

A floor member key starts with `a-z`, uses only `a-z`, `0-9` and `_`, and contains neither doubled nor trailing
underscores. It is one flat catalogue column and never a dotted path; any other key refuses with
`kernel.compiled-member-key-invalid`. The floor describes the kernel's system
records, not the authored Records grammar: DES-0004 §1 documents the correspondence (`field_key` to `key`,
`reference.required_trait_id` to `reference_trait_id` and so on). Record Type carries `record_class`, and Field carries
a reference's `reference_target_type_id` or `reference_target_class_id`, `reference_cardinality`, `reference_on_delete`,
`reference_parent` and `reference_trait_id` (L091, L092, L1424). A member change bumps the shape's revision: Record Type
and Field are at 2.

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
