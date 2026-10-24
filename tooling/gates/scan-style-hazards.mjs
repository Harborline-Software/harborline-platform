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
const specRoot = resolve(root, 'specs/modules/ui')
const canary = process.argv.includes('--canary')

const registry = existsSync(resolve(root, 'catalog/ui-theme-registry.json'))
  ? readFileSync(resolve(root, 'catalog/ui-theme-registry.json'), 'utf8')
  : ''

// A system colour is only meaningful in forced-colors, where the UA supplies BOTH halves of a pair.
// Reached outside that context it is a coin toss: MarkText renders black, so a foreground that falls
// to it over a background that resolved to a real colour splits the pair and produces 3.19:1.
const SYSTEM_COLOURS = /\b(MarkText|Mark|CanvasText|Canvas|HighlightText|Highlight|ButtonText|ButtonFace|LinkText|GrayText|FieldText|Field|AccentColor|AccentColorText)\b/

const findings = []
const report = (rule, module, detail) => findings.push({rule, module, detail})

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
    for (const declaration of rule.body.matchAll(/(--hl-[a-z0-9-]+)\s*:\s*([^;]+)/g)) {
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
  return {rules, terminals}
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
        + 'which is not a text colour outside forced-colors')
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
//   - outline and outline-color, in any enabled selector (an outline is a focus indicator here);
//   - the colour of box-shadow, only in selectors that mention :focus (a shadow is decoration
//     elsewhere);
//   - background and background-color, only on the named indicator classes below, whose fill is
//     the indicator itself.
// Only a whole terminal counts, as in R4: color-mix(in srgb, Highlight 12%, Canvas) is a tint whose
// contrast depends on its partner, and is left to the rendered gallery. Forced-colors blocks and
// disabled selectors are exempt for R4's reasons.
const NON_TEXT_INDICATOR_FALLBACKS = /^(Highlight|SelectedItem|Mark|GrayText)$/i
const INDICATOR_CLASSES = /__(unread|dot|indicator|marker)\b/
const FOCUS_SELECTOR = /:focus/

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
  const {rules, terminals} = unthemedRules(css)
  for (const rule of rules) {
    const enabled = selectorList(rule.selector).filter(selector => !DISABLED_SELECTOR.test(selector))
    if (enabled.length === 0) continue
    for (const declaration of rule.body.matchAll(/(?:^|;)\s*(outline|outline-color|box-shadow|background|background-color)\s*:\s*([^;]+)/g)) {
      const [, property, value] = declaration
      let selectors = enabled
      if (property === 'box-shadow') selectors = enabled.filter(selector => FOCUS_SELECTOR.test(selector))
      if (property.startsWith('background')) selectors = enabled.filter(selector => INDICATOR_CLASSES.test(selector))
      if (selectors.length === 0) continue
      const reached = [...new Set(valueParts(value).flatMap(part => terminals(part, new Set()))
        .filter(terminal => NON_TEXT_INDICATOR_FALLBACKS.test(terminal)))]
      if (reached.length === 0) continue
      report('indicator-falls-to-low-contrast-system-colour', moduleId,
        `${selectors.join(', ').replace(/\s+/g, ' ')} { ${property}: ${value.trim()} } can fall back to ${reached.join(', ')}, `
        + 'which does not reach 3:1 non-text contrast on Canvas outside forced-colors')
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
  // And the exemptions must hold: these may NOT fire.
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

// Two modules are known to lack a Blazor stylesheet and are ticketed. They are listed rather than
// silenced, and the exemption is held to the same standard as the rule: if one of them GAINS a
// stylesheet the exemption is stale and this fails, because an exemption nobody revisits is how a
// scanner quietly stops describing the repository.
const ACKNOWLEDGED = new Map([
  // Same GrayText fallback the b19 batch fixed in dialog, sheet, spotlight and window, but latent here:
  // the page renders inside the themed scene and no scenario reaches the fallback. Changing
  // style.css re-pins the page's recorded design-review approval (design-surface-inputs), which is
  // the owner's call, not a tooling batch's. Fixing it makes this entry stale and the scan red.
  ['text-colour-falls-to-non-text-system-colour:hlp.ui.page', 'b19 follow-up: owner design re-review needed to change page style.css'],
  // R5 (b20) fixed every overlay module (notification-center, popover, window, confirm-dialog; dialog,
  // sheet, spotlight and user-menu were clean). These are real findings, not false positives, left
  // for a follow-up: in-page controls that render inside the host's themed subtree (no portal), so
  // the Highlight/Mark fallback is latent. page, schema-form and tooltip also carry a live
  // design-review approval that pins style.css, so changing them is the owner's call. Fixing any
  // one makes its entry stale and the scan red.
  ...[
    ['page', 'b20 follow-up: focus ring falls to Highlight; owner design re-review needed to change page style.css'],
    ['schema-form', 'b20 follow-up: focus ring falls to Highlight; owner design re-review needed to change schema-form style.css'],
    ['tooltip', 'b20 follow-up: trigger focus ring falls to Highlight; owner design re-review needed to change tooltip style.css'],
    ['date-field', 'b20 follow-up: focus ring falls to Highlight (in-page control, outside the overlay scope)'],
    ['date-time-field', 'b20 follow-up: focus ring falls to Highlight (in-page control, outside the overlay scope)'],
    ['gantt', 'b20 follow-up: focus rings fall to Highlight (in-page control, outside the overlay scope)'],
    ['layers-rail', 'b20 follow-up: focus ring falls to Highlight (in-page control, outside the overlay scope)'],
    ['radio-group', 'b20 follow-up: focus ring to Highlight, error outline to Mark (in-page control, outside the overlay scope)'],
    ['scroll-affordance', 'b20 follow-up: focus ring falls to Highlight (in-page control, outside the overlay scope)'],
  ].map(([module, reason]) => [`indicator-falls-to-low-contrast-system-colour:hlp.ui.${module}`, reason]),
])

const key = finding => `${finding.rule}:${finding.module}`
const live = findings.filter(finding => !ACKNOWLEDGED.has(key(finding)))
const acknowledged = findings.filter(finding => ACKNOWLEDGED.has(key(finding)))
const stale = [...ACKNOWLEDGED.keys()].filter(entry => !findings.some(finding => key(finding) === entry))

if (acknowledged.length > 0) {
  process.stdout.write(`acknowledged, tracked by ticket (${acknowledged.length})\n`)
  for (const finding of acknowledged) {
    process.stdout.write(`  ${finding.module.replace('hlp.ui.', '').padEnd(22)}${ACKNOWLEDGED.get(key(finding))}\n`)
  }
}
if (stale.length > 0) {
  process.stdout.write(`\nSTALE EXEMPTION -- the hazard is gone, remove the entry (${stale.length})\n`)
  for (const entry of stale) process.stdout.write(`  ${entry}\n`)
  process.exit(1)
}
if (live.length === 0) {
  process.stdout.write('\n0 unacknowledged style hazards\n')
  process.exit(0)
}
findings.length = 0
findings.push(...live)
const byRule = new Map()
for (const finding of findings) byRule.set(finding.rule, [...(byRule.get(finding.rule) ?? []), finding])
for (const [rule, group] of byRule) {
  process.stdout.write(`${rule} (${group.length})\n`)
  for (const finding of group) {
    process.stdout.write(`  ${finding.module.replace('hlp.ui.', '').padEnd(22)}${finding.detail}\n`)
  }
}
process.stdout.write(`\n${findings.length} style hazard(s)\n`)
process.exit(1)
