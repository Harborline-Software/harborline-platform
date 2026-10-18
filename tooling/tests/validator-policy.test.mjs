#!/usr/bin/env node
// The five mutation-check suites that used to run inside validate-repository.mjs.
//
// They are unit tests over pure predicates, and they were re-running on every gate execution -
// twice, since catalog-preflight and catalog-final are the same script. Here they run once, under
// run-tooling-selftests.mjs, where a failure names the assertion instead of pushing a string into
// the validator's error list (ticket 099 item 6).
//
// A fifth suite came along that the report never counted: blazorRootScopedThemeAliasMutationChecks
// was asserted but had no counter, so three of these assertions were invisible even to the
// four-counter report they sat beside.
import assert from 'node:assert/strict'
import {readFileSync} from 'node:fs'
import {test} from 'node:test'
import {
  errorContractPolicyErrors,
  dependencylessVitestConfigError, interfaceDependencyMismatch, isGateFailureEvidence,
  npmPublicDistributionAuthorized, nugetPublicDistributionAuthorized,
  presentationPolicyErrors, requiresQualityProfile, retiredProvenanceFieldErrors,
  rootScopedThemeAliasError, themingPolicyErrors,
} from '../validator-policy.mjs'

test('theme applicability policy', () => {
  assert.equal(presentationPolicyErrors('mutation.nonvisual', {disposition: 'not-applicable', rationale: 'too short'}).length, 1)
  assert.equal(themingPolicyErrors('mutation.visual', {disposition: 'visual'}, {disposition: 'not-applicable'}).length, 1)
  assert.equal(themingPolicyErrors('mutation.nonvisual', {disposition: 'not-applicable'}, {disposition: 'required'}).length, 1)
  assert.equal(requiresQualityProfile({moduleKind: 'runtime'}, {disposition: 'visual'}), true)
})

test('distribution policy', () => {
  assert.equal(npmPublicDistributionAuthorized({private: true}), false)
  assert.equal(npmPublicDistributionAuthorized({}), true)
  assert.equal(nugetPublicDistributionAuthorized('<HarborlinePublicDistributionAuthorized>false</HarborlinePublicDistributionAuthorized>'), false)
  assert.equal(nugetPublicDistributionAuthorized('<HarborlinePublicDistributionAuthorized>true</HarborlinePublicDistributionAuthorized>'), true)
})

test('contribution config dependency', () => {
  const path = 'projections/react/ui/hlp.ui.example/vitest.config.ts'
  assert.notEqual(dependencylessVitestConfigError(path, "import { defineConfig } from 'vitest/config'", false), null)
  assert.equal(dependencylessVitestConfigError(path, 'export default { test: {} }', false), null)
  assert.equal(dependencylessVitestConfigError(path, "import { defineConfig } from 'vitest/config'", true), null)
})

test('interface dependency parity', () => {
  // Equal sets in either spelling agree.
  assert.equal(interfaceDependencyMismatch('m', ['hlp.ui.cn', 'hlp.ui.locale-provider'],
    [{moduleId: 'hlp.ui.locale-provider'}, {moduleId: 'hlp.ui.cn'}]), null)
  // Spec names a module the catalog does not.
  assert.notEqual(interfaceDependencyMismatch('m', ['hlp.ui.cn'], ['hlp.ui.cn', 'hlp.ui.bogus']), null)
  // Catalog edge absent from the spec.
  assert.notEqual(interfaceDependencyMismatch('m', ['hlp.ui.cn', 'hlp.ui.locale-provider'], ['hlp.ui.cn']), null)
  // Deleting the array is not a bypass: absent is the empty set.
  assert.notEqual(interfaceDependencyMismatch('m', ['hlp.ui.cn'], undefined), null)
  assert.equal(interfaceDependencyMismatch('m', [], undefined), null)
})

test('module token alias theme scope, React', () => {
  const path = 'projections/react/ui/hlp.ui.example/src/style.css'
  assert.notEqual(rootScopedThemeAliasError(path, ':root { --hl-example-muted: var(--hl-muted-foreground, #475569); }'), null)
  assert.equal(rootScopedThemeAliasError(path, '.hl-example { --hl-example-muted: var(--hl-muted-foreground, #475569); }'), null)
  assert.equal(rootScopedThemeAliasError('gallery/styles/canvas.css', ':root { --hl-example-muted: var(--hl-muted-foreground, #475569); }'), null)
})

test('module token alias theme scope, Blazor', () => {
  const path = 'projections/blazor/ui/hlp.ui.example/wwwroot/example.css'
  assert.notEqual(rootScopedThemeAliasError(path, ':root { --hl-example-muted: var(--hl-muted-foreground, #475569); }'), null)
  assert.equal(rootScopedThemeAliasError(path, '.hl-example { --hl-example-muted: var(--hl-muted-foreground, #475569); }'), null)
})

// Ticket 300: dialog, sheet, spotlight and window used to be exempted from this check because they
// carried :root theme aliases that the pairing rule could not yet express. 282 replaced those root
// blocks with component/portal scope, so the exemption set went dead - every entry named a file with
// no root alias behind it, which would have silently permitted a :root regression in exactly the
// files where the check is meant to be strictest. The exemption is gone; these four now get no
// special treatment, same as any other module stylesheet.
test('formerly pairing-constrained Blazor stylesheets have no exemption left', () => {
  for (const path of [
    'projections/blazor/ui/hlp.ui.dialog/wwwroot/dialog.css',
    'projections/blazor/ui/hlp.ui.sheet/wwwroot/sheet.css',
    'projections/blazor/ui/hlp.ui.spotlight/wwwroot/spotlight.css',
    'projections/blazor/ui/hlp.ui.window/wwwroot/window.css',
  ]) {
    assert.notEqual(rootScopedThemeAliasError(path, ':root { --hl-window-muted: var(--hl-muted, Canvas); }'), null,
      `${path}: reintroducing a :root alias must be caught now that no exemption covers it`)
  }
})

// Ticket 269: sourcePaths/sourceBlobs were retired because they could not be validated against any
// reachable upstream commit. This is the only thing standing between that decision and their quiet
// return in the next hand-edited record.
test('retired provenance source fields', () => {
  assert.deepEqual(retiredProvenanceFieldErrors([{moduleId: 'm', contentHashes: {}}]), [])
  assert.equal(retiredProvenanceFieldErrors([{moduleId: 'm', sourcePaths: []}]).length, 1)
  assert.equal(retiredProvenanceFieldErrors([{moduleId: 'm', sourceBlobs: {}}]).length, 1)
  // A value is not what makes it wrong -- the field's presence is, so an empty or null one still fails.
  assert.equal(retiredProvenanceFieldErrors([{moduleId: 'm', sourcePaths: null, sourceBlobs: null}]).length, 2)
  assert.deepEqual(retiredProvenanceFieldErrors(undefined), [])
})

test("the gate's own failure evidence is not repository source", () => {
  // T-658: exactly the shape the gate writes, and nothing else.
  assert.equal(isGateFailureEvidence('.claude/gate-evidence/dependency-ledger.log'), true)
  assert.equal(isGateFailureEvidence('.claude/gate-evidence/gallery-gate.log'), true)
  assert.equal(isGateFailureEvidence('.claude/gate-evidence/pr40-phase4/report.log'), false)
  assert.equal(isGateFailureEvidence('.claude/gate-evidence/report.json'), false)
  assert.equal(isGateFailureEvidence('.claude/settings.json'), false)
  assert.equal(isGateFailureEvidence('.claude/agents/gate.log'), false)
  assert.equal(isGateFailureEvidence('docs/gate-evidence/gate.log'), false)
})

test('optional error-contract pilot metadata and canonical literal consistency', () => {
  // Oracles: Chris's approved T1034 tuple and the reviewed pilot applicability contract, not
  // production Codes constants. This synthetic C# input is independent of the actual producer.
  const sourcePath = 'projections/dotnet/LayoutDefinitionAdmission.cs'
  const source = [
    'public static class LayoutDefinitionCodes', '{',
    '    public const string BodyInvalid = "layout.definition.body_invalid";',
    '    public const string Mirror = "layout.definition.body_invalid";', '}',
  ].join('\n')
  const sources = new Map([[sourcePath, source]])
  const entry = () => ({
    code: 'layout.definition.body_invalid',
    when: 'A verified entry fails syntax or strict typed-body decode, including an object missing binding_kind at an owned Layout binding position, not semantic admission.',
    errorContract: {
      owner: 'Layout', canonicalSource: `${sourcePath}#LayoutDefinitionCodes.BodyInvalid`,
      meaning: 'One whole-entry refusal, including the owned missing-binding_kind failure; skip its later checks, continue other entries and reject the pack.',
      recovery: {category: 'correct_input', action: 'Correct the entry and resubmit; no automatic retry.'},
      pointer: {applicability: 'required', relativeTo: 'layout-entry-payload', wholeDocument: true, examples: ['']},
      target: {applicability: 'conditional', rule: 'Verified DefinitionId@Version only when safe, authorized and fetchable.'},
      stage: {applicability: 'required', vocabulary: 'DefinitionAdmissionPhase', allowed: ['Install']},
      presentation: {rule: 'Client-derived localized code; no exception Message/Path or submitted body.', arguments: []},
    },
  })
  const diagnose = (row, inputs = sources) => errorContractPolicyErrors('pilot', [row], inputs)
  const refuses = (mutate, expected) => {
    const row = entry()
    mutate(row)
    assert.ok(diagnose(row).some(message => message.includes(expected)), expected)
  }

  assert.deepEqual(diagnose(entry()), [])
  assert.deepEqual(errorContractPolicyErrors('legacy', undefined), [])
  assert.deepEqual(errorContractPolicyErrors('legacy', [
    {code: 'verification-outcome-missing', when: 'Existing published spelling.'},
    {code: 'pack.install.refused.unsupported_content_kind.terminology_override', when: 'Existing variable-depth spelling.'},
  ]), [])
  for (const code of ['legacy-code', 'definition.missing', 'pack.install.refused.unsupported']) {
    const row = entry()
    row.code = code
    const literal = source.replaceAll('layout.definition.body_invalid', code)
    assert.deepEqual(diagnose(row, new Map([[sourcePath, literal]])), [], 'naming is guidance even for annotated published identities')
  }

  assert.ok(diagnose(entry(), new Map()).some(message => message.includes('one readable, unambiguous direct string literal')))
  assert.ok(diagnose(entry(), new Map([[sourcePath, source.replaceAll('layout.definition.body_invalid', 'layout.definition.other')]])).some(message => message.includes('exact canonical source literal')))
  assert.ok(diagnose(entry(), new Map([[sourcePath, source.replace('BodyInvalid', 'Other')]])).some(message => message.includes('one readable, unambiguous direct string literal')))
  for (const ambiguous of [source + '\n' + source, source.replace('    public const string Mirror', '    public const string BodyInvalid')]) {
    assert.ok(diagnose(entry(), new Map([[sourcePath, ambiguous]])).some(message => message.includes('one readable, unambiguous direct string literal')))
  }
  const computed = source.replace('"layout.definition.body_invalid";', '"layout.definition." + "body_invalid";')
  assert.ok(diagnose(entry(), new Map([[sourcePath, computed]])).some(message => message.includes('one readable, unambiguous direct string literal')))
  // F1: a literal in an inactive preprocessor branch is not an available compiled symbol.
  // Reject directives conservatively rather than interpret C# conditions or configured symbols.
  assert.deepEqual(diagnose(entry(), sources), [], 'ordinary direct source remains the positive control')
  for (const conditional of [`#if false\n${source}\n#endif`, `#define PILOT\n#if PILOT\n${source}\n#endif`]) {
    assert.ok(diagnose(entry(), new Map([[sourcePath, conditional]])).some(message => message.includes('one readable, unambiguous direct string literal')))
  }
  const commented = '// public class LayoutDefinitionCodes { fake }\n' + source.replace('    public const string Mirror', '    /* direct literal only */ public const string Mirror')
  assert.deepEqual(diagnose(entry(), new Map([[sourcePath, commented]])), [])
  for (const unsupported of [
    source.replace('    public const string BodyInvalid', '    const string BodyInvalid'),
    ['public static class LayoutDefinitionCodes', '{', '    public static class Nested', '    {',
      '        public const string BodyInvalid = "layout.definition.body_invalid";', '    }', '}',
    ].join('\n'),
    source + '\npublic class Other\n{\n    public const string Raw = """not a bounded source""";\n}',
  ]) {
    assert.ok(diagnose(entry(), new Map([[sourcePath, unsupported]])).some(message => message.includes('one readable, unambiguous direct string literal')))
  }
  for (const reference of ['../outside.cs#Codes.Value', '/absolute.cs#Codes.Value', 'C:/outside.cs#Codes.Value', 'a\\outside.cs#Codes.Value', 'a/./outside.cs#Codes.Value', 'a//outside.cs#Codes.Value', `${sourcePath}#LayoutDefinitionCodes.BodyInvalid\n`]) {
    refuses(row => { row.errorContract.canonicalSource = reference }, 'safe repository-relative')
  }
  refuses(row => { row.code = 'Layout.definition.body_invalid' }, 'exact canonical source literal')
  refuses(row => { row.errorContract = null }, 'must be an object')
  refuses(row => { delete row.errorContract.meaning }, 'meaning must be nonblank')
  refuses(row => { delete row.errorContract.recovery }, 'recovery must declare')
  refuses(row => { row.errorContract.recovery.action = ' ' }, 'recovery must declare nonblank')
  refuses(row => { row.errorContract.recovery.category = '' }, 'recovery must declare nonblank')
  const explicitCategory = entry()
  explicitCategory.errorContract.recovery.category = 'caller_declared_category'
  assert.deepEqual(diagnose(explicitCategory), [], 'there is no global or prefix-derived recovery enum')

  refuses(row => { delete row.errorContract.pointer.relativeTo }, 'coordinate base')
  refuses(row => { row.errorContract.pointer.examples = ['/'] }, 'wholeDocument attribution')
  for (const pointer of ['bare', '\n', '/bad~', '/bad~2']) {
    refuses(row => { row.errorContract.pointer = {applicability: 'required', relativeTo: 'entry', wholeDocument: false, examples: [pointer]} }, 'not an RFC6901 pointer')
  }
  const memberPointers = entry()
  memberPointers.errorContract.pointer = {applicability: 'optional', relativeTo: 'entry', wholeDocument: false, examples: ['/', '/a~1b/~0', '/~01']}
  assert.deepEqual(diagnose(memberPointers), [], 'slash is an empty-name member; escaping is syntax, not resolution')
  const notApplicable = entry()
  notApplicable.errorContract.pointer = {applicability: 'not-applicable', reason: 'No implicated document in this synthetic contract.'}
  notApplicable.errorContract.target = {applicability: 'not-applicable', reason: 'No safe fetchable target.'}
  notApplicable.errorContract.stage = {applicability: 'not-applicable', reason: 'This synthetic boundary is not stage-bearing.'}
  assert.deepEqual(diagnose(notApplicable), [])
  notApplicable.errorContract.pointer.examples = ['']
  assert.ok(diagnose(notApplicable).some(message => message.includes('omit document coordinates and examples')))
  refuses(row => { row.errorContract.pointer = {applicability: 'not-applicable'} }, 'pointer needs a reason')
  refuses(row => { row.errorContract.pointer.examples = [] }, 'explicit RFC6901 examples')
  refuses(row => { delete row.errorContract.pointer.wholeDocument }, 'declare wholeDocument explicitly')
  refuses(row => { delete row.errorContract.target.rule }, 'disclosure rule')
  refuses(row => { row.errorContract.target = {applicability: 'not-applicable', reason: 'No target.', rule: 'A contradictory target.'} }, 'omit its applicable fields')
  refuses(row => { delete row.errorContract.stage.vocabulary }, 'actual vocabulary')
  refuses(row => { row.errorContract.stage = {applicability: 'not-applicable'} }, 'stage needs a reason')
  refuses(row => { row.errorContract.stage.allowed = ['Decode'] }, 'outside the declared DefinitionAdmissionPhase vocabulary')
  refuses(row => { row.errorContract.stage.allowed = [] }, 'explicit allowed values')
  refuses(row => { row.errorContract.presentation.arguments = [{name: 'exception.Message'}] }, 'only empty presentation arguments')
  refuses(row => { row.errorContract.presentation.detail = {payload: 'private'} }, 'outside this bounded metadata pilot')
  refuses(row => { row.errorContract.presentation.rule = '' }, 'presentation needs a safe rule')

  const mirror = entry()
  mirror.errorContract = {...mirror.errorContract, recovery: {action: 'Correct the entry and resubmit; no automatic retry.', category: 'correct_input'}}
  assert.deepEqual(errorContractPolicyErrors('pilot', [entry(), mirror], sources), [], 'canonical mirrors are not blanket duplicate-code failures')
  for (const mutate of [
    row => { row.errorContract.meaning = 'Different meaning.' },
    row => { row.errorContract.recovery.category = 'different_category' },
    row => { row.errorContract.pointer.relativeTo = 'outer-http-request' },
    row => { row.errorContract.target.rule = 'Different disclosure rule.' },
    row => { row.errorContract.stage.allowed = ['Publish'] },
    row => { row.errorContract.canonicalSource = `${sourcePath}#LayoutDefinitionCodes.Mirror` },
  ]) {
    const conflict = entry()
    mutate(conflict)
    assert.ok(errorContractPolicyErrors('pilot', [entry(), conflict], sources).some(message => message.includes('conflicting')), 'conflicting meaning/canonical metadata must not silently merge')
  }
})

test('checked-in Layout pilot metadata conforms with its actual canonical source', () => {
  const definition = JSON.parse(readFileSync(new URL('../../specs/modules/blocks/hlp.blocks.builder-definitions/interface.yaml', import.meta.url), 'utf8'))
  const rows = definition.errors.filter(row => row.code === 'layout.definition.body_invalid')
  assert.equal(rows.length, 1)
  const sourcePath = 'projections/dotnet/blocks/hlp.blocks.builder-definitions/LayoutDefinitionAdmission.cs'
  const source = readFileSync(new URL(`../../${sourcePath}`, import.meta.url), 'utf8')
  assert.deepEqual(errorContractPolicyErrors(definition.moduleId, rows, new Map([[sourcePath, source]])), [])
})
