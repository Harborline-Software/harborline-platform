import { spawnSync } from 'node:child_process'
import { appendFileSync, cpSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { build } from 'esbuild'

const root = resolve(import.meta.dirname, '..')
const dist = resolve(root, 'dist')
const typeScript = resolve(root, 'node_modules/typescript/bin/tsc')
const formsContractsRoot = resolve(root, '../../../typescript/contracts/hlp.contracts.forms')
const contextRoot = resolve(root, '../hlp.ui.context-menu')
const ruleRuntimeRoot = resolve(root, '../../../typescript/foundation/hlp.foundation.rule-runtime')

rmSync(dist, { recursive: true, force: true })
mkdirSync(dist, { recursive: true })

function compile(cwd, args) {
  const result = spawnSync(process.execPath, [typeScript, ...args], { cwd, encoding: 'utf8' })
  if (result.status !== 0) {
    process.stderr.write(`${result.stdout}${result.stderr}`)
    process.exit(result.status ?? 1)
  }
}

// FormView consumes the canonical Forms declarations, so establish them before
// compiling any aggregate contribution.
compile(formsContractsRoot, ['-p', 'tsconfig.build.json'])

// SchemaForm's tsconfig maps @harborline-software/rule-engine to the rule-runtime projection's
// dist/index.d.ts, so establish that too. It was the one pre-build this list omitted, and dist/ is
// gitignored, so the omission was invisible on any machine that had built rule-runtime at some
// point in the past and fatal on a checkout that had not: TS2307 in SchemaForm.types.ts and
// useFormRuleGraph.ts. Reproduced in a clean worktree before fixing (control ticket 089).
compile(ruleRuntimeRoot, ['-p', 'tsconfig.build.json'])

const contributions = [
  ['aspect-lens', resolve(root, '../hlp.ui.aspect-lens'), true],
  ['cn', resolve(root, '../hlp.ui.cn'), true],
  ['default-strings', resolve(root, '../hlp.ui.default-strings'), true],
  ['accordion', resolve(root, '../hlp.ui.accordion'), true],
  ['card', resolve(root, '../hlp.ui.card'), true],
  ['icon-button', resolve(root, '../hlp.ui.icon-button'), true],
  ['layers-state', resolve(root, '../hlp.ui.layers-state'), true],
  ['separator', resolve(root, '../hlp.ui.separator'), true],
  ['locale-provider', resolve(root, '../hlp.ui.locale-provider'), false],
  ['tone-style', resolve(root, '../hlp.ui.tone-style'), true],
  ['breadcrumb', resolve(root, '../hlp.ui.breadcrumb'), true],
  ['check-box', resolve(root, '../hlp.ui.check-box'), true],
  ['collapsible', resolve(root, '../hlp.ui.collapsible'), true],
  ['form-field', resolve(root, '../hlp.ui.form-field'), true],
  ['date-field', resolve(root, '../hlp.ui.date-field'), true],
  ['date-time-field', resolve(root, '../hlp.ui.date-time-field'), true],
  ['number-field', resolve(root, '../hlp.ui.number-field'), true],
  ['popover', resolve(root, '../hlp.ui.popover'), true],
  ['radio-group', resolve(root, '../hlp.ui.radio-group'), true],
  ['sheet', resolve(root, '../hlp.ui.sheet'), true],
  ['spotlight', resolve(root, '../hlp.ui.spotlight'), true],
  ['text-area', resolve(root, '../hlp.ui.text-area'), true],
  ['tooltip', resolve(root, '../hlp.ui.tooltip'), true],
  ['table', resolve(root, '../hlp.ui.table'), true],
  ['window', resolve(root, '../hlp.ui.window'), true],
  ['alert', resolve(root, '../hlp.ui.alert'), true],
  ['badge', resolve(root, '../hlp.ui.badge'), true],
  ['use-outside-click', resolve(root, '../hlp.ui.use-outside-click'), true],
  ['data-export-button', resolve(root, '../hlp.ui.data-export-button'), true],
  ['side-nav-group', resolve(root, '../hlp.ui.side-nav-group'), true],
  ['spinner', resolve(root, '../hlp.ui.spinner'), true],
  ['action-menu', resolve(root, '../hlp.ui.action-menu'), true],
  ['chip', resolve(root, '../hlp.ui.chip'), true],
  ['conversation-list', resolve(root, '../hlp.ui.conversation-list'), true],
  ['error-card', resolve(root, '../hlp.ui.error-card'), true],
  ['form-view', resolve(root, '../hlp.ui.form-view'), true],
  ['loading-state', resolve(root, '../hlp.ui.loading-state'), true],
  ['rail-labels', resolve(root, '../hlp.ui.rail-labels'), true],
  ['touch-target', resolve(root, '../hlp.ui.touch-target'), true],
  ['use-media-query', resolve(root, '../hlp.ui.use-media-query'), true],
  ['use-can-show-master-detail', resolve(root, '../hlp.ui.use-can-show-master-detail'), true],
  ['use-is-mobile', resolve(root, '../hlp.ui.use-is-mobile'), true],
  ['activity-log', resolve(root, '../hlp.ui.activity-log'), true],
  ['use-nav-collapsed', resolve(root, '../hlp.ui.use-nav-collapsed'), true],
  ['use-scroll-affordance', resolve(root, '../hlp.ui.use-scroll-affordance'), true],
  ['empty-state', resolve(root, '../hlp.ui.empty-state'), true],
  ['guarded-control', resolve(root, '../hlp.ui.guarded-control'), true],
  ['input', resolve(root, '../hlp.ui.input'), true],
  ['layers-rail', resolve(root, '../hlp.ui.layers-rail'), true],
  ['notification-center', resolve(root, '../hlp.ui.notification-center'), true],
  ['page', resolve(root, '../hlp.ui.page'), true],
  ['search-input', resolve(root, '../hlp.ui.search-input'), true],
  ['segmented-control', resolve(root, '../hlp.ui.segmented-control'), true],
  ['select-field', resolve(root, '../hlp.ui.select-field'), true],
  ['switch', resolve(root, '../hlp.ui.switch'), true],
  ['toaster', resolve(root, '../hlp.ui.toaster'), true],
  ['user-menu', resolve(root, '../hlp.ui.user-menu'), true],
  ['app-layout', resolve(root, '../hlp.ui.app-layout'), true],
  ['app-shell', resolve(root, '../hlp.ui.app-shell'), true],
  ['detail-panel', resolve(root, '../hlp.ui.detail-panel'), true],
  ['chart', resolve(root, '../hlp.ui.chart'), true],
  ['chat', resolve(root, '../hlp.ui.chat'), true],
  ['data-grid', resolve(root, '../hlp.ui.data-grid'), true],
  ['view-runtime', resolve(root, '../hlp.ui.view-runtime'), true],
  ['gantt', resolve(root, '../hlp.ui.gantt'), true],
  ['numeric-text-box', resolve(root, '../hlp.ui.numeric-text-box'), true],
  ['scroll-affordance', resolve(root, '../hlp.ui.scroll-affordance'), false],
  ['dialog', resolve(root, '../hlp.ui.dialog'), true],
  ['confirm-dialog', resolve(root, '../hlp.ui.confirm-dialog'), true],
  ['side-nav', resolve(root, '../hlp.ui.side-nav'), false],
  ['switch-field', resolve(root, '../hlp.ui.switch-field'), true],
  ['text-box', resolve(root, '../hlp.ui.text-box'), true],
  ['scheduler', resolve(root, '../hlp.ui.scheduler'), true],
  // Late: its declaration emit resolves against the dist of every form module above it.
  // Exported explicitly rather than by star, because it re-exports form-view value types.
  ['schema-form', resolve(root, '../hlp.ui.schema-form'), false],
  // Last: its declaration emit resolves against schema-form's dist. Exported explicitly
  // rather than by star, because it re-exports schema-form's RuleGraphLike type.
  ['use-form-rule-graph', resolve(root, '../hlp.ui.use-form-rule-graph'), false],
]

for (const [name, contributionRoot] of contributions) {
  const contributionDist = resolve(contributionRoot, 'dist')
  rmSync(contributionDist, { recursive: true, force: true })
  compile(contributionRoot, [
    '-p', 'tsconfig.build.json', '--declaration', '--emitDeclarationOnly',
    '--outDir', contributionDist, '--rootDir', resolve(contributionRoot, 'src'),
  ])
}

const toneStyleDeclaration = resolve(root, '../hlp.ui.tone-style/dist/toneStyle.d.ts')
writeFileSync(toneStyleDeclaration, readFileSync(toneStyleDeclaration, 'utf8')
  .replaceAll('@harborline-platform/hlp.ui.aspect-lens', '../aspect-lens/index'))
const layersRailDeclaration = resolve(root, '../hlp.ui.layers-rail/dist/LayersRail.d.ts')
writeFileSync(layersRailDeclaration, readFileSync(layersRailDeclaration, 'utf8')
  .replaceAll('@harborline-platform/hlp.ui.aspect-lens', '../aspect-lens/index')
  .replaceAll('@harborline-platform/hlp.ui.layers-state', '../layers-state/index')
  .replaceAll('@harborline-platform/hlp.ui.rail-labels', '../rail-labels/index'))
const contributionDeclarationAliases = [
  ['../hlp.ui.scroll-affordance/dist/ScrollAffordance.d.ts', '@harborline-platform/hlp.ui.use-scroll-affordance', '../use-scroll-affordance/index'],
  ['../hlp.ui.side-nav/dist/SideNav.d.ts', '@harborline-platform/hlp.ui.side-nav-group', '../side-nav-group/index'],
  ['../hlp.ui.switch-field/dist/SwitchField.d.ts', '@harborline-platform/hlp.ui.switch', '../switch/index'],
  ['../hlp.ui.text-box/dist/TextBox.d.ts', '@harborline-platform/hlp.ui.input', '../input/index'],
  ['../hlp.ui.schema-form/dist/index.d.ts', '@harborline-platform/hlp.ui.form-view', '../form-view/index'],
  ['../hlp.ui.schema-form/dist/SchemaForm.d.ts', '@harborline-platform/hlp.ui.form-view', '../form-view/index'],
  ['../hlp.ui.schema-form/dist/SchemaForm.types.d.ts', '@harborline-platform/hlp.ui.form-view', '../form-view/index'],
  ['../hlp.ui.schema-form/dist/controls.d.ts', '@harborline-platform/hlp.ui.form-view', '../form-view/index'],
  ['../hlp.ui.schema-form/dist/useFormRuleGraph.d.ts', '@harborline-platform/hlp.ui.form-view', '../form-view/index'],
  ['../hlp.ui.use-form-rule-graph/dist/useFormRuleGraph.d.ts', '@harborline-platform/hlp.ui.form-view', '../form-view/index'],
  ['../hlp.ui.use-form-rule-graph/dist/useFormRuleGraph.d.ts', '@harborline-platform/hlp.ui.schema-form', '../schema-form/index'],
  ['../hlp.ui.app-shell/dist/AppShell.d.ts', '@harborline-platform/hlp.ui.app-layout', '../app-layout/index'],
]
for (const [relativePath, source, replacement] of contributionDeclarationAliases) {
  const declaration = resolve(root, relativePath)
  writeFileSync(declaration, readFileSync(declaration, 'utf8').replaceAll(source, replacement))
}

// The retained package source delegates its legacy locale exports to the new
// contribution declarations built above.
compile(root, ['-p', 'tsconfig.build.json'])
const localeDeclaration = resolve(dist, 'locale.d.ts')
writeFileSync(localeDeclaration, readFileSync(localeDeclaration, 'utf8')
  .replaceAll('@harborline-platform/hlp.ui.locale-provider', './locale-provider/index')
  .replaceAll('@harborline-platform/hlp.ui.default-strings', './default-strings/index'))

compile(contextRoot, ['-p', 'tsconfig.build.json'])
appendFileSync(resolve(dist, 'index.d.ts'), "\nexport { ContextMenu, clampContextMenuPosition } from './context-menu/ContextMenu'\nexport type { ContextMenuProps, ContextMenuGroup, ContextMenuItem } from './context-menu/ContextMenu'\n")

for (const [name, contributionRoot, exportFromIndex] of contributions) {
  cpSync(resolve(contributionRoot, 'dist'), resolve(dist, name), { recursive: true })
  if (exportFromIndex) appendFileSync(resolve(dist, 'index.d.ts'), `\nexport * from './${name}/index'\n`)
}
appendFileSync(resolve(dist, 'index.d.ts'), "\nexport { ScrollAffordance } from './scroll-affordance/index'\nexport type { ScrollAffordanceProps } from './scroll-affordance/index'\nexport { SideNav } from './side-nav/index'\nexport type { SideNavItem, SideNavProps, SideNavStructure, SideNavGroup as SideNavNavigationGroup } from './side-nav/index'\n")
// Named rather than star: schema-form re-exports FormValues, FormView and ValidationResult,
// which form-view already contributes to this index.
appendFileSync(resolve(dist, 'index.d.ts'), "\nexport { SchemaForm, DEFAULT_CONTROLS } from './schema-form/index'\nexport type { ControlArgs, ControlRegistry, ControlRenderer, RuleGraphLike, SchemaFormProps, SchemaFormStrings } from './schema-form/index'\n")
// Named rather than star: use-form-rule-graph re-exports schema-form's RuleGraphLike type.
appendFileSync(resolve(dist, 'index.d.ts'), "\nexport { projectRuleOutcomes, useFormRuleGraph, ReactiveSchemaForm } from './use-form-rule-graph/index'\nexport type { UseFormRuleGraphResult, ReactiveSchemaFormProps } from './use-form-rule-graph/index'\n")

const external = ['react', 'react-dom', 'react/jsx-runtime', '@radix-ui/react-slot', 'clsx', 'tailwind-merge']
const alias = {
  '@harborline-software/contracts/authorization': resolve(formsContractsRoot, 'dist/authorization.js'),
  '@harborline-platform/hlp.ui.aspect-lens': resolve(root, '../hlp.ui.aspect-lens/src/index.ts'),
  '@harborline-platform/hlp.ui.default-strings': resolve(root, '../hlp.ui.default-strings/src/index.ts'),
  '@harborline-platform/hlp.ui.form-field-context': resolve(root, '../hlp.ui.form-field/src/FormFieldContext.tsx'),
  '@harborline-platform/hlp.ui.cn': resolve(root, '../hlp.ui.cn/src/index.ts'),
  '@harborline-platform/hlp.ui.locale-provider': resolve(root, '../hlp.ui.locale-provider/src/index.ts'),
  '@harborline-platform/hlp.ui.use-media-query': resolve(root, '../hlp.ui.use-media-query/src/index.ts'),
  '@harborline-platform/hlp.ui.use-is-mobile': resolve(root, '../hlp.ui.use-is-mobile/src/index.ts'),
  '@harborline-platform/hlp.ui.use-outside-click': resolve(root, '../hlp.ui.use-outside-click/src/index.ts'),
  '@harborline-platform/hlp.ui.layers-state': resolve(root, '../hlp.ui.layers-state/src/index.ts'),
  '@harborline-platform/hlp.ui.rail-labels': resolve(root, '../hlp.ui.rail-labels/src/index.ts'),
  '@harborline-platform/hlp.ui.tone-style': resolve(root, '../hlp.ui.tone-style/src/index.ts'),
  '@harborline-platform/hlp.ui.popover': resolve(root, '../hlp.ui.popover/src/index.ts'),
  '@harborline-platform/hlp.ui.use-can-show-master-detail': resolve(root, '../hlp.ui.use-can-show-master-detail/src/index.ts'),
  '@harborline-platform/hlp.ui.use-scroll-affordance': resolve(root, '../hlp.ui.use-scroll-affordance/src/index.ts'),
  '@harborline-platform/hlp.ui.side-nav-group': resolve(root, '../hlp.ui.side-nav-group/src/index.ts'),
  '@harborline-platform/hlp.ui.tooltip': resolve(root, '../hlp.ui.tooltip/src/index.ts'),
  '@harborline-platform/hlp.ui.switch': resolve(root, '../hlp.ui.switch/src/index.ts'),
  '@harborline-platform/hlp.ui.input': resolve(root, '../hlp.ui.input/src/index.ts'),
  '@harborline-platform/hlp.ui.dialog': resolve(root, '../hlp.ui.dialog/src/index.ts'),
  '@harborline-platform/hlp.ui.form-view': resolve(root, '../hlp.ui.form-view/src/index.ts'),
  '@harborline-platform/hlp.ui.form-field': resolve(root, '../hlp.ui.form-field/src/index.ts'),
  '@harborline-platform/hlp.ui.text-box': resolve(root, '../hlp.ui.text-box/src/index.ts'),
  '@harborline-platform/hlp.ui.text-area': resolve(root, '../hlp.ui.text-area/src/index.ts'),
  '@harborline-platform/hlp.ui.data-grid': resolve(root, '../hlp.ui.data-grid/src/index.ts'),
  '@harborline-platform/hlp.ui.select-field': resolve(root, '../hlp.ui.select-field/src/index.ts'),
  '@harborline-platform/hlp.ui.check-box': resolve(root, '../hlp.ui.check-box/src/index.ts'),
  '@harborline-platform/hlp.ui.radio-group': resolve(root, '../hlp.ui.radio-group/src/index.ts'),
  '@harborline-platform/hlp.ui.date-field': resolve(root, '../hlp.ui.date-field/src/index.ts'),
  '@harborline-platform/hlp.ui.date-time-field': resolve(root, '../hlp.ui.date-time-field/src/index.ts'),
  '@harborline-platform/hlp.ui.number-field': resolve(root, '../hlp.ui.number-field/src/index.ts'),
  '@harborline-platform/hlp.ui.numeric-text-box': resolve(root, '../hlp.ui.numeric-text-box/src/index.ts'),
  '@harborline-platform/hlp.ui.schema-form': resolve(root, '../hlp.ui.schema-form/src/index.ts'),
  '@harborline-platform/hlp.ui.app-layout': resolve(root, '../hlp.ui.app-layout/src/index.ts'),
}
await build({ entryPoints: [resolve(root, 'scripts/aggregate-entry.ts')], bundle: true, external, alias, format: 'esm', outfile: resolve(dist, 'index.js'), platform: 'browser', jsx: 'automatic', minify: true })
await build({ entryPoints: [resolve(root, 'scripts/aggregate-entry.ts')], bundle: true, external, alias, format: 'cjs', outfile: resolve(dist, 'index.cjs'), platform: 'browser', jsx: 'automatic', minify: true })
writeFileSync(resolve(dist, 'style.css'), [
  readFileSync(resolve(root, 'src/style.css'), 'utf8'),
  readFileSync(resolve(contextRoot, 'src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.accordion/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.card/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.icon-button/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.separator/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.error-card/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.loading-state/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.empty-state/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.breadcrumb/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.check-box/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.collapsible/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.confirm-dialog/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.form-field/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.schema-form/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.date-field/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.date-time-field/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.number-field/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.popover/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.radio-group/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.sheet/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.spotlight/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.text-area/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.tooltip/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.table/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.window/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.alert/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.badge/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.data-export-button/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.spinner/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.action-menu/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.activity-log/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.chip/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.conversation-list/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.guarded-control/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.input/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.layers-rail/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.notification-center/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.page/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.search-input/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.segmented-control/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.select-field/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.switch/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.toaster/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.user-menu/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.app-layout/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.app-shell/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.detail-panel/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.chart/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.chat/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.data-grid/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.view-runtime/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.gantt/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.numeric-text-box/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.scroll-affordance/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.dialog/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.side-nav/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.text-box/src/style.css'), 'utf8'),
  readFileSync(resolve(root, '../hlp.ui.scheduler/src/style.css'), 'utf8'),
].join('\n'))
