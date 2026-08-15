#!/usr/bin/env bash
#
# Fails if a converted file still carries the template's patterns.
#
# This exists because "the pages are converted" was reported twice while thirteen of them still had
# template cards. A grep cannot tell you whether something looks good — but it can tell you whether it
# was actually converted, which is precisely the claim that turned out to be wrong.
#
# Usage:  scripts/ui-guard.sh            # check every file listed as converted
#         scripts/ui-guard.sh <file>...  # check specific files
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

# Files that have been through the redesign. Add a file here as you convert it — that is the point of
# commitment: from then on it cannot regress without failing.
CONVERTED=(
  "src/app/(admin)/page.tsx"
  "src/app/(admin)/sessions/page.tsx"
  "src/app/(admin)/sessions/[id]/page.tsx"
  "src/app/(admin)/memory/page.tsx"
  "src/components/continuum/TranscriptEvent.tsx"
  "src/components/continuum/MemoryList.tsx"
  "src/app/(admin)/rooms/page.tsx"
  "src/app/(admin)/agents/page.tsx"
  "src/app/(admin)/projects/page.tsx"
  "src/layout/AppSidebar.tsx"
  "src/layout/AppHeader.tsx"
)

# pattern :: why it is banned
BANNED=(
  'rounded-2xl::use rounded-card / rounded-control / rounded-chip'
  'border-gray-(200|800)::use border-line, or shadow-card for elevation'
  'text-2xl font-bold::use <PageHeader>'
  '\bh-1[01]\b::controls are 32px (h-8); see components/bui/form'
  '(bg|text|border)-brand-::use accent / accent-ink / accent-tint'
  'dark:bg-white/\[0\.0[0-9]\]::surfaces come from bg-surface, which already flips'
)

targets=("$@")
[[ ${#targets[@]} -eq 0 ]] && targets=("${CONVERTED[@]}")

fail=0
for f in "${targets[@]}"; do
  [[ -f "$f" ]] || { echo "  ?  missing: $f"; continue; }
  for entry in "${BANNED[@]}"; do
    pat="${entry%%::*}"; why="${entry#*::}"
    if hits=$(grep -nE "$pat" "$f"); then
      echo "  ✗  $f"
      echo "     $why"
      echo "$hits" | sed 's/^/       /'
      fail=1
    fi
  done
done

if [[ $fail -eq 0 ]]; then
  echo "  ✓  ${#targets[@]} file(s) clean"
else
  echo
  echo "UI guard failed. Either fix the file, or if it genuinely isn't converted yet, remove it from CONVERTED."
fi
exit $fail
