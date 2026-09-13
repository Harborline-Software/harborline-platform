# Harborline Platform

> **Status: pre-release.** Harborline is under active development and is not ready for production use. APIs, schemas, storage formats and package names change without notice, and there are no supported installs yet. Source is licensed under [Apache-2.0](LICENSE); see [NOTICE](NOTICE) and the [trademark policy](TRADEMARKS.md).

Harborline Platform defines reusable capabilities through module specifications and supplies their language- and framework-specific implementations. Consumers compose these capabilities to support Harborline's domain models, execution and human interfaces. Conformance checks assess implementations against those contracts.

The [product purpose](PRODUCT.md) explains the users and design principles. The [domain language](CONTEXT.md) defines module, interface, projection and related terms.

## Specifications and inventory

Start with [module specifications](specs/modules/) for intended behavior and quality requirements. The [module catalog](catalog/modules.yaml) records ownership, dependencies and interfaces; the [projection catalog](catalog/projections.yaml) records implementation locations and declared status. [Conformance fixtures](conformance/) provide shared checks.

Use the catalogs to calculate inventory and coverage for the revision you are reviewing. A module can have several projections, and a projection can live inside another module's directory. UI support behavior may have a .NET implementation without a standalone Blazor component. Directory counts therefore do not measure behavioral coverage.

For example, this read-only command reports distinct registered UI modules per projection from the JSON-formatted projection catalog:

```sh
node --input-type=module -e "import fs from 'node:fs'; const rows=JSON.parse(fs.readFileSync('catalog/projections.yaml','utf8')).projections.filter(r=>r.moduleId.startsWith('hlp.ui.')); for(const lane of [...new Set(rows.map(r=>r.projection))].sort()) console.log(lane,new Set(rows.filter(r=>r.projection===lane).map(r=>r.moduleId)).size);"
```

These totals describe registrations, not passing tests or production readiness. Inspect recorded statuses and run the relevant checks before making a conformance claim. Package identity and distribution decisions are recorded in [repository metadata](repository.yaml); consult the applicable release workflow and its evidence before publishing.

## Verification

Run the repository gate with:

```bash
npm run gate:phase4
```

The [gate implementation](tooling/run-phase-4-gate.mjs) defines the checks and prerequisites for this checkout. Consult it when preparing an environment or investigating a failure.

For focused checks, run `npm run` to list the commands available in this checkout. [package.json](package.json) maps each command to its maintained implementation. Choose the native, shared, package-consumer, UI or gallery checks relevant to the change; a passing subset does not establish that the complete gate passed.

See [CONTRIBUTING.md](CONTRIBUTING.md) for change and compatibility requirements.
