#!/usr/bin/env bash
# Sequential FINAL battery (one suite at a time - never concurrent: the master DB
# and the demo state are shared, and concurrent harnesses poison each other).
set -u
cd "$(dirname "$0")/verify_run" || exit 1
EXE=./AlphaElite.winforms.exe
run() {
  local name="$1"; shift
  echo "=== START $name $(date +%H:%M:%S) ==="
  "$EXE" "$@" > "../final_${name}.log" 2>&1
  local code=$?
  echo "EXIT=$code" >> "../final_${name}.log"
  echo "=== DONE  $name $(date +%H:%M:%S) exit=$code ==="
}
run remove-legacy-plans            --master-remove-legacy-plans
run master-model-diff              --master-model-diff
run master-verify-subs             --master-verify-subs
run phase3-subscription-verify     --phase3-subscription-verify
run phase4-subscription-workflow-verify --phase4-subscription-workflow-verify
run phase7-subscription-verify     --phase7-subscription-verify
run phase9-subscription-verify     --phase9-subscription-verify
run super-admin-bi-verify          --super-admin-bi-verify
run subscription-redesign-verify   --subscription-redesign-verify
run master-connectivity-verify     --master-connectivity-verify
run theme-verify                   --theme-verify
run phase5-verify                  --phase5-verify
run phase4-verify                  --phase4-verify
run phase6-verify                  --phase6-verify
run phase8-verify                  --phase8-verify
run phase8-fixes-verify            --phase8-fixes-verify
run complaints-verify              --complaints-verify
run tenantb-verify                 --tenantb-verify
run tenantc-verify-bi              --tenantc-verify-bi
echo "=== ALL FINAL SUITES COMPLETE $(date +%H:%M:%S) ==="
