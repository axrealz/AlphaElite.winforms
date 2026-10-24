#!/usr/bin/env bash
# Sequential regression battery — one suite at a time (capture-at-start checks
# require no concurrent Tenant A/B/C writers). Exit status of each suite is logged
# inside its own log file as an EXIT= line.
cd "$(dirname "$0")" || exit 1

run_suite() {
  local name="$1"; shift
  echo "=== START $name $(date +%H:%M:%S) ==="
  dotnet run --project AlphaElite.winforms/AlphaElite.winforms.csproj --no-build -- "$@" \
    > "suite_${name}.log" 2>&1
  echo "EXIT=$?" >> "suite_${name}.log"
  echo "=== DONE  $name $(date +%H:%M:%S) exit=$(tail -n1 "suite_${name}.log") ==="
}

run_suite phase1       --phase1-verify
run_suite phase2       --phase2-verify
run_suite phase4       --phase4-verify
run_suite phase5       --phase5-verify
run_suite phase6       --phase6-verify
run_suite phase8fixes  --phase8-fixes-verify
run_suite phase9       --phase9-subscription-verify
run_suite phase8       --phase8-verify
run_suite complaints   --complaints-verify
run_suite tenantb      --tenantb-verify

echo "=== ALL SUITES COMPLETE $(date +%H:%M:%S) ==="
for f in suite_*.log; do echo "$f $(tail -n1 "$f")"; done
