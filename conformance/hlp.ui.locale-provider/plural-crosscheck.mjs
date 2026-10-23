#!/usr/bin/env node
// Generates plural-crosscheck.json: the cardinal plural category that this Node's
// Intl.PluralRules (ICU) selects for every locale in the embedded CLDR data that Intl supports,
// over a fixed list of source numbers. The .NET suite compares its CLDR evaluator against this
// file offline (CldrPluralRulesTests.MatchesNodeIntlPluralRulesOrAnAllowListedCldrChange); any
// disagreement must appear in plural-crosscheck-allowlist.json with the CLDR change behind it.
//
// Usage: node conformance/hlp.ui.locale-provider/plural-crosscheck.mjs [--check]
//   --check  regenerate in memory and fail if the committed file differs (Node/ICU must match).

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

const integers = [...Array.from({length: 301}, (_, value) => String(value)), '1000', '1000000']
const decimals = ['0.0', '0.5', '1.0', '1.5', '2.0', '2.5', '10.0', '100.5',
  '0.00', '0.01', '0.10', '1.00', '1.25', '2.10', '11.0', '21.0', '101.0', '1000.0', '1000000.0']
// A one-digit fraction sweep and two-digit fractions exercise the f, t, v, and w rules
// (lv, lt, mk, bs/hr/sr, is, fil, gv and others key on the fraction's last digits).
const sweep = ['0', '1', '2', '3', '5', '11', '21', '22', '25', '101']
  .flatMap(whole => Array.from({length: 10}, (_, digit) => `${whole}.${digit}`))
const twoDigit = ['0.11', '0.21', '0.22', '0.25', '1.01', '1.11', '1.21', '2.02', '2.12', '3.04']
const numbers = [...new Set([...integers, ...decimals, ...sweep, ...twoDigit])]

// One letter per category keeps the fixture small: z one t(wo) f(ew) m(any) x(other).
const letter = {zero: 'z', one: 'o', two: 't', few: 'f', many: 'm', other: 'x'}

const locales = {}
const unsupported = []
// Deprecated CLDR codes (jw, mo, sh, tl) are supported by Intl through canonicalisation to their
// replacement (jv, ro, sr, fil); they are compared too, and the alias is recorded.
const canonicalised = {}
for (const locale of Object.keys(data['plurals-type-cardinal']).sort()) {
  const supported = Intl.PluralRules.supportedLocalesOf([locale], {localeMatcher: 'lookup'})
  const resolved = supported.length ? new Intl.PluralRules(locale).resolvedOptions().locale : undefined
  if (!supported.length) {
    unsupported.push(locale)
    continue
  }
  if (resolved.toLowerCase() !== locale.toLowerCase()) canonicalised[locale] = resolved
  locales[locale] = numbers.map(source => {
    const scale = source.includes('.') ? source.split('.')[1].length : 0
    const options = scale ? {minimumFractionDigits: scale, maximumFractionDigits: scale} : {}
    return letter[new Intl.PluralRules(locale, options).select(Number(source))]
  }).join('')
}

const fixture = {
  schemaVersion: 1,
  generator: 'conformance/hlp.ui.locale-provider/plural-crosscheck.mjs',
  intl: {node: process.version, icu: process.versions.icu, cldr: process.versions.cldr},
  data: {
    path: dataPath,
    sha256: createHash('sha256').update(dataBytes).digest('hex'),
    cldr: data.version._cldrVersion,
  },
  categoryLetters: letter,
  numbers,
  unsupported,
  canonicalised,
  locales,
}
const text = `${JSON.stringify(fixture, null, 1)}\n`
if (process.argv.includes('--check')) {
  const committed = readFileSync(outputPath, 'utf8')
  if (committed !== text) {
    console.error('plural-crosscheck.json differs from this Node/ICU; regenerate and review the allow-list.')
    process.exitCode = 1
  } else console.log(`plural-crosscheck.json matches (${Object.keys(locales).length} locales x ${numbers.length} numbers)`)
} else {
  writeFileSync(outputPath, text)
  console.log(`wrote ${Object.keys(locales).length} locales x ${numbers.length} numbers; ${unsupported.length} unsupported by Intl`)
}
