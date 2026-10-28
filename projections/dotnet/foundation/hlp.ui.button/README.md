# Harborline.Foundation compatibility and UI-support projection

This prerelease package preserves the existing `Harborline.Foundation` package and
`Harborline.Foundation` assembly identities. It contributes the Button vocabulary plus the
framework-neutral Aspect Lens, class composer, exact default-string catalog, and FormView UI
binding. FormView references canonical `Harborline.Contracts` Forms types and does not establish a
second wire authority. The package remains a bounded internal-only projection and is not yet
authoritative for the full legacy Foundation package.

## Third-party data

Locale plural categories come from Unicode CLDR 47 cardinal plural rules (`cldr-core` 47.0.0,
`supplemental/plurals.json`), embedded unmodified under the Unicode License v3. The licence and
notice are embedded in the assembly beside the data and packed under `third-party/unicode-cldr/`.
If the embedded data is missing, plural selection throws `InvalidOperationException` rather than
guessing.
