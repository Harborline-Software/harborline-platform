# Harborline.UIAdapters.Blazor

This prerelease Platform extraction preserves the existing NuGet package identity and the
`Harborline.UIAdapters.Blazor` assembly identity while aggregating the proven application-essential UI
waves through wave 03-03, including Guarded Control, Input, Layers Rail, Notification Center, Page,
and Search Input. The original capture retained package authority under the earlier repository name pending aggregate UI compatibility; see ticket 063.

Wrap Button content in the public `HarborlineLocaleProvider` to supply the host locale, text direction,
and the `common.loading` catalog value. The provider is packaged in the
`Harborline.UIAdapters.Blazor.Localization` namespace and does not impose a resource-file format on
the consuming application.

Hosts theme Button through inherited public CSS custom properties: `--hl-button-primary`,
`--hl-button-primary-hover`, `--hl-button-primary-active`, `--hl-button-primary-foreground`,
`--hl-button-secondary`, `--hl-button-secondary-hover`, `--hl-button-secondary-active`,
`--hl-button-secondary-foreground`, `--hl-button-border`, and `--hl-button-focus`. The packed static
web asset consumes every token and owns the matching hover, active, focus-visible, inert,
forced-colors, and reduced-motion behavior.

Error Card exposes page/default/compact alert presentation with an optional caller-owned retry
action and localized label. Loading State exposes page/inline polite status presentation; it
intentionally adds neither an action nor a spinner contract.

Spotlight announces an empty result set. When no option and no loading section remain, the one
polite `role="status"` region, rendered whenever the palette is open, carries the caller's `Empty`
copy in place of the `ResultsCountLabel` text, so typing to zero results is announced once and
never beside a count (WCAG 2.2 SC 4.1.3 Status Messages). The combobox then reports
`aria-expanded="false"`: the APG combobox pattern sets `aria-expanded` to false when the popup is
not visible and true when it is, and its autocomplete example calls `close()` when filtering finds
nothing. The empty listbox has no content and its stylesheet collapses it, so it is not a visible
popup. It stays in the DOM, and `aria-controls` keeps pointing at it, which the pattern allows for
an element that is not visible. A loading section is displayed content, so the popup stays
expanded while one is present.
