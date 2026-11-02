# HLP-0009 — The module catalog and provenance map keep one file per module

Status: proposed (2026-11-01)

`catalog/modules.yaml` and `docs/provenance/source-map.yaml` each held a row per module in one
document. Almost every module change had to edit both: the catalog pins the module's
`interface.revision` (the validator requires it to equal the interface's own `interfaceRevision`),
and the provenance record pins a sha256 for every file the module owns. So pull requests about
different modules changed the same two files.

Measured on 2026-11-01 over the 28 open pull requests, counting each one's own diff against its
base: 16 changed the source map and 8 the catalog, while no other path was changed by more than 6.
Six of the eight catalog edits were only `interface.revision` bumps; the source-map edits were hash
values and the record's prose. The effects:

- Rebases conflicted on the source map (the open-PR sweep found it in 348 and the 364 stack).
- The control repository's slice picker holds a slice when an open pull request already changes a
  path the slice touches. `catalog/modules.yaml` sat in the paths of two M11 slices and in 13 open
  pull requests, so it held both slices for reasons unrelated to their work.
- `tooling/gates/select-affected.mjs` treats `catalog/` as shared ground, so a one-line revision bump
  for one module selected every module.
- A slice could not honestly list its paths: T-493's first slice touches the taxonomy interface but
  declares the source map outside its paths, while the validator makes it edit the hash there.

## The decision

Each module's rows live in their own file, named by module id:

- `catalog/modules/<moduleId>.json` holds `{moduleId, module, seams?}`. A seam is stored with the
  module that owns its interface, so `interfaceOwner` is the file's module and is not restated.
- `docs/provenance/records/<moduleId>.json` holds the provenance record unchanged.

The original two paths stay as headers holding only what is not per module: schema version,
repository identity, the source authority and baseline, and the directory the rows live in. They
refuse rows (`rowFileErrors`), so a stale branch cannot quietly put a monolith back.

No combined copy is checked in. Every reader calls `loadModuleCatalog` or `loadProvenance` in
`tooling/module-catalog.mjs`, which return the shape the single documents had, sorted by id. The
validator's output on the split tree is identical to its output on the tree before it.

Row files are canonical JSON (two-space indent, trailing newline) and the validator refuses any
other layout, so a revision bump stays a one-line diff in one file. `select-affected` now treats a
module's own row as that module's input rather than shared ground.

`tooling/refresh-provenance-hashes.mjs` recomputes hash values from the files, for one module, for
the paths changed since a ref, or for all; it never adds or drops a hashed path, which stays a
reviewed decision. This gives the platform a writer for the hashes it validates, which control
ADR-0013 (G-5) records as missing.

`tooling/migrate-catalog-branch.mjs` carries a pre-split branch's edits into the per-module files
after it merges main: it works out which modules the branch changed between its merge base and its
tip and applies only those, so nothing main changed since is reverted, and it reports any module
both sides changed instead of choosing.

## Alternatives considered

- **A generated aggregate, rebuilt in CI or the merge queue.** Still a checked-in file every branch
  regenerates, so rebases still conflict textually. A merge queue tests the merge commit it built
  and cannot add a regeneration commit to it, so the rebuild would need a write-token job after
  merge. More moving parts for the same result.
- **A custom git merge driver or `merge=union`.** GitHub's server-side merge, which the merge queue
  uses, runs no custom driver; it only helps on a developer's machine. A line union of JSON can
  produce duplicate keys or invalid documents and loses the fail-loud property.
- **Marking the files as generated so the picker ignores them.** Hides the overlap from the picker
  but not from git: two writers would be dispatched and then conflict on rebase.
- **Deriving the catalog's interface revision from the interface file.** Removes the most common
  catalog edit, but drops a deliberate cross-check and leaves the provenance map, the hotter file,
  as it was. Can still be done later, inside the per-module file.

The same pattern is common elsewhere: Nx replaced its root `workspace.json` with per-project
`project.json` files, and GitLab moved changelog entries into one file per change after the shared
`CHANGELOG.md` conflicted on most merge requests.

## Consequences

A change to one module touches files named for that module, so slice records can list exactly
`catalog/modules/<id>.json` and `docs/provenance/records/<id>.json`, and two writers on different
modules no longer share a path. Adding a module adds files rather than editing shared ones.

Open pull requests made before this change conflict on the two headers when they merge main. That
conflict is intended; `migrate-catalog-branch.mjs` resolves it mechanically.

`gallery/aria-parity-known-divergences.json` (6 open pull requests, one stack) is the next most
shared file and is a candidate for the same treatment if it keeps colliding.
