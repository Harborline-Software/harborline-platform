#!/usr/bin/env node

// Ticket 098 deliberately separates this mechanical floor from design review. This scanner can
// prove that a focus ring is large enough to read, themeable, and detached from the control edge;
// whether that treatment is aesthetically coherent remains a human verdict.

import {existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {join, resolve} from 'node:path'

// The tool lives two levels below the platform root, so callers need no path in the usual case.
const defaultPlatformRoot = resolve(join(import.meta.dirname, '/../..'))

const TOKEN_COLOUR = /var\(\s*--hl-[a-z0-9-]+/i
const PIXEL_WIDTH = /(-?(?:\d+(?:\.\d+)?|\.\d+))px\b/i

// CSS selectors and declaration bodies are enough for this floor. Ignoring quoted braces keeps the
// small parser from mistaking generated content or data values for the end of a rule.
function focusVisibleBodies(css) {
  const source = css.replace(/\/\*[\s\S]*?\*\//g, '')
  const bodies = []
  const blocks = []
  let boundary = 0
  let quote = null
  let escaped = false

  for (let index = 0; index < source.length; index += 1) {
    const character = source[index]
    if (quote !== null) {
      if (escaped) escaped = false
      else if (character === '\\') escaped = true
      else if (character === quote) quote = null
      continue
    }
    if (character === '"' || character === "'") {
      quote = character
      continue
    }
    if (character === '{') {
      const selector = source.slice(boundary, index)
      // In forced-colors mode the user agent replaces author colours with system colours, and the
      // spec's own remedy is to name a system keyword: `outline-color: Highlight` is the CORRECT
      // thing to write there, and a Harborline token would be the wrong thing. Counting those
      // declarations made this gate fail forty-eight modules precisely BECAUSE they honour high
      // contrast properly -- a check punishing the accessible behaviour it exists to protect. The
      // flag is inherited, so a rule nested any depth below the at-rule is excluded too.
      const forcedColours = /forced-colors|prefers-contrast/i.test(selector)
        || blocks.some(block => block.forcedColours)
      blocks.push({
        isFocusVisible: selector.includes(':focus-visible'),
        forcedColours,
        bodyStart: index + 1,
      })
      boundary = index + 1
      continue
    }
    if (character === '}') {
      const block = blocks.pop()
      if (block?.isFocusVisible && !block.forcedColours) bodies.push(source.slice(block.bodyStart, index))
      boundary = index + 1
    }
  }

  return bodies
}

function outlineDeclarations(body) {
  const declarations = []
  const pattern = /(?:^|;)\s*(outline(?:-(?:width|color|offset))?)\s*:\s*([^;}]+)/gi
  for (const match of body.matchAll(pattern)) {
    declarations.push({property: match[1].toLowerCase(), value: match[2].trim()})
  }
  return declarations
}

export function analyze(css) {
  const bodies = focusVisibleBodies(css)
  if (bodies.length === 0) return {verdict: 'FAIL', findings: ['no :focus-visible rule']}

  const findings = []
  let hasOffset = false
  let hasColourDeclaration = false

  const addFinding = finding => {
    if (!findings.includes(finding)) findings.push(finding)
  }

  for (const body of bodies) {
    const declarations = outlineDeclarations(body)
    hasOffset ||= declarations.some(declaration => declaration.property === 'outline-offset')

    for (const declaration of declarations) {
      if (declaration.property !== 'outline' && declaration.property !== 'outline-width') continue
      const match = declaration.value.match(PIXEL_WIDTH)
      if (match === null) continue
      const width = Number.parseFloat(match[1])
      if (width < 2) addFinding(`${width}px ring reads as a border`)
    }

    // The last shorthand or colour declaration in a rule controls its ring colour. Width and offset
    // declarations do not reset colour, so they are intentionally absent from this check.
    const colourDeclarations = declarations.filter(
      declaration => declaration.property === 'outline' || declaration.property === 'outline-color',
    )
    if (colourDeclarations.length > 0) {
      hasColourDeclaration = true
      const effectiveColour = colourDeclarations.at(-1).value
      if (!TOKEN_COLOUR.test(effectiveColour)) addFinding('ring colour is a literal, not a token')
    }
  }

  // With no colour declaration the browser falls back to currentColor, which still does not resolve
  // through a Harborline token and therefore cannot establish the required theme contract.
  if (!hasColourDeclaration) addFinding('ring colour is a literal, not a token')
  if (!hasOffset) addFinding('no outline-offset; the ring sits on the control edge')

  return {verdict: findings.length > 0 ? 'FAIL' : 'PASS', findings}
}

export function runScan(root) {
  const specsDir = resolve(root, 'specs/modules/ui')
  if (!existsSync(specsDir)) throw new Error(`no UI specs under ${specsDir}`)

  const modules = []
  // The prefix excludes support directories and is also the mutation point used by the repository's
  // canary verifier. If discovery breaks, that verifier must be able to prove this gate can fail.
  for (const moduleId of readdirSync(specsDir).filter(id => id.startsWith('hlp.ui.')).sort()) {
    const stylePath = resolve(specsDir, moduleId, 'style.css')
    if (!existsSync(stylePath)) continue
    modules.push({moduleId, ...analyze(readFileSync(stylePath, 'utf8'))})
  }

  const fail = modules.filter(module => module.verdict === 'FAIL').length
  return {modules, counts: {pass: modules.length - fail, fail}}
}

// ---- canary -----------------------------------------------------------------------------------
// These cases call the exported analyzer directly because a private canary implementation could
// keep passing after the production rule changed or disappeared.
function canary() {
  const cases = [
    {
      name: 'missing focus-visible',
      css: '.hl-x{color:red}',
      verdict: 'FAIL',
      findings: ['no :focus-visible rule'],
    },
    {
      name: 'thin ring without offset',
      css: '.hl-x:focus-visible{outline:1px solid var(--hl-focus)}',
      verdict: 'FAIL',
      findings: ['1px ring reads as a border', 'no outline-offset; the ring sits on the control edge'],
    },
    {
      name: 'literal ring colour',
      css: '.hl-x:focus-visible{outline:2px solid #2563eb;outline-offset:2px}',
      verdict: 'FAIL',
      findings: ['ring colour is a literal, not a token'],
    },
    {
      name: 'token ring with offset',
      css: '.hl-x:focus-visible{outline:2px solid var(--hl-focus);outline-offset:2px}',
      verdict: 'PASS',
      findings: [],
    },
  ]
  const failures = []
  const root = resolve(tmpdir(), `hlp-focus-quality-canary-${process.pid}`)

  try {
    for (const [index, testCase] of cases.entries()) {
      const result = analyze(testCase.css)
      if (result.verdict !== testCase.verdict) {
        failures.push(`${testCase.name}: expected ${testCase.verdict}, got ${result.verdict}`)
      }
      if (JSON.stringify(result.findings) !== JSON.stringify(testCase.findings)) {
        failures.push(`${testCase.name}: expected ${JSON.stringify(testCase.findings)}, got ${JSON.stringify(result.findings)}`)
      }

      const moduleId = `hlp.ui.canary-${index + 1}`
      mkdirSync(resolve(root, `specs/modules/ui/${moduleId}`), {recursive: true})
      writeFileSync(resolve(root, `specs/modules/ui/${moduleId}/style.css`), testCase.css)
    }

    const report = runScan(root)
    for (const [index, testCase] of cases.entries()) {
      const moduleId = `hlp.ui.canary-${index + 1}`
      const result = report.modules.find(module => module.moduleId === moduleId)
      if (result === undefined) failures.push(`${testCase.name}: ${moduleId} was not discovered`)
      else if (result.verdict !== testCase.verdict) {
        failures.push(`${testCase.name}: sweep expected ${testCase.verdict}, got ${result.verdict}`)
      }
    }
  } catch (error) {
    failures.push(`canary threw: ${error.message}`)
  } finally {
    try { rmSync(root, {recursive: true, force: true}) } catch {}
  }

  if (failures.length > 0) {
    process.stderr.write(`canary FAIL:\n${failures.map(failure => `  ${failure}`).join('\n')}\n`)
    process.exit(1)
  }

  process.stdout.write('canary OK -- visible token ring floor distinguishes missing, thin, literal-colour, and conforming focus styles\n')
  process.exit(0)
}

if (process.argv.slice(2).includes('--canary')) canary()

const argv = process.argv.slice(2)
const root = argv.find(argument => !argument.startsWith('--')) ?? defaultPlatformRoot
const report = runScan(resolve(root))

if (argv.includes('--json')) {
  process.stdout.write(`${JSON.stringify(report, null, 2)}\n`)
} else {
  process.stdout.write(`Focus quality: ${report.modules.length} modules checked\n`)
  process.stdout.write(`PASS  ${report.counts.pass}\n`)
  process.stdout.write(`FAIL  ${report.counts.fail}\n`)
  for (const module of report.modules.filter(module => module.verdict === 'FAIL')) {
    process.stdout.write(`\n${module.moduleId} (${module.findings.length})\n`)
    for (const finding of module.findings) process.stdout.write(`  - ${finding}\n`)
  }
}

process.exitCode = report.counts.fail > 0 ? 1 : 0
