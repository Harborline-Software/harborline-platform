#!/usr/bin/env node

import { createHash } from 'node:crypto'
import { cleanUpScratchOnSignal, runFixtureStep, sweepStaleScratchTrees, writeScratchPidFile } from './resolve-command.mjs'
import {
  copyFileSync,
  cpSync,
  lstatSync,
  mkdirSync,
  mkdtempSync,
  readFileSync,
  readdirSync,
  rmSync,
  statSync,
  writeFileSync,
} from 'node:fs'
import { homedir, tmpdir } from 'node:os'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { inflateRawSync } from 'node:zlib'

import { parsePackageFixtureArguments } from './package-fixture-selection.mjs'
import { computePackageVersion, writePackageVersionProps } from './package-version.mjs'
import { resolvePinnedDotnet } from './resolve-dotnet.mjs'

const options = parsePackageFixtureArguments(process.argv.slice(2))
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const reactRoot = resolve(root, 'projections/react/ui/hlp.ui.button')
const formsRoot = resolve(root, 'projections/typescript/contracts/hlp.contracts.forms')
const ruleRuntimeRoot = resolve(root, 'projections/typescript/foundation/hlp.foundation.rule-runtime')
const ruleAuthoringRoot = resolve(root, 'projections/typescript/foundation/hlp.foundation.rule-authoring')
const artifactRoot = resolve(root, 'artifacts/packages')
const npmArtifacts = resolve(artifactRoot, 'npm')
const nugetArtifacts = resolve(artifactRoot, 'nuget')
// Ticket 289: sweep orphans from a prior killed/failed run before minting this run's own tree.
const CONSUMER_SCRATCH_PREFIX = 'harborline-platform-consumers-'
for (const removed of sweepStaleScratchTrees(CONSUMER_SCRATCH_PREFIX)) {
  process.stderr.write(`removed stale consumer scratch tree (owner gone, older than 2h): ${removed}\n`)
}
const fixtureRoot = mkdtempSync(join(tmpdir(), CONSUMER_SCRATCH_PREFIX))
writeScratchPidFile(fixtureRoot)
const disposeSignalCleanup = cleanUpScratchOnSignal(() => { rmSync(fixtureRoot, { recursive: true, force: true }) })
const dotnet = resolvePinnedDotnet(root)
const globalNugetPackages = process.env.NUGET_PACKAGES
  ? resolve(process.env.NUGET_PACKAGES)
  : resolve(homedir(), '.nuget/packages')
const budgets = {
  npm: { packedBytesMaximum: 750000, unpackedBytesMaximum: 3000000 },
  nuget: { packedBytesMaximum: 1500000, unpackedBytesMaximum: 5000000 },
}
const publicThemeTokens = [
  '--hl-button-primary',
  '--hl-button-primary-hover',
  '--hl-button-primary-active',
  '--hl-button-primary-foreground',
  '--hl-button-secondary',
  '--hl-button-secondary-hover',
  '--hl-button-secondary-active',
  '--hl-button-secondary-foreground',
  '--hl-button-border',
  '--hl-button-focus',
]

function assertPublicThemeSurface(css, artifact) {
  const missingTokens = publicThemeTokens.filter(token => !css.includes(`var(${token}`))
  const missingStates = ['hover', 'active', 'focus-visible'].filter(state => !css.includes(`:${state}`))
  if (missingTokens.length || missingStates.length) {
    throw new Error(`${artifact} public theme surface is incomplete: ${JSON.stringify({ missingTokens, missingStates })}`)
  }
}

function assertContextMenuSurface(css, artifact) {
  const required = [
    '.hl-context-menu',
    '.hl-context-menu__item',
    '.hl-context-menu__separator',
    'prefers-reduced-motion',
    'forced-colors',
    'var(--hl-accent',
    'var(--hl-danger',
  ]
  const missing = required.filter(value => !css.includes(value))
  if (missing.length) throw new Error(`${artifact} Context Menu surface is incomplete: ${JSON.stringify({ missing })}`)
}

function assertReactFeedbackSurface(css, artifact) {
  const required = [
    '.hl-error-card',
    '.hl-error-card--compact',
    '.hl-error-card__retry:hover',
    '.hl-error-card__retry:focus-visible',
    '.hl-loading-state',
    '.hl-loading-state--page',
    '.hl-loading-state--inline',
    'var(--hl-error-card-surface)',
    'var(--hl-loading-state-foreground)',
    '.hl-empty-state',
    '.hl-empty-state__action:hover',
    '.hl-empty-state__action:focus-visible',
    'var(--hl-empty-state-foreground)',
    'forced-colors',
    'prefers-reduced-motion',
  ]
  const missing = required.filter(value => !css.includes(value))
  if (missing.length) throw new Error(`${artifact} React feedback surface is incomplete: ${JSON.stringify({ missing })}`)
}

function assertBlazorFeedbackSurface(css, artifact) {
  const required = [
    '.hl-error-card',
    '.hl-error-card--compact',
    '.hl-error-card__retry:hover',
    '.hl-error-card__retry:focus-visible',
    '.hl-loading-state',
    '.hl-loading-state--page',
    '.hl-loading-state--inline',
    // 282 s5: the Blazor lane's feedback.css is now a byte-identical copy of the error-card
    // authority and loading-state ships its own copy, so both lanes carry the same variables.
    'var(--hl-error-card-surface)',
    'var(--hl-loading-state-foreground)',
    '.hl-empty-state',
    '.hl-empty-state__action:hover',
    '.hl-empty-state__action:focus-visible',
    'var(--hl-empty-state-foreground)',
    'forced-colors',
    'prefers-reduced-motion',
  ]
  const missing = required.filter(value => !css.includes(value))
  if (missing.length) throw new Error(`${artifact} Blazor feedback surface is incomplete: ${JSON.stringify({ missing })}`)
}

function assertNpmContributionTypeSurface(installed) {
  const rootDeclaration = readFileSync(resolve(installed, 'dist/index.d.ts'), 'utf8')
  const contributionPaths = [
    'aspect-lens',
    'cn',
    'default-strings',
    'error-card',
    'form-view',
    'loading-state',
    'rail-labels',
    'touch-target',
    'use-media-query',
    'use-outside-click',
    'empty-state',
    'tone-style',
    'use-can-show-master-detail',
    'use-is-mobile',
    'breadcrumb',
    'check-box',
    'collapsible',
    'sheet',
    'spotlight',
    'text-area',
    'tooltip',
    'table',
    'window',
    'use-nav-collapsed',
    'use-scroll-affordance',
    'action-menu',
    'activity-log',
    'chip',
    'conversation-list',
    'guarded-control',
    'input',
    'layers-rail',
    'notification-center',
    'page',
    'search-input',
    'segmented-control',
    'select-field',
    'switch',
    'toaster',
    'user-menu',
    'app-layout',
    'scroll-affordance',
    'dialog',
    'side-nav',
    'switch-field',
    'text-box',
    'scheduler',
  ]
  const missingRootExports = contributionPaths.filter(name => !rootDeclaration.includes(`'./${name}/index'`))
  if (missingRootExports.length) {
    throw new Error(`npm aggregate declaration entry is incomplete: ${JSON.stringify({ missingRootExports })}`)
  }

  // Locale surface names are re-exported from the root entry (not a ./<name>/index contribution),
  // so the root declaration is asserted by name. Ticket 256 slice 8 renamed all twelve.
  const rootLocaleNames = [
    'HarborlineLocaleProvider', 'HarborlineLocaleProviderProps', 'directionForLocale',
    'useHarborlineLocale', 'useHarborlineStrings', 'UseHarborlineStringsResult',
    'HarborlineDirection', 'HarborlineLocaleCatalog', 'HarborlineLocaleContextValue',
    'HarborlineNumberFormatter', 'HarborlineNumberFormatRequest', 'HARBORLINE_DEFAULT_STRINGS',
  ]
  const missingLocaleNames = rootLocaleNames.filter(name => !new RegExp(`\\b${name}\\b`).test(rootDeclaration))
  if (missingLocaleNames.length) {
    throw new Error(`npm aggregate locale surface is incomplete: ${JSON.stringify({ missingLocaleNames })}`)
  }

  const expectedNames = {
    'aspect-lens': [
      'CanvasNode', 'CanvasModel', 'LensTone', 'AspectState', 'AspectEdge', 'AspectLens',
      'ProvenanceSource', 'ProvenanceInfo', 'ProvenanceResolver',
    ],
    cn: ['cn', 'ClassValue'],
    'default-strings': ['defaultStrings', 'interpolate', 'HarborlineStringCatalog', 'HarborlineStringKey'],
    'error-card': ['ErrorCard', 'ErrorCardProps', 'ErrorCardVariant'],
    'form-view': [
      'Cardinality', 'CollectionColumn', 'CollectionTableConfig', 'ContentNode', 'ControlHint',
      'FieldPlacement', 'FieldWidth', 'FlexDirection', 'FlexWrap', 'FormActionConfig',
      'FormActionKind', 'InternationalizedText', 'LayoutAlign', 'LayoutBreakpoint',
      'LayoutDensity', 'LayoutGap', 'PresentationOutcome', 'SectionLayout', 'SectionLayoutKind',
      'Severity', 'ValidationError', 'ValidationErrorKind', 'ValidationResult', 'FormValues',
      'FormViewField', 'FormViewItem', 'FormViewSection', 'FormView', 'resolveText',
    ],
    'loading-state': ['LoadingState', 'LoadingStateProps', 'LoadingStateVariant'],
    'rail-labels': ['RailLabels', 'defaultRailLabels'],
    'touch-target': ['touchTarget', 'touchTargetPseudoOverlay', 'touchTargetPseudo'],
    'use-media-query': ['useMediaQuery', 'MediaQuery', 'MediaQueryProps'],
    'use-outside-click': ['useOutsideClick'],
    'empty-state': ['EmptyState', 'EmptyStateAction', 'EmptyStateProps', 'EmptyStateVariant'],
    'tone-style': ['toneStyle', 'ToneStyle'],
    'use-can-show-master-detail': ['MASTER_DETAIL_RAIL_QUERY', 'useFormFactor', 'useCanShowMasterDetail', 'useTouchSizing', 'useShowHoverAffordance', 'useCanSplitBuilderPanes', 'FormFactorState'],
    'use-is-mobile': ['BP_PHONE', 'BP_DOCK', 'useIsMobile', 'useCanShowRail'],
    breadcrumb: ['Breadcrumb', 'BreadcrumbItem', 'BreadcrumbProps'],
    'check-box': ['CheckBox', 'CheckBoxState', 'CheckBoxLabelPlacement', 'CheckBoxSize', 'CheckBoxProps'],
    collapsible: ['Collapsible', 'CollapsibleProps'],
    sheet: ['Sheet', 'SheetClose', 'SheetContent', 'SheetDescription', 'SheetFooter', 'SheetHeader', 'SheetTitle', 'SheetTrigger', 'SheetProps', 'SheetSide'],
    spotlight: ['Spotlight', 'SpotlightItem', 'SpotlightProps', 'SpotlightSection'],
    'text-area': ['TextArea', 'TextAreaFillMode', 'TextAreaProps', 'TextAreaResize', 'TextAreaRounded', 'TextAreaSize'],
    tooltip: ['Tooltip', 'TooltipProps', 'TooltipSide'],
    table: ['Table', 'TableBody', 'TableCaption', 'TableCell', 'TableHead', 'TableHeaderCell', 'TableRow', 'TableDensity', 'TableProps'],
    window: ['Window', 'WindowActionsBar', 'WindowActionsBarProps', 'WindowProps', 'WindowState'],
    'use-nav-collapsed': ['useNavCollapsed', 'UseNavCollapsedOptions', 'UseNavCollapsedResult'],
    'use-scroll-affordance': ['handleScrollAffordanceKeyDown', 'useScrollAffordance', 'ScrollAffordanceOrientation', 'ScrollAffordanceState', 'UseScrollAffordanceOptions'],
    'action-menu': ['ActionMenu', 'ActionMenuScopeContext', 'ActionMenuEntry', 'ActionMenuItem', 'ActionMenuProps'],
    'activity-log': ['ActivityLog', 'ActivityEntry', 'ActivityLogLayout', 'ActivityLogProps', 'ActivityTone'],
    chip: ['Chip', 'ChipFillMode', 'ChipProps', 'ChipRounded', 'ChipSize', 'ChipThemeColor'],
    'conversation-list': ['ConversationList', 'ConversationListLabels', 'ConversationListProps', 'ConversationSummary'],
    'guarded-control': ['GuardedControl', 'GuardedControlEvent', 'GuardedControlProps', 'GuardedControlState', 'GuardRecoveryReason'],
    input: ['Input', 'InputFillMode', 'InputProps', 'InputRounding', 'InputSize'],
    'layers-rail': ['LayersRail', 'LayersRailProps'],
    'notification-center': ['NotificationCenter', 'NotificationCenterItem', 'NotificationCenterLabels', 'NotificationCenterProps', 'NotificationGroup', 'NotificationKind'],
    page: ['Page', 'PageProps'],
    'search-input': ['SearchInput', 'SearchInputProps'],
    'segmented-control': ['SegmentedControl', 'SegmentedControlProps', 'SegmentedControlSize', 'SegmentedOption'],
    'select-field': ['SelectField', 'SelectFieldProps', 'SelectFieldSize', 'SelectOption'],
    switch: ['Switch', 'SwitchProps', 'SwitchSize'],
    toaster: ['Toaster', 'createToastService', 'ToastAction', 'ToastDefaults', 'ToastEntry', 'ToastOptions', 'ToastPosition', 'ToastService', 'ToastServiceOptions', 'ToastSnapshot', 'ToastVariant', 'ToasterProps', 'TrackAsyncMessages'],
    'user-menu': ['UserMenu', 'userInitials', 'UserIdentity', 'UserMenuActionItem', 'UserMenuCustomItem', 'UserMenuItem', 'UserMenuLabels', 'UserMenuLinkItem', 'UserMenuProps', 'UserMenuSeparatorItem'],
    'app-layout': ['APP_LAYOUT_RAIL_QUERY', 'AppLayout', 'AppLayoutProps', 'ContentScroll', 'SideNavMode'],
    'scroll-affordance': ['ScrollAffordance', 'ScrollAffordanceOrientation', 'ScrollAffordanceProps'],
    dialog: ['Dialog', 'DialogProps'],
    'side-nav': ['SideNav', 'SideNavGroup', 'SideNavItem', 'SideNavProps', 'SideNavStructure'],
    'switch-field': ['SwitchField', 'SwitchFieldProps'],
    'text-box': ['TextBox', 'TextBoxProps'],
    scheduler: ['Scheduler', 'SCHEDULER_RECURRENCE_CAP', 'expandRecurrence', 'parseRRule', 'SchedulerEvent', 'SchedulerModelFields', 'SchedulerProps', 'SchedulerView', 'SchedulerViewType'],
  }
  const missingNames = []
  let assertedNames = 0
  for (const [name, names] of Object.entries(expectedNames)) {
    const declaration = readFileSync(resolve(installed, `dist/${name}/index.d.ts`), 'utf8')
    for (const exportedName of names) {
      assertedNames += 1
      if (!new RegExp(`\\b${exportedName}\\b`).test(declaration)) missingNames.push(`${name}:${exportedName}`)
    }
  }
  if (missingNames.length) {
    throw new Error(`npm aggregate contribution type surface is incomplete: ${JSON.stringify({ missingNames })}`)
  }
  return assertedNames
}

// Ticket 275: every restore/pack/install below is budgeted and announced individually -- see
// runFixtureStep. Before this, a wedged restore among the ~20 serial ones was indistinguishable
// silence, and the outer budget could only ever say "the whole fixture step".
function run(executable, args, options = {}) {
  if (executable === dotnet.executable) args = [...args, '-nodeReuse:false', '-maxcpucount:6']
  return runFixtureStep(executable, args, { cwd: options.cwd ?? root, env: options.env })
}

function zipEntries(path) {
  const archive = readFileSync(path)
  let end = archive.length - 22
  const earliest = Math.max(0, archive.length - 65557)
  while (end >= earliest && archive.readUInt32LE(end) !== 0x06054b50) end -= 1
  if (end < earliest) throw new Error(`${path}: ZIP end record not found`)
  const entryCount = archive.readUInt16LE(end + 10)
  let cursor = archive.readUInt32LE(end + 16)
  const entries = []
  for (let index = 0; index < entryCount; index += 1) {
    if (archive.readUInt32LE(cursor) !== 0x02014b50) throw new Error(`${path}: invalid ZIP directory`)
    const method = archive.readUInt16LE(cursor + 10)
    const compressedSize = archive.readUInt32LE(cursor + 20)
    const uncompressedSize = archive.readUInt32LE(cursor + 24)
    const nameLength = archive.readUInt16LE(cursor + 28)
    const extraLength = archive.readUInt16LE(cursor + 30)
    const commentLength = archive.readUInt16LE(cursor + 32)
    const localOffset = archive.readUInt32LE(cursor + 42)
    const name = archive.subarray(cursor + 46, cursor + 46 + nameLength).toString('utf8')
    entries.push({ name, method, compressedSize, uncompressedSize, localOffset })
    cursor += 46 + nameLength + extraLength + commentLength
  }
  return {
    entries,
    text(name) {
      const entry = entries.find(candidate => candidate.name === name)
      if (!entry) throw new Error(`${path}: missing ${name}`)
      const nameLength = archive.readUInt16LE(entry.localOffset + 26)
      const extraLength = archive.readUInt16LE(entry.localOffset + 28)
      const start = entry.localOffset + 30 + nameLength + extraLength
      const compressed = archive.subarray(start, start + entry.compressedSize)
      if (entry.method === 0) return compressed.toString('utf8')
      if (entry.method === 8) return inflateRawSync(compressed).toString('utf8')
      throw new Error(`${path}: unsupported ZIP compression ${entry.method}`)
    },
  }
}

function metadata(nuspec, name) {
  return new RegExp(`<${name}(?:\\s[^>]*)?>([^<]+)</${name}>`).exec(nuspec)?.[1]
}

function sha256(path) {
  return createHash('sha256').update(readFileSync(path)).digest('hex')
}

// ---- capability-vertical helpers --------------------------------------------------------------
//
// Ticket 099 item 1. Eight verticals each wrote these fragments out longhand: six packed-artifact
// resolvers, five source-link checks, ten PackageReference reads, twelve source-or-project
// assertions.
//
// Five small helpers rather than one descriptor-driven runner, deliberately. Reading all eight, they
// are NOT one script: they differ in lane count, lane order (Aggregates runs NuGet first so the
// renderer can compare against the engine's timeline), proof-line count, verdict semantics, and
// whether a closure is asserted at all. A table would have to encode every one of those, and each
// vertical would then have to be read through the runner as well as its own row. What is genuinely
// one script written eight times is these fragments.

function packedArtifact(prefix, label) {
  const names = readdirSync(npmArtifacts).filter(name => name.startsWith(prefix) && name.endsWith('.tgz'))
  if (names.length !== 1) throw new Error(`${label} requires exactly one packed ${prefix}* artifact: ${JSON.stringify(names)}`)
  return resolve(npmArtifacts, names[0])
}

function installPackedArtifacts(consumer, prefixes, label) {
  run('npm', ['install', '--ignore-scripts', '--no-audit', '--no-fund', '--package-lock=false',
    ...prefixes.map(prefix => packedArtifact(prefix, label))], { cwd: consumer })
}

function assertPackedInstall(consumer, packageNames, label) {
  for (const name of packageNames) {
    const installed = resolve(consumer, `node_modules/@harborline-software/${name}`)
    if (lstatSync(installed).isSymbolicLink()) throw new Error(`${label} resolved @harborline-software/${name} as a source link`)
    const manifest = readFileSync(resolve(installed, 'package.json'), 'utf8')
    if (manifest.includes(root) || manifest.includes('file:') || manifest.includes('workspace:')) {
      throw new Error(`${label} package @harborline-software/${name} contains a local source reference`)
    }
  }
}

function assertDirectPackageReferences(consumer, expected, label) {
  const project = readFileSync(resolve(consumer, 'Consumer.csproj'), 'utf8')
  const direct = [...project.matchAll(/<PackageReference\s+Include="([^"]+)"/g)].map(match => match[1])
  if (JSON.stringify(direct) !== JSON.stringify(expected)) {
    throw new Error(`${label} direct package references drift: ${JSON.stringify({ direct, expected })}`)
  }
  return direct
}

function clientRun(consumer) {
  const output = run(process.execPath, ['exercise.mjs'], { cwd: consumer })
  return { output, lines: output.split('\n').map(line => line.trimEnd()) }
}

function runNugetConsumer(consumer, packageCache) {
  // The fixtures are copied to a temp directory, outside the reach of the repository's
  // Directory.Build.props, so the content-derived version reaches them on the command line. They
  // ask for it by name (Version="$(HarborlinePackedVersion)") rather than repeating a literal, so a
  // fixture can never pin a version the pack step no longer produces.
  const packedVersion = `-p:HarborlinePackedVersion=${computePackageVersion(root)}`
  run(dotnet.executable, ['restore', 'Consumer.csproj', '--source', nugetArtifacts, '--packages', packageCache, '--force', '--no-cache', packedVersion, '-v:minimal'], { cwd: consumer })
  const output = run(dotnet.executable, ['run', '--project', 'Consumer.csproj', '--no-restore', packedVersion, '-v:minimal'], {
    cwd: consumer,
    env: { NUGET_PACKAGES: packageCache },
  })
  return { output, lines: output.split('\n').map(line => line.trimEnd()) }
}

function proofLines({ lines, output }, prefixes, label) {
  const found = prefixes.map(prefix => lines.find(line => line.startsWith(prefix)))
  const absent = prefixes.filter((_, index) => !found[index])
  if (absent.length > 0) throw new Error(`${label} omitted proof lines ${JSON.stringify(absent)}: ${output}`)
  return found
}

// Split from assertPackageClosure on purpose. verifyWorkflowsCapability asserts no source or project
// dependency but declares NO expected closure -- a real coverage hole recorded in ticket 099, held
// out of item 1 so it lands with its own positive control rather than being quietly filled by a
// refactor. Keeping the two halves separate leaves that gap visible in the call site.
function assertNoSourceDependencies(consumer, label) {
  const assetsText = readFileSync(resolve(consumer, 'obj/project.assets.json'), 'utf8')
  if (assetsText.includes(resolve(root, 'projections')) || /"type"\s*:\s*"project"/.test(assetsText)) {
    throw new Error(`${label} resolved a source or project dependency`)
  }
  return assetsText
}

function assertPackageClosure(consumer, expected, label, pollution) {
  const target = Object.values(JSON.parse(assertNoSourceDependencies(consumer, label)).targets ?? {})[0] ?? {}
  const closure = Object.keys(target)
    .filter(key => /^Harborline\./.test(key))
    .map(key => key.split('/')[0])
    .sort()
  const expectedClosure = [...expected].sort()
  if (JSON.stringify(closure) !== JSON.stringify(expectedClosure)) {
    throw new Error(`${label} transitive package closure mismatch: ${JSON.stringify({ closure, expectedClosure })}`)
  }
  if (pollution && closure.some(id => pollution.test(id))) {
    throw new Error(`${label} closure-pollution FAILED condition: ${JSON.stringify(closure)}`)
  }
  return closure
}

function verifyNpm() {
  run('npm', ['run', 'build'], { cwd: reactRoot })
  const packed = JSON.parse(run('npm', ['pack', '--json', '--pack-destination', npmArtifacts], { cwd: reactRoot }))[0]
  const artifact = resolve(npmArtifacts, packed.filename)
  const names = new Set(packed.files.map(entry => entry.path))
  const required = [
    'README.md',
    'LICENSE',
    'package.json',
    'dist/index.js',
    'dist/index.cjs',
    'dist/index.d.ts',
    'dist/style.css',
    'dist/aspect-lens/index.d.ts',
    'dist/cn/cn.d.ts',
    'dist/cn/index.d.ts',
    'dist/default-strings/catalog.d.ts',
    'dist/default-strings/index.d.ts',
    'dist/error-card/ErrorCard.d.ts',
    'dist/error-card/index.d.ts',
    'dist/form-view/index.d.ts',
    'dist/loading-state/LoadingState.d.ts',
    'dist/loading-state/index.d.ts',
    'dist/rail-labels/index.d.ts',
    'dist/touch-target/index.d.ts',
    'dist/use-media-query/index.d.ts',
    'dist/use-media-query/useMediaQuery.d.ts',
    'dist/use-outside-click/index.d.ts',
    'dist/use-outside-click/useOutsideClick.d.ts',
    'dist/empty-state/EmptyState.d.ts',
    'dist/empty-state/index.d.ts',
    'dist/tone-style/index.d.ts',
    'dist/use-can-show-master-detail/index.d.ts',
    'dist/use-is-mobile/index.d.ts',
    'dist/breadcrumb/index.d.ts',
    'dist/check-box/index.d.ts',
    'dist/collapsible/index.d.ts',
    'dist/form-field/index.d.ts',
    'dist/date-field/index.d.ts',
    'dist/date-time-field/index.d.ts',
    'dist/number-field/index.d.ts',
    'dist/popover/index.d.ts',
    'dist/radio-group/index.d.ts',
    'dist/sheet/index.d.ts',
    'dist/spotlight/index.d.ts',
    'dist/text-area/index.d.ts',
    'dist/tooltip/index.d.ts',
    'dist/table/index.d.ts',
    'dist/window/index.d.ts',
    'dist/use-nav-collapsed/index.d.ts',
    'dist/use-scroll-affordance/index.d.ts',
    'dist/action-menu/index.d.ts',
    'dist/activity-log/index.d.ts',
    'dist/chip/index.d.ts',
    'dist/conversation-list/index.d.ts',
    'dist/guarded-control/index.d.ts',
    'dist/input/index.d.ts',
    'dist/layers-rail/index.d.ts',
    'dist/notification-center/index.d.ts',
    'dist/page/index.d.ts',
    'dist/search-input/index.d.ts',
    'dist/segmented-control/index.d.ts',
    'dist/select-field/index.d.ts',
    'dist/switch/index.d.ts',
    'dist/toaster/index.d.ts',
    'dist/user-menu/index.d.ts',
    'dist/app-layout/index.d.ts',
  ]
  const missing = required.filter(name => !names.has(name))
  const forbidden = packed.files.map(entry => entry.path).filter(name =>
    name.endsWith('.map') || /(^|\/)(?:src|tests|coverage|node_modules|\.cache)(\/|$)/.test(name))
  const manifest = JSON.parse(readFileSync(resolve(reactRoot, 'package.json'), 'utf8'))
  const localDependencies = Object.entries({
    ...manifest.dependencies,
    ...manifest.peerDependencies,
    ...manifest.optionalDependencies,
  }).filter(([, value]) => /^(?:file|link|portal|workspace):/.test(value))
  if (missing.length || forbidden.length || localDependencies.length
      || packed.size > budgets.npm.packedBytesMaximum
      || packed.unpackedSize > budgets.npm.unpackedBytesMaximum
      || manifest.name !== '@harborline-software/ui-react') {
    throw new Error(`npm package inspection failed: ${JSON.stringify({ missing, forbidden, localDependencies, packed })}`)
  }

  const consumer = resolve(fixtureRoot, 'npm-consumer')
  cpSync(resolve(root, 'tests/package-consumers/npm'), consumer, { recursive: true })
  const contractsArtifacts = readdirSync(npmArtifacts)
    .filter(name => name.startsWith('harborline-software-contracts-') && name.endsWith('.tgz'))
  if (contractsArtifacts.length !== 1) {
    throw new Error(`npm UI package proof requires one packed contracts peer: ${JSON.stringify(contractsArtifacts)}`)
  }
  const contractsArtifact = resolve(npmArtifacts, contractsArtifacts[0])
  const ruleEngineArtifacts = readdirSync(npmArtifacts)
    .filter(name => name.startsWith('harborline-software-rule-engine-') && name.endsWith('.tgz'))
  if (ruleEngineArtifacts.length !== 1) {
    throw new Error(`npm UI package proof requires one packed rule-engine peer: ${JSON.stringify(ruleEngineArtifacts)}`)
  }
  const ruleEngineArtifact = resolve(npmArtifacts, ruleEngineArtifacts[0])
  run('npm', [
    'install', '--ignore-scripts', '--no-audit', '--no-fund', '--package-lock=false',
    artifact,
    contractsArtifact,
    ruleEngineArtifact,
  ], { cwd: consumer })
  const output = run(process.execPath, ['exercise.mjs'], { cwd: consumer }).trim()
  const installed = resolve(consumer, 'node_modules/@harborline-software/ui-react')
  if (lstatSync(installed).isSymbolicLink()) throw new Error('npm consumer resolved a source link')
  const installedManifest = readFileSync(resolve(installed, 'package.json'), 'utf8')
  if (installedManifest.includes(root) || installedManifest.includes('file:') || installedManifest.includes('workspace:')) {
    throw new Error('npm consumer package contains a local source reference')
  }
  const installedCss = readFileSync(resolve(installed, 'dist/style.css'), 'utf8')
  assertPublicThemeSurface(installedCss, 'npm')
  assertContextMenuSurface(installedCss, 'npm')
  assertReactFeedbackSurface(installedCss, 'npm')
  const aggregateContributionTypeNames = assertNpmContributionTypeSurface(installed)
  run(process.execPath, [resolve(consumer, 'node_modules/typescript/bin/tsc'), '-p', 'tsconfig.json'], { cwd: consumer })
  return {
    id: 'ui-react-package',
    status: 'PASS',
    artifactIdentity: `${packed.name}@${packed.version}`,
    artifactSha256: sha256(artifact),
    packedBytes: packed.size,
    unpackedBytes: packed.unpackedSize,
    entryCount: packed.entryCount,
    budget: budgets.npm,
    localOrSiblingDependencies: 0,
    publicThemeTokens: publicThemeTokens.length,
    contextMenuPublicSurface: true,
    aggregateContributionTypeNames,
    aggregateContributionTypecheck: true,
    feedbackPublicSurface: true,
    consumerBehavior: output,
  }
}

function verifyCopilotContractsNpm() {
  const copilotRoot = resolve(root, 'projections/typescript/application/hlp.copilot.contracts')
  run('npm', ['install', '--ignore-scripts', '--no-audit', '--no-fund'], { cwd: copilotRoot })
  run('npm', ['run', 'build'], { cwd: copilotRoot })
  const packed = JSON.parse(run('npm', ['pack', '--json', '--pack-destination', npmArtifacts], { cwd: copilotRoot }))[0]
  const artifact = resolve(npmArtifacts, packed.filename)
  const names = new Set(packed.files.map(entry => entry.path))
  const required = ['README.md', 'LICENSE', 'package.json',
    'dist/index.js', 'dist/index.d.ts', 'dist/proposal.js', 'dist/proposal.d.ts',
    'dist/manifest.js', 'dist/manifest.d.ts', 'dist/types.js', 'dist/types.d.ts']
  const missing = required.filter(name => !names.has(name))
  const forbidden = packed.files.map(entry => entry.path).filter(name =>
    name.endsWith('.map') || /(^|\/)(?:src|tests|coverage|node_modules|\.cache)(\/|$)/.test(name))
  const manifest = JSON.parse(readFileSync(resolve(copilotRoot, 'package.json'), 'utf8'))
  const localDependencies = Object.entries({
    ...manifest.dependencies,
    ...manifest.peerDependencies,
    ...manifest.optionalDependencies,
  }).filter(([, value]) => /^(?:file|link|portal|workspace):/.test(value))
  if (missing.length || forbidden.length || localDependencies.length
      || manifest.name !== '@harborline-software/copilot-contracts') {
    throw new Error(`copilot contracts npm package inspection failed: ${JSON.stringify({ missing, forbidden, localDependencies })}`)
  }

  const consumer = resolve(fixtureRoot, 'copilot-contracts-npm-consumer')
  cpSync(resolve(root, 'tests/package-consumers/copilot-contracts-npm'), consumer, { recursive: true })
  run('npm', ['install', '--ignore-scripts', '--no-audit', '--no-fund', '--package-lock=false', artifact], { cwd: consumer })
  run(process.execPath, ['exercise.mjs'], { cwd: consumer })
  const installed = resolve(consumer, 'node_modules/@harborline-software/copilot-contracts')
  if (lstatSync(installed).isSymbolicLink()) throw new Error('copilot contracts npm consumer resolved a source link')
  const installedManifest = readFileSync(resolve(installed, 'package.json'), 'utf8')
  if (installedManifest.includes(root) || installedManifest.includes('file:') || installedManifest.includes('workspace:')) {
    throw new Error('copilot contracts npm consumer package contains a local source reference')
  }
  return {
    id: 'copilot-contracts-npm',
    packageId: manifest.name,
    artifact: relative(root, artifact).replaceAll('\\', '/'),
    packedFiles: packed.files.length,
    adapterSeamExported: true,
  }
}

function verifyFormsNpm() {
  run('npm', ['run', 'build'], { cwd: formsRoot })
  const packed = JSON.parse(run('npm', ['pack', '--json', '--pack-destination', npmArtifacts], { cwd: formsRoot }))[0]
  const artifact = resolve(npmArtifacts, packed.filename)
  const names = new Set(packed.files.map(entry => entry.path))
  const required = ['README.md', 'LICENSE', 'package.json', 'dist/index.js', 'dist/index.d.ts', 'dist/forms.js', 'dist/forms.d.ts', 'dist/authorization.js', 'dist/authorization.d.ts', 'dist/wire.js', 'dist/wire.d.ts', 'dist/workflow.js', 'dist/workflow.d.ts', 'dist/workflow-wire.js', 'dist/workflow-wire.d.ts', 'dist/workflow-admission.js', 'dist/workflow-admission.d.ts']
  const missing = required.filter(name => !names.has(name))
  const forbidden = packed.files.map(entry => entry.path).filter(name =>
    name.endsWith('.map') || /(^|\/)(?:src|tests|coverage|node_modules|\.cache)(\/|$)/.test(name))
  const manifest = JSON.parse(readFileSync(resolve(formsRoot, 'package.json'), 'utf8'))
  const localDependencies = Object.entries({
    ...manifest.dependencies,
    ...manifest.peerDependencies,
    ...manifest.optionalDependencies,
  }).filter(([, value]) => /^(?:file|link|portal|workspace):/.test(value))
  if (missing.length || forbidden.length || localDependencies.length
      || packed.size > budgets.npm.packedBytesMaximum
      || packed.unpackedSize > budgets.npm.unpackedBytesMaximum
      || manifest.name !== '@harborline-software/contracts') {
    throw new Error(`forms npm package inspection failed: ${JSON.stringify({ missing, forbidden, localDependencies, packed })}`)
  }

  const consumer = resolve(fixtureRoot, 'forms-npm-consumer')
  mkdirSync(consumer, {recursive: true})
  writeFileSync(resolve(consumer, 'package.json'), '{"private":true,"type":"module"}\n')
  writeFileSync(resolve(consumer, 'exercise.mjs'), `import assert from 'node:assert/strict'\nimport {createWorkflowAuthorityResolver, formsWireSurface, readFormsWire, readWorkflowWire, RoleVocabulary, roleGateAllows, HARBORLINE_JSONLOGIC_V1, validateWorkflowAdmission} from '@harborline-software/contracts'\nconst item = readFormsWire('FormItem', {kind:'collection',key:'photos',items:[{kind:'field',key:'photo'}]})\nassert.equal(item.items[0].key, 'photo')\nassert.equal(HARBORLINE_JSONLOGIC_V1, 'harborline-jsonlogic/v1')\nassert.deepEqual(formsWireSurface.closedValues.OutputType, ['Value','Validity','Visibility','Presentation','Options'])\nconst options = readFormsWire('RuleOutcome', {ruleId:'opt.result',target:'field:result',outputType:'Options',options:{state:'Resolved',options:['PASS','FAIL']}})\nassert.deepEqual(options.options.options, ['PASS','FAIL'])\nconst definition = readFormsWire('FormDefinition', {id:'inspection',version:'1.0.0',status:'Published',tenant:'acme',owner:{scheme:'system',value:'harborline'},schemaRef:'sha256:test',overlay:{fields:{},sections:[],rules:[]},createdAt:'2026-08-08T00:00:00Z',updatedAt:'2026-08-08T00:00:00Z',fieldsMeta:{condition:{type:'radio',required:true,validations:[{code:'required'}],options:['PASS','FAIL']}}})\nassert.equal(definition.fieldsMeta.condition.type, 'radio')\nconst workflow = readWorkflowWire('WorkflowDefinition', {key:'consumer-workflow',version:'1.0.0',status:'Draft',tenant:'acme',owner:{scheme:'system',value:'harborline'},title:{defaultLocale:'en',values:{en:'Consumer workflow'}},mutability:'Locked',initialState:'Draft',states:[{id:'Draft',label:{defaultLocale:'en',values:{en:'Draft'}},kind:'Normal'},{id:'Done',label:{defaultLocale:'en',values:{en:'Done'}},kind:'Terminal'}],transitions:[{id:'complete',from:'Draft',on:'approve',to:'Done'}],triggers:[{id:'approve',kind:'HumanAction',task:'approval'}],actions:[{id:'notify',on:{transition:'complete'},kind:'Notify',capabilityRef:'notify.email',classification:'AP'}],guards:[],createdAt:'2026-08-09T00:00:00Z',updatedAt:'2026-08-09T00:00:00Z'})\nconst auditor={vocabulary:'sys.platform-roles',name:'auditor'}\nconst vocabulary=RoleVocabulary.fromApi([{roleDefinitionId:'3b69e3cb-ec5e-4ad7-8c90-16ebc1896102',role:auditor,displayName:'Auditor',owner:{kind:'Platform',ownerId:'platform'},isSealed:true}])\nassert.equal(roleGateAllows({requiredRoles:[auditor]},vocabulary,{roles:[auditor]}),true)\nassert.equal(validateWorkflowAdmission(workflow, createWorkflowAuthorityResolver({'notify.email':'AP'}), vocabulary).isValid, true)\nassert.throws(() => readFormsWire('FormItem', {kind:'script',key:'unsafe'}), /unknown-discriminator/)\nprocess.stdout.write('packed npm Dynamic Forms revision-5, Workflow revision-2, and Authorization revision-1 contracts passed wire and admission checks\\n')\n`)
  const exercisePath = resolve(consumer, 'exercise.mjs')
  const exercise = readFileSync(exercisePath, 'utf8')
    .replace(
      "assert.throws(() => readFormsWire('FormItem'",
      "const field = readFormsWire('FormViewField', {name:'total',label:{defaultLocale:'en',values:{en:'Total'}},isSensitive:false,isReadable:true,rules:{visible:true,required:false,readOnly:true,computed:10}})\nassert.equal(field.rules.computed, 10)\nassert.throws(() => readFormsWire('FormItem'",
    )
    .replace('revision-3 contract', 'revision-4 contract')
  writeFileSync(exercisePath, exercise)
  run('npm', ['install', '--ignore-scripts', '--no-audit', '--no-fund', '--package-lock=false', artifact], { cwd: consumer })
  const output = run(process.execPath, ['exercise.mjs'], { cwd: consumer }).trim()
  const installed = resolve(consumer, 'node_modules/@harborline-software/contracts')
  if (lstatSync(installed).isSymbolicLink()) throw new Error('forms npm consumer resolved a source link')
  const installedManifest = readFileSync(resolve(installed, 'package.json'), 'utf8')
  if (installedManifest.includes(root) || installedManifest.includes('file:') || installedManifest.includes('workspace:')) {
    throw new Error('forms npm consumer package contains a local source reference')
  }
  return {
    id: 'forms-contracts-package',
    status: 'PASS',
    artifactIdentity: `${packed.name}@${packed.version}`,
    artifactSha256: sha256(artifact),
    packedBytes: packed.size,
    unpackedBytes: packed.unpackedSize,
    entryCount: packed.entryCount,
    budget: budgets.npm,
    localOrSiblingDependencies: 0,
    consumerBehavior: output,
  }
}

function verifyRuleRuntimeNpm() {
  run('pnpm', ['run', 'build'], { cwd: ruleRuntimeRoot })
  const packed = JSON.parse(run('npm', ['pack', '--json', '--pack-destination', npmArtifacts], { cwd: ruleRuntimeRoot }))[0]
  const artifact = resolve(npmArtifacts, packed.filename)
  const names = new Set(packed.files.map(entry => entry.path))
  const required = ['README.md', 'LICENSE', 'package.json', 'dist/index.js', 'dist/index.d.ts']
  const missing = required.filter(name => !names.has(name))
  const forbidden = packed.files.map(entry => entry.path).filter(name =>
    name.endsWith('.map') || /(^|\/)(?:src|tests|coverage|node_modules|\.cache)(\/|$)/.test(name))
  const manifest = JSON.parse(readFileSync(resolve(ruleRuntimeRoot, 'package.json'), 'utf8'))
  const localDependencies = Object.entries({
    ...manifest.dependencies,
    ...manifest.peerDependencies,
    ...manifest.optionalDependencies,
  }).filter(([, value]) => /^(?:file|link|portal|workspace):/.test(value))
  if (missing.length || forbidden.length || localDependencies.length
      || packed.size > budgets.npm.packedBytesMaximum
      || packed.unpackedSize > budgets.npm.unpackedBytesMaximum
      || manifest.name !== '@harborline-software/rule-engine') {
    throw new Error(`rule-runtime npm package inspection failed: ${JSON.stringify({ missing, forbidden, localDependencies, packed })}`)
  }

  const consumer = resolve(fixtureRoot, 'rule-runtime-npm-consumer')
  mkdirSync(consumer, {recursive: true})
  writeFileSync(resolve(consumer, 'package.json'), '{"private":true,"type":"module"}\n')
  writeFileSync(resolve(consumer, 'exercise.mjs'), `import assert from 'node:assert/strict'\nimport {compile, FormRuleGraph, RuleInstance, serializeOutcome} from '@harborline-software/rule-engine'\nconst rule = {id:'opt.result',tier:'JsonLogic',scope:'Field',scopeTarget:'result',expression:['PASS','FAIL'],action:'Options'}\nconst result = new FormRuleGraph(compile([rule])).evaluateInstance(RuleInstance.fromJson({}))\nassert.equal(serializeOutcome(result.byRule.get('opt.result')), '{"options":{"options":["PASS","FAIL"],"state":"Resolved"},"outputType":"Options","ruleId":"opt.result","target":"field:result"}')\nprocess.stdout.write('packed npm reactive Rule Runtime emitted canonical Options outcome\\n')\n`)
  run('npm', ['install', '--ignore-scripts', '--no-audit', '--no-fund', '--package-lock=false', artifact], { cwd: consumer })
  const output = run(process.execPath, ['exercise.mjs'], { cwd: consumer }).trim()
  const installed = resolve(consumer, 'node_modules/@harborline-software/rule-engine')
  if (lstatSync(installed).isSymbolicLink()) throw new Error('rule-runtime npm consumer resolved a source link')
  const installedManifest = readFileSync(resolve(installed, 'package.json'), 'utf8')
  if (installedManifest.includes(root) || installedManifest.includes('file:') || installedManifest.includes('workspace:')) {
    throw new Error('rule-runtime npm consumer package contains a local source reference')
  }
  return {
    id: 'rule-runtime-package',
    status: 'PASS',
    artifactIdentity: `${packed.name}@${packed.version}`,
    artifactSha256: sha256(artifact),
    packedBytes: packed.size,
    unpackedBytes: packed.unpackedSize,
    entryCount: packed.entryCount,
    budget: budgets.npm,
    localOrSiblingDependencies: 0,
    consumerBehavior: output,
  }
}

function verifyRuleAuthoringNpm() {
  run('pnpm', ['run', 'build'], { cwd: ruleAuthoringRoot })
  const packed = JSON.parse(run('npm', ['pack', '--json', '--pack-destination', npmArtifacts], { cwd: ruleAuthoringRoot }))[0]
  const artifact = resolve(npmArtifacts, packed.filename)
  const names = new Set(packed.files.map(entry => entry.path))
  const required = ['README.md', 'LICENSE', 'package.json', 'dist/index.js', 'dist/index.d.ts']
  const missing = required.filter(name => !names.has(name))
  const forbidden = packed.files.map(entry => entry.path).filter(name =>
    name.endsWith('.map') || /(^|\/)(?:src|tests|coverage|node_modules|\.cache)(\/|$)/.test(name))
  const manifest = JSON.parse(readFileSync(resolve(ruleAuthoringRoot, 'package.json'), 'utf8'))
  const localDependencies = Object.entries({
    ...manifest.dependencies,
    ...manifest.peerDependencies,
    ...manifest.optionalDependencies,
  }).filter(([, value]) => /^(?:file|link|portal|workspace):/.test(value))
  if (missing.length || forbidden.length || localDependencies.length
      || packed.size > budgets.npm.packedBytesMaximum
      || packed.unpackedSize > budgets.npm.unpackedBytesMaximum
      || manifest.name !== '@harborline-software/rule-authoring') {
    throw new Error(`rule-authoring npm package inspection failed: ${JSON.stringify({ missing, forbidden, localDependencies, packed })}`)
  }

  const consumer = resolve(fixtureRoot, 'rule-authoring-npm-consumer')
  mkdirSync(consumer, {recursive: true})
  const packedEngine = (() => {
    const engineNames = readdirSync(npmArtifacts).filter(name => name.startsWith('harborline-software-rule-engine-') && name.endsWith('.tgz'))
    if (engineNames.length !== 1) throw new Error(`rule-authoring consumer requires exactly one packed rule-engine artifact: ${JSON.stringify(engineNames)}`)
    return resolve(npmArtifacts, engineNames[0])
  })()
  writeFileSync(resolve(consumer, 'package.json'), '{"private":true,"type":"module"}\n')
  writeFileSync(resolve(consumer, 'exercise.mjs'), `import assert from 'node:assert/strict'\nimport {blankTableDraft, InMemoryRuleCatalogStore, publishRule, RuleCatalog} from '@harborline-software/rule-authoring'\nconst catalog = new RuleCatalog(new InMemoryRuleCatalogStore())\nconst blank = blankTableDraft()\nawait catalog.createRule({ruleKey: 'route', name: 'Route', skinType: 'table', draft: blank})\nconst refused = await publishRule(catalog, 'route', blank)\nassert.deepEqual(refused, {ok: false, code: 'rule.skin.no_match_unresolved', message: 'no-match is unresolved'})\nconst resolvedDraft = {...blank, rows: [{id: 'r1', cells: {[blank.columns[0].id]: {kind: 'range', lo: '0', hi: '100'}}, output: 'low', priority: 0}], noMatch: {kind: 'default', value: 'high'}}\nconst published = await publishRule(catalog, 'route', resolvedDraft)\nassert.equal(published.ok && published.version, '1.0.0')\nprocess.stdout.write('packed npm authoring bridge refused the unresolved no-match and minted 1.0.0 through the fence\\n')\n`)
  run('npm', ['install', '--ignore-scripts', '--no-audit', '--no-fund', '--package-lock=false', artifact, packedEngine], { cwd: consumer })
  const output = run(process.execPath, ['exercise.mjs'], { cwd: consumer }).trim()
  const installed = resolve(consumer, 'node_modules/@harborline-software/rule-authoring')
  if (lstatSync(installed).isSymbolicLink()) throw new Error('rule-authoring npm consumer resolved a source link')
  const installedManifest = readFileSync(resolve(installed, 'package.json'), 'utf8')
  if (installedManifest.includes(root) || installedManifest.includes('file:') || installedManifest.includes('workspace:')) {
    throw new Error('rule-authoring npm consumer package contains a local source reference')
  }
  return {
    id: 'rule-authoring-package',
    status: 'PASS',
    artifactIdentity: `${packed.name}@${packed.version}`,
    artifactSha256: sha256(artifact),
    packedBytes: packed.size,
    unpackedBytes: packed.unpackedSize,
    entryCount: packed.entryCount,
    budget: budgets.npm,
    localOrSiblingDependencies: 0,
    consumerBehavior: output,
  }
}

function verifyNuget() {
  // Stamp the version BEFORE packing: Directory.Build.props imports this file, so every `dotnet
  // pack` below carries a version derived from the sources it is packing. Two packs of the same
  // tree agree; any edit to a packaged source produces a version that is not in anyone's
  // global-packages folder, which is what forces NuGet back to the feed.
  const { version: packedVersion } = writePackageVersionProps(root)
  const contracts = 'projections/dotnet/contracts/hlp.contracts.identities/Harborline.Contracts.csproj'
  const tenancy = 'projections/dotnet/foundation/hlp.foundation.tenancy/Harborline.Foundation.MultiTenancy.csproj'
  const actor = 'projections/dotnet/foundation/hlp.foundation.actor/Harborline.Foundation.Authorization.csproj'
  const session = 'projections/dotnet/foundation/hlp.foundation.session/Harborline.Foundation.Session.csproj'
  const schemaValidation = 'projections/dotnet/kernel/hlp.kernel.schema-validation/Harborline.Kernel.SchemaValidation.csproj'
  const workItems = 'projections/dotnet/kernel/hlp.kernel.work-items/Harborline.Kernel.WorkItems.csproj'
  const inspectionReview = 'projections/dotnet/blocks/hlp.blocks.inspection-review/Harborline.Blocks.InspectionReview.csproj'
  const builderDefinitions = 'projections/dotnet/blocks/hlp.blocks.builder-definitions/Harborline.Blocks.BuilderDefinitions.csproj'
  const aggregates = 'projections/dotnet/blocks/hlp.blocks.aggregates/Harborline.Blocks.Aggregates.csproj'
  const relativeChains = 'projections/dotnet/blocks/hlp.blocks.relative-chains/Harborline.Blocks.RelativeChains.csproj'
  const workflow = 'projections/dotnet/blocks/hlp.blocks.workflow/Harborline.Blocks.Workflow.csproj'
  const workflowInterpreter = 'projections/dotnet/blocks/hlp.blocks.workflow-interpreter/Harborline.Blocks.Workflow.Interpreter.csproj'
  const entityViews = 'projections/dotnet/blocks/hlp.blocks.entity-views/Harborline.Blocks.EntityViews.csproj'
  const foundationScheduling = 'projections/dotnet/foundation/hlp.foundation.scheduling/Harborline.Foundation.Scheduling.csproj'
  const blocksScheduling = 'projections/dotnet/blocks/hlp.blocks.scheduling/Harborline.Blocks.Scheduling.csproj'
  const blocksCalendar = 'projections/dotnet/blocks/hlp.blocks.calendar/Harborline.Blocks.Calendar.csproj'
  const blocksReports = 'projections/dotnet/blocks/hlp.blocks.reports/Harborline.Blocks.Reports.csproj'
  const blocksActivityTimeline = 'projections/dotnet/blocks/hlp.blocks.activity-timeline/Harborline.Blocks.ActivityTimeline.csproj'
  const ruleRuntime = 'projections/dotnet/foundation/hlp.foundation.rule-runtime/Harborline.Foundation.RuleEngine.csproj'
  const ruleAuthoring = 'projections/dotnet/foundation/hlp.foundation.rule-authoring/Harborline.Foundation.RuleAuthoring.csproj'
  const formsState = 'projections/dotnet/foundation/hlp.foundation.forms/Harborline.Foundation.Forms.csproj'
  const formsEngine = 'projections/dotnet/foundation/hlp.foundation.forms-engine/Harborline.Foundation.Forms.Engine.csproj'
  const foundation = 'projections/dotnet/foundation/hlp.ui.button/Harborline.Foundation.csproj'
  const ui = 'projections/blazor/ui/hlp.ui.button/Harborline.UIAdapters.Blazor.csproj'
  run(dotnet.executable, ['pack', contracts, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', tenancy, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', actor, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', session, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', schemaValidation, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', workItems, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', inspectionReview, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', builderDefinitions, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', aggregates, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', relativeChains, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', workflow, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', workflowInterpreter, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', entityViews, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', foundationScheduling, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', blocksScheduling, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', blocksCalendar, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', blocksReports, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', blocksActivityTimeline, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', ruleRuntime, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', ruleAuthoring, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', formsState, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', formsEngine, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', foundation, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  run(dotnet.executable, ['pack', ui, '--configuration', 'Release', '--output', nugetArtifacts, '-v:minimal'])
  const packages = readdirSync(nugetArtifacts).filter(name => name.endsWith('.nupkg') && !name.endsWith('.symbols.nupkg'))
  const packageMetadata = packages.map(name => {
    const path = resolve(nugetArtifacts, name)
    const zip = zipEntries(path)
    const nuspecName = zip.entries.find(entry => entry.name.endsWith('.nuspec'))?.name
    const nuspec = zip.text(nuspecName)
    return { name, path, zip, nuspec, id: metadata(nuspec, 'id'), version: metadata(nuspec, 'version') }
  })
  const expectedIds = ['Harborline.Blocks.ActivityTimeline', 'Harborline.Blocks.Aggregates', 'Harborline.Blocks.BuilderDefinitions', 'Harborline.Blocks.Calendar', 'Harborline.Blocks.EntityViews', 'Harborline.Blocks.InspectionReview', 'Harborline.Blocks.RelativeChains', 'Harborline.Blocks.Reports', 'Harborline.Blocks.Scheduling', 'Harborline.Blocks.Workflow', 'Harborline.Blocks.Workflow.Interpreter', 'Harborline.Foundation', 'Harborline.Foundation.Forms.Engine', 'Harborline.Foundation.MultiTenancy', 'Harborline.Foundation.RuleAuthoring', 'Harborline.Foundation.RuleEngine', 'Harborline.Foundation.Scheduling', 'Harborline.Kernel.SchemaValidation', 'Harborline.Kernel.WorkItems', 'Harborline.UIAdapters.Blazor', 'Harborline.Contracts', 'Harborline.Foundation.Authorization', 'Harborline.Foundation.Forms', 'Harborline.Foundation.Session']
  const actualIds = packageMetadata.map(entry => entry.id).sort()
  if (JSON.stringify(actualIds) !== JSON.stringify(expectedIds.sort())) {
    throw new Error(`NuGet artifact ownership mismatch: ${JSON.stringify(actualIds)}`)
  }
  const packageIds = new Set(actualIds)
  const missingDependencies = [...new Set(packageMetadata.flatMap(entry =>
    [...entry.nuspec.matchAll(/<dependency\s+id="(Harborline\.[^"]+)"/g)].map(match => match[1])))]
    .filter(id => !packageIds.has(id))
  if (missingDependencies.length) throw new Error(`NuGet feed lacks dependencies: ${missingDependencies.join(', ')}`)

  const inspections = packageMetadata.map(entry => {
    const names = entry.zip.entries.map(item => item.name)
    const targetFrameworks = [...new Set([...entry.nuspec.matchAll(/targetFramework="([^"]+)"/g)].map(match => match[1]))]
    const assemblyFrameworks = [...new Set(names.filter(name => /^lib\/[^/]+\/[^/]+\.dll$/i.test(name)).map(name => name.split('/')[1]))]
    if (JSON.stringify(targetFrameworks) !== '["net10.0"]' || JSON.stringify(assemblyFrameworks) !== '["net10.0"]') {
      throw new Error(`NuGet target framework mismatch for ${entry.id}: ${JSON.stringify({ targetFrameworks, assemblyFrameworks })}`)
    }
    const packedBytes = readFileSync(entry.path).length
    const unpackedBytes = entry.zip.entries.reduce((total, item) => total + item.uncompressedSize, 0)
    const forbidden = names.filter(name => name.endsWith('.map') || /(^|\/)(?:src|tests|bin|obj|\.cache)(\/|$)/.test(name))
    if (!names.includes('README.md') || !names.includes('LICENSE') || forbidden.length
        || packedBytes > budgets.nuget.packedBytesMaximum || unpackedBytes > budgets.nuget.unpackedBytesMaximum
        || metadata(entry.nuspec, 'license') !== 'Apache-2.0') {
      throw new Error(`NuGet inspection failed for ${entry.id}: ${JSON.stringify({ names, forbidden, packedBytes, unpackedBytes })}`)
    }
    if (entry.id === 'Harborline.UIAdapters.Blazor') {
      const cssNames = names.filter(name => name.startsWith('staticwebassets/') && name.endsWith('.css'))
      if (!cssNames.length) throw new Error('Blazor NuGet package lacks its public static-web-asset stylesheets')
      if (!names.includes('staticwebassets/context-menu.js')) throw new Error('Blazor NuGet package lacks the Context Menu DOM bridge')
      if (!names.includes('staticwebassets/popover.js')) throw new Error('Blazor NuGet package lacks the Popover DOM bridge')
      for (const bridge of ['sheet.js', 'spotlight.js', 'window.js']) {
        if (!names.includes(`staticwebassets/${bridge}`)) throw new Error(`Blazor NuGet package lacks the ${bridge} wave-02-04 DOM bridge`)
      }
      for (const bridge of ['media-query.js', 'outside-pointer.js']) {
        if (!names.includes(`staticwebassets/${bridge}`)) throw new Error(`Blazor NuGet package lacks the ${bridge} browser bridge`)
      }
      for (const bridge of ['scroll-affordance.js', 'action-menu.js', 'conversation-list.js']) {
        if (!names.includes(`staticwebassets/${bridge}`)) throw new Error(`Blazor NuGet package lacks the ${bridge} wave-03-02 DOM bridge`)
      }
      for (const bridge of ['guarded-control.js', 'layers-rail.js', 'notification-center.js']) {
        if (!names.includes(`staticwebassets/${bridge}`)) throw new Error(`Blazor NuGet package lacks the ${bridge} wave-03-03 DOM bridge`)
      }
      for (const bridge of ['segmented-control.js', 'select-field.js', 'user-menu.js', 'app-layout.js']) {
        if (!names.includes(`staticwebassets/${bridge}`)) throw new Error(`Blazor NuGet package lacks the ${bridge} wave-03-04 DOM bridge`)
      }
      for (const bridge of ['scroll-affordance-component.js', 'dialog.js']) {
        if (!names.includes(`staticwebassets/${bridge}`)) throw new Error(`Blazor NuGet package lacks the ${bridge} wave-04-01 DOM bridge`)
      }
      const publicCss = cssNames.map(name => entry.zip.text(name)).join('\n')
      assertPublicThemeSurface(publicCss, 'nuget')
      assertContextMenuSurface(publicCss, 'nuget')
      assertBlazorFeedbackSurface(publicCss, 'nuget')
    }
    const assemblies = names
      .filter(name => /^lib\/[^/]+\/[^/]+\.dll$/i.test(name))
      .map(name => name.split('/').at(-1).replace(/\.dll$/i, ''))
      .sort()
    return {
      id: entry.id,
      version: entry.version,
      artifactSha256: sha256(entry.path),
      assemblies,
      targetFrameworks,
      packedBytes,
      unpackedBytes,
      entryCount: names.length,
    }
  })
  const assemblyOwners = new Map()
  for (const inspection of inspections) {
    for (const assembly of inspection.assemblies) {
      const owners = assemblyOwners.get(assembly) ?? []
      owners.push(inspection.id)
      assemblyOwners.set(assembly, owners)
    }
  }
  const ambiguousAssemblies = [...assemblyOwners].filter(([, owners]) => owners.length > 1)
  if (ambiguousAssemblies.length) throw new Error(`NuGet assembly ambiguity: ${JSON.stringify(ambiguousAssemblies)}`)

  const consumer = resolve(fixtureRoot, 'nuget-consumer')
  const packageCache = resolve(fixtureRoot, 'nuget-packages')
  for (const [id, version] of [
    ['jsonschema.net', '9.2.2'],
    ['jsonpointer.net', '7.0.1'],
    ['json.more.net', '3.0.1'],
    ['humanizer.core', '3.0.10'],
    ['microsoft.extensions.dependencyinjection.abstractions', '10.0.10'],
    ['microsoft.extensions.dependencyinjection', '10.0.10'],
    ['microsoft.extensions.logging.abstractions', '10.0.10'],
    ['microsoft.extensions.dependencyinjection.abstractions', '11.0.0-preview.7.26381.103'],
    ['microsoft.extensions.dependencyinjection', '11.0.0-preview.7.26381.103'],
  ]) {
    const packagePath = resolve(globalNugetPackages, id, version, `${id}.${version}.nupkg`)
    const artifactPath = resolve(nugetArtifacts, `${id}.${version}.nupkg`)
    if (statSync(packagePath, { throwIfNoEntry: false })?.isFile()) {
      copyFileSync(packagePath, artifactPath)
    } else {
      // The M.E.DI pair is framework-implicit on the matching SDK, so no restore on a CLEAN
      // machine ever lays its nupkg into the global cache - the cache copy above only works on
      // developer machines that restored it under an older SDK. Fetch the exact pinned version
      // from the nuget.org flat container instead; the offline consumer restore below remains
      // the actual assertion, this only furnishes the feed.
      run(process.execPath, ['-e',
        'const [u, d] = process.argv.slice(1); fetch(u).then(r => { if (!r.ok) throw new Error(`${r.status} ${u}`); return r.arrayBuffer() }).then(b => require("node:fs").writeFileSync(d, Buffer.from(b))).catch(e => { console.error(e); process.exit(1) })',
        `https://api.nuget.org/v3-flatcontainer/${id}/${version}/${id}.${version}.nupkg`, artifactPath])
      if (!statSync(artifactPath, { throwIfNoEntry: false })?.isFile()) {
        throw new Error(`third-party dependency package is unavailable locally and from nuget.org: ${id}/${version}`)
      }
    }
  }
  cpSync(resolve(root, 'tests/package-consumers/nuget'), consumer, { recursive: true })
  const output = runNugetConsumer(consumer, packageCache).output.trim().split('\n').at(-1)
  const assets = readFileSync(resolve(consumer, 'obj/project.assets.json'), 'utf8')
  if (assets.includes(resolve(root, 'projections')) || /"type"\s*:\s*"project"/.test(assets)) {
    throw new Error('NuGet consumer resolved a source or project dependency')
  }
  const assetDocument = JSON.parse(assets)
  const target = Object.values(assetDocument.targets ?? {})[0] ?? {}
  const harborlineNodes = Object.keys(target).filter(key => /^Harborline\./.test(key))
  const expectedNodes = inspections.map(entry => `${entry.id}/${entry.version}`).sort()
  if (JSON.stringify(harborlineNodes.sort()) !== JSON.stringify(expectedNodes)) {
    throw new Error(`NuGet consumer Harborline closure mismatch: ${JSON.stringify({ harborlineNodes, expectedNodes })}`)
  }

  const formsEngineConsumer = resolve(fixtureRoot, 'forms-engine-nuget-consumer')
  const formsEnginePackageCache = resolve(fixtureRoot, 'forms-engine-nuget-packages')
  cpSync(resolve(root, 'tests/package-consumers/forms-engine-nuget'), formsEngineConsumer, { recursive: true })
  const formsEngineConsumerProject = readFileSync(resolve(formsEngineConsumer, 'Consumer.csproj'), 'utf8')
  const formsEngineDirectReferences = [...formsEngineConsumerProject.matchAll(/<PackageReference\s+Include="([^"]+)"/g)]
    .map(match => match[1])
  if (JSON.stringify(formsEngineDirectReferences) !== JSON.stringify(['Harborline.Foundation.Forms.Engine'])) {
    throw new Error(`Forms Engine consumer must reference only Harborline.Foundation.Forms.Engine: ${JSON.stringify(formsEngineDirectReferences)}`)
  }
  const formsEngineConsumerOutputText = runNugetConsumer(formsEngineConsumer, formsEnginePackageCache).output
  const formsEngineConsumerOutput = formsEngineConsumerOutputText
    .split('\n')
    .find(line => line.startsWith('PACKAGE_HOST_PASS:'))
  if (!formsEngineConsumerOutput) {
    throw new Error(`Forms Engine package-only authoring host did not emit its completion proof: ${formsEngineConsumerOutputText}`)
  }
  const formsEngineAssetsText = readFileSync(resolve(formsEngineConsumer, 'obj/project.assets.json'), 'utf8')
  if (formsEngineAssetsText.includes(resolve(root, 'projections')) || /"type"\s*:\s*"project"/.test(formsEngineAssetsText)) {
    throw new Error('Forms Engine NuGet consumer resolved a source or project dependency')
  }
  const formsEngineAssets = JSON.parse(formsEngineAssetsText)
  const formsEngineTarget = Object.values(formsEngineAssets.targets ?? {})[0] ?? {}
  const formsEngineHarborlineNodes = Object.keys(formsEngineTarget)
    .filter(key => /^Harborline\./.test(key))
    .sort()
  const formsEngineExpectedIds = [
    'Harborline.Foundation.Forms.Engine',
    'Harborline.Foundation.MultiTenancy',
    'Harborline.Foundation.RuleEngine',
    'Harborline.Kernel.SchemaValidation',
    'Harborline.Contracts',
    'Harborline.Foundation.Authorization',
    'Harborline.Foundation.Forms',
  ]
  const formsEngineExpectedNodes = inspections
    .filter(entry => formsEngineExpectedIds.includes(entry.id))
    .map(entry => `${entry.id}/${entry.version}`)
    .sort()
  if (JSON.stringify(formsEngineHarborlineNodes) !== JSON.stringify(formsEngineExpectedNodes)) {
    throw new Error(`Forms Engine transitive package closure mismatch: ${JSON.stringify({ formsEngineHarborlineNodes, formsEngineExpectedNodes })}`)
  }

  const inspectionReviewConsumer = resolve(fixtureRoot, 'inspection-review-nuget-consumer')
  const inspectionReviewPackageCache = resolve(fixtureRoot, 'inspection-review-nuget-packages')
  cpSync(resolve(root, 'tests/package-consumers/inspection-review-nuget'), inspectionReviewConsumer, { recursive: true })
  const inspectionReviewConsumerProject = readFileSync(resolve(inspectionReviewConsumer, 'Consumer.csproj'), 'utf8')
  const inspectionReviewDirectReferences = [...inspectionReviewConsumerProject.matchAll(/<PackageReference\s+Include="([^"]+)"/g)]
    .map(match => match[1])
  if (JSON.stringify(inspectionReviewDirectReferences) !== JSON.stringify(['Harborline.Blocks.InspectionReview'])) {
    throw new Error(`Inspection Review consumer must reference only Harborline.Blocks.InspectionReview: ${JSON.stringify(inspectionReviewDirectReferences)}`)
  }
  const inspectionReviewConsumerOutputText = runNugetConsumer(inspectionReviewConsumer, inspectionReviewPackageCache).output
  const inspectionReviewConsumerOutput = inspectionReviewConsumerOutputText
    .split('\n')
    .find(line => line.startsWith('INSPECTION_REVIEW_PACKAGE_PASS:'))
  if (!inspectionReviewConsumerOutput) {
    throw new Error(`Inspection Review package-only vertical did not emit its completion proof: ${inspectionReviewConsumerOutputText}`)
  }
  const inspectionReviewAssetsText = readFileSync(resolve(inspectionReviewConsumer, 'obj/project.assets.json'), 'utf8')
  if (inspectionReviewAssetsText.includes(resolve(root, 'projections')) || /"type"\s*:\s*"project"/.test(inspectionReviewAssetsText)) {
    throw new Error('Inspection Review NuGet consumer resolved a source or project dependency')
  }
  const inspectionReviewAssets = JSON.parse(inspectionReviewAssetsText)
  const inspectionReviewTarget = Object.values(inspectionReviewAssets.targets ?? {})[0] ?? {}
  const inspectionReviewHarborlineNodes = Object.keys(inspectionReviewTarget)
    .filter(key => /^Harborline\./.test(key))
    .sort()
  const inspectionReviewExpectedIds = [
    'Harborline.Blocks.InspectionReview',
    'Harborline.Foundation.Forms.Engine',
    'Harborline.Foundation.MultiTenancy',
    'Harborline.Foundation.RuleEngine',
    'Harborline.Kernel.SchemaValidation',
    'Harborline.Kernel.WorkItems',
    'Harborline.Contracts',
    'Harborline.Foundation.Authorization',
    'Harborline.Foundation.Forms',
  ]
  const inspectionReviewExpectedNodes = inspections
    .filter(entry => inspectionReviewExpectedIds.includes(entry.id))
    .map(entry => `${entry.id}/${entry.version}`)
    .sort()
  if (JSON.stringify(inspectionReviewHarborlineNodes) !== JSON.stringify(inspectionReviewExpectedNodes)) {
    throw new Error(`Inspection Review transitive package closure mismatch: ${JSON.stringify({ inspectionReviewHarborlineNodes, inspectionReviewExpectedNodes })}`)
  }

  const workflowConsumer = resolve(fixtureRoot, 'workflow-nuget-consumer')
  const workflowPackageCache = resolve(fixtureRoot, 'workflow-nuget-packages')
  cpSync(resolve(root, 'tests/package-consumers/workflow-nuget'), workflowConsumer, { recursive: true })
  const workflowConsumerProject = readFileSync(resolve(workflowConsumer, 'Consumer.csproj'), 'utf8')
  const workflowDirectReferences = [...workflowConsumerProject.matchAll(/<PackageReference\s+Include="([^"]+)"/g)]
    .map(match => match[1])
    .filter(id => /^Harborline\./.test(id))
  if (JSON.stringify(workflowDirectReferences) !== JSON.stringify(['Harborline.Blocks.Workflow.Interpreter'])) {
    throw new Error(`Workflow engine consumer must reference only Harborline.Blocks.Workflow.Interpreter: ${JSON.stringify(workflowDirectReferences)}`)
  }
  const workflowConsumerOutputText = runNugetConsumer(workflowConsumer, workflowPackageCache).output
  const workflowConsumerOutput = workflowConsumerOutputText
    .split('\n')
    .find(line => line.startsWith('WORKFLOW_PACKAGE_PASS:'))
  if (!workflowConsumerOutput) {
    throw new Error(`Workflow engine-substrate package consumer did not emit its completion proof: ${workflowConsumerOutputText}`)
  }
  const workflowAssetsText = readFileSync(resolve(workflowConsumer, 'obj/project.assets.json'), 'utf8')
  if (workflowAssetsText.includes(resolve(root, 'projections')) || /"type"\s*:\s*"project"/.test(workflowAssetsText)) {
    throw new Error('Workflow engine NuGet consumer resolved a source or project dependency')
  }
  const workflowAssets = JSON.parse(workflowAssetsText)
  const workflowTarget = Object.values(workflowAssets.targets ?? {})[0] ?? {}
  const workflowHarborlineNodes = Object.keys(workflowTarget)
    .filter(key => /^Harborline\./.test(key))
    .sort()
  const workflowExpectedIds = [
    'Harborline.Blocks.Workflow',
    'Harborline.Blocks.Workflow.Interpreter',
  ]
  const workflowExpectedNodes = inspections
    .filter(entry => workflowExpectedIds.includes(entry.id))
    .map(entry => `${entry.id}/${entry.version}`)
    .sort()
  if (JSON.stringify(workflowHarborlineNodes) !== JSON.stringify(workflowExpectedNodes)) {
    throw new Error(`Workflow engine transitive package closure mismatch: ${JSON.stringify({ workflowHarborlineNodes, workflowExpectedNodes })}`)
  }

  return {
    id: 'platform-dotnet-package-group',
    status: 'PASS',
    artifactIdentity: `Harborline.UIAdapters.Blazor@${packedVersion}`,
    assemblyIdentities: ['Harborline.Blocks.InspectionReview', 'Harborline.Blocks.Aggregates', 'Harborline.Blocks.RelativeChains', 'Harborline.Blocks.BuilderDefinitions', 'Harborline.Blocks.Workflow', 'Harborline.Blocks.Workflow.Interpreter', 'Harborline.Contracts', 'Harborline.Foundation.MultiTenancy', 'Harborline.Foundation.Authorization', 'Harborline.Foundation.Forms', 'Harborline.Foundation.Forms.Engine', 'Harborline.Foundation.Session', 'Harborline.Foundation.RuleEngine', 'Harborline.Kernel.SchemaValidation', 'Harborline.Kernel.WorkItems', 'Harborline.UIAdapters.Blazor', 'Harborline.Foundation'],
    artifacts: inspections,
    budget: budgets.nuget,
    localFeedArtifactCount: packageMetadata.length,
    thirdPartyLocalFeedArtifactCount: readdirSync(nugetArtifacts).filter(name => name.endsWith('.nupkg')).length - packageMetadata.length,
    missingArtifactDependencies: 0,
    sourceOrProjectDependencies: 0,
    assemblyAmbiguities: 0,
    harborlineArtifactsFromSingleCohort: true,
    formsEngineDirectPackageReferences: formsEngineDirectReferences,
    formsEngineTransitiveHarborlineArtifacts: formsEngineHarborlineNodes.length,
    formsEngineAuthoringHostBehavior: formsEngineConsumerOutput,
    inspectionReviewDirectPackageReferences: inspectionReviewDirectReferences,
    inspectionReviewTransitiveHarborlineArtifacts: inspectionReviewHarborlineNodes.length,
    inspectionReviewVerticalBehavior: inspectionReviewConsumerOutput,
    workflowDirectPackageReferences: workflowDirectReferences,
    workflowTransitiveHarborlineArtifacts: workflowHarborlineNodes.length,
    workflowEngineSubstrateBehavior: workflowConsumerOutput,
    publicThemeTokens: publicThemeTokens.length,
    contextMenuPublicSurface: true,
    feedbackPublicSurface: true,
    aggregateFoundationSupportSurface: true,
    consumerBehavior: output,
  }
}

function verifyDynamicFormsCapability() {
  const corpusSource = resolve(root, 'projections/dotnet/foundation/hlp.foundation.forms-engine.tests/ValidationParityCorpus/cases.json')
  const corpusCases = JSON.parse(readFileSync(corpusSource, 'utf8')).cases.length

  const rendererConsumer = resolve(fixtureRoot, 'dynamic-forms-npm-consumer')
  cpSync(resolve(root, 'tests/package-consumers/dynamic-forms-npm'), rendererConsumer, { recursive: true })
  copyFileSync(corpusSource, resolve(rendererConsumer, 'cases.json'))
  installPackedArtifacts(rendererConsumer, ['harborline-software-ui-react-', 'harborline-software-contracts-', 'harborline-software-rule-engine-'], 'Dynamic Forms renderer')
  assertPackedInstall(rendererConsumer, ['ui-react', 'contracts', 'rule-engine'], 'Dynamic Forms renderer')
  const [rendererBehavior] = proofLines(clientRun(rendererConsumer), ['DYNAMIC_FORMS_CLIENT_PASS:'], 'Dynamic Forms packed reference lane')
  const clientVerdicts = JSON.parse(readFileSync(resolve(rendererConsumer, 'client-verdicts.json'), 'utf8'))
  if (clientVerdicts.length !== corpusCases) {
    throw new Error(`Dynamic Forms renderer verdicts (${clientVerdicts.length}) do not cover the ${corpusCases}-case corpus`)
  }

  const engineConsumer = resolve(fixtureRoot, 'dynamic-forms-nuget-consumer')
  const enginePackageCache = resolve(fixtureRoot, 'dynamic-forms-nuget-packages')
  cpSync(resolve(root, 'tests/package-consumers/dynamic-forms-nuget'), engineConsumer, { recursive: true })
  copyFileSync(corpusSource, resolve(engineConsumer, 'cases.json'))
  copyFileSync(resolve(rendererConsumer, 'client-verdicts.json'), resolve(engineConsumer, 'client-verdicts.json'))
  const engineDirectReferences = assertDirectPackageReferences(engineConsumer, ['Harborline.Foundation.Forms.Engine'], 'Dynamic Forms engine')
  const [engineBehavior] = proofLines(runNugetConsumer(engineConsumer, enginePackageCache), ['DYNAMIC_FORMS_PACKAGE_PASS:'], 'Dynamic Forms package-only engine vertical')
  const engineHarborlineNodes = assertPackageClosure(engineConsumer, [
    'Harborline.Foundation.Forms.Engine',
    'Harborline.Foundation.MultiTenancy',
    'Harborline.Foundation.RuleEngine',
    'Harborline.Kernel.SchemaValidation',
    'Harborline.Contracts',
    'Harborline.Foundation.Authorization',
    'Harborline.Foundation.Forms',
  ], 'Dynamic Forms engine')

  return {
    id: 'dynamic-forms-capability-vertical',
    status: 'PASS',
    corpusCases,
    corpusSha256: sha256(corpusSource),
    rendererArtifacts: ['@harborline-software/ui-react', '@harborline-software/contracts', '@harborline-software/rule-engine'],
    engineDirectPackageReferences: engineDirectReferences,
    engineTransitiveHarborlineArtifacts: engineHarborlineNodes.length,
    crossLaneVerdicts: clientVerdicts.length,
    sourceOrProjectDependencies: 0,
    rendererBehavior,
    engineBehavior,
  }
}

function verifyWorkflowsCapability() {
  const corpusSource = resolve(root, 'conformance/hlp.blocks.workflow/admission-mirror-cases.json')
  const corpusCases = JSON.parse(readFileSync(corpusSource, 'utf8')).cases.length
  if (corpusCases !== 13) throw new Error(`Workflows admission corpus must carry exactly 13 pairs: ${corpusCases}`)

  const rendererConsumer = resolve(fixtureRoot, 'workflow-npm-consumer')
  cpSync(resolve(root, 'tests/package-consumers/workflow-npm'), rendererConsumer, { recursive: true })
  copyFileSync(corpusSource, resolve(rendererConsumer, 'admission-mirror-cases.json'))
  installPackedArtifacts(rendererConsumer, ['harborline-software-contracts-'], 'Workflows renderer')
  assertPackedInstall(rendererConsumer, ['contracts'], 'Workflows renderer')
  const [rendererBehavior] = proofLines(clientRun(rendererConsumer), ['WORKFLOW_CLIENT_PASS:'], 'Workflows packed renderer lane')
  const clientVerdicts = JSON.parse(readFileSync(resolve(rendererConsumer, 'client-verdicts.json'), 'utf8'))
  if (clientVerdicts.length !== corpusCases) {
    throw new Error(`Workflows renderer verdicts (${clientVerdicts.length}) do not cover the ${corpusCases}-case corpus`)
  }

  const engineConsumer = resolve(fixtureRoot, 'workflow-capability-nuget-consumer')
  const enginePackageCache = resolve(fixtureRoot, 'workflow-capability-nuget-packages')
  cpSync(resolve(root, 'tests/package-consumers/workflow-nuget'), engineConsumer, { recursive: true })
  copyFileSync(corpusSource, resolve(engineConsumer, 'admission-mirror-cases.json'))
  copyFileSync(resolve(rendererConsumer, 'client-verdicts.json'), resolve(engineConsumer, 'client-verdicts.json'))
  // Was a hardcoded one-element literal in the report below, and it was WRONG: this consumer is the
  // only one carrying an explicit Microsoft.Extensions.DependencyInjection reference, which the
  // literal omitted. Now read from the csproj like every sibling vertical (ticket 099 item 1).
  const engineDirectReferences = assertDirectPackageReferences(engineConsumer, ['Harborline.Blocks.Workflow.Interpreter', 'Microsoft.Extensions.DependencyInjection'], 'Workflows engine')
  const [engineSubstrate, engineBehavior] = proofLines(runNugetConsumer(engineConsumer, enginePackageCache), ['WORKFLOW_PACKAGE_PASS:', 'WORKFLOW_CAPABILITY_PASS:'], 'Workflows package-only capability vertical')
  // NO closure assertion, deliberately. This vertical is the one coverage hole ticket 099 recorded
  // and held out of item 1's scope so it lands with its own positive control rather than being
  // quietly filled by a refactor. assertNoSourceDependencies is the half that does exist.
  assertNoSourceDependencies(engineConsumer, 'Workflows capability engine consumer')

  return {
    id: 'workflows-capability-vertical',
    status: 'PASS',
    corpusCases,
    corpusSha256: sha256(corpusSource),
    rendererArtifacts: ['@harborline-software/contracts'],
    engineDirectPackageReferences: engineDirectReferences,
    crossLaneVerdicts: clientVerdicts.length,
    sourceOrProjectDependencies: 0,
    rendererBehavior,
    engineBehavior,
    engineSubstrate,
  }
}

function verifyCalculationsCapability() {
  const corpusSource = resolve(root, 'conformance/hlp.foundation.rule-authoring/authoring-verdict-cases.json')
  const corpusCases = JSON.parse(readFileSync(corpusSource, 'utf8')).cases.length
  if (corpusCases !== 12) throw new Error(`Calculations authoring corpus must carry exactly 12 cases: ${corpusCases}`)

  const rendererConsumer = resolve(fixtureRoot, 'calculations-npm-consumer')
  cpSync(resolve(root, 'tests/package-consumers/calculations-npm'), rendererConsumer, { recursive: true })
  copyFileSync(corpusSource, resolve(rendererConsumer, 'authoring-verdict-cases.json'))
  installPackedArtifacts(rendererConsumer, ['harborline-software-rule-authoring-', 'harborline-software-rule-engine-'], 'Calculations renderer')
  assertPackedInstall(rendererConsumer, ['rule-authoring', 'rule-engine'], 'Calculations renderer')
  const [rendererBehavior] = proofLines(clientRun(rendererConsumer), ['CALCULATIONS_CLIENT_PASS:'], 'Calculations packed renderer lane')
  const clientVerdicts = JSON.parse(readFileSync(resolve(rendererConsumer, 'client-verdicts.json'), 'utf8'))
  if (clientVerdicts.length !== corpusCases) {
    throw new Error(`Calculations renderer verdicts (${clientVerdicts.length}) do not cover the ${corpusCases}-case corpus`)
  }

  const engineConsumer = resolve(fixtureRoot, 'calculations-nuget-consumer')
  const enginePackageCache = resolve(fixtureRoot, 'calculations-nuget-packages')
  cpSync(resolve(root, 'tests/package-consumers/calculations-nuget'), engineConsumer, { recursive: true })
  copyFileSync(corpusSource, resolve(engineConsumer, 'authoring-verdict-cases.json'))
  copyFileSync(resolve(rendererConsumer, 'client-verdicts.json'), resolve(engineConsumer, 'client-verdicts.json'))
  const engineDirectReferences = assertDirectPackageReferences(engineConsumer, ['Harborline.Foundation.RuleAuthoring'], 'Calculations engine')
  const [engineSubstrate, engineBehavior] = proofLines(runNugetConsumer(engineConsumer, enginePackageCache), ['CALCULATIONS_PACKAGE_PASS:', 'CALCULATIONS_CAPABILITY_PASS:'], 'Calculations package-only capability vertical')
  const engineHarborlineNodes = assertPackageClosure(engineConsumer, [
    'Harborline.Foundation.RuleAuthoring',
    'Harborline.Foundation.RuleEngine',
    'Harborline.Contracts',
  ], 'Calculations engine')

  return {
    id: 'calculations-capability-vertical',
    status: 'PASS',
    corpusCases,
    corpusSha256: sha256(corpusSource),
    rendererArtifacts: ['@harborline-software/rule-authoring', '@harborline-software/rule-engine'],
    engineDirectPackageReferences: engineDirectReferences,
    engineTransitiveHarborlineArtifacts: engineHarborlineNodes.length,
    crossLaneVerdicts: clientVerdicts.length,
    sourceOrProjectDependencies: 0,
    rendererBehavior,
    engineBehavior,
    engineSubstrate,
  }
}

function verifyViewsCapability() {
  const corpusSource = resolve(root, 'conformance/hlp.blocks.entity-views/views-vertical-cases.json')
  const corpus = JSON.parse(readFileSync(corpusSource, 'utf8'))
  const corpusCases = corpus.anchors.length + corpus.pairs.length + corpus.routes.length
  if (corpus.anchors.length !== 9 || corpus.pairs.length !== 4 || corpus.routes.length !== 20 || corpusCases !== 33) {
    throw new Error(`Views corpus must carry 9 anchors, 4 pairs, and 20 route rows: ${JSON.stringify({ anchors: corpus.anchors.length, pairs: corpus.pairs.length, routes: corpus.routes.length })}`)
  }

  const rendererConsumer = resolve(fixtureRoot, 'views-npm-consumer')
  cpSync(resolve(root, 'tests/package-consumers/views-npm'), rendererConsumer, { recursive: true })
  mkdirSync(resolve(rendererConsumer, '../corpus'), { recursive: true })
  copyFileSync(corpusSource, resolve(rendererConsumer, '../corpus/views-vertical-cases.json'))
  installPackedArtifacts(rendererConsumer, ['harborline-software-ui-react-'], 'Views renderer')
  assertPackedInstall(rendererConsumer, ['ui-react'], 'Views renderer')
  const [rendererBehavior] = proofLines(clientRun(rendererConsumer), ['VIEWS_CLIENT_PASS:'], 'Views packed renderer lane')
  const clientVerdicts = JSON.parse(readFileSync(resolve(rendererConsumer, 'client-verdicts.json'), 'utf8'))
  if (clientVerdicts.length !== 4 || clientVerdicts.some(row => row.lane !== 'renderer-acknowledged')) {
    throw new Error(`Views renderer must acknowledge exactly four pairing contracts: ${JSON.stringify(clientVerdicts)}`)
  }
  for (const pair of corpus.pairs) {
    const verdict = clientVerdicts.find(row => row.pair === pair.pair)
    if (!verdict || verdict.fallbackLaneCase !== pair.fallbackLaneCase || verdict.transportLaneCase !== pair.transportLaneCase) {
      throw new Error(`Views renderer pairing contract drift for pair ${pair.pair}`)
    }
  }

  const engineConsumer = resolve(fixtureRoot, 'views-capability-nuget-consumer')
  const enginePackageCache = resolve(fixtureRoot, 'views-capability-nuget-packages')
  cpSync(resolve(root, 'tests/package-consumers/views-nuget'), engineConsumer, { recursive: true })
  copyFileSync(corpusSource, resolve(engineConsumer, 'views-vertical-cases.json'))
  const engineDirectReferences = assertDirectPackageReferences(engineConsumer, ['Harborline.Blocks.EntityViews'], 'Views engine')
  const [engineSubstrate, engineBehavior] = proofLines(runNugetConsumer(engineConsumer, enginePackageCache), ['VIEWS_PACKAGE_PASS:', 'VIEWS_CAPABILITY_PASS:'], 'Views engine')
  assertPackageClosure(
    engineConsumer,
    ['Harborline.Blocks.EntityViews', 'Harborline.Contracts'],
    'Views engine',
    /Forms.*(?:Builder|Authoring)|(?:Builder|Authoring).*Forms/i,
  )

  return {
    id: 'views-capability-vertical',
    status: 'PASS',
    corpusCases,
    corpusSha256: sha256(corpusSource),
    rendererArtifacts: ['@harborline-software/ui-react'],
    engineDirectPackageReferences: engineDirectReferences,
    crossLaneVerdicts: clientVerdicts.length,
    sourceOrProjectDependencies: 0,
    rendererBehavior,
    engineBehavior,
    engineSubstrate,
  }
}


function verifySchedulingCapability() {
  const corpusSource = resolve(root, 'conformance/hlp.blocks.scheduling/scheduling-vertical-cases.json')
  const corpus = JSON.parse(readFileSync(corpusSource, 'utf8'))
  if (corpus.anchors.length !== 174 || corpus.httpExpectations.length !== 88 || corpus.crossLanePairs.length !== 0) {
    throw new Error(`Scheduling corpus drift: ${JSON.stringify({ anchors: corpus.anchors.length, hostRows: corpus.httpExpectations.length, pairs: corpus.crossLanePairs.length })}`)
  }

  const consumer = resolve(fixtureRoot, 'scheduling-capability-nuget-consumer')
  const packageCache = resolve(fixtureRoot, 'scheduling-capability-nuget-packages')
  cpSync(resolve(root, 'tests/package-consumers/scheduling-nuget'), consumer, { recursive: true })
  copyFileSync(corpusSource, resolve(consumer, 'scheduling-vertical-cases.json'))
  const direct = assertDirectPackageReferences(consumer, ['Harborline.Blocks.Calendar', 'Harborline.Blocks.Scheduling', 'Harborline.Foundation.Scheduling'], 'Scheduling')
  const [packageProof, capabilityProof] = proofLines(runNugetConsumer(consumer, packageCache), ['SCHEDULING_PACKAGE_PASS:', 'SCHEDULING_CAPABILITY_PASS:'], 'Scheduling')
  // Derived from the landed csprojs: Calendar -> Contracts + Foundation.Scheduling;
  // Blocks.Scheduling and Foundation.Scheduling add no Harborline package edges.
  const closure = assertPackageClosure(
    consumer,
    ['Harborline.Blocks.Calendar', 'Harborline.Blocks.Scheduling', 'Harborline.Foundation.Scheduling', 'Harborline.Contracts'],
    'Scheduling',
    /(?:Forms|Reports|Workflows|EntityViews|Kernel|Blazor|React)/i,
  )

  return { id: 'scheduling-capability-vertical', status: 'PASS', anchors: 174, hostRows: 88, crossLanePairs: 0, corpusSha256: sha256(corpusSource), directPackageReferences: direct, packageClosure: closure, sourceOrProjectDependencies: 0, packageProof, capabilityProof }
}


function verifyReportsCapability() {
  const corpusSource = resolve(root, 'conformance/hlp.blocks.reports/reports-vertical-cases.json')
  const corpus = JSON.parse(readFileSync(corpusSource, 'utf8'))
  if (corpus.anchors.length !== 162 || corpus.httpExpectations.length !== 12 || corpus.clientContract.length !== 3 || corpus.crossLanePairs.length !== 0) {
    throw new Error(`Reports corpus drift: ${JSON.stringify({ anchors: corpus.anchors.length, hostRows: corpus.httpExpectations.length, clientRows: corpus.clientContract.length, pairs: corpus.crossLanePairs.length })}`)
  }

  const consumer = resolve(fixtureRoot, 'reports-capability-nuget-consumer')
  const packageCache = resolve(fixtureRoot, 'reports-capability-nuget-packages')
  cpSync(resolve(root, 'tests/package-consumers/reports-nuget'), consumer, { recursive: true })
  copyFileSync(corpusSource, resolve(consumer, 'reports-vertical-cases.json'))
  const direct = assertDirectPackageReferences(consumer, ['Harborline.Blocks.Reports'], 'Reports')
  const [packageProof, capabilityProof] = proofLines(runNugetConsumer(consumer, packageCache), ['REPORTS_PACKAGE_PASS:', 'REPORTS_CAPABILITY_PASS:'], 'Reports')
  // Derived from the landed csproj: Blocks.Reports -> Contracts only.
  const closure = assertPackageClosure(
    consumer,
    ['Harborline.Blocks.Reports', 'Harborline.Contracts'],
    'Reports',
    /(?:Financial|Tax|Forms|Workflows|EntityViews|Kernel|Blazor|React)/i,
  )

  return { id: 'reports-capability-vertical', status: 'PASS', anchors: 162, hostRows: 12, clientRows: 3, crossLanePairs: 0, corpusSha256: sha256(corpusSource), directPackageReferences: direct, packageClosure: closure, sourceOrProjectDependencies: 0, packageProof, capabilityProof }
}

function verifyAggregatesCapability() {
  const corpusSource = resolve(root, 'conformance/hlp.blocks.activity-timeline/aggregates-vertical-cases.json')
  const corpus = JSON.parse(readFileSync(corpusSource, 'utf8'))
  const counts = {
    ported: corpus.portedCases.length,
    authored: corpus.authoredCases.length,
    carried: corpus.carriedUiCases.length,
    pairs: corpus.crossLanePairs.length,
  }
  if (JSON.stringify(counts) !== JSON.stringify({ ported: 7, authored: 4, carried: 20, pairs: 4 })) {
    throw new Error(`Aggregates corpus drift: ${JSON.stringify(counts)}`)
  }
  if (corpus.authoredCases.some(row => row.authored !== true || !row.obligationRef)) {
    throw new Error('Aggregates authored rows must be marked and cite an obligation')
  }
  if (corpus.carriedUiCases.some(row => row.carried !== true || !row.conformanceId)) {
    throw new Error('Aggregates UI rows must remain explicitly carried with conformance ids')
  }

  // NuGet lane runs FIRST here, unlike every sibling: the renderer's proof is that its timeline
  // matches the engine's byte for byte, so the engine's has to exist before the renderer runs.
  const engineConsumer = resolve(fixtureRoot, 'aggregates-capability-nuget-consumer')
  const enginePackageCache = resolve(fixtureRoot, 'aggregates-capability-nuget-packages')
  cpSync(resolve(root, 'tests/package-consumers/aggregates-nuget'), engineConsumer, { recursive: true })
  copyFileSync(corpusSource, resolve(engineConsumer, 'aggregates-vertical-cases.json'))
  const engineDirectReferences = assertDirectPackageReferences(engineConsumer, ['Harborline.Blocks.ActivityTimeline'], 'Aggregates engine')
  const [engineTimeline, enginePackageProof, engineCapabilityProof] = proofLines(
    runNugetConsumer(engineConsumer, enginePackageCache),
    ['CROSS_LANE_TIMELINE:', 'AGGREGATES_PACKAGE_PASS:', 'AGGREGATES_CAPABILITY_PASS:'],
    'Aggregates engine',
  )
  const engineClosure = assertPackageClosure(engineConsumer, ['Harborline.Blocks.ActivityTimeline', 'Harborline.Contracts'], 'Aggregates engine')

  const rendererConsumer = resolve(fixtureRoot, 'aggregates-capability-npm-consumer')
  cpSync(resolve(root, 'tests/package-consumers/aggregates-npm'), rendererConsumer, { recursive: true })
  mkdirSync(resolve(rendererConsumer, '../corpus'), { recursive: true })
  copyFileSync(corpusSource, resolve(rendererConsumer, '../corpus/aggregates-vertical-cases.json'))
  // Exact views-npm precedent: pass only the packed ui-react tarball; npm resolves its declared peers/dependencies.
  installPackedArtifacts(rendererConsumer, ['harborline-software-ui-react-'], 'Aggregates renderer')
  assertPackedInstall(rendererConsumer, ['ui-react'], 'Aggregates renderer')
  const [rendererTimeline, rendererProof] = proofLines(clientRun(rendererConsumer), ['CROSS_LANE_TIMELINE:', 'AGGREGATES_RENDERER_PASS:'], 'Aggregates renderer')
  if (engineTimeline !== rendererTimeline) {
    throw new Error(`Aggregates cross-lane timeline diverged byte-for-byte: ${JSON.stringify({ engineTimeline, rendererTimeline })}`)
  }

  return {
    id: 'aggregates-capability-vertical',
    status: 'PASS',
    ...counts,
    corpusSha256: sha256(corpusSource),
    rendererArtifacts: ['@harborline-software/ui-react'],
    engineDirectPackageReferences: engineDirectReferences,
    enginePackageClosure: engineClosure,
    sourceOrProjectDependencies: 0,
    crossLaneTimeline: engineTimeline,
    enginePackageProof,
    engineCapabilityProof,
    rendererProof,
  }
}

function stageCheckedTarball(sourcePath, destination, expectedSha256) {
  if (!sourcePath) throw new Error(`App-shell capability-host staging: environment tarball path missing for ${destination}`)
  const bytes = readFileSync(sourcePath)
  const actual = createHash('sha256').update(bytes).digest('hex')
  if (actual !== expectedSha256) throw new Error(`App-shell capability-host staging: SHA-256 mismatch for ${sourcePath}: ${actual} !== ${expectedSha256}`)
  writeFileSync(destination, bytes)
}

function walkFiles(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const child = resolve(dir, entry.name)
    return entry.isDirectory() ? walkFiles(child) : [child]
  })
}

function verifyAppShellCapability() {
  const corpusSource = resolve(root, 'conformance/hlp.app-shell/appshell-vertical-cases.json')
  const corpus = JSON.parse(readFileSync(corpusSource, 'utf8'))
  if (corpus.narrowing.candidateSet !== 244 || corpus.sessionTenantAnchors.length !== 8 ||
      corpus.hullAdminRows.length !== 12 || corpus.routeContractEntry.length !== 1) {
    throw new Error(`App-shell corpus drift: ${JSON.stringify({
      candidates: corpus.narrowing.candidateSet, anchors: corpus.sessionTenantAnchors.length,
      capabilityHost: corpus.hullAdminRows.length, route: corpus.routeContractEntry.length,
    })}`)
  }
  if (corpus.narrowing.exclusions.reduce((n, row) => n + row.count, 0) !== 236)
    throw new Error('App-shell narrowing must name all 236 excluded candidate rows')

  // Ticket 092 ruling (Option A, user 2026-08-16): the four-field contract corpus is
  // FROZEN; both lanes replay it and their canonical projections must be byte-identical.
  // This is CORPUS CONFORMANCE, never independent semantic agreement — the field
  // implementations are named acceptance conditions on the harborline-app migration.
  const contractSource = resolve(root, 'conformance/hlp.app-shell/appshell-contract-cases.json')
  const contract = JSON.parse(readFileSync(contractSource, 'utf8'))
  if (contract._header.scenarioCount !== 6 || contract.scenarios.length !== 6 ||
      JSON.stringify(contract._header.scope) !== JSON.stringify(['visibility', 'permissionVocabulary', 'lifecycleBuildModeFolding', 'routeJoin'])) {
    throw new Error(`App-shell contract corpus drift: ${JSON.stringify(contract._header)}`)
  }

  const engineConsumer = resolve(fixtureRoot, 'appshell-capability-nuget-consumer')
  const engineCache = resolve(fixtureRoot, 'appshell-capability-nuget-packages')
  cpSync(resolve(root, 'tests/package-consumers/appshell-nuget'), engineConsumer, { recursive: true })
  copyFileSync(corpusSource, resolve(engineConsumer, 'appshell-vertical-cases.json'))
  copyFileSync(contractSource, resolve(engineConsumer, 'appshell-contract-cases.json'))
  const project = readFileSync(resolve(engineConsumer, 'Consumer.csproj'), 'utf8')
  const direct = [...project.matchAll(/<PackageReference\s+Include="([^"]+)"/g)].map(x => x[1])
  const expectedDirect = ['Harborline.Foundation.Session', 'Harborline.Foundation.Authorization', 'Harborline.Foundation.MultiTenancy']
  if (JSON.stringify(direct) !== JSON.stringify(expectedDirect)) throw new Error(`App-shell direct refs drift: ${JSON.stringify(direct)}`)
  const engineOutput = runNugetConsumer(engineConsumer, engineCache).output
  if (!engineOutput.includes('APPSHELL_ENGINE_PASS:')) throw new Error(`App-shell engine proof absent: ${engineOutput}`)
  const assetsText = readFileSync(resolve(engineConsumer, 'obj/project.assets.json'), 'utf8')
  if (assetsText.includes(resolve(root, 'projections')) || /"type"\s*:\s*"project"/.test(assetsText)) throw new Error('App-shell resolved source/project dependencies')
  const assets = JSON.parse(assetsText)
  const target = Object.values(assets.targets ?? {})[0] ?? {}
  const closure = Object.keys(target).filter(x => /^Harborline\./.test(x)).map(x => x.split('/')[0]).sort()
  const expectedClosure = ['Harborline.Foundation.MultiTenancy', 'Harborline.Contracts', 'Harborline.Foundation.Authorization', 'Harborline.Foundation.Session'].sort()
  if (JSON.stringify(closure) !== JSON.stringify(expectedClosure)) throw new Error(`App-shell closure drift: ${JSON.stringify(closure)}`)

  // Capability-host rows: cross-repo tarballs staged by explicit environment paths with the
  // ticket-089 SHA-256 identities pinned here; never built or searched for implicitly.
  const npmConsumer = resolve(fixtureRoot, 'appshell-capability-npm-consumer')
  cpSync(resolve(root, 'tests/package-consumers/appshell-npm'), npmConsumer, { recursive: true })
  copyFileSync(contractSource, resolve(npmConsumer, 'appshell-contract-cases.json'))
  const feed = resolve(npmConsumer, 'feed')
  mkdirSync(feed, { recursive: true })
  // Read the pins from the manifest rather than repeating them. resolve-appshell-feed.mjs already
  // parses this file and verifies the same sha256 before use; a second copy here is a second thing
  // to forget when a ref moves (ticket 099 item 7).
  const feedProvenance = JSON.parse(readFileSync(resolve(root, 'tooling/appshell-feed-provenance.json'), 'utf8'))
  for (const artifact of feedProvenance.artifacts) {
    stageCheckedTarball(process.env[artifact.env], resolve(feed, artifact.tarball), artifact.sha256)
  }
  // The committed manifest carries no local package protocol (repository rule); the
  // disposable copy gets the staged-feed dependencies written here, at run time only.
  const npmManifestPath = resolve(npmConsumer, 'package.json')
  const npmManifest = JSON.parse(readFileSync(npmManifestPath, 'utf8'))
  npmManifest.dependencies = { '@harborline-software/capability-host': 'file:./feed/harborline-software-capability-host-0.0.0.tgz' }
  // 246: the api wire package is @harborline-software/api-contracts; take name and tarball from the provenance the feed was staged from.
  const apiContracts = feedProvenance.artifacts.find(a => a.id === 'contracts')
  npmManifest.overrides = { [apiContracts.packageName]: `file:./feed/${apiContracts.tarball}` }
  writeFileSync(npmManifestPath, `${JSON.stringify(npmManifest, null, 2)}\n`)
  run('npm', ['install', '--ignore-scripts', '--no-audit', '--no-fund', '--package-lock=false'], { cwd: npmConsumer })
  const npmOutput = run(process.execPath, ['--conditions=browser', 'exercise.mjs'], { cwd: npmConsumer })
  if (!npmOutput.includes('APPSHELL_CAPABILITY_HOST_PASS:')) throw new Error(`App-shell capability-host proof absent: ${npmOutput}`)
  const consumerSources = walkFiles(npmConsumer).filter(x => !x.includes('node_modules') && /\.(?:js|mjs|ts|tsx)$/.test(x) && !x.endsWith(`${'exercise'}.mjs`))
  const forkPattern = /function\s+(?:resolveEdition|projectForTenant|packSpecificity|membershipOf)\s*\(|class\s+CompositionError\b/
  if (consumerSources.some(file => forkPattern.test(readFileSync(file, 'utf8')))) throw new Error('shell forks capability-host resolution FAILED condition')

  const engineContract = engineOutput.split('\n').map(l => l.trim()).find(l => l.startsWith('APPSHELL_CONTRACT:'))
  const npmContract = npmOutput.split('\n').map(l => l.trim()).find(l => l.startsWith('APPSHELL_CONTRACT:'))
  if (!engineContract || !npmContract) throw new Error('App-shell contract conformance line absent from a lane')
  if (engineContract !== npmContract) throw new Error(`App-shell contract conformance divergence:\n${engineContract}\n${npmContract}`)

  return {
    id: 'appshell-capability-vertical', status: 'PASS',
    candidateAnchors: 244, provedAnchors: 8, capabilityHostRows: 12, routeEntryRows: 1,
    contractScenarios: 6, contractConformance: 'byte-identical',
    semanticAgreement: 'DEFERRED-BY-RULING (ticket 092 Option A: named conditions on harborline-app)',
    corpusSha256: sha256(corpusSource), contractCorpusSha256: sha256(contractSource),
    directPackageReferences: direct, packageClosure: closure, sourceOrProjectDependencies: 0,
  }
}

try {
  rmSync(artifactRoot, { recursive: true, force: true })
  mkdirSync(npmArtifacts, { recursive: true })
  mkdirSync(nugetArtifacts, { recursive: true })
  const completed = new Map()
  const runFixture = (id, verify, prerequisites = []) => {
    for (const prerequisite of prerequisites) execute(prerequisite)
    const result = verify()
    if (result.id !== id) throw new Error(`fixture ${id} returned mismatched id ${result.id}`)
    completed.set(id, result)
  }
  const plans = new Map([
    ['ui-react-package', () => runFixture('ui-react-package', verifyNpm, ['forms-contracts-package', 'rule-runtime-package'])],
    ['forms-contracts-package', () => runFixture('forms-contracts-package', verifyFormsNpm)],
    ['rule-runtime-package', () => runFixture('rule-runtime-package', verifyRuleRuntimeNpm)],
    ['rule-authoring-package', () => runFixture('rule-authoring-package', verifyRuleAuthoringNpm, ['rule-runtime-package'])],
    ['platform-dotnet-package-group', () => runFixture('platform-dotnet-package-group', verifyNuget)],
    ['dynamic-forms-capability-vertical', () => runFixture('dynamic-forms-capability-vertical', verifyDynamicFormsCapability, ['ui-react-package', 'forms-contracts-package', 'rule-runtime-package', 'platform-dotnet-package-group'])],
    ['workflows-capability-vertical', () => runFixture('workflows-capability-vertical', verifyWorkflowsCapability, ['forms-contracts-package', 'platform-dotnet-package-group'])],
    ['calculations-capability-vertical', () => runFixture('calculations-capability-vertical', verifyCalculationsCapability, ['rule-authoring-package', 'platform-dotnet-package-group'])],
    ['views-capability-vertical', () => runFixture('views-capability-vertical', verifyViewsCapability, ['ui-react-package', 'platform-dotnet-package-group'])],
    ['scheduling-capability-vertical', () => runFixture('scheduling-capability-vertical', verifySchedulingCapability, ['platform-dotnet-package-group'])],
    ['reports-capability-vertical', () => runFixture('reports-capability-vertical', verifyReportsCapability, ['platform-dotnet-package-group'])],
    ['aggregates-capability-vertical', () => runFixture('aggregates-capability-vertical', verifyAggregatesCapability, ['ui-react-package', 'platform-dotnet-package-group'])],
    ['appshell-capability-vertical', () => runFixture('appshell-capability-vertical', verifyAppShellCapability, ['ui-react-package', 'platform-dotnet-package-group'])],
    ['copilot-contracts-npm', () => runFixture('copilot-contracts-npm', verifyCopilotContractsNpm)],
  ])
  const execute = id => {
    if (!completed.has(id)) plans.get(id)()
  }
  const requestedIds = options.only === null ? [...plans.keys()] : [options.only]
  for (const id of requestedIds) execute(id)
  const fixtures = requestedIds.map(id => completed.get(id))
  process.stdout.write(`${JSON.stringify({
    schemaVersion: 1,
    status: 'PASS',
    dotnetSdk: dotnet.version,
    sourceCheckoutUsedByConsumers: false,
    artifactRoot: 'artifacts/packages',
    fixtures,
  }, null, 2)}\n`)
} catch (error) {
  process.stderr.write(`${error instanceof Error ? error.stack : String(error)}\n`)
  process.exitCode = 1
} finally {
  disposeSignalCleanup()
}
