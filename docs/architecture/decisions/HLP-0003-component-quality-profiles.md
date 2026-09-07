# HLP-0003: Mandatory component quality profiles

Status: accepted

Every catalog module declares a presentation disposition. A visual module must declare a neutral
quality profile alongside its semantic interface; a nonvisual module must record a bounded explicit
not-applicable rationale. Every interactive or non-interactive component module declares a quality
profile regardless of presentation disposition. Accessibility, internationalization, and theming
are mandatory dimensions. A profile may mark a dimension or individual applicability point not
applicable only with a bounded rationale that repository validation can inspect.

The profile is projection-neutral and names the required behaviors and fixtures. Framework-native
tests, packed-package consumers, private galleries, and clean-clone gates project that contract into
each supported framework. Galleries consume only public package interfaces, so they verify the same
surface available to downstream applications rather than implementation internals.

Component stages are contract, projection-native, package-consumer, gallery, and clean-clone. Each
stage must cover all three quality dimensions. Locale-sensitive formatting remains conditional on the
component owning formatted dates, numbers, currencies, lists, or relative time; omitting it requires
an explicit rationale in the quality profile.

Visual profiles define separate neutral light and dark fixtures. Both fixtures resolve public
design tokens through packed package surfaces and must cover default, hover, active, focus-visible,
disabled, loading, icon, border, text, and background states. The gallery gate verifies WCAG
contrast, runtime theme switching without remounting, 200% reflow, reduced motion, and
cross-projection visual parity in each fixture. Forced-colors remains an independent accessibility
profile and is not treated as a third theme.
