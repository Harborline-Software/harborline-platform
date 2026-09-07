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
