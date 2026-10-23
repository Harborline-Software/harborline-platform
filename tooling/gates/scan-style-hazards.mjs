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
const NON_TEXT_SYSTEM_COLOURS = /^(GrayText|Highlight|Mark|ButtonFace|Field|AccentColor)$/
const DISABLED_SELECTOR = /:disabled|\[disabled|aria-disabled|data-disabled|--disabled\b/

function withoutForcedColours(css) {
  let out = ''
  let index = 0
  const opener = /@media\s*\([^)]*forced-colors[^)]*\)\s*\{/g
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

function scanTextColourFallbacks(moduleId, css) {
  const source = withoutForcedColours(css).replace(/\/\*[\s\S]*?\*\//g, '')
  const rules = [...source.matchAll(/([^{}]+)\{([^{}]*)\}/g)].map(match => ({selector: match[1].trim(), body: match[2]}))
  const local = new Map()
  for (const rule of rules) {
    for (const declaration of rule.body.matchAll(/(--hl-[a-z0-9-]+)\s*:\s*([^;]+)/g)) {
      local.set(declaration[1], [...(local.get(declaration[1]) ?? []), declaration[2].trim()])
    }
  }
  // Every value the declaration can end at when no host token reaches it.
  const terminals = (value, seen) => {
    const bare = value.replace(/!important/, '').trim()
    if (!bare.startsWith('var(')) return [bare]
    const {name, fallback} = splitVar(bare)
    if (local.has(name) && !seen.has(name)) return local.get(name).flatMap(definition => terminals(definition, new Set([...seen, name])))
    return fallback === undefined ? [] : terminals(fallback, seen)
  }
  for (const rule of rules) {
    if (DISABLED_SELECTOR.test(rule.selector)) continue
    for (const declaration of rule.body.matchAll(/(?:^|;)\s*color\s*:\s*([^;]+)/g)) {
      const reached = [...new Set(terminals(declaration[1], new Set()).filter(value => NON_TEXT_SYSTEM_COLOURS.test(value)))]
      if (reached.length === 0) continue
      report('text-colour-falls-to-non-text-system-colour', moduleId,
        `${rule.selector.replace(/\s+/g, ' ')} { color: ${declaration[1].trim()} } can fall back to ${reached.join(', ')}, `
        + 'which is not a text colour outside forced-colors')
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
  probe('foreground-falls-to-system-colour', () => scanForegroundFallbacks('canary',
    '.x{background:var(--hl-danger,#b42318);color:var(--hl-not-a-real-token,MarkText)}'))
  probe('single-fallback-across-themes', () => scanForegroundFallbacks('canary',
    '.x{background:var(--hl-danger,#b42318);color:var(--hl-not-a-real-token,#fff)}'
    + "[data-theme='dark'] .x{--hl-danger:#ff8a80}"))
  probe('graphics-inside-live-region', () => scanLiveRegions('canary',
    [['canary.tsx', '<p role="status"><svg /><span>Loading</span></p>']]))
  probe('text-colour-falls-to-non-text-system-colour', () => scanTextColourFallbacks('canary',
    '.x{--hl-x-muted:var(--hl-muted-foreground,GrayText)}.x__copy{color:var(--hl-x-muted)}'))
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
