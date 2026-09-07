// Ticket 138 slice 3 -- the digest a design verdict binds for a file that CARRIES a render.
//
// HLP-0008 bound a declared surface and rejected source-tree hashing because of the false-positive
// stream: a rename, a reformat, a comment would each expire a verdict nobody's design changed.
// Slice 4 then proved that stream is real -- binding the raw bytes of `Button.tsx` turned four
// behaviour-neutral edits red. Slice 3 needs the reach without the noise, so a render-carrying file
// is hashed over a NORMALISED form rather than its bytes:
//
//   - comments and formatting are removed, because neither reaches a render;
//   - top-level import statements are sorted, because their order does not reach a render;
//   - identifiers that cannot escape the module are replaced by their first-appearance position,
//     because renaming a private helper cannot reach a render. JSX element names and JSX text are
//     never among them: the parser marks their spans and they are carried through verbatim.
//
// "Cannot escape the module" is decided from the syntax tree, not guessed: an identifier is KEPT
// verbatim when it is imported, exported, or used as a member/property/attribute name -- every way
// a name reaches a consumer, a DOM attribute or a stylesheet. Everything else is local, and local
// names are exactly what a behaviour-neutral refactor moves. A rename of an exported symbol or a
// prop still changes the digest, and should: that IS a review-worthy change.
//
// Ceiling: the Blazor lane gets comment-stripping and whitespace normalisation only. Renaming a
// local inside a `.razor` @code block or a `.cs` helper therefore still expires the verdict -- a
// false positive this slice accepts rather than hides, because the honest fix is a Roslyn parse and
// this file will not fake one with a regex. Upgrade path: normaliseCSharp via Roslyn, in the .NET
// tooling, if the Blazor lane starts producing that noise.

import {createHash} from 'node:crypto'
import {readFileSync} from 'node:fs'

import ts from 'typescript'

// Files whose bytes ARE the design (spec authority, scenario catalogs, fixtures) are hashed raw:
// every byte of them was written to be read, so there is no noise to normalise away.
const TS_LIKE = /\.(tsx|ts|jsx|js|mjs)$/
const CSS_LIKE = /\.css$/
const CSHARP_LIKE = /\.(razor|cs)$/

export const carriesRender = path => TS_LIKE.test(path) || CSS_LIKE.test(path) || CSHARP_LIKE.test(path)

// A name is protected when it can be seen from outside the file. Collected from the tree so that
// "outside" is a syntactic fact rather than a naming convention.
//
// The same walk also collects VERBATIM RANGES: source spans the scanner must not be allowed to
// tokenise at all, because outside a JSX parsing context it lexes them as ordinary identifiers and
// they would be replaced by positional placeholders. A JSX element name and JSX text ARE the render
// -- swapping every `span` for `div`, or a button's text from `Save` to `Cancel`, has to move the
// digest -- so the parser (which does have the JSX context) hands their spans over verbatim.
function protectedNames(source) {
  const kept = new Set()
  const ranges = []
  const keep = node => { if (node && ts.isIdentifier(node)) kept.add(node.text) }
  const verbatim = (node, text) => { if (node) ranges.push({start: node.getStart(source), end: node.end, text}) }
  const visit = node => {
    if (ts.isImportSpecifier(node) || ts.isImportClause(node) || ts.isNamespaceImport(node) || ts.isExportSpecifier(node)) keep(node.name)
    // Anything the module exports keeps its name: a consumer -- and the packed package fixtures --
    // reads it by that name.
    else if (ts.canHaveModifiers(node) && ts.getModifiers(node)?.some(m => m.kind === ts.SyntaxKind.ExportKeyword)) {
      if (node.name) keep(node.name)
      if (ts.isVariableStatement(node)) for (const d of node.declarationList.declarations) keep(d.name)
    }
    // Member and attribute names: props, DOM attributes, style keys, object keys. Each of these can
    // reach the rendered output, so a change to one is a change to the surface.
    if (ts.isPropertyAccessExpression(node) || ts.isPropertySignature(node) || ts.isPropertyDeclaration(node)
      || ts.isPropertyAssignment(node) || ts.isMethodSignature(node) || ts.isMethodDeclaration(node)
      || ts.isJsxAttribute(node) || ts.isEnumMember(node) || ts.isBindingElement(node)) keep(node.name)
    // The element being rendered, and the words it renders.
    if (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node) || ts.isJsxClosingElement(node)) {
      verbatim(node.tagName, node.tagName.getText(source))
    }
    if (ts.isJsxText(node)) verbatim(node, node.text.replace(/\s+/g, ' ').trim())
    ts.forEachChild(node, visit)
  }
  visit(source)
  ranges.sort((a, b) => a.start - b.start)
  return {kept, ranges}
}

// One normalised token stream per top-level statement, so import statements can be sorted without
// re-splicing text (which would move every position the rename map is keyed on). Verbatim ranges cut
// the statement into segments; only the segments between them are scanned, so no token can straddle
// a boundary and no JSX name or text can be mistaken for a local identifier.
function scanSegment(text, start, end, kept, placeholders, out) {
  if (end <= start) return
  const scanner = ts.createScanner(ts.ScriptTarget.Latest, /* skipTrivia */ true, ts.LanguageVariant.JSX, text, undefined, start, end - start)
  for (let token = scanner.scan(); token !== ts.SyntaxKind.EndOfFileToken && scanner.getTokenStart() < end; token = scanner.scan()) {
    const value = scanner.getTokenText()
    if (token !== ts.SyntaxKind.Identifier || kept.has(scanner.getTokenValue())) { out.push(value); continue }
    const name = scanner.getTokenValue()
    if (!placeholders.has(name)) placeholders.set(name, `local#${placeholders.size}`)
    out.push(placeholders.get(name))
  }
}

function statementTokens(text, start, end, kept, ranges, placeholders) {
  const out = []
  let cursor = start
  for (const range of ranges) {
    if (range.end <= start || range.start >= end) continue
    scanSegment(text, cursor, Math.min(range.start, end), kept, placeholders, out)
    if (range.text) out.push(range.text)
    cursor = Math.max(cursor, Math.min(range.end, end))
  }
  scanSegment(text, cursor, end, kept, placeholders, out)
  return out.join(' ')
}

export function normaliseTypeScript(text, fileName = 'file.tsx') {
  const source = ts.createSourceFile(fileName, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX)
  const {kept, ranges} = protectedNames(source)
  const placeholders = new Map()
  const imports = []
  const rest = []
  for (const statement of source.statements) {
    const line = statementTokens(text, statement.getStart(source), statement.end, kept, ranges, placeholders)
    ;(ts.isImportDeclaration(statement) ? imports : rest).push(line)
  }
  return [...imports.sort(), ...rest].join('\n')
}

// CSS has no local names -- every selector and custom property is reachable from a render -- so the
// normalisation is comments and whitespace only.
export const normaliseCss = text => text.replace(/\/\*[\s\S]*?\*\//g, ' ').replace(/\s+/g, ' ').trim()

// See the ceiling note at the top: comments and whitespace only.
export const normaliseCSharp = text => text
  .replace(/@\*[\s\S]*?\*@/g, ' ')
  .replace(/<!--[\s\S]*?-->/g, ' ')
  .replace(/^[ \t]*\/\/.*$/gm, ' ')
  .replace(/\s+/g, ' ')
  .trim()

// The digest itself. There is no raw-bytes fallback: a normaliser that silently reverted to bytes
// on a file it could not parse would reintroduce the false-positive stream on exactly those files,
// and quietly. TypeScript's parser recovers rather than throwing, and the scanner tokenises a
// malformed file too, so the worst case is a coarser stream over the same content -- never a
// narrower one, and never a change that the digest cannot see.
export function renderDigest(path, text = readFileSync(path, 'utf8')) {
  const normalised = TS_LIKE.test(path) ? normaliseTypeScript(text, path)
    : CSS_LIKE.test(path) ? normaliseCss(text)
      : CSHARP_LIKE.test(path) ? normaliseCSharp(text)
        : text
  return createHash('sha256').update(normalised).digest('hex')
}

// For the canary: the first INTRINSIC element the file renders, read off the parse tree. A regex
// over `<name` finds type arguments and comparisons too, and mutating one of those moves the digest
// for the wrong reason -- which is how a markup-edit assertion ends up unable to fail.
export function firstIntrinsicTag(text, fileName = 'file.tsx') {
  const source = ts.createSourceFile(fileName, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX)
  let found = null
  const visit = node => {
    if (!found && (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node))
      && ts.isIdentifier(node.tagName) && /^[a-z]/.test(node.tagName.text)) found = node.tagName.text
    if (!found) ts.forEachChild(node, visit)
  }
  visit(source)
  return found
}
