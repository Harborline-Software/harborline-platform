// Every module-prefixed class a projection emits must be a selector the module's authority
// stylesheet defines. The breadcrumb Blazor lane emitted hl-breadcrumb__current for a year while
// the authority only knew hl-breadcrumb__text--current; the lane's hand-written stylesheet hid it
// and the whole-scene visual parity probe could not see one span's typography. This check sees it
// at the source, in both lanes, before any stylesheet or screenshot is involved.
import {existsSync, readFileSync, readdirSync, statSync} from 'node:fs'
import {relative, resolve} from 'node:path'

const sourceExtensions = new Set(['.tsx', '.ts', '.razor', '.cs'])
const skippedDirectories = new Set(['node_modules', 'bin', 'obj', 'dist', '__tests__'])

function sourceFiles(directory) {
  if (!existsSync(directory)) return []
  return readdirSync(directory, {withFileTypes: true}).flatMap(entry => {
    const path = resolve(directory, entry.name)
    if (entry.isDirectory()) return skippedDirectories.has(entry.name) ? [] : sourceFiles(path)
    return [...sourceExtensions].some(extension => entry.name.endsWith(extension)) && !/\.(test|stories)\./.test(entry.name) ? [path] : []
  })
}

export function authoritySelectors(css) {
  return new Set([...css.matchAll(/\.(hl-[a-z0-9]+(?:-[a-z0-9]+)*(?:__[a-z0-9]+(?:-[a-z0-9]+)*)?(?:--[a-z0-9]+(?:-[a-z0-9]+)*)?)/g)].map(match => match[1]))
}

// A literal that ends in a BEM joiner (hl-input--size-, hl-badge__) is an interpolation prefix and
// is satisfied when some authority selector starts with it. A literal ending in a bare single dash
// (hl-accordion-${key}-header) is an element id being built, not a class, and is skipped. Anything
// else must match a selector exactly.
export function emittedClassLiterals(source, slug) {
  const literals = new Set()
  const pattern = new RegExp(`(?<![a-z0-9_-])(hl-${slug}(?:__[a-z0-9-]*|--[a-z0-9-]*|-[a-z0-9-]*)*)(?![a-z0-9])`, 'g')
  for (const match of source.matchAll(pattern)) if (!(/-$/.test(match[1]) && !/__|--/.test(match[1]))) literals.add(match[1])
  return literals
}

export function undefinedClasses(selectors, literals) {
  return [...literals].filter(literal => {
    if (/(?:__|--)(?:[a-z0-9-]*-)?$/.test(literal)) return ![...selectors].some(selector => selector.startsWith(literal))
    return !selectors.has(literal)
  }).sort()
}

// Returns {moduleId, projection, file, classes} rows for every source file emitting a class the
// authority does not define, after removing rows the baseline still tolerates. The baseline is
// keyed by "moduleId projection" and lists the tolerated class literals; a literal the source no
// longer emits is reported so the baseline burns down and never re-grows.
export function uiClassVocabularyFindings(root, baseline = {}) {
  const specRoot = resolve(root, 'specs/modules/ui')
  const findings = []
  const staleBaseline = []
  for (const moduleId of readdirSync(specRoot).filter(name => name.startsWith('hlp.ui.')).sort()) {
    const authorityPath = resolve(specRoot, moduleId, 'style.css')
    if (!statSync(authorityPath, {throwIfNoEntry: false})?.isFile()) continue
    const selectors = authoritySelectors(readFileSync(authorityPath, 'utf8'))
    const slug = moduleId.slice('hlp.ui.'.length)
    for (const projection of ['react', 'blazor']) {
      const key = `${moduleId} ${projection}`
      const tolerated = new Set(baseline[key] ?? [])
      const seen = new Set()
      for (const file of sourceFiles(resolve(root, `projections/${projection}/ui/${moduleId}`))) {
        const missing = undefinedClasses(selectors, emittedClassLiterals(readFileSync(file, 'utf8'), slug))
        for (const literal of missing) seen.add(literal)
        const reported = missing.filter(literal => !tolerated.has(literal))
        if (reported.length > 0) findings.push({moduleId, projection, file: relative(root, file).replaceAll('\\', '/'), classes: reported})
      }
      for (const literal of tolerated) if (!seen.has(literal)) staleBaseline.push({key, literal})
    }
  }
  return {findings, staleBaseline}
}

export function uiClassVocabularyErrors(root, baseline) {
  const {findings, staleBaseline} = uiClassVocabularyFindings(root, baseline)
  return [
    ...findings.map(finding => `${finding.moduleId}: ${finding.projection} emits ${finding.classes.join(', ')} in ${finding.file} but specs/modules/ui/${finding.moduleId}/style.css defines no such selector`),
    ...staleBaseline.map(entry => `ui-class-vocabulary baseline still tolerates ${entry.literal} for ${entry.key}, which no source emits; remove the row`),
  ]
}
