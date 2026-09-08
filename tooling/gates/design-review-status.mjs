// Keep the runner's aggregation independent of the render parser: phase 4 imports this before
// root-clean-install has installed TypeScript in a fresh clone.
export const EXPIRED_RULE = 'An EXPIRED verdict on a module counted by the catalogue fails the gate on every host.'

// The sweep supplies exactly the UI modules counted by catalog/modules.yaml. Their lifecycle
// status does not waive an expired human verdict; other unfinished gates retain their worklist role.
export function designReviewSummary(modules) {
  const expired = modules.filter(module => module.gates.some(row => row.id === 'assertDesignReview'
    && row.status === 'FAIL' && /\bEXPIRED\b/.test(row.note))).map(module => module.moduleId)
  return {status: expired.length ? 'FAIL' : 'PASS', expired, rule: EXPIRED_RULE}
}
