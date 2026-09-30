// T-705 oracle: owner asset inventory and strict failure properties; isolated git corpus.
import assert from 'node:assert/strict'
import {execFileSync, spawnSync} from 'node:child_process'
import {createHash} from 'node:crypto'
import {mkdirSync, mkdtempSync, rmSync, writeFileSync, readFileSync, readdirSync} from 'node:fs'
import {tmpdir} from 'node:os'
import {resolve} from 'node:path'
import test from 'node:test'
import {ARTIFACT_PATH, EVIDENCE_PATH, emitReleaseReceipt} from '../release-receipt.mjs'
import {requiredStepIds} from '../gate-contract.mjs'
import {stage, check} from '../platform-release.mjs'
const sha256 = bytes => createHash('sha256').update(bytes).digest('hex')

// The gate step's setting. Attestation and current design reviews both default ON, so every check
// that is about neither opts out explicitly here, exactly as the gate step does.
const asGateStep = {requireAttestation: false, requireCurrentDesignReviews: false}

/** The canonical export idiom PlatformPackageExporter uses: digest over the digest-free bytes. */
function artifact(revision = '1.0.0', payload = {id: 'harborline.platform'}) {
  const unsigned = `${JSON.stringify({schemaVersion: 1, packageKey: 'harborline.platform', revision, items: [payload]})}\n`
  const digest = sha256(Buffer.from(unsigned, 'utf8'))
  const body = JSON.stringify({schemaVersion: 1, packageKey: 'harborline.platform', revision, items: [payload]})
  return `${body.slice(0, -1)},"digest":{"algorithm":"sha256","value":"${digest}"}}\n`
}

function release(options = {}) {
  const root = mkdtempSync(resolve(tmpdir(), 'harborline-release-receipt-'))
  const git = (...args) => execFileSync('git', args, {cwd: root, encoding: 'utf8'}).trim()
  git('init', '--quiet')
  git('config', 'user.email', 'lane@harborline.test')
  git('config', 'user.name', 'lane')
  write(root, ARTIFACT_PATH, options.artifact ?? artifact())
  git('add', '.')
  git('commit', '--quiet', '-m', 'artifact')
  // The recorded run transcript names the commit that produced it, so it can only be written once
  // that commit exists -- which is why it is a second commit rather than part of the first.
  const recordedRun = git('rev-parse', 'HEAD')
  write(root, EVIDENCE_PATH, `${JSON.stringify({
    schemaVersion: 3,
    phase: 4,
    status: options.evidenceStatus ?? 'PASS',
    ...('designReview' in options ? {designReview: options.designReview} : {designReview: {status: 'PASS', expired: [], failingExpired: []}}),
    subject: {
      repository: 'harborline-platform',
      baseHead: options.recordedRun ?? recordedRun,
      testedTree: git('rev-parse', 'HEAD^{tree}'),
      mode: 'current-index',
    },
  }, null, 2)}\n`)
  git('add', '.')
  git('commit', '--quiet', '-m', 'evidence')
  return {root, git, dispose: () => { try { rmSync(root, {recursive: true, force: true}) } catch {} }}
}

function write(root, filePath, content) {
  const target = resolve(root, filePath)
  mkdirSync(resolve(target, '..'), {recursive: true})
  writeFileSync(target, content)
}


function ready(options) {
  const fixture = release(options)
  const {root, git} = fixture
  const commit = git('rev-parse', 'HEAD')
  const tree = git('rev-parse', 'HEAD^{tree}')
  const gate = {schemaVersion: 3, phase: 4, status: 'PASS',
    subject: {repository: 'harborline-platform', baseHead: commit, testedTree: tree, mode: 'exact-staged-tree'},
    designReview: {expired: []}, requiredStepIds,
    results: requiredStepIds.map(id => ({id, passed: true}))}
  write(root, '.git/harborline-phase4-receipt.json', JSON.stringify({schemaVersion: 3,
    repository: 'harborline-platform', phase: 4, baseHead: commit, testedTree: tree,
    reportSha256: sha256(JSON.stringify(gate)), gate}))
  write(root, '.git/harborline-release-receipt.json', emitReleaseReceipt(root, {mode: 'exact-staged-tree'}))
  return {...fixture, commit, directory: resolve(root, 'assets'), gate}
}
test('stages the three owner-decided payloads and checksums bind independently hashed bytes', () => {
  const f = ready()
  try {
    stage(f.root, f.directory, f.commit)
    assert.deepEqual(readdirSync(f.directory).sort(), ['SHA256SUMS', 'platform-pack.export.json', 'release-receipt.json'])
    const sums = readFileSync(resolve(f.directory, 'SHA256SUMS'), 'utf8')
    for (const name of ['platform-pack.export.json', 'release-receipt.json']) {
      const hash = createHash('sha256').update(readFileSync(resolve(f.directory, name))).digest('hex')
      assert.ok(sums.includes(`${hash}  ${name}\n`))
    }
    check(f.root, f.directory, f.commit)
    write(f.root, 'assets/platform-pack.export.json', '{}')
    assert.throws(() => check(f.root, f.directory, f.commit), /seed differs/)
  } finally { f.dispose() }
})
test('missing payload, extra asset, altered checksums and missing bundle fail closed', () => {
  for (const fault of ['missing', 'extra', 'checksums', 'bundle']) {
    const f = ready()
    try {
      stage(f.root, f.directory, f.commit)
      if (fault === 'missing') rmSync(resolve(f.directory, 'release-receipt.json'))
      if (fault === 'extra') write(f.root, 'assets/unexpected', 'x')
      if (fault === 'checksums') write(f.root, 'assets/SHA256SUMS', 'x')
      assert.throws(() => check(f.root, f.directory, f.commit, fault === 'bundle'))
    } finally { f.dispose() }
  }
})
test('expired committed design reviews prevent staging', () => {
  const f = ready({designReview: {expired: ['hlp.ui.button']}})
  try { assert.throws(() => stage(f.root, f.directory, f.commit), /design-review-expired/) }
  finally { f.dispose() }
})
test('a different approved commit and an incomplete current gate prevent staging', () => {
  const f = ready()
  try {
    assert.throws(() => stage(f.root, f.directory, '0'.repeat(40)), /approved commit/)
    f.gate.results.pop()
    write(f.root, '.git/harborline-phase4-receipt.json', JSON.stringify({schemaVersion: 3,
      repository: 'harborline-platform', phase: 4, baseHead: f.commit,
      testedTree: f.git('rev-parse', 'HEAD^{tree}'), gate: f.gate,
      reportSha256: sha256(JSON.stringify(f.gate))}))
    assert.throws(() => stage(f.root, f.directory, f.commit), /complete phase-4/)
  } finally { f.dispose() }
})

test('release workflow constrains producer/ref, defaults to dry-run, and verifies before publish', () => {
  const workflow = readFileSync(new URL('../../.github/workflows/release-platform.yml', import.meta.url), 'utf8')
  assert.match(workflow, /default: false/)
  assert.match(workflow, /defaults:\n  run:\n    shell: bash/)
  assert.match(workflow, /runs-on: ubuntu-latest/)
  assert.match(workflow, /--signer-workflow/)
  assert.match(workflow, /--source-ref refs\/heads\/main --source-digest "\$APPROVED_COMMIT" --deny-self-hosted-runners/)
  assert.ok(workflow.indexOf('platform-release.mjs stage') < workflow.indexOf('uses: actions/attest@'))
  assert.ok(workflow.indexOf('gh release download') < workflow.indexOf('--draft=false'))
  assert.match(workflow, /cmp "\$RUNNER_TEMP\/release\/\$asset"/)
  const staging = workflow.split('\n  stage:\n')[1].split('\n  publish:\n')[0]
  const publication = workflow.split('\n  publish:\n')[1]
  assert.doesNotMatch(staging, /(?:id-token|attestations|contents): write|gh release|gh attestation|uses: actions\/attest@|GH_TOKEN:/)
  assert.match(staging, /permissions:\n      contents: read/)
  assert.match(publication, /needs: stage/)
  assert.match(publication, /if: inputs.publish == true && github.ref == 'refs\/heads\/main'/)
  assert.match(publication, /platform-release-staging-\$\{\{ github.run_id \}\}-\$\{\{ github.run_attempt \}\}/)
  assert.ok(publication.indexOf('platform-release.mjs check') < publication.indexOf('uses: actions/attest@'))
  assert.ok(publication.indexOf('immutable-releases') < publication.indexOf('uses: actions/attest@'))
  assert.match(publication, /\.enabled == true and \.enforced_by_owner == true/)
  assert.match(publication, /matching-refs\/tags\/v0\.1\.0" \| jq -e 'length == 0'/)
  assert.match(publication, /\.isDraft == true and \.targetCommitish == \$commit/)
  for (const line of workflow.split('\n').filter(line => /uses:/.test(line)))
    assert.match(line, /uses: [a-zA-Z0-9_./-]+@[0-9a-f]{40} # v\d/)
})

test('no self-hosted workflow may grant publishing or signing permissions', () => {
  const directory = new URL('../../.github/workflows/', import.meta.url)
  for (const file of readdirSync(directory).filter(name => /\.ya?ml$/.test(name))) {
    const workflow = readFileSync(new URL(file, directory), 'utf8')
    // Conservative lint: refuse even if the sensitive grant belongs to a different job.
    const lines = workflow.split('\n')
    for (let index = 0; index < lines.length; index++) {
      const runner = /^(\s*)runs-on:(.*)$/.exec(lines[index])
      if (!runner) continue
      let value = runner[2]
      let next = index + 1
      while (next < lines.length && (lines[next].trim() === '' || lines[next].match(/^\s*/)[0].length > runner[1].length)) value += lines[next++]
      if (/self-hosted|\$\{\{/.test(value)) assert.doesNotMatch(workflow, /(?:id-token|attestations|packages):\s*write/, file)
    }
  }
})


test('library attestations are pinned and precede package push only on main publication', () => {
  const workflow = readFileSync(new URL('../../.github/workflows/validate.yml', import.meta.url), 'utf8')
  assert.ok(workflow.indexOf('uses: actions/attest@') < workflow.indexOf('dotnet nuget push'))
  assert.match(workflow, /subject-path: artifacts\/packages\/nuget\/\*\.nupkg/)
  assert.match(workflow, /id-token: write\n      attestations: write/)
  for (const line of workflow.split('\n').filter(line => /uses:/.test(line)))
    assert.match(line, /uses: [a-zA-Z0-9_./-]+@[0-9a-f]{40} # v\d/)
})

test('a fresh current run with expired or absent reviews blocks even with clean committed evidence', () => {
  for (const designReview of [{expired: ['hlp.ui.button']}, undefined]) {
    const f = ready()
    try {
      f.gate.designReview = designReview
      write(f.root, '.git/harborline-phase4-receipt.json', JSON.stringify({schemaVersion: 3,
        repository: 'harborline-platform', phase: 4, baseHead: f.commit,
        testedTree: f.git('rev-parse', 'HEAD^{tree}'), gate: f.gate,
        reportSha256: sha256(JSON.stringify(f.gate))}))
      assert.throws(() => stage(f.root, f.directory, f.commit), /current design reviews absent or expired/)
    } finally { f.dispose() }
  }
})

test('downloaded inventory must include a nonempty bundle and unchanged receipt', () => {
  const f = ready()
  try {
    stage(f.root, f.directory, f.commit)
    write(f.root, 'assets/provenance.sigstore.json', '')
    assert.throws(() => check(f.root, f.directory, f.commit, true), /empty provenance bundle/)
    // A structural bundle placeholder is not claimed cryptographic evidence; gh verifies it in workflow.
    write(f.root, 'assets/provenance.sigstore.json', '{"fixture":"not cryptographic evidence"}')
    check(f.root, f.directory, f.commit, true)
    write(f.root, 'assets/release-receipt.json', '{}')
    assert.throws(() => check(f.root, f.directory, f.commit, true), /release-receipt/)
  } finally { f.dispose() }
})


test('release preflight fails closed for API errors, disabled immutability and existing versions (mock gh only)', () => {
  const workflow = readFileSync(new URL('../../.github/workflows/release-platform.yml', import.meta.url), 'utf8')
  const block = workflow.split('      - name: Require immutable releases and no existing version\n')[1].split('      - name: Attest')[0]
  const script = block.split('        run: |\n')[1].split('\n').map(line => line.replace(/^          /, '')).join('\n')
  const mock = `gh() {
    case "$FAULT" in error) return 1;; esac
    case "$*" in
      *immutable-releases*)
        if [ "$FAULT" = disabled ]; then printf '%s' '{"enabled":false,"enforced_by_owner":true}';
        else printf '%s' '{"enabled":true,"enforced_by_owner":true}'; fi;;
      *matching-refs*)
        if [ "$FAULT" = tag ]; then printf '%s' '[{"ref":"refs/tags/v0.1.0"}]'; else printf '%s' '[]'; fi;;
      *releases*)
        if [ "$FAULT" = draft ]; then printf '%s' '[[{"tag_name":"v0.1.0","draft":true}]]';
        else printf '%s' '[[]]'; fi;;
      *) return 99;;
    esac
  }
  `
  for (const fault of ['none', 'error', 'disabled', 'tag', 'draft']) {
    const result = spawnSync('bash', ['-e', '-o', 'pipefail', '-c', mock + script], {
      env: {...process.env, REPOSITORY: 'fixture/repository', FAULT: fault}, encoding: 'utf8'})
    if (fault === 'none') assert.equal(result.status, 0, result.stderr)
    else assert.notEqual(result.status, 0, `must refuse ${fault}`)
  }
})
