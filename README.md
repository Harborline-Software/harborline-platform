# Harborline Platform

This repository begins with a fresh public history as of September 2026. The earlier private history is kept, unchanged, in the private archive repository, and every design decision it records is carried forward in the Harborline control tickets. Nothing was rewritten; the history simply starts here.


> **Status: pre-release.** Harborline is under active development and is not ready for production use. APIs, schemas, storage formats and package names change without notice, and there are no supported installs yet. Source is licensed under [Apache-2.0](LICENSE); see [NOTICE](NOTICE) and the [trademark policy](TRADEMARKS.md).

Harborline Platform owns reusable module interfaces and their TypeScript, .NET, React, Blazor, and
other justified projections. Modules are designed around projection-neutral contracts and verified
through native tests, shared conformance fixtures, package consumers, and private galleries.

The UI surface is 73 modules. Each carries a lane-neutral Spec — interface and quality profile
under `specs/modules/ui/`, conformance fixtures under `conformance/`, and the component stylesheet
and gallery scenarios alongside them — sufficient to produce a projection without reading another
lane's code. React projects all 73 modules; Blazor projects 62. Projections include the narrowly
scoped `Harborline.Foundation` compatibility vocabulary required by the Blazor public interface,
and private npm/NuGet shadow packages. Existing aggregate package IDs and assembly names remain
compatibility identities only; distribution is not authorized by this repository.

Run the complete destination-native gate with:

```bash
npm run gate:phase4
```

The gate performs a clean React dependency install, restores the pinned .NET SDK, validates the
module catalog and provenance hashes, builds and runs native tests, executes shared conformance,
packs and installs disposable npm/NuGet consumers, and verifies the React and Blazor galleries.

Useful focused commands are:

```bash
npm run validate
npm run generate:authorization
npm run build
npm run test:native
npm run test:shared
npm run test:packages
npm run test:gallery
npm run test:compatibility
```

Migration planning, source discovery, component inventories, work packets, and cross-repository
orchestration are intentionally external to this product repository. Platform contains only the
contracts, implementations, tests, compatibility records, and gates needed to maintain and prove
the resulting product.
