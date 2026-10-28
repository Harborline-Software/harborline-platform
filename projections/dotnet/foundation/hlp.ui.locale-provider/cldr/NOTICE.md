# Unicode CLDR plural rules

`plurals.json` in this directory is Unicode CLDR data, copied byte-for-byte and unmodified.

| Field | Value |
| --- | --- |
| Source package | `cldr-core` 47.0.0 (npm), file `supplemental/plurals.json` |
| CLDR release | 47 (the file's own `supplemental.version._cldrVersion`; Unicode 16.0.0) |
| SHA-256 | `83f10932abb4ce2c4681725c6d11e4c01097a758830677127142c80c79bd6322` |
| Licence | Unicode License v3 (`SPDX-License-Identifier: Unicode-3.0`), full text in `LICENSE` beside this file |
| Copyright | Copyright © 2004-2025 Unicode, Inc. |

The file holds the `plurals-type-cardinal` rules only. `Harborline.Foundation` embeds it as the
resource `Harborline.Foundation.Localization.cldr.plurals.json` and evaluates it in
`CldrPluralRules.cs` per UTS #35 Part 3, "Language Plural Rules".

The Unicode License v3 notice accompanies every copy of the data:

- inside `Harborline.Foundation.dll`, as the resources
  `Harborline.Foundation.Localization.cldr.LICENSE` and
  `Harborline.Foundation.Localization.cldr.NOTICE.md`, so it travels even when only the DLL ships;
- inside the NuGet package, under `third-party/unicode-cldr/`.

If the embedded data resource is missing, plural selection fails closed: it throws
`InvalidOperationException` instead of guessing a category.

To move to a later CLDR release, replace `plurals.json` with the new release's file unchanged,
update the version and SHA-256 above, regenerate
`conformance/hlp.ui.locale-provider/plural-crosscheck.json`, and review the allow-list.
