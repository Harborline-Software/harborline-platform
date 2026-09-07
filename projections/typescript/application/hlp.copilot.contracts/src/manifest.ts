import type { CommandSpec } from './types.js'
export function manifestLine(spec: CommandSpec): string { return `- ${spec.id} ${spec.argsHint} — ${spec.summary}` }
export function manifestLines(specs: readonly CommandSpec[]): string[] { return specs.filter(s => s.classification.tier !== 'never').map(manifestLine) }
export function envelopeHeader(surface: string): string[] { return ['Return exactly one JSON object in a fenced json block:', `{"schema":"pilot.proposal/3","surface":"${surface}","command":"<id>","args":{}}`] }
export function buildManifest(surface: string, specs: readonly CommandSpec[]): string { return [...envelopeHeader(surface), '', 'Commands:', ...manifestLines(specs)].join('\n') }
