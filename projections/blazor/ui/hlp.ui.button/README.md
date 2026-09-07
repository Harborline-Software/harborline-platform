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
