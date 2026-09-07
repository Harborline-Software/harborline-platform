#!/usr/bin/env bash
set -euo pipefail

repo_root=$(cd "$(dirname "$0")/.." && pwd)

# The forbidden consumer codename is assembled from character codes rather than written out.
#
# A guard that bans a name by spelling it out defeats its own purpose: a grep for that name across
# this repository finds the guard itself, and the repository is meant to carry no trace of any
# consumer -- in its code, its paths, its fixtures, or its checks. An earlier version of this file
# split the literal across a concatenation so the script would not match ITSELF. That solved a
# smaller, different problem: the name stayed plainly readable to anyone, or any tool, reading
# these files.
#
# printf with hex escapes is used rather than base64 because it needs no external binary and
# behaves identically under bash on Linux, macOS, and Git Bash (macOS base64 spells its decode
# flag differently, which would make this pass vacuously on one platform).
#
# POSITIVE CONTROL, per docs/adr/0010 in harborline-control. A guard nobody has watched fail is
# not yet a guard. To confirm this one still bites, write the decoded string into any tracked file
# and run this script: it must exit 1 and print that file's path. Do that after any edit here.
consumer_name=$(printf '\x43\x6f\x6d\x65\x74\x58')

content_matches=$(git -C "$repo_root" grep -ni "$consumer_name" -- . || true)
path_matches=$(git -C "$repo_root" ls-files | grep -i "$consumer_name" || true)

if [ -n "$content_matches" ] || [ -n "$path_matches" ]; then
  echo "Consumer-specific naming is forbidden in Harborline Platform." >&2
  [ -z "$path_matches" ] || printf '%s\n' "$path_matches" >&2
  [ -z "$content_matches" ] || printf '%s\n' "$content_matches" >&2
  exit 1
fi

echo "Harborline Platform consumer-neutral boundary: PASS"
