# Engineering reuse

Read this for nontrivial implementation, review, or a capability/architecture investment. Routine spelling, formatting and equivalent maintenance edits need no research exercise. The author explains a routine classification; the reviewer assesses the actual scope. File count, extension and the presence of code do not classify a change.

Start with the relevant canonical implementation and its actual consumers. Find the module through `catalog/modules/<moduleId>.json`, its public interface, tests and existing provenance as applicable; inspect where callers supply or duplicate the behavior. Search the existing Control [research register](https://github.com/Harborline-Software/harborline-control/blob/main/research/RESEARCH.md), owning design and resources before opening another study. Reuse a matching note and unchanged sources, recording their revision and limits.

Research only the remaining decision gap: primary industry standards first, then mature maintained open-source implementations and commercial alternatives. Read the relevant primary documentation, implementation or license at an identified version/date. Consider fit, compatibility and operating/maintenance cost at the scope of the task. A product name, search result or long link list is not evidence of current capability. Prefer the canonical implementation, a standard facility, an admitted maintained dependency or a minimal adapter before custom machinery. If custom code is still the smallest complete solution, give the concrete reason; existing dependency-admission and contract rules still apply.

Stop when the selected approach is justified for the actual task and its callers, with material gaps named. Do not turn a routine fix into a landscape audit. For major capability/architecture investment or milestone planning that depends on stale product assumptions, use the incremental [product-alternatives index](https://github.com/Harborline-Software/harborline-control/blob/main/research/R-0140-product-alternatives/note.md); refresh the relevant gap, preserving dated findings. Research informs decisions and does not govern or certify uniqueness.

## One compact Reuse note

For a substantial change, put this section in the existing PR body or review, not in a new tracked per-PR report:

```markdown
## Reuse note
- Scope: substantial
- Revision: <full current PR head SHA>
- Checked: [canonical implementation](relative/path), [consumer](relative/path), [existing research](https://example.invalid/relevant-note)
- Chosen: reuse the existing producer with one adapter.
- Custom: none; or the concrete gap that requires custom code.
```

Use real checked references in place of the illustrative paths/URL. The Checked line identifies repository and research/resources consulted; explain a genuine absence rather than invent a reference. Add primary/alternative sources only for the gap investigated. Keep the other lines concise. For a routine change, Scope may instead be `routine: <reason>`; retain Revision, with no research fields required. Reviewers challenge classification or the choice where warranted.

Update Revision when the PR head changes, and reassess the note against that candidate. For an uncommitted review, identify the exact source snapshot in the review until a PR head exists; a preflight head comparison cannot verify an uncommitted snapshot or a clean working tree.

## Advisory preflight

The existing `tooling/run-pr-preflight.mjs` reads the Reuse note from the GitHub PR event body. For a local PR/review body, pass `--reuse-note /path/to/body.md` to that same command. It compares the note with the event's current PR head, or local checkout HEAD outside a PR event. Markdown inline local links are repository-relative; `[label](path#fragment)` and `[label](<path with spaces>)` are supported. It checks path existence, not line/anchor correctness. Cross-repository references use normal repository URLs; no URL is fetched or validated.

Missing or incomplete applicable notes, unknown scope, broken local paths and stale/unavailable revisions produce advisories only. The existing preflight's required checks and exit behavior remain independent. A routine reason is human supplied, not automatically inferred from code. Mechanical feedback cannot judge correctness, novelty, research completeness, maintenance quality or the best design. Reviewers assess those questions from the cited source and actual candidate. This adds no workflow, required gate, global hook or report service, and does not change visual-design review or headless/base-policy classification.
