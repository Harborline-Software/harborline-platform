// Minimal ambient `AbortSignal` for the pure-logic tiers.
//
// The engine reads exactly one member — `.aborted` — as the NON-authoritative
// liveness/cancel seam (the TS analog of the .NET `CancellationToken`; D1, 2026-07-01).
// This package is pure logic that runs in BOTH the browser reactive tier and Node, so
// it must not pull the whole `DOM` (or `@types/node`) lib in merely to NAME this type.
//
// A real `AbortSignal` (browser `AbortController().signal` or the Node global) is
// structurally assignable to this shape. This declaration is a build-time INPUT only —
// `tsc` does not emit hand-authored `.d.ts` files, so it is NOT shipped in `dist/`;
// consumers that load `lib.dom` / `@types/node` still see the full `AbortSignal` from
// their own lib. It exists so the package's own hermetic dist build (`types: []`, no
// `DOM` lib) resolves the name deterministically instead of depending on an ambient
// `@types/node` accidentally discovered by walking up `node_modules/@types`.
interface AbortSignal {
  readonly aborted: boolean
}
