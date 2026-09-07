import type { LensTone } from '@harborline-platform/hlp.ui.aspect-lens'

export interface ToneStyle {
  swatch: string
  border: string
  softBg: string
  text: string
}

const styles: Readonly<Record<LensTone, Readonly<ToneStyle>>> = {
  accent: {
    swatch: 'var(--color-accent)',
    border: 'var(--color-accent)',
    softBg: 'var(--color-accent-soft)',
    text: 'var(--color-accent-foreground)',
  },
  warning: {
    swatch: 'var(--color-warning)',
    border: 'var(--color-warning)',
    softBg: 'var(--color-warning-soft)',
    text: 'var(--color-warning)',
  },
  danger: {
    swatch: 'var(--color-danger)',
    border: 'var(--color-danger)',
    softBg: 'var(--color-danger-soft)',
    text: 'var(--color-danger)',
  },
  success: {
    swatch: 'var(--color-success)',
    border: 'var(--color-success)',
    softBg: 'var(--color-success-soft)',
    text: 'var(--color-success)',
  },
  info: {
    swatch: 'var(--color-secondary)',
    border: 'var(--color-secondary)',
    softBg: 'var(--color-muted)',
    text: 'var(--color-secondary-foreground)',
  },
  muted: {
    swatch: 'var(--color-muted-foreground)',
    border: 'var(--color-border)',
    softBg: 'var(--color-muted)',
    text: 'var(--color-muted-foreground)',
  },
  'sensitivity-none': {
    swatch: 'var(--color-muted-foreground)',
    border: 'var(--color-border)',
    softBg: 'var(--color-muted)',
    text: 'var(--color-muted-foreground)',
  },
  'sensitivity-low': {
    swatch: 'var(--color-accent)',
    border: 'var(--color-accent)',
    softBg: 'var(--color-accent-soft)',
    text: 'var(--color-accent-foreground)',
  },
  'sensitivity-medium': {
    swatch: 'var(--color-warning)',
    border: 'var(--color-warning)',
    softBg: 'var(--color-warning-soft)',
    text: 'var(--color-warning)',
  },
  'sensitivity-high': {
    swatch: 'var(--color-danger)',
    border: 'var(--color-danger)',
    softBg: 'var(--color-danger-soft)',
    text: 'var(--color-danger)',
  },
}

export function toneStyle(tone: LensTone): ToneStyle {
  return styles[tone]
}
