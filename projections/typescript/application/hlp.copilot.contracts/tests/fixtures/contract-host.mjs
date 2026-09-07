import { assertDispatched, confirmDispatch } from '../../dist/index.js'

// Executable host contract specimen, not a shipped adapter or host containment proof.
// A destination host must run these obligations against its real composition (S3/S4).
export function contractHost() {
  return {
    liveKey: 'form#1', authorized: true, failure: null, state: [], attempts: 0,
    currentContextKey() { return this.liveKey },
    apply(receipt) { return confirmDispatch(receipt, this.currentContextKey(receipt.surface)) },
    async execute(receipt, gesture) {
      try { assertDispatched(receipt, this.currentContextKey(receipt?.surface), gesture) }
      catch (error) { return { ok: false, code: error.message } }
      if (!this.authorized) return { ok: false, code: 'forbidden' }
      this.attempts++
      // Stage the complete change and commit only after all effect steps succeed.
      const staged = [...this.state, receipt.args]
      if (this.failure) return { ok: false, code: this.failure }
      this.state = staged
      return { ok: true }
    },
  }
}
