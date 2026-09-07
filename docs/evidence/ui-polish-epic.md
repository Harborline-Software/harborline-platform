# Platform UI polish epic

## Outcome

Bring every rendered Harborline UI component to production quality with intentional React and Blazor parity. Preserve public behavior, use shared visual and interaction conventions, expose meaningful states in both galleries, and verify the packed artifacts rather than source-linked builds.

## Definition of done for every ticket

- Audit hierarchy, typography, spacing, color, interaction states, accessibility, responsive behavior, theming, motion, localization, and edge cases.
- Implement equivalent outcomes in React and Blazor without requiring framework-internal symmetry.
- Represent applicable default, hover, active, focus-visible, disabled, loading, validation, empty, error, destructive, RTL, dark-theme, reduced-motion, forced-colors, and narrow-width states in both galleries.
- Pass component tests, package-consumer verification, accessibility checks, responsive inspection, and live side-by-side gallery review from freshly rebuilt npm and NuGet artifacts.
- Document any intentional projection difference; resolve every unintended difference.

## Ticket backlog

### 1. Feedback foundations — complete

Components: Alert, Badge, Chip, Spinner, Loading State, Empty State, Error Card, Toaster, Notification Center.

Alert is polished and verified. Complete status density, semantic color discipline, icon treatment, live-region behavior, action placement, long-copy reflow, and notification stacking for the remaining components.

Evidence: Alert retains its previously verified authored semantic icons and responsive content hierarchy. Blazor Badge now implements the same size padding, weight, semantic fills, radius options, and logical overlay positioning as React. Chip, Empty State, Error Card, Toaster, and Notification Center share the platform light/dark focus treatment; Chip uses semantic disabled surfaces instead of whole-control opacity. Toaster status markers, loading indicator, and close action now use authored SVGs in both projections, as does Notification Center item dismissal; reduced-motion behavior disables the loading animation. Spinner and Loading State were audited and retained because their motion and noninteractive status contracts were already aligned. Regression assertions cover glyph-free authored icons. Focused suites for the remaining components passed (95 React, 16 Blazor), npm and NuGet package-consumer verification passed, galleries were restored from fresh artifacts, and all 61 feedback gallery accessibility and visual-conformance scenarios passed. Live packed-gallery comparison confirmed equal Badge geometry, six paired Toaster markers with no glyph content, paired close/dismiss SVGs, and zero root/component overflow at 360px. No intentional projection differences remain.

### 2. Actions — complete

Components: Button, Icon Button, Data Export Button, Action Menu, Context Menu, User Menu.

Polished control geometry, semantic states, focus treatment, menu elevation, disabled presentation, authored icons, and trigger/menu alignment. React and Blazor packed galleries were verified side by side.

### 3. Text entry and field framing — complete

Components: Form Field, Input, Text Box, Text Area, Search Input.

Unify field height, border/radius, label-help-error rhythm, placeholders, clear/search affordances, read-only and disabled states, validation treatment, focus rings, long content, RTL, forced colors, and narrow-width reflow.

Evidence: paired React and Blazor source treatment completed; 51 focused React tests and 24 focused Blazor tests passed; npm and NuGet package-consumer verification passed; galleries were restored from fresh artifacts; 43 family gallery accessibility and visual-conformance scenarios passed. Live packed-gallery inspection confirmed equal control geometry and authored action icons, and the 360px React reflow had no horizontal overflow. No intentional projection differences were introduced.

### 4. Structured and numeric entry — complete

Components: Number Field, Numeric Text Box, Date Field, Date-Time Field, Select Field.

Polish parsing and formatting feedback, steppers and picker affordances, unit/suffix placement, invalid and partial values, locale formats, keyboard behavior, touch targets, and constrained-width layouts.

Evidence: paired control scales, focus treatment, disabled surfaces, Select menu elevation, and coarse-pointer numeric steppers completed; focused React and Blazor suites passed; npm and NuGet package-consumer verification passed; galleries were restored from fresh artifacts; 28 family gallery accessibility and visual-conformance scenarios passed. Live packed-gallery inspection found identical React/Blazor Numeric Text Box geometry (41.33px height, 8px radius, 40px desktop steppers), and the 360px appearance fixture had no horizontal overflow. Native date controls intentionally retain system-color fallbacks and no component motion per their quality contract.

### 5. Selection controls — complete

Components: Check Box, Radio Group, Switch, Switch Field, Segmented Control, Guarded Control.

Align selection indicators, label hit areas, group spacing, mixed/indeterminate and disabled states, destructive confirmations, keyboard navigation, RTL direction, and compact layouts.

Evidence: Check Box now uses the same authored control treatment in React and Blazor; Radio Group typography, descriptions, validation, and disabled states are aligned; Switch and Switch Field share updated focus and disabled tokens; Segmented Control uses the platform 32/40/48px scale and selected elevation; Guarded Control uses semantic disabled surfaces without opacity loss. Focused suites passed (57 React, 20 Blazor), npm and NuGet package-consumer verification passed, galleries were restored from fresh artifacts, and 38 family gallery accessibility and visual-conformance scenarios passed. Live packed-gallery inspection found identical checked-box geometry and rendering in both projections (16px, 3px radius, authored appearance), while the 360px segmented fixture had no horizontal overflow. No intentional projection differences remain in this family.

### 6. Navigation and application structure — complete

Components: App Layout, Page, Window, Side Nav, Breadcrumb, Layers Rail, Scroll Affordance.

Clarify application hierarchy, active location, responsive collapse, overflow, landmarks, skip/focus behavior, dense navigation, mobile transitions, and shell elevation.

Evidence: App Layout and Window now use authored SVG controls instead of font glyphs; Page spacing and action reflow are consistent at compact widths; Side Nav uses a logical-edge active marker, semantic disabled treatment, and the platform focus token; Breadcrumb uses an authored logical separator and aligned focus/type treatment; Layers Rail and Scroll Affordance were reviewed and retained where their paired contracts were already aligned. Focused suites passed (76 React, 27 Blazor), npm and NuGet package-consumer verification passed, galleries were restored from fresh artifacts, and all 50 family gallery accessibility and visual-conformance scenarios passed. Live packed-gallery inspection confirmed equivalent authored icon counts and component geometry in paired Breadcrumb, Side Nav, and Window stories. At 360px, every inspected gallery probe had zero horizontal overflow; Blazor's small body-width deltas were isolated to the Storybook host margin and did not affect the document root or component probes. No intentional component-level projection differences remain.

### 7. Overlays and disclosure — complete

Components: Dialog, Confirm Dialog, Sheet, Popover, Tooltip, Spotlight, Accordion, Collapsible.

Unify overlay layers, scrims, placement, dismissal, focus trapping/restoration, destructive emphasis, disclosure indicators, motion, viewport collision, reduced motion, and mobile presentation.

Evidence: Dialog, Sheet, Popover, and Spotlight share the platform violet focus token; Dialog and Sheet use the same 48% tokenized scrim; Sheet now preserves clear header space for its close action, provides intentional footer rhythm, and exposes close hover feedback; Accordion and Collapsible use semantic disabled surfaces and text instead of fading the whole control. Blazor Dialog's modal surface is now programmatically focusable, matching React's focus-trap fallback, with a regression assertion. Focused suites passed (134 React, 35 Blazor), npm and NuGet package-consumer verification passed, galleries were restored from fresh artifacts, and all 33 matching gallery accessibility and visual-conformance scenarios passed. Live packed-gallery review found exact paired disabled treatments, 48% overlays, focus tokens, 512px Dialog geometry, and `tabindex="-1"` fallback focus behavior. Every inspected React and Blazor probe had zero document-root or component overflow at 360px. No intentional projection differences remain.

### 8. Content and data display — complete

Components: Card, Separator, Table, Data Grid, Chart, Activity Log.

Improve information hierarchy, density, row and selection states, sorting/filtering affordances, empty/loading/error presentation, data legibility, responsive overflow, and accessible nonvisual equivalents.

Evidence: Card, Separator, and Table were audited and retained as-is because their paired token, hierarchy, density, logical-border, forced-color, and responsive-overflow contracts were already aligned. Data Grid now replaces font disclosure glyphs with the same authored SVG in both projections, exposes explicit collapsed state for styling, and uses the platform focus-token chain; Chart and Activity Log use the same light/dark focus fallbacks while preserving their accessible data and semantic-tone paths. Regression assertions cover the authored Data Grid icon contract. Focused suites passed (96 React, 19 Blazor), npm and NuGet package-consumer verification passed, galleries were restored from fresh artifacts, and all 41 family gallery accessibility and visual-conformance scenarios passed (the matching run also passed 11 Error Card scenarios tracked under feedback). Live packed-gallery comparison found exact paired Data Grid icon count, glyph-free content, collapsed state, and rotation; Chart and Activity Log focus tokens also matched. At 360px, all inspected React and Blazor probes had zero root/component overflow and retained local data scrollers. No intentional projection differences remain.

### 9. Complex workflows and composites — complete

Components: Schema Form, Scheduler, Gantt, Chat, Conversation List.

Apply the finished primitives to high-density workflows; reduce cognitive load, preserve context across responsive layouts, strengthen keyboard paths, and verify realistic loading, empty, error, conflict, and destructive flows.

Evidence: Schema Form uses the shared focus-token chain without bypassing forced-color behavior. Scheduler, Gantt, Chat, and Conversation List use aligned light/dark focus treatment; Chat disabled controls retain full opacity with paired semantic disabled surfaces and foregrounds. Conversation List rename, delete, and danger actions use the same authored SVG system in both projections with accessible names and no Unicode action glyphs. Gantt gallery fixtures now constrain the component to the available review stage instead of triggering circular intrinsic sizing; the component's 48rem schedule canvas is intentionally local-scrollable, bounded, named, keyboard-focusable even when empty, and visibly focused. Regression coverage includes the empty Gantt scroll viewport and authored action icons. Focused suites passed (106 React, 38 Blazor), fresh npm (`de097270935c55c1d13c494a26af8b13b9bef4e4d9b1eef16d466000959ef362`) and NuGet package-consumer verification passed, galleries were restored from those artifacts, and all 43 complex-family gallery accessibility and visual-conformance scenarios passed. Live packed-gallery comparison confirmed paired Conversation List SVG actions, paired Chat disabled styling, matching Scheduler/Gantt focus tokens, and zero page/probe overflow at 360px; Gantt retained an intentional, equal 768px internal schedule scroller in both projections. No intentional projection differences remain.

## Inventory boundary

The epic covers rendered components with paired React and Blazor gallery stories. Utility-only modules and hooks are verified through their consuming components rather than treated as visual polish tickets.

## Final platform audit

All nine family tickets are complete across the 57 paired rendered component catalogs. The authoritative native gate passed 2,996 tests: 1,072 React tests, 275 Blazor tests across 76 discovered classes, and 1,649 supporting TypeScript/.NET platform tests. Final package-only consumers passed from npm artifact `de097270935c55c1d13c494a26af8b13b9bef4e4d9b1eef16d466000959ef362` and NuGet aggregate artifact `aba99f1b84b798e35a397606181a906b4182361389c37e6661cd6c840e9eaaf1`; gallery preparation confirmed no source-linked dependencies. The final packed-gallery gate passed all 419 checks, covering exact scenario inventory, Axe accessibility, visual parity, keyboard focus, 200% reflow, RTL and pseudo-localization, light/dark runtime themes, forced colors, and reduced motion. The bounded live review confirmed zero page/probe overflow at 360px for the inspected complex surfaces and intentional local scrolling for schedule data. The one manual Impeccable detector pass identified two obsolete Scheduler side-accent declarations; both were removed, and the active design hook reported no deterministic issues on the corrected file. `git diff --check` passes. No unresolved or intentional React/Blazor visual differences remain.
