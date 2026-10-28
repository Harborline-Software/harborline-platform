#!/usr/bin/env node
// Three hazards that the gallery gate catches in seven minutes and a browser, and that are visible
// in the source in under a second. Every one of them cost a full gate cycle on 2026-08-26.
//
// This does NOT replace the gallery. Real axe coverage and real pixel comparison need a rendered
// page. It catches shapes we have already been bitten by, so the twenty-minute run stops being the
// thing that discovers them.
import {existsSync, readFileSync, readdirSync} from 'node:fs'
import {resolve} from 'node:path'

const root = resolve(`${import.meta.dirname}/../..`)
// --spec-root <dir> scans a copy of specs/modules/ui instead (the tests seed regressions there
// without touching the repository).
const specRootArgument = process.argv.indexOf('--spec-root')
const specRoot = specRootArgument > 0 ? resolve(process.argv[specRootArgument + 1]) : resolve(root, 'specs/modules/ui')
const canary = process.argv.includes('--canary')

const registry = existsSync(resolve(root, 'catalog/ui-theme-registry.json'))
  ? readFileSync(resolve(root, 'catalog/ui-theme-registry.json'), 'utf8')
  : ''

// A system colour is only meaningful in forced-colors, where the UA supplies BOTH halves of a pair.
// Reached outside that context it is a coin toss: MarkText renders black, so a foreground that falls
// to it over a background that resolved to a real colour splits the pair and produces 3.19:1.
const SYSTEM_COLOURS = /\b(MarkText|Mark|CanvasText|Canvas|HighlightText|Highlight|ButtonText|ButtonFace|LinkText|GrayText|FieldText|Field|AccentColor|AccentColorText)\b/

const findings = []
// `subject` is what an acknowledgement names: the selector list and property for the declaration
// rules (R4, R5), the detail itself for the others. One finding per declaration.
const report = (rule, module, detail, subject = detail) => findings.push({rule, module, detail, subject})

// ---------------------------------------------------------------------------
// R1 — a foreground whose token is absent from the registry, over a background whose token the
// module redefines per theme. A single literal fallback cannot serve both themes: white reads
// 6.57:1 on the light danger colour and 2.28:1 on the dark one.
function scanForegroundFallbacks(moduleId, css) {
  const darkBlocks = css.match(/\[data-theme=['"]dark['"]\][^{]*\{[^}]*\}/g) ?? []
  const redefinedInDark = new Set()
  for (const block of darkBlocks) {
    for (const match of block.matchAll(/(--hl-[a-z0-9-]+)\s*:/g)) redefinedInDark.add(match[1])
  }

  for (const rule of css.matchAll(/\{[^}]*\}/g)) {
    const body = rule[0]
    const colour = body.match(/(?:^|[;{])\s*color\s*:\s*var\((--hl-[a-z0-9-]+)\s*,\s*([^)]*)\)/)
    if (!colour) continue
    const [, token, fallback] = colour
    if (registry.includes(token.slice(2))) continue          // the token exists; it will resolve
    const background = body.match(/background(?:-color)?\s*:\s*var\((--hl-[a-z0-9-]+)/)
    if (!background) continue

    if (SYSTEM_COLOURS.test(fallback)) {
      report('foreground-falls-to-system-colour', moduleId,
        `color falls back to ${fallback.trim()} over ${background[1]}; a system colour only pairs `
        + 'correctly inside forced-colors')
      continue
    }
    // A literal fallback is fine only if the background it sits on cannot change under the dark
    // theme, or the module overrides the foreground there too.
    if (redefinedInDark.has(background[1]) && !/color\s*:/.test(darkBlocks.join(''))) {
      report('single-fallback-across-themes', moduleId,
        `color falls back to ${fallback.trim()} while ${background[1]} is redefined for the dark `
        + 'theme; one literal cannot satisfy both')
    }
  }
}

// ---------------------------------------------------------------------------
// R2 — a live region that also renders graphics. `role="status"` announces text; an svg inside it is
// something a reader cannot hear and a forced-colors user may not see, and the gallery asserts the
// region holds no button, no [role=progressbar] and no svg.
function scanLiveRegions(moduleId, sources) {
  for (const [file, text] of sources) {
    for (const match of text.matchAll(/role=["']status["']/g)) {
      const tail = text.slice(match.index, match.index + 1200)
      const closed = tail.search(/<\/(p|div|span|section)>/)
      const region = closed > 0 ? tail.slice(0, closed) : tail
      const offender = region.match(/<svg|<button|role=["']progressbar["']/)
      if (offender) {
        report('graphics-inside-live-region', moduleId,
          `${file} renders ${offender[0]} inside role="status"`)
      }
    }
  }
}

// ---------------------------------------------------------------------------
// R3 — the spec stylesheet is the authority, but sync pairs it only with React. A module whose
// Blazor lane carries none of its rules is styled in one lane and bare in the other, and nothing
// says so until visual parity fails. Ticket 140.
function scanLaneCoverage(moduleId, css, blazorSheets) {
  const prefix = css.match(/\.(hl-[a-z0-9-]+)/)?.[1]
  if (!prefix) return
  const blazorSheet = blazorSheets.find(sheet => sheet.text.includes(`.${prefix}`))
  if (blazorSheet) return
  const scoped = existsSync(resolve(root, `projections/blazor/ui/${moduleId}`))
    && readdirSync(resolve(root, `projections/blazor/ui/${moduleId}`), {recursive: true})
      .some(entry => String(entry).endsWith('.razor.css'))
  if (scoped) return
  report('no-blazor-stylesheet', moduleId, `no Blazor stylesheet defines .${prefix}`)
}

// ---------------------------------------------------------------------------
// R4 — a TEXT colour whose fallback chain ends in a system colour that is not a text colour: GrayText
// (CSS Color 4: disabled text, which is exempt from contrast and reads 3.94:1 on Canvas), or a
// BACKGROUND colour (Highlight reads 1.49:1 as text on Canvas, Mark 1.07:1). Unlike R1 this follows
// module-local custom properties (`color: var(--hl-x-muted)` where `--hl-x-muted:
// var(--hl-muted-foreground, GrayText)`) and does NOT trust a registry token to resolve: a layer the
// module portals to document.body leaves whatever subtree the host scoped its tokens to -- the gallery
// scopes them to the scene, and the portaled overlay scan (b19) found dialog, sheet, spotlight and
// notification-center text rendering in exactly these fallbacks. Canvas is not listed: it is the text
// half of an inverted CanvasText pair (tooltips, filled buttons). Forced-colors blocks and disabled
// states are exempt -- there the UA supplies both halves, and disabled text owes no contrast.
// System colour keywords are ASCII case-insensitive (CSS Color 4), so `graytext` is GrayText.
const NON_TEXT_SYSTEM_COLOURS = /^(GrayText|Highlight|SelectedItem|Mark|ButtonFace|ButtonBorder|Field|AccentColor)$/i
const DISABLED_SELECTOR = /:disabled|\[disabled|aria-disabled|data-disabled|--disabled\b/

// A selector list split at its top-level commas (a comma inside :is()/:not() does not split it).
function selectorList(selector) {
  const parts = []
  let depth = 0
  let start = 0
  for (let cursor = 0; cursor < selector.length; cursor += 1) {
    const character = selector[cursor]
    if (character === '(' || character === '[') depth += 1
    else if (character === ')' || character === ']') depth -= 1
    else if (character === ',' && depth === 0) {
      parts.push(selector.slice(start, cursor).trim())
      start = cursor + 1
    }
  }
  parts.push(selector.slice(start).trim())
  return parts.filter(Boolean)
}

function withoutForcedColours(css) {
  let out = ''
  let index = 0
  // Any media query that mentions forced-colors: `@media (forced-colors: active)` and
  // `@media screen and (forced-colors: active)` alike.
  const opener = /@media[^{;]*forced-colors[^{;]*\{/g
  for (;;) {
    opener.lastIndex = index
    const hit = opener.exec(css)
    if (!hit) return out + css.slice(index)
    out += css.slice(index, hit.index)
    let depth = 1
    let cursor = hit.index + hit[0].length
    while (cursor < css.length && depth > 0) {
      if (css[cursor] === '{') depth += 1
      else if (css[cursor] === '}') depth -= 1
      cursor += 1
    }
    index = cursor
  }
}

function splitVar(value) {
  let depth = 0
  let comma = -1
  for (let cursor = 4; cursor < value.length; cursor += 1) {
    const character = value[cursor]
    if (character === '(') depth += 1
    else if (character === ',' && depth === 0 && comma < 0) comma = cursor
    else if (character === ')') {
      if (depth === 0) {
        return {name: value.slice(4, comma < 0 ? cursor : comma).trim(), fallback: comma < 0 ? undefined : value.slice(comma + 1, cursor).trim()}
      }
      depth -= 1
    }
  }
  return {name: value.slice(4).trim(), fallback: undefined}
}

// The module's rules outside forced-colors, and a resolver for every value a declaration can end at
// when no host token reaches it (following module-local custom properties, trusting no host token).
function unthemedRules(css) {
  const source = withoutForcedColours(css).replace(/\/\*[\s\S]*?\*\//g, '')
  const rules = [...source.matchAll(/([^{}]+)\{([^{}]*)\}/g)].map(match => ({selector: match[1].trim(), body: match[2]}))
  const local = new Map()
  for (const rule of rules) {
    // Any module-local custom property, not only --hl-*: a private --x-ring is followed the same way.
    for (const declaration of rule.body.matchAll(/(--[a-z0-9-]+)\s*:\s*([^;]+)/gi)) {
      local.set(declaration[1], [...(local.get(declaration[1]) ?? []), declaration[2].trim()])
    }
  }
  const terminals = (value, seen) => {
    const bare = value.replace(/!important/, '').trim()
    if (!bare.startsWith('var(')) return [bare]
    const {name, fallback} = splitVar(bare)
    if (local.has(name) && !seen.has(name)) return local.get(name).flatMap(definition => terminals(definition, new Set([...seen, name])))
    return fallback === undefined ? [] : terminals(fallback, seen)
  }
  // R5: like terminals, but a composite value (`0 0 0 2px var(--hl-focus, Highlight)`, reached
  // directly or through a local token such as `box-shadow: var(--x-ring)`) is split into its parts
  // again at every step, so a colour inside it is reached however deeply it is composed.
  const partTerminals = (value, seen) => valueParts(value).flatMap(part => {
    if (!part.startsWith('var(')) return [part]
    const {name, fallback} = splitVar(part)
    if (local.has(name) && !seen.has(name)) return local.get(name).flatMap(definition => partTerminals(definition, new Set([...seen, name])))
    return fallback === undefined ? [] : partTerminals(fallback, seen)
  })
  return {rules, terminals, partTerminals}
}

function scanTextColourFallbacks(moduleId, css) {
  const {rules, terminals} = unthemedRules(css)
  for (const rule of rules) {
    // The exemption is per selector: in `.x:disabled, .x__hint { ... }` the hint still owes contrast.
    const enabled = selectorList(rule.selector).filter(selector => !DISABLED_SELECTOR.test(selector))
    if (enabled.length === 0) continue
    for (const declaration of rule.body.matchAll(/(?:^|;)\s*color\s*:\s*([^;]+)/g)) {
      const reached = [...new Set(terminals(declaration[1], new Set()).filter(value => NON_TEXT_SYSTEM_COLOURS.test(value)))]
      if (reached.length === 0) continue
      report('text-colour-falls-to-non-text-system-colour', moduleId,
        `${enabled.join(', ').replace(/\s+/g, ' ')} { color: ${declaration[1].trim()} } can fall back to ${reached.join(', ')}, `
        + 'which is not a text colour outside forced-colors',
        `${enabled.join(', ').replace(/\s+/g, ' ')} { color }`)
    }
  }
}

// ---------------------------------------------------------------------------
// R5 — R4's sibling for NON-TEXT indicators (WCAG 2.2 SC 1.4.11 Non-text Contrast, 3:1). A focus
// ring or a meaningful indicator whose fallback chain ends in Highlight (about 1.5:1 on Canvas),
// SelectedItem (its forced-colors twin), Mark (1.07:1) or GrayText (the UA's disabled colour, which
// signals "unavailable" and is not specified to reach any contrast) is invisible or misleading when
// no host token reaches it, which is exactly the portaled-overlay case R4 describes. b20 found the
// notification-center unread dot (role="img") and its focus ring on Highlight. Narrow by design:
//   - outline, outline-color and accent-color, in any enabled selector (an outline is a focus
//     indicator here; accent-color paints a checkbox, radio or range as its state indicator);
//   - the colour of box-shadow, border and border-*, only in focus selectors (:focus, :focus-visible,
//     :focus-within, [data-focus-visible], [data-focused]); elsewhere they are decoration;
//   - background and background-color, only on the named indicator classes below, whose fill is
//     the indicator itself (a badge's fill carries its count as much as its text does).
// A composite value is split into its parts at every step, including through module-local tokens
// (`--x-ring: 0 0 0 2px var(--hl-focus, Highlight)` used as `box-shadow: var(--x-ring)`). Only a
// whole part counts, as in R4: color-mix(in srgb, Highlight 12%, Canvas) is a tint whose contrast
// depends on its partner, and is left to the rendered gallery. Forced-colors blocks and disabled
// selectors are exempt for R4's reasons.
const NON_TEXT_INDICATOR_FALLBACKS = /^(Highlight|SelectedItem|Mark|GrayText)$/i
const INDICATOR_CLASSES = /__(unread|dot|indicator|marker|badge)\b/
const FOCUS_SELECTOR = /:focus|\[data-focus(?:-visible|ed)?\b/
const ANY_SELECTOR_PROPERTIES = /^(outline|outline-color|accent-color)$/
const FOCUS_ONLY_PROPERTIES = /^(box-shadow|border(?:-[a-z-]+)?)$/

// Every top-level var(...) in a declaration value, plus the bare words outside them.
function valueParts(value) {
  const parts = []
  let rest = ''
  for (let cursor = 0; cursor < value.length;) {
    if (value.startsWith('var(', cursor)) {
      let depth = 0
      let end = cursor
      for (; end < value.length; end += 1) {
        if (value[end] === '(') depth += 1
        else if (value[end] === ')' && --depth === 0) break
      }
      parts.push(value.slice(cursor, end + 1))
      rest += ' '
      cursor = end + 1
    } else {
      rest += value[cursor]
      cursor += 1
    }
  }
  return [...parts, ...rest.replace(/!important/, '').split(/[\s,]+/).filter(Boolean)]
}

function scanNonTextIndicatorFallbacks(moduleId, css) {
  const {rules, partTerminals} = unthemedRules(css)
  for (const rule of rules) {
    const enabled = selectorList(rule.selector).filter(selector => !DISABLED_SELECTOR.test(selector))
    if (enabled.length === 0) continue
    for (const declaration of rule.body.matchAll(/(?:^|;)\s*(outline(?:-color)?|accent-color|box-shadow|border(?:-[a-z-]+)?|background(?:-color)?)\s*:\s*([^;]+)/g)) {
      const [, property, value] = declaration
      let selectors
      if (ANY_SELECTOR_PROPERTIES.test(property)) selectors = enabled
      else if (FOCUS_ONLY_PROPERTIES.test(property)) selectors = enabled.filter(selector => FOCUS_SELECTOR.test(selector))
      else if (/^background(?:-color)?$/.test(property)) selectors = enabled.filter(selector => INDICATOR_CLASSES.test(selector))
      else continue
      if (selectors.length === 0) continue
      const reached = [...new Set(partTerminals(value, new Set()).filter(terminal => NON_TEXT_INDICATOR_FALLBACKS.test(terminal)))]
      if (reached.length === 0) continue
      const subject = `${selectors.join(', ').replace(/\s+/g, ' ')} { ${property} }`
      report('indicator-falls-to-low-contrast-system-colour', moduleId,
        `${selectors.join(', ').replace(/\s+/g, ' ')} { ${property}: ${value.trim()} } can fall back to ${reached.join(', ')}, `
        + 'which does not reach 3:1 non-text contrast on Canvas outside forced-colors', subject)
    }
  }
}

// ---------------------------------------------------------------------------
const blazorSheets = []
const blazorRoot = resolve(root, 'projections/blazor/ui')
for (const dir of existsSync(blazorRoot) ? readdirSync(blazorRoot) : []) {
  const wwwroot = resolve(blazorRoot, dir, 'wwwroot')
  if (!existsSync(wwwroot)) continue
  for (const file of readdirSync(wwwroot)) {
    if (file.endsWith('.css')) blazorSheets.push({file, text: readFileSync(resolve(wwwroot, file), 'utf8')})
  }
}

for (const moduleId of readdirSync(specRoot).filter(name => name.startsWith('hlp.ui.')).sort()) {
  const stylePath = resolve(specRoot, moduleId, 'style.css')
  if (!existsSync(stylePath)) continue
  const css = readFileSync(stylePath, 'utf8')
  scanForegroundFallbacks(moduleId, css)
  scanTextColourFallbacks(moduleId, css)
  scanNonTextIndicatorFallbacks(moduleId, css)
  scanLaneCoverage(moduleId, css, blazorSheets)

  const sources = []
  for (const [lane, directory] of [
    ['react', resolve(root, `projections/react/ui/${moduleId}/src`)],
    ['blazor', resolve(root, `projections/blazor/ui/${moduleId}`)],
  ]) {
    if (!existsSync(directory)) continue
    for (const entry of readdirSync(directory, {withFileTypes: true})) {
      if (entry.isFile() && /\.(tsx|razor)$/.test(entry.name)) {
        sources.push([`${lane}/${entry.name}`, readFileSync(resolve(directory, entry.name), 'utf8')])
      }
    }
  }
  scanLiveRegions(moduleId, sources)
}

// The canary. A scanner that cannot fail is the failure ticket 138 is about, so each rule is run
// against a fixture that must trip it. If any of these stops firing, the rule has gone dead.
if (canary) {
  const dead = []
  const probe = (name, fn) => { const before = findings.length; fn(); if (findings.length === before) dead.push(name) }
  const quiet = (name, fn) => { const before = findings.length; fn(); if (findings.length !== before) dead.push(`${name} (fired; must not)`) }
  probe('foreground-falls-to-system-colour', () => scanForegroundFallbacks('canary',
    '.x{background:var(--hl-danger,#b42318);color:var(--hl-not-a-real-token,MarkText)}'))
  probe('single-fallback-across-themes', () => scanForegroundFallbacks('canary',
    '.x{background:var(--hl-danger,#b42318);color:var(--hl-not-a-real-token,#fff)}'
    + "[data-theme='dark'] .x{--hl-danger:#ff8a80}"))
  probe('graphics-inside-live-region', () => scanLiveRegions('canary',
    [['canary.tsx', '<p role="status"><svg /><span>Loading</span></p>']]))
  probe('text-colour-falls-to-non-text-system-colour', () => scanTextColourFallbacks('canary',
    '.x{--hl-x-muted:var(--hl-muted-foreground,GrayText)}.x__copy{color:var(--hl-x-muted)}'))
  probe('text-colour-falls-to-non-text-system-colour (lower-case keyword)', () => scanTextColourFallbacks('canary',
    '.x__copy{color:var(--hl-muted-foreground,graytext)}'))
  probe('text-colour-falls-to-non-text-system-colour (SelectedItem, ButtonBorder)', () => scanTextColourFallbacks('canary',
    '.x__a{color:var(--hl-a,SelectedItem)}.x__b{color:var(--hl-b,ButtonBorder)}'))
  probe('text-colour-falls-to-non-text-system-colour (enabled selector beside a disabled one)', () => scanTextColourFallbacks('canary',
    '.x:disabled,.x__hint{color:var(--hl-muted-foreground,GrayText)}'))
  probe('indicator-falls-to-low-contrast-system-colour (focus outline via a local token)', () => scanNonTextIndicatorFallbacks('canary',
    '.x{--hl-x-focus:var(--hl-focus,Highlight)}.x button:focus-visible{outline:2px solid var(--hl-x-focus)}'))
  probe('indicator-falls-to-low-contrast-system-colour (outline-color, lower-case keyword)', () => scanNonTextIndicatorFallbacks('canary',
    '.x:focus-visible{outline-color:var(--hl-ring,highlight)}'))
  probe('indicator-falls-to-low-contrast-system-colour (focus box-shadow)', () => scanNonTextIndicatorFallbacks('canary',
    '.x:focus-visible{box-shadow:0 0 0 2px var(--hl-ring,Mark)}'))
  probe('indicator-falls-to-low-contrast-system-colour (indicator background)', () => scanNonTextIndicatorFallbacks('canary',
    '.x__unread{background:var(--hl-primary,Highlight)}'))
  // b20 review: composite tokens, border on focus, accent-color, data-attribute focus, badges.
  probe('indicator rule (composite ring through a local token)', () => scanNonTextIndicatorFallbacks('canary',
    '.x{--x-ring:0 0 0 2px var(--hl-focus,Highlight)}.x:focus-visible{box-shadow:var(--x-ring)}'))
  probe('indicator rule (composite outline through a local token)', () => scanNonTextIndicatorFallbacks('canary',
    '.x{--x-outline:2px solid var(--hl-focus,Highlight)}.x:focus-visible{outline:var(--x-outline)}'))
  probe('indicator rule (border-color on focus)', () => scanNonTextIndicatorFallbacks('canary',
    '.x:focus-visible{border-color:var(--hl-focus,Highlight)}'))
  probe('indicator rule (border shorthand on focus-within)', () => scanNonTextIndicatorFallbacks('canary',
    '.x:focus-within{border:1px solid var(--hl-focus,Mark)}'))
  probe('indicator rule (accent-color)', () => scanNonTextIndicatorFallbacks('canary',
    '.x__check{accent-color:var(--hl-primary,Highlight)}'))
  probe('indicator rule ([data-focus-visible] box-shadow)', () => scanNonTextIndicatorFallbacks('canary',
    '.x[data-focus-visible]{box-shadow:0 0 0 2px var(--hl-focus,Highlight)}'))
  probe('indicator rule (badge background)', () => scanNonTextIndicatorFallbacks('canary',
    '.x__badge{background:var(--hl-danger,Mark)}'))
  // And the exemptions must hold: these may NOT fire.
  quiet('indicator rule: border-color outside focus', () => scanNonTextIndicatorFallbacks('canary',
    '.x__tab[aria-selected="true"]{border-color:var(--hl-accent,Highlight)}'))
  quiet('indicator rule: forced-colors outline', () => scanNonTextIndicatorFallbacks('canary',
    '@media (forced-colors: active){.x:focus-visible{outline-color:Highlight}}'))
  quiet('indicator rule: box-shadow outside :focus', () => scanNonTextIndicatorFallbacks('canary',
    '.x{box-shadow:0 1px 2px var(--hl-shadow,Highlight)}'))
  quiet('indicator rule: background on a non-indicator class', () => scanNonTextIndicatorFallbacks('canary',
    '.x__tab[aria-selected="true"]{background:var(--hl-accent,Highlight)}'))
  quiet('indicator rule: CanvasText-based fallback', () => scanNonTextIndicatorFallbacks('canary',
    '.x:focus-visible{outline:2px solid var(--hl-focus,color-mix(in srgb,CanvasText 72%,Canvas))}.x__dot{background:var(--hl-primary,CanvasText)}'))
  quiet('forced-colors block under a media type', () => scanTextColourFallbacks('canary',
    '@media screen and (forced-colors: active){.x__copy{color:GrayText}}'))
  quiet('disabled-only selector list', () => scanTextColourFallbacks('canary',
    '.x:disabled,.x[aria-disabled="true"]{color:var(--hl-muted-foreground,GrayText)}'))
  probe('no-blazor-stylesheet', () => scanLaneCoverage('canary', '.hl-canary-nothing-defines-this{}', []))
  process.stdout.write(dead.length
    ? `canary FAILED: rules that did not fire: ${dead.join(', ')}\n`
    : 'canary OK -- every rule fired on a fixture built to trip it\n')
  process.exit(dead.length ? 1 : 0)
}

// Acknowledged findings, each named down to its selector list and property (the finding's
// `subject`), with the number of findings it covers (one unless stated). A new finding in an
// acknowledged module is NOT covered: it is live and fails the scan. An entry that covers nothing
// (the hazard was fixed, or a partial fix changed the selector) or a different number of findings
// than declared is stale and fails the scan too, because an exemption nobody revisits is how a
// scanner quietly stops describing the repository. `--list` prints every finding's key.
const OWNER_REVIEW = 'owner design re-review needed to change this style.css (live design-review approval pins it)'
const IN_PAGE = 'in-page control, outside the b20 overlay scope; renders inside the host themed subtree'
const R4 = 'text-colour-falls-to-non-text-system-colour'
const R5 = 'indicator-falls-to-low-contrast-system-colour'
const ACKNOWLEDGED = [
  // Same GrayText fallback the b19 batch fixed in dialog, sheet, spotlight and window, but latent here:
  // the page renders inside the themed scene and no scenario reaches the fallback.
  {rule: R4, module: 'hlp.ui.page', subject: '.hl-page__subtitle { color }', reason: `b19 follow-up: ${OWNER_REVIEW}`},
  // b20 review, deliberate: the notification-center badge's unthemed pair is CanvasText fill with
  // Canvas text (21:1 text, 21:1 against Canvas), which the UA keeps together. With the registry's
  // --hl-danger (#b42318 light, #ff8a80 dark) Canvas reads 6.6:1 and about 8:1 under the matching
  // color-scheme, where the #fff it replaces read 2.28:1 in dark. R1 cannot see that pairing.
  {rule: 'foreground-falls-to-system-colour', module: 'hlp.ui.notification-center', subject: 'color falls back to Canvas over --hl-danger; a system colour only pairs correctly inside forced-colors', reason: 'b20 review: deliberate CanvasText/Canvas badge pair (see comment)'},
  // R5 (b20) fixed every overlay module (notification-center, popover, window, confirm-dialog; dialog,
  // sheet, spotlight and user-menu were clean). These are real findings, not false positives, left
  // for a follow-up.
  {rule: R5, module: 'hlp.ui.page', subject: '.hl-page__actions>button:not([data-hl-intent]):focus-visible { outline }', reason: `b20 follow-up: focus ring to Highlight; ${OWNER_REVIEW}`},
  {rule: R5, module: 'hlp.ui.page', subject: '.hl-page__body:focus-visible { outline }', reason: `b20 follow-up: focus ring to Highlight; ${OWNER_REVIEW}`},
  {rule: R5, module: 'hlp.ui.schema-form', subject: ".hl-schema-form :is(fieldset, output, [role='alert']):focus-visible { outline }", reason: `b20 follow-up: focus ring to Highlight; ${OWNER_REVIEW}`},
  {rule: R5, module: 'hlp.ui.tooltip', subject: '.hl-tooltip__trigger > button:not([data-hl-intent]):focus-visible { outline }', reason: `b20 follow-up: trigger focus ring to Highlight; ${OWNER_REVIEW}`},
  {rule: R5, module: 'hlp.ui.date-field', subject: '.hl-date-field { accent-color }', reason: `b20 follow-up: picker accent to Highlight; ${IN_PAGE}`},
  {rule: R5, module: 'hlp.ui.date-field', subject: '.hl-date-field:focus-visible { border-color }', reason: `b20 follow-up: focus border to Highlight; ${IN_PAGE}`},
  {rule: R5, module: 'hlp.ui.date-field', subject: '.hl-date-field:focus-visible { outline }', reason: `b20 follow-up: focus ring to Highlight; ${IN_PAGE}`},
  {rule: R5, module: 'hlp.ui.date-time-field', subject: '.hl-date-time-field { accent-color }', reason: `b20 follow-up: picker accent to Highlight; ${IN_PAGE}`},
  {rule: R5, module: 'hlp.ui.date-time-field', subject: '.hl-date-time-field:focus-visible { border-color }', reason: `b20 follow-up: focus border to Highlight; ${IN_PAGE}`},
  {rule: R5, module: 'hlp.ui.date-time-field', subject: '.hl-date-time-field:focus-visible { outline }', reason: `b20 follow-up: focus ring to Highlight; ${IN_PAGE}`},
  {rule: R5, module: 'hlp.ui.gantt', subject: '.hl-gantt__scroll:focus-visible { outline }', reason: `b20 follow-up: focus ring to Highlight; ${IN_PAGE}`},
  {rule: R5, module: 'hlp.ui.gantt', subject: '.hl-gantt td:focus-visible, .hl-gantt__zoom select:focus-visible { outline }', reason: `b20 follow-up: focus ring to Highlight; ${IN_PAGE}`},
  {rule: R5, module: 'hlp.ui.layers-rail', subject: '.hl-layers-rail button:focus-visible { outline }', reason: `b20 follow-up: focus ring to Highlight; ${IN_PAGE}`},
  {rule: R5, module: 'hlp.ui.radio-group', subject: '.hl-radio-group__control { accent-color }', reason: `b20 follow-up: radio accent to Highlight; ${IN_PAGE}`},
  {rule: R5, module: 'hlp.ui.radio-group', subject: '.hl-radio-group__control:focus-visible { outline }', reason: `b20 follow-up: focus ring to Highlight; ${IN_PAGE}`},
  {rule: R5, module: 'hlp.ui.radio-group', subject: '.hl-radio-group--error .hl-radio-group__control { accent-color }', reason: `b20 follow-up: error accent to Mark; ${IN_PAGE}`},
  {rule: R5, module: 'hlp.ui.radio-group', subject: '.hl-radio-group--error .hl-radio-group__control { outline-color }', reason: `b20 follow-up: error outline to Mark; ${IN_PAGE}`},
  {rule: R5, module: 'hlp.ui.scroll-affordance', subject: '.hl-scroll-affordance:focus-visible { outline }', reason: `b20 follow-up: focus ring to Highlight; ${IN_PAGE}`},
]

if (process.argv.includes('--list')) {
  for (const finding of findings) process.stdout.write(`${JSON.stringify([finding.rule, finding.module, finding.subject])}\n`)
  process.exit(0)
}

const covers = (entry, finding) => entry.rule === finding.rule && entry.module === finding.module && entry.subject === finding.subject
const live = findings.filter(finding => !ACKNOWLEDGED.some(entry => covers(entry, finding)))
const acknowledged = findings.filter(finding => ACKNOWLEDGED.some(entry => covers(entry, finding)))
const stale = ACKNOWLEDGED
  .map(entry => ({entry, count: findings.filter(finding => covers(entry, finding)).length}))
  .filter(({entry, count}) => count !== (entry.count ?? 1))

if (acknowledged.length > 0) {
  process.stdout.write(`acknowledged, tracked by ticket (${acknowledged.length})\n`)
  for (const finding of acknowledged) {
    const entry = ACKNOWLEDGED.find(candidate => covers(candidate, finding))
    process.stdout.write(`  ${finding.module.replace('hlp.ui.', '').padEnd(22)}${finding.subject} -- ${entry.reason}\n`)
  }
}
// Both failures are reported before exiting, so a partial fix shows its stale entry AND what it left.
if (stale.length > 0) {
  process.stdout.write(`\nSTALE EXEMPTION -- the entry no longer matches exactly the findings it declares; remove or correct it (${stale.length})\n`)
  for (const {entry, count} of stale) {
    process.stdout.write(`  ${entry.rule}:${entry.module}:${entry.subject} (declares ${entry.count ?? 1}, found ${count})\n`)
  }
}
if (live.length === 0) {
  if (stale.length > 0) process.exit(1)
  process.stdout.write('\n0 unacknowledged style hazards\n')
  process.exit(0)
}
const byRule = new Map()
for (const finding of live) byRule.set(finding.rule, [...(byRule.get(finding.rule) ?? []), finding])
process.stdout.write('\n')
for (const [rule, group] of byRule) {
  process.stdout.write(`${rule} (${group.length})\n`)
  for (const finding of group) {
    process.stdout.write(`  ${finding.module.replace('hlp.ui.', '').padEnd(22)}${finding.detail}\n`)
  }
}
process.stdout.write(`\n${live.length} style hazard(s)\n`)
process.exit(1)
