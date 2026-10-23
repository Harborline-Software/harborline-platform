#!/usr/bin/env node
// Generates plural-crosscheck.json: the cardinal plural category that this Node's
// Intl.PluralRules (ICU) selects for every locale in the embedded CLDR data that Intl supports,
// plus the legacy codes iw, ji and in, over fixed lists of source numbers. The .NET suite compares
// its CLDR evaluator against this file offline (CldrPluralRulesTests); any disagreement must
// appear in plural-crosscheck-allowlist.json with the CLDR change behind it.
//
// Sections:
//   locales  - every number with its source scale visible (Intl minimum/maximumFractionDigits =
//              scale), compared with HarborlineLocaleScope.PluralCategory(count, scale).
//   defaults - the numbers whose source spelling has trailing fraction zeros, selected with a
//              plain Intl.PluralRules(locale).select(Number(source)) (a JavaScript number has no
//              scale), compared with the default HarborlineLocaleScope.PluralCategory(count).
//
// No compact-exponent section: Intl.PluralRules({notation: 'compact'}) in Node 24 (ICU 78.3)
// selects on the full value with e = 0 (fr 1.5 M is "other", although CLDR's own sample 1.1c6
// is "many"), so Intl cannot express the e operand. The CLDR c/e samples are checked instead by
// CldrPluralRulesTests.EverySampleEvaluatesToItsOwnCategory.
//
// Usage: node conformance/hlp.ui.locale-provider/plural-crosscheck.mjs [--check]
//   --check  regenerate in memory and fail if the committed file differs; the recorded Node,
//            ICU, and CLDR versions are informational and ignored by the comparison.

import {createHash} from 'node:crypto'
import {readFileSync, writeFileSync} from 'node:fs'
import {dirname, resolve} from 'node:path'
import {fileURLToPath} from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const root = resolve(here, '../..')
const dataPath = 'projections/dotnet/foundation/hlp.ui.locale-provider/cldr/plurals.json'
const outputPath = resolve(here, 'plural-crosscheck.json')

const dataBytes = readFileSync(resolve(root, dataPath))
const data = JSON.parse(dataBytes.toString('utf8')).supplemental
const rules = data['plurals-type-cardinal']

const integers = [...Array.from({length: 301}, (_, value) => String(value)), '1000', '1000000']
const decimals = ['0.0', '0.5', '1.0', '1.5', '2.0', '2.5', '10.0', '100.5',
  '0.00', '0.01', '0.10', '1.00', '1.25', '2.10', '11.0', '21.0', '101.0', '1000.0', '1000000.0']
// A one-digit fraction sweep and two-digit fractions exercise the f, t, v, and w rules
// (lv, lt, mk, bs/hr/sr, is, fil, gv and others key on the fraction's last digits).
const sweep = ['0', '1', '2', '3', '5', '11', '21', '22', '25', '101']
  .flatMap(whole => Array.from({length: 10}, (_, digit) => `${whole}.${digit}`))
const twoDigit = ['0.11', '0.21', '0.22', '0.25', '1.01', '1.11', '1.21', '2.02', '2.12', '3.04']
// Negatives select on the absolute value; large decimals and a three-digit scale.
const negatives = ['-1', '-2', '-3', '-5', '-11', '-21', '-1.5', '-1.0', '-2.50']
const large = ['1000.5', '1001.0', '1234.5', '10000.25', '1000000.5', '2000000.0']
const threeDigit = ['0.001', '1.125', '1.000', '2.500', '21.001']
const numbers = [...new Set([...integers, ...decimals, ...sweep, ...twoDigit, ...negatives, ...large, ...threeDigit])]
const defaultNumbers = numbers.filter(source => String(Number(source)) !== source && Number(source) !== 0)

// Legacy ISO 639 codes the .NET lookup aliases (iw is he, ji is yi, in is id); Intl canonicalises them.
const legacyAliases = ['in', 'iw', 'ji']

// One letter per category keeps the fixture small: z one t(wo) f(ew) m(any) x(other).
const letter = {zero: 'z', one: 'o', two: 't', few: 'f', many: 'm', other: 'x'}
const scaleOf = source => source.includes('.') ? source.split('.')[1].length : 0

const locales = {}
const defaults = {}
const unsupported = []
// Deprecated CLDR codes (jw, mo, sh, tl) and the legacy aliases are supported by Intl through
// canonicalisation to their replacement; they are compared too, and the mapping is recorded.
const canonicalised = {}
for (const locale of [...Object.keys(rules), ...legacyAliases].sort()) {
  const supported = Intl.PluralRules.supportedLocalesOf([locale], {localeMatcher: 'lookup'})
  if (!supported.length) {
    unsupported.push(locale)
    continue
  }
  const resolved = new Intl.PluralRules(locale).resolvedOptions().locale
  if (resolved.toLowerCase() !== locale.toLowerCase()) canonicalised[locale] = resolved
  locales[locale] = numbers.map(source => {
    const scale = scaleOf(source)
    const options = scale ? {minimumFractionDigits: scale, maximumFractionDigits: scale} : {}
    return letter[new Intl.PluralRules(locale, options).select(Number(source))]
  }).join('')
  defaults[locale] = defaultNumbers.map(source => letter[new Intl.PluralRules(locale).select(Number(source))]).join('')
}

const fixture = {
  schemaVersion: 2,
  generator: 'conformance/hlp.ui.locale-provider/plural-crosscheck.mjs',
  intl: {node: process.version, icu: process.versions.icu, cldr: process.versions.cldr},
  data: {
    path: dataPath,
    sha256: createHash('sha256').update(dataBytes).digest('hex'),
    cldr: data.version._cldrVersion,
  },
  categoryLetters: letter,
  numbers,
  defaultNumbers,
  legacyAliases,
  unsupported,
  canonicalised,
  locales,
  defaults,
}
const text = `${JSON.stringify(fixture, null, 1)}\n`
if (process.argv.includes('--check')) {
  const withoutVersions = json => JSON.stringify({...JSON.parse(json), intl: null})
  const committed = readFileSync(outputPath, 'utf8')
  if (withoutVersions(committed) !== withoutVersions(text)) {
    console.error('plural-crosscheck.json differs from this Intl; regenerate and review the allow-list.')
    process.exitCode = 1
  } else console.log(`plural-crosscheck.json matches (${Object.keys(locales).length} locales x ${numbers.length} numbers, ${defaultNumbers.length} default-mode numbers; Node ${process.version}, ICU ${process.versions.icu})`)
} else {
  writeFileSync(outputPath, text)
  console.log(`wrote ${Object.keys(locales).length} locales x ${numbers.length} numbers, ${defaultNumbers.length} default-mode numbers; ${unsupported.length} unsupported by Intl`)
}
