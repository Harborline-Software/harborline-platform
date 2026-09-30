# Harborline.Foundation.ExecutionRuntime

Revision 1 of `hlp.foundation.execution-runtime`, the execution runtime substrate (DES-0056, T-528
slice 1): run-kind registration, one kind-qualified run identity, a closed run status vocabulary with
legal transitions, the substrate's named retry profiles, the dead-letter path, and a tenant-scoped
`IRunStore` port with an in-memory reference implementation. Slice 2 adds one tenant-scoped effect
receipt per logical effect, its closed effect-status vocabulary, and ambiguity reconciliation evidence.

Each engine registers its run kind and keeps its own state in its own store; the substrate holds
identity, status, attempts, retry profile, dead-letter and `caused_by` only. Effect receipts retain
their required non-secret references and evidence but never engine payloads or secret values. Engine
adoption, the execution trace and the observe channel are later T-528 slices. The package is local
and distribution-blocked.
