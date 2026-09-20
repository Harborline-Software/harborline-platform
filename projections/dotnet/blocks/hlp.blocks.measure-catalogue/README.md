# Harborline.Blocks.MeasureCatalogue

One addressing and resolution path for every measure, of either kind (DES-0031).

This package owns **no math**. It owns four things and nothing else:

- **One address.** `MeasureRef` is the only reference type. Its text never says whether the entry
  behind it is a configurer-authored declaration or code written at bound depth, which is what lets
  one replace the other without breaking a held reference.
- **One method.** `IMeasureCatalogue.EvaluateAsync` resolves, admits and evaluates. A consumer has
  no kind-specific branch to write and no second path to take.
- **The input set.** Before any grouping, paging or fold, the caller's rows go through the
  production Access set filter promoted by T-625, bound at the caller's explicit tenant, principal
  and instant. The catalogue builds no access system of its own.
- **The narrowing rule.** A caller may narrow a measure's authored filter and may never widen it; a
  supplied filter that no longer carries the authored one as a conjunct is refused before rows are
  read.

The caller supplies the rows and the clock. The same address binds three ways — live rows at now, a
pinned basis at a period boundary, a draft record set at edit — and changing only those inputs
changes the result without creating a new measure.

## Where the math lives

- **Declared entries** (`DeclaredMeasureEntry`) hand the narrowed rows and the composed filter to
  the shipped `Harborline.Blocks.Aggregates` evaluator and return its typed cells unchanged.
- **Bound entries** are registered by whoever owns the code. `Harborline.Blocks.Reports` registers
  the seven report computations as entries here; Reports keeps layout, basis and issuance and no
  longer holds a second catalogue of its own (ADR 0075).

Cells carry value, null and unavailable as distinct states through both kinds, so an absent figure
is never rendered as a zero.
