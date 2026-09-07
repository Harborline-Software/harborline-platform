export const PACKAGE_FIXTURE_IDS = Object.freeze([
  'ui-react-package',
  'forms-contracts-package',
  'rule-runtime-package',
  'rule-authoring-package',
  'platform-dotnet-package-group',
  'dynamic-forms-capability-vertical',
  'workflows-capability-vertical',
  'calculations-capability-vertical',
  'views-capability-vertical',
  'scheduling-capability-vertical',
  'reports-capability-vertical',
  'aggregates-capability-vertical',
  'appshell-capability-vertical',
  'copilot-contracts-npm',
])

export function parsePackageFixtureArguments(args) {
  let only = null
  let record = false
  let phase4Gate = false

  for (let index = 0; index < args.length; index += 1) {
    const argument = args[index]
    if (argument === '--record') {
      record = true
    } else if (argument === '--phase-4-gate') {
      phase4Gate = true
    } else if (argument === '--only') {
      if (only !== null) throw new Error('--only may be specified only once')
      only = args[index + 1]
      if (!only || only.startsWith('--')) throw new Error('--only requires a fixture ID')
      index += 1
    } else {
      throw new Error(`unknown argument: ${argument}`)
    }
  }

  if (only !== null && !PACKAGE_FIXTURE_IDS.includes(only)) {
    throw new Error(`unknown fixture ID for --only: ${only}; expected one of ${PACKAGE_FIXTURE_IDS.join(', ')}`)
  }
  if (only !== null && record) throw new Error('--only is development-only and is refused under --record')
  if (only !== null && phase4Gate) throw new Error('--only is development-only and is refused by the phase-4 gate')

  return Object.freeze({only, record, phase4Gate})
}
