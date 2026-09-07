// Ticket 138 slice 3. The digest a design verdict binds for a render-carrying file has to hold two
// properties at once, and one of them without the other is a defect this ticket family already paid
// for: it must NOT move for an edit that cannot reach a render (slice 4's false-positive stream),
// and it MUST move for an edit that can (the blindness ticket 138 opened with). Every case below is
// a pair, so a normaliser that flattens everything to a constant fails as loudly as one that
// flattens nothing.

import assert from 'node:assert/strict'
import {test} from 'node:test'

import {carriesRender, normaliseCSharp, normaliseCss, renderDigest} from '../gates/render-digest.mjs'

const TSX = 'Button.tsx'
const SOURCE = `import { cn } from './cn'
import * as React from 'react'

// a leading comment
export interface ButtonProps { fillMode?: string; children: React.ReactNode }

function buildFillModeClass(fillMode: string): string {
  const prefix = 'hl-button--'
  return prefix + fillMode
}

export function Button({fillMode, children}: ButtonProps) {
  const cls = buildFillModeClass(fillMode ?? 'solid')
  return <button className={cn('hl-button', cls)} data-fill={fillMode}>{children}</button>
}
`

const digest = text => renderDigest(TSX, text)
const base = digest(SOURCE)

// --- edits that cannot reach a render: the digest must not move -------------------------------

test('renaming a module-local helper does not move the digest', () => {
  assert.equal(digest(SOURCE.replaceAll('buildFillModeClass', 'fillModeClassFor')), base)
})

test('renaming a local variable does not move the digest', () => {
  assert.equal(digest(SOURCE.replaceAll('const prefix', 'const classPrefix').replaceAll('prefix +', 'classPrefix +')), base)
})

test('reformatting does not move the digest', () => {
  assert.equal(digest(SOURCE.replace(/^( +)/gm, spaces => spaces.repeat(3)).replaceAll('\n', '\n\n')), base)
})

test('a comment does not move the digest', () => {
  assert.equal(digest(`/* ticket 138 */\n${SOURCE.replace('const prefix', '// why\n  const prefix')}`), base)
})

test('reordering imports does not move the digest', () => {
  const lines = SOURCE.split('\n')
  ;[lines[0], lines[1]] = [lines[1], lines[0]]
  assert.equal(digest(lines.join('\n')), base)
})

test('CRLF does not move the digest', () => {
  assert.equal(digest(SOURCE.replaceAll('\n', '\r\n')), base)
})

// --- edits that CAN reach a render: the digest must move ---------------------------------------

test('a class name in the rendered markup moves the digest', () => {
  assert.notEqual(digest(SOURCE.replace("'hl-button'", "'hl-button hl-button--wide'")), base)
})

test('renaming an EXPORTED symbol moves the digest: a consumer reads it by that name', () => {
  assert.notEqual(digest(SOURCE.replaceAll('ButtonProps', 'ButtonProperties')), base)
})

test('renaming a prop moves the digest: a prop is how a consumer changes the render', () => {
  assert.notEqual(digest(SOURCE.replaceAll('fillMode', 'fill')), base)
})

test('changing a DOM attribute name moves the digest', () => {
  assert.notEqual(digest(SOURCE.replace('data-fill=', 'data-fill-mode=')), base)
})

// These two are the pair review 1 found missing: both are satisfied ONLY by the parser's
// verbatim-range path (JSX element names and JSX text), and both are green under a normaliser that
// keeps every protected name but lexes JSX with a bare scanner. The text edit is deliberately word-
// for-word the same length, so a token-count change cannot be what moves the digest.
test('swapping the rendered element moves the digest', () => {
  assert.notEqual(digest(SOURCE.replace('<button', '<span').replace('</button>', '</span>')), base)
})

test('changing rendered text moves the digest, at the same token count', () => {
  const withText = SOURCE.replace('{children}', 'Save')
  assert.notEqual(digest(withText.replace('Save', 'Cancel')), digest(withText))
})

test('changing an attribute value moves the digest', () => {
  assert.notEqual(digest(SOURCE.replace("data-fill={fillMode}", "data-fill=\"solid\"")), base)
})

test('whitespace around rendered text is still formatting', () => {
  const withText = SOURCE.replace('{children}', 'Save')
  assert.equal(digest(withText.replace('>Save<', '>\n    Save\n  <')), digest(withText))
})

test('deleting rendered markup moves the digest', () => {
  assert.notEqual(digest(SOURCE.replace('{children}', '')), base)
})

test('changing an import source moves the digest', () => {
  assert.notEqual(digest(SOURCE.replace("'./cn'", "'./classnames'")), base)
})

// --- the other two lanes -----------------------------------------------------------------------

test('css: comments and whitespace are noise, a declaration is not', () => {
  assert.equal(normaliseCss('/* why */\n.a {\n  color: red;\n}\n'), normaliseCss('.a { color: red; }'))
  assert.notEqual(normaliseCss('.a { color: red }'), normaliseCss('.a { color: blue }'))
})

test('razor: comments and whitespace are noise, markup is not', () => {
  assert.equal(normaliseCSharp('@* note *@\n<button   class="hl">\n  @Text\n</button>'), normaliseCSharp('<button class="hl"> @Text </button>'))
  assert.notEqual(normaliseCSharp('<button class="hl">'), normaliseCSharp('<button class="hl-wide">'))
})

// --- the classification itself -----------------------------------------------------------------

test('only render-carrying extensions are normalised; the spec authority is hashed raw', () => {
  for (const path of ['a/Button.tsx', 'a/cn.ts', 'a/style.css', 'a/X.razor', 'a/X.cs', 'a/x.js']) {
    assert.equal(carriesRender(path), true, path)
  }
  for (const path of ['specs/modules/ui/x/scenarios.json', 'conformance/x/fixtures.yaml', 'a/README.md']) {
    assert.equal(carriesRender(path), false, path)
  }
})

test('a non-render file is hashed over its exact bytes, comments included', () => {
  assert.notEqual(renderDigest('x.json', '{"a": 1}'), renderDigest('x.json', '{ "a": 1 }'))
})
