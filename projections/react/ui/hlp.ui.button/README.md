# @harborline-software/ui-react

This prerelease Platform extraction preserves the existing npm identity while aggregating the
proven application-essential UI waves through wave 03-03, including Guarded Control, Input, Layers
Rail, Notification Center, Page, and Search Input, plus the shared support surfaces. It is a
candidate artifact. The original capture retained package authority under the earlier repository
name pending aggregate UI compatibility; see ticket 063.

```tsx
import { Button } from '@harborline-software/ui-react'
import '@harborline-software/ui-react/style.css'
```

Data Grid columns require an integer `removalPriority`. As available width narrows, lower-priority
columns are removed first while retained columns preserve declaration order.

```tsx
const columns = [
  { id: 'structure', field: 'structure', header: 'Structure', removalPriority: 20 },
  { id: 'photos', field: 'photos', header: 'Photos', removalPriority: 10 },
]
```

The FormView declarations are a compatibility binding over the canonical
`@harborline-software/contracts/forms` types. TypeScript consumers that use FormView install the
matching `@harborline-software/contracts` candidate artifact; the UI package does not duplicate its
wire declarations.

Component-owned loading text uses the public `HarborlineLocaleProvider` catalog key
`common.loading`. The provider also propagates the selected `lang` and `dir` to Button without
coupling the package to an application translation library.

Hosts theme Button through inherited public CSS custom properties: `--hl-button-primary`,
`--hl-button-primary-hover`, `--hl-button-primary-active`, `--hl-button-primary-foreground`,
`--hl-button-secondary`, `--hl-button-secondary-hover`, `--hl-button-secondary-active`,
`--hl-button-secondary-foreground`, `--hl-button-border`, and `--hl-button-focus`. The packed
stylesheet consumes every token and owns the matching hover, active, focus-visible, inert,
forced-colors, and reduced-motion behavior.

Spotlight announces an empty result set. When no option and no loading section remain, the one
polite `role="status"` region, rendered whenever the palette is open, carries the caller's `empty`
copy in place of the `resultsCountLabel` text, so typing to zero results is announced once and
never beside a count (WCAG 2.2 SC 4.1.3 Status Messages). The combobox then reports
`aria-expanded="false"`: the APG combobox pattern sets `aria-expanded` to false when the popup is
not visible and true when it is, and its autocomplete example calls `close()` when filtering finds
nothing. The empty listbox has no content and its stylesheet collapses it, so it is not a visible
popup. It stays in the DOM, and `aria-controls` keeps pointing at it, which the pattern allows for
an element that is not visible. A loading section is displayed content, so the popup stays
expanded while one is present.
