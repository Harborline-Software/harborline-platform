# Harborline.Kernel.Core

This internal package owns the producer floor shared by kernel-facing modules: one injected
authoritative clock, one-command atomic commit and rollback, and the three compiled shapes that
exist before a catalogue seed is installed.

It contains no host persistence, member interpreter, package installer, or HTTP adapter.
