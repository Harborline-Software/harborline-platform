#!/usr/bin/env node
// Ticket 268 fix 1. The budgeted performance rows must run in the serial `perf-budgets` slot and
// NOWHERE else. Ticket 265's ceilings are 2x a QUIET p95; with the rows still riding inside the
// parallel fan-outs -- `blazor-native` runs twenty-five `dotnet test` projects at once, and
// `react-native` runs beside them -- the gate failed on both Macs on
// DataGridPerformanceTests.TenThousandRowsRenderWithinTheRowBudget*. Moving the rows into a quiet
// slot only helps if they LEAVE the loud one, which is what these two tests hold.
//
// Both discover their inventory rather than listing it: the budgeted rows are the tests that call
// the budget helper (`reportRow` in the React lane, `AssertWithinBudget` in the Blazor lane), and
// the perf-bearing React modules are the directories those files live in. A new budgeted row that
// nobody marked is red here, and a marked test that does not measure is red too -- exact both ways.

import assert from 'node:assert/strict'
import {readFileSync, readdirSync} from 'node:fs'
import {dirname, resolve} from 'node:path'
import {test} from 'node:test'
import {fileURLToPath} from 'node:url'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '../..')
const read = relative => readFileSync(resolve(root, relative), 'utf8')

const MARKER = '[PerfBudget]'
const CATEGORY_EXCLUDE = 'Category!=PerfBudget'
const CATEGORY_INCLUDE = 'Category=PerfBudget'
// The exclusion as it appears in package.json's JSON-escaped script text.
const NAME_EXCLUDE = String.raw`-t \"^(?!.*\\[PerfBudget\\])\"`

const reactUi = resolve(root, 'projections/react/ui')
const buttonPackageText = read('projections/react/ui/hlp.ui.button/package.json')
const nativeText = read('tooling/run-native.mjs')
const stabilityText = read('tooling/perf-budget-stability.mjs')

/** Every React test file that calls the budget helper, with its module directory. */
function budgetedReactFiles() {
  const found = []
  for (const module of readdirSync(reactUi, {withFileTypes: true}).filter(entry => entry.isDirectory())) {
    const tests = resolve(reactUi, module.name, 'src/__tests__')
    let names
    try { names = readdirSync(tests) } catch { continue }
    for (const name of names.filter(file => file.endsWith('.test.tsx') || file.endsWith('.test.ts'))) {
      const text = readFileSync(resolve(tests, name), 'utf8')
      if (/\breportRow\(/.test(text)) found.push({module: module.name, file: `${module.name}/src/__tests__/${name}`, text})
    }
  }
  return found
}

/** One block per `it(` / `it.each(` in a vitest file: from that call to the next one, or EOF. */
function itBlocks(text) {
  const starts = [...text.matchAll(/\bit(?:\.each)?\(/g)].map(match => match.index)
  return starts.map((start, index) => text.slice(start, starts[index + 1] ?? text.length))
}

/** One block per `[Fact]` in an xUnit file, same slicing. */
function factBlocks(text) {
  const starts = [...text.matchAll(/\[Fact\]/g)].map(match => match.index)
  return starts.map((start, index) => text.slice(start, starts[index + 1] ?? text.length))
}

/** Every Blazor test file that reports a budgeted row. */
function blazorBudgetFiles() {
  const found = []
  const walk = current => {
    for (const entry of readdirSync(current, {withFileTypes: true})) {
      const path = resolve(current, entry.name)
      if (entry.isDirectory()) { if (entry.name !== 'bin' && entry.name !== 'obj') walk(path); continue }
      if (!entry.name.endsWith('.cs')) continue
      const text = readFileSync(path, 'utf8')
      if (text.includes('[perf] row=')) found.push({file: entry.name, text})
    }
  }
  walk(resolve(root, 'projections/blazor/ui'))
  return found
}

const rowIds = text => [...text.matchAll(/['"]((?:react|blazor)-[a-z0-9-]+)['"]/g)].map(match => match[1])

test('the parallel fan-outs exclude the budgeted rows and the serial step runs exactly them', () => {
  // Blazor: run-native.mjs is the fan-out (twenty-five projects in parallel); the perf step is the inverse.
  assert.match(nativeText, /const EXCLUDE_PERF_BUDGET = 'Category!=PerfBudget'/, 'run-native.mjs must declare the exclusion')
  assert.match(nativeText, /\['blazor', 'blazor-native', blazorTestProject, EXCLUDE_PERF_BUDGET\]/, 'the blazor suite row must carry the exclusion')
  assert.match(nativeText, /\.\.\.\(filter \? \['--filter', filter\] : \[\]\)/, 'the exclusion must reach the dotnet test command line')
  assert.ok(!new RegExp(`'${CATEGORY_INCLUDE}'`).test(nativeText), 'the fan-out never selects the category as a filter value')

  assert.match(stabilityText, /const PERF_BUDGET_CATEGORY = 'Category=PerfBudget'/, 'the perf step must declare the inverse')
  assert.match(stabilityText, /lane: 'blazor', filter: PERF_BUDGET_CATEGORY/, 'the blazor target selects the category and nothing else')
  assert.match(stabilityText, /'--filter', target\.filter,/, 'the category must reach the dotnet test command line')
  assert.ok(!new RegExp(`'${CATEGORY_EXCLUDE}'`).test(stabilityText), 'the perf step never excludes the category it exists to run')

  // React: the perf-bearing module scripts inside the `react-native` fan-out, discovered.
  const modules = [...new Set(budgetedReactFiles().map(entry => entry.module))]
  assert.ok(modules.length >= 4, `expected the budgeted React modules to be discovered, got ${modules.join(', ')}`)
  for (const module of modules) {
    const script = `"test:${module.replace(/^hlp\.ui\./, '')}"`
    const line = buttonPackageText.split('\n').find(text => text.trimStart().startsWith(`${script}:`))
    assert.ok(line, `hlp.ui.button/package.json must run ${module} as ${script}`)
    assert.ok(line.includes(NAME_EXCLUDE), `${script} must exclude ${MARKER} from the fan-out; got ${line.trim()}`)
  }
  assert.ok(stabilityText.includes(String.raw`const PERF_BUDGET_NAME_PATTERN = '\\[PerfBudget\\]'`), 'the perf step must select the marked tests')
  assert.match(stabilityText, /'-t', PERF_BUDGET_NAME_PATTERN/, 'the name pattern must reach the vitest command line')
})

test('every budgeted row is marked and every marked test is a budgeted row', () => {
  const declared = new Set([...stabilityText.matchAll(/rows: \[([^\]]+)\]/g)].flatMap(match => rowIds(match[1])))
  assert.equal(declared.size, 10, `perf-budget-stability.mjs declares ten budgeted rows, saw ${declared.size}`)
  const marked = new Set()

  const reactFiles = budgetedReactFiles()
  assert.ok(reactFiles.length >= 5, 'the React budget helper must be discoverable')
  for (const {file, text} of reactFiles) {
    for (const block of itBlocks(text)) {
      const measures = /\breportRow\(/.test(block)
      const carries = block.includes(MARKER)
      assert.equal(carries, measures, `${file}: a test that calls reportRow must carry ${MARKER}, and only those may; block starts ${block.slice(0, 90)}`)
      if (measures) for (const row of rowIds(block)) marked.add(row)
    }
  }

  const blazorFiles = blazorBudgetFiles()
  assert.ok(blazorFiles.length >= 1, 'the Blazor budget helper must be discoverable')
  for (const {file, text} of blazorFiles) {
    for (const block of factBlocks(text)) {
      const measures = /(?<!Task )AssertWithinBudget\(/.test(block)
      const carries = /\[Trait\("Category", "PerfBudget"\)\]/.test(block)
      assert.equal(carries, measures, `${file}: a [Fact] that calls AssertWithinBudget must carry the PerfBudget trait, and only those may; block starts ${block.slice(0, 90)}`)
      if (measures) for (const row of rowIds(block)) marked.add(row)
    }
  }

  assert.deepEqual([...marked].sort(), [...declared].sort(), 'the marked rows and the rows the perf step declares must be the same set')
})
