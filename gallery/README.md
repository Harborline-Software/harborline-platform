# Button galleries

These private applications present the same neutral `hlp.ui.button` scenario catalog through two
framework projections:

- React Storybook: `http://127.0.0.1:6106`
- Blazor Blazing Story: `http://127.0.0.1:6107`

Run `npm run gallery:react` or `npm run gallery:blazor` from the repository root to start one
gallery, and `npm run gallery` to start both. Each command first packs the candidate npm and NuGet
artifacts and installs or restores them into the gallery. The gallery sources do not import a
projection implementation directory, use a project reference, or depend on another Harborline
checkout.

The neutral catalog in `scenarios/hlp.ui.button.json` maps every gallery state back to the shared
Button conformance fixture. It covers defaults, variants, sizes, fill modes, radii, loading,
disabled, icons, accessibility, host attributes, right-to-left content, separate light and dark
theme fixtures, and English, pseudolocale, and Arabic localization states.

Run `npm run test:gallery` for the automated gallery gate. It requires exact discovery of all 16
scenarios, an Axe scan in both projections, and 22 rendered cross-projection comparisons with no
more than a 1.5% changed-pixel ratio. The comparison captures the shared gallery probe rather than
either tool's navigation shell. The same gate verifies public-catalog translation, locale and
direction propagation, mixed-direction content, pseudolocale expansion, keyboard and focus-visible
behavior, forced colors, and light/dark token resolution, contrast, interaction states, runtime
switching, reduced motion, and 200% reflow. Forced colors is intentionally independent of the two
theme fixtures.

Both applications are verification surfaces only. The React manifest is private, the Blazor
project is non-packable, and neither is included in the public npm or NuGet artifacts. Storybook is
configured with its accessibility addon. Blazing Story is a private development dependency and is
not part of the Harborline package surface; its upstream project is
[jsakamoto/BlazingStory](https://github.com/jsakamoto/BlazingStory).
