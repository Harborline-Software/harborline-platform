// Hermetic-build shim (the rule-runtime `abort-signal.d.ts` precedent): `structuredClone`
// is a runtime global in every supported host, but the pinned `lib: ES2022` + `types: []`
// build sees no declaration for it. Declare the narrow shape this package uses.
declare function structuredClone<T>(value: T): T
