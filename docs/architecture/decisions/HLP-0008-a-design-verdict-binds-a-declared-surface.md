# HLP-0008 — A design verdict binds a declared surface, not a picture and not the source

Status: accepted (2026-09-06, ticket 138)

`assertDesignReview` is a recorded human judgement, and it expires when the thing that was judged
changes. Until ticket 138 it computed the thing that was judged as a sha256 over two files:
`gallery/scenarios/<id>.json` and `specs/modules/ui/<id>/style.css`. The projection sources were
not hashed. A `.tsx`, a `.razor` or a component stylesheet could change what a reviewer would see
today while their verdict from last week still read as current.

Hashing everything is the obvious repair and it is the wrong one. Every rename, reformat and
behaviour-neutral refactor would expire every verdict it touched. A reviewer re-approving pixels
that did not move learns nothing, and learns it often enough to stop reading. That trades one false
negative for a stream of false positives, which is how a gate becomes decoration.

## The decision

A verdict binds a **declared surface**: an explicitly named set of files that decide what a reviewer
saw, hashed one file at a time and stored on the record. Expiry is per file, and the refusal names
the file that moved.

Three properties make it the choice.

It is **declarable**. The set is written down in one function, reviewed like any other code, and
widened deliberately. "What expires this verdict" is answerable by reading a list rather than by
reasoning about a build graph.

It is **nameable**. Per-file digests, not one blended hash. A blended hash can only say that
something moved; a refusal that cannot name the file it refuses over sends a reviewer hunting
instead of to a diff.

It is **fail-closed**. A missing file hashes as empty, so acquiring a stylesheet later moves the
surface. A record binding an empty or partial surface is refused rather than trusted — that hole
was found in review of slice 1, where `surface: {}` read PASS and skipped even the old check.

## What was rejected

**Hashing rendered output.** The most direct answer to the question the ticket asks: hash what the
reviewer actually looked at. It fails on determinism and on cost. A rendered hash is a hash per
shell, per theme, per viewport, per locale, per font stack, per browser build — the cross product
this repository already runs in the gallery gate, at seven minutes and a browser per pass, against a
verdict check that must run in milliseconds inside `run-ui-gate-model.mjs`. Worse, an exact hash of
a rendered image is the one comparison the industry has already abandoned (see below): text
antialiasing, subpixel rounding and font-version drift move the bytes without moving the design. The
tools that survive this all soften the comparison with thresholds, masks and layout-level matching,
and a softened comparison is a judgement call. A judgement call is what the reviewer is for. This
option remains open for a *later* signal — a rendered-output fingerprint captured by the gallery
gate, which already pays the rendering cost — but it cannot be the expiry rule the verdict binds.

**Hashing the module's source tree.** Correct in reach and wrong in noise, for the reason the ticket
states. A test-only edit, a comment, a formatting pass and a rename all expire verdicts they cannot
affect. Slice 4 exists to prove the declared surface does *not* behave this way, and it could not be
written against this option.

**Hashing the build output of the projection.** Between the two: a bundle digest ignores comments
and, with minification, most renames. It was rejected for opacity. The digest is a single number
over a generated artefact, so a refusal cannot name a file, the artefact is not tracked, and its
value depends on toolchain versions the design does not. It buys a weaker version of the surface's
reach at the cost of the surface's whole point.

**A reviewed-at date, or an explicit re-review flag.** Both put the detection in a person's memory,
which is where it was before ticket 098 and is the reason 138 exists.

## What the industry does

Harborline chooses proven over derived, so this was looked up rather than reasoned out.

- **Chromatic TurboSnap** does not re-render every story on every build. It reads the bundler's
  dependency graph, intersects it with the git diff, and captures only the stories the changed files
  can reach; the rest copy their baselines forward. That is the same shape as a declared surface
  with the declaration computed rather than written — and it is why a declared surface is not an
  exotic choice. TurboSnap's own guidance is that the graph must be complete or the narrowing is
  unsound, which is the fail-closed rule above.
- **Percy** captures a DOM snapshot in the test and re-renders it in its own controlled environment
  rather than diffing the browser's pixels directly, precisely to keep the client's rendering
  environment out of the comparison.
- **Applitools Eyes** ships match levels — strict, content, layout — and explicit ignore regions,
  because exact pixel comparison produces more noise than signal. Antialiasing suppression is a
  named feature.
- The standard advice for pixel-diff suites (thresholds, masked dynamic regions, disabled
  animations, layout-level rather than pixel-level matching) is an admission that raw rendered
  output is not a stable identity for a design.

The reading is consistent: nobody treats an exact hash of rendered output as an expiry key. They
either narrow by dependency graph, or they compare renders with deliberate tolerance under human
review. This decision takes the first and leaves the second to the gallery gate.

Sources: [Chromatic TurboSnap](https://www.chromatic.com/docs/turbosnap/),
[Cypress visual testing guide](https://docs.cypress.io/app/tooling/visual-testing),
[visual regression tool comparison](https://www.desplega.ai/blog/deep-dive-7-visual-regression-testing-ui-bugs).

## What carries the appearance

Slice 1 landed the mechanism with a deliberately narrow set — `specs/modules/ui/<id>/scenarios.json`,
`specs/modules/ui/<id>/style.css`, `gallery/scenarios/<id>.json` — so that widening could be judged
separately from the change of shape. That set does not meet the ticket's second acceptance line, and
it is not the final answer.

The surface is the files that can change what the gallery draws for that module:

- the spec authority for its stories and its stylesheet, and the derived gallery scenario catalog
  (bound today);
- the projection sources at the paths `catalog/projections.yaml` records for the module — the
  React `src/**` and the Blazor component, template, stylesheet and `wwwroot/**` — excluding test
  projects, test setup and build configuration, which cannot reach a render;
- the module's story files in the gallery application, which are named after the component rather
  than the module id and therefore have to be resolved through the catalog, not guessed from a
  string template.

Two of the five modules ticket 138 names changed in projection source
(`hlp.ui.data-export-button`, `hlp.ui.data-grid`); the rest changed in scenario files that slice 1
already binds, and expire on re-record. Slice 3 owns the widening and the flagging.

The rule that makes the widening safe: a module whose surface cannot be resolved fails rather than
passes. A verdict that binds a set nobody can recompute is the same defect as a verdict that binds
nothing, which is the defect this record exists to close.

---

## Addendum (2026-09-06, ticket 138 slice 3) — the surface reaches the render through a normalised digest

The decision above stands. This section records the one thing it left open: *how* a file that
carries a render enters the declared surface, now that slice 3 has widened the surface to reach the
projection sources and the gallery stories.

Widening alone is not safe, and that is measured rather than feared. Slice 4 built a case table that
edits a real file of a real module and asks the gate's own `reviewVerdict` what happened. Binding the
raw bytes of `Button.tsx` turned **four of six behaviour-neutral edits red** — a private-helper
rename, a whitespace reformat, a comment, an import reorder — each refusal naming `Button.tsx`. That
is precisely the "source-tree hashing: the false-positive stream" this record rejected, arriving
through the front door of the widening it asked for.

### The decision

A file that **carries a render** — `.tsx`, `.ts`, `.jsx`, `.js`, `.css`, `.razor`, `.cs` — is hashed
over a **normalised** form, not over its bytes. Everything else in the surface (the spec authority,
the scenario catalogs, the conformance fixtures) is still hashed raw: every byte of those was
written to be read, so there is no noise to remove.

For the React lane the normalisation is done with the TypeScript compiler that is already a
dependency of this repository, and it removes exactly three things:

1. **comments and formatting**, because neither reaches a render;
2. **top-level import order**, because it does not reach a render;
3. **identifiers that cannot escape the file**, replaced by their first-appearance position.

Point 3 is the one that needs the honest statement. "Cannot escape" is decided from the syntax tree,
not from a naming convention: an identifier is kept verbatim when it is imported, exported, or used
as a member, property, prop or attribute name. The rendered markup itself is not normalised at all —
the parser marks the span of every JSX element name and of every run of JSX text, and those spans go
into the digest verbatim (whitespace-collapsed), because an element name and the words between the
tags ARE the render. String and template literals are likewise kept, so an attribute value cannot
change unseen. What remains replaceable is a name that is only ever read inside its own file, and
that is exactly what a behaviour-neutral refactor moves. The guarantee this buys is narrower than
"every syntactic way a name reaches a consumer" and it is the one the tests hold: the tag rendered,
the text rendered, the attribute names and values, and every name a consumer can import or pass.

**A rename of an exported symbol or of a prop still expires the verdict, and should**: that is a change a consumer can see, and it is review-worthy.

CSS gets comments and whitespace only, because CSS has no local names — every selector and custom
property is reachable from a render.

### Why not the build output

The brief for this slice offered a build-output digest as the alternative, and this record had
already rejected it, for opacity: a single number over a generated artefact cannot name a file, the
artefact is not tracked, and its value depends on toolchain versions the design does not. Nothing
found in slice 3 disturbs that. The normalised digest keeps the property the whole decision is built
on — the refusal names the file — while buying the same stability under renames that minification
would have bought, and it costs 475 ms for all 102 modules rather than a bundle and a `dotnet build`
per module inside a check that runs in `run-ui-gate-model.mjs`.

The normalised digest is also, unlike a bundle, *reviewable*: it is a function in the repository,
read like any other code, which is the same argument that chose a declared surface over a computed
one.

### The ceiling, stated rather than hidden

**The Blazor lane gets comment-stripping and whitespace normalisation only.** Renaming a local
inside a `.razor` `@code` block or a `.cs` helper still expires the verdict — a false positive this
slice accepts rather than papers over, because the honest fix is a Roslyn parse and a regex
pretending to be one would be worse than the noise. Upgrade path: a `normaliseCSharp` backed by
Roslyn, in the .NET tooling, if the Blazor lane starts producing that noise. It has not yet: the five
modules ticket 138 names expire on real Blazor edits, and none of the neutral cases is Blazor code.

A second, smaller ceiling: the content of a template literal is preserved, so reindenting code that
is embedded in one moves the digest. That is deliberate — a template literal's content can be
rendered — and it is why the canary's neutral edit is comments only rather than a reformat.
