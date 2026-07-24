#!/usr/bin/env bash
# End-to-end smoke test for both the Inward and Outward flows, run against a live API.
#
# Usage:
#   dotnet run --project src/WarehouseGate.Api --urls "http://localhost:5080;https://localhost:7174"
#   ./scripts/test-full-flow.sh                                             # in another terminal
#
# Exercises the seeded sample data for Inward (see SeedData.cs: 3 POs, 3 vehicles); Outward pick
# lists are generated from fresh Dispatch Plan (VehicleLogisticsRecord) rows created by this script
# itself via the Logistics Manager API, matching how Office actually generates them in the app.
# Logins: security1/supervisor1/supervisor2/office1/logistics1. Requires python3 (JSON parsing).
set -euo pipefail

# The API's launchSettings/dev setup binds both an http port (mobile app) and an https port (web
# portal) at once; UseHttpsRedirection then 307-redirects plain http requests, which curl doesn't
# follow by default. Hit the https port directly and skip cert validation (local dev cert only).
curl() { command curl -k "$@"; }

BASE_URL="${BASE_URL:-https://localhost:7174}"
PASS=0
FAIL=0
TMP_PHOTO="$(dirname "$0")/_smoke-test-photo.jpg"

status=$(curl -s -o /dev/null -w "%{http_code}" "$BASE_URL/swagger/index.html" || echo "000")
if [ "$status" != "200" ]; then
  echo "ERROR: cannot reach API at $BASE_URL (status $status)."
  echo "Start it first: cd src/WarehouseGate.Api && dotnet run --urls \"$BASE_URL\""
  exit 1
fi

login() {
  curl -s -X POST "$BASE_URL/api/auth/login" -H "Content-Type: application/json" \
    -d "{\"userName\":\"$1\",\"password\":\"Pass123\$\"}" \
    | python3 -c "import json,sys; print(json.load(sys.stdin)['token'])"
}

# field <json> <python-expr-on-d> -- evaluates a python expression against the parsed JSON
field() {
  python3 -c "import json,sys; d=json.loads(sys.argv[1]); print($2)" "$1"
}

check() {
  if [ "$2" == "$3" ]; then
    echo "  PASS: $1 (got $2)"
    PASS=$((PASS + 1))
  else
    echo "  FAIL: $1 (expected $3, got $2)"
    FAIL=$((FAIL + 1))
  fi
}

echo "sample photo content" > "$TMP_PHOTO"
cleanup() { rm -f "$TMP_PHOTO"; }
trap cleanup EXIT

echo "Logging in as security1, supervisor1, supervisor2, office1, logistics1..."
SECURITY_TOKEN=$(login security1)
SUP1_TOKEN=$(login supervisor1)
SUP2_TOKEN=$(login supervisor2)
OFFICE_TOKEN=$(login office1)
LOGISTICS_TOKEN=$(login logistics1)
STAMP=$(date +%s)
echo "done."
echo

# Outward pick lists are now generated from Dispatch Plan (VehicleLogisticsRecord) data uploaded/
# entered by the Logistics Manager, not from seeded DispatchOrder rows - create-a-row helper used
# by the OUTWARD sections below. Warehouse 1 = Mumbai DC (office1's own warehouse), Warehouse 3 =
# Bengaluru DC (see SeedData.cs); SkuCodes below match real seeded Product rows so pick-list
# generation's Product-matching requirement is satisfied.
create_dispatch_plan_row() {
  curl -s -X POST "$BASE_URL/api/logistics/vehicle-records" -H "Authorization: Bearer $LOGISTICS_TOKEN" -H "Content-Type: application/json" \
    -d "{\"vehicleNumber\":\"$1\",\"sku\":\"$2\",\"skuCode\":\"$3\",\"boxQuantity\":$4,\"fromWarehouseId\":1,\"toWarehouseId\":3}" > /dev/null
}

# ============================================================================
# INWARD 1 — PO-1001 / MH04GT3312: clean receipt, every line Ok -> no exceptions
# ============================================================================
echo "=== INWARD 1: clean receipt (PO-1001, MH04GT3312) ==="
JOB=$(curl -s -X POST "$BASE_URL/api/gate/checkin" -H "Authorization: Bearer $SECURITY_TOKEN" -H "Content-Type: application/json" \
  -d "{\"vehicleNumber\":\"MH04GT3312\",\"inwardTxnNumber\":\"INW-TEST-A-$STAMP\",\"poNumber\":\"PO-1001\",\"remarks\":\"Tarpaulin slightly torn on left side\"}")
ID=$(field "$JOB" "d['id']")
check "check-in remarks stored" "$(field "$JOB" "d['remarks']")" "Tarpaulin slightly torn on left side"

GATE_PHOTO=$(curl -s -X POST "$BASE_URL/api/gate/$ID/photos" -H "Authorization: Bearer $SECURITY_TOKEN" \
  -F "type=VehicleFront" -F "file=@$TMP_PHOTO;type=image/jpeg")
check "structured gate photo VehicleFront stored" "$(field "$GATE_PHOTO" "any(p['type']=='VehicleFront' for p in d['photos'])")" "True"

SEAL_PHOTO=$(curl -s -X POST "$BASE_URL/api/gate/$ID/photos" -H "Authorization: Bearer $SECURITY_TOKEN" \
  -F "type=VehicleSeal" -F "file=@$TMP_PHOTO;type=image/jpeg")
check "structured gate photo VehicleSeal stored" "$(field "$SEAL_PHOTO" "any(p['type']=='VehicleSeal' for p in d['photos'])")" "True"

curl -s -X POST "$BASE_URL/api/inward/$ID/claim" -H "Authorization: Bearer $SUP1_TOKEN" > /dev/null

CLAIM2_STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE_URL/api/inward/$ID/claim" -H "Authorization: Bearer $SUP2_TOKEN")
check "second supervisor's claim is rejected (already claimed)" "$CLAIM2_STATUS" "400"

curl -s -X POST "$BASE_URL/api/inward/$ID/dock-in" -H "Authorization: Bearer $SUP1_TOKEN" -H "Content-Type: application/json" \
  -d '{"bayName":"Bay-1"}' > /dev/null
curl -s -X POST "$BASE_URL/api/inward/$ID/photos" -H "Authorization: Bearer $SUP1_TOKEN" \
  -F "type=VehicleBefore" -F "file=@$TMP_PHOTO;type=image/jpeg" > /dev/null

LINES_JSON=$(field "$JOB" "json.dumps([{'purchaseOrderLineId': l['id'], 'receivedQty': l['expectedQty'], 'condition': 'Ok', 'notes': None} for l in d['lines']])")
curl -s -X POST "$BASE_URL/api/inward/$ID/inspection" -H "Authorization: Bearer $SUP1_TOKEN" -H "Content-Type: application/json" \
  -d "{\"lines\":$LINES_JSON}" > /dev/null
RESULT=$(curl -s -X POST "$BASE_URL/api/inward/$ID/complete" -H "Authorization: Bearer $SUP1_TOKEN")
check "GRN hasExceptions" "$(field "$RESULT" "d['grn']['hasExceptions']")" "False"

ACTIVE_AFTER=$(curl -s "$BASE_URL/api/gate/transactions?activeOnly=true" -H "Authorization: Bearer $SECURITY_TOKEN")
check "completed vehicle absent from active-only list" "$(field "$ACTIVE_AFTER" "any(t['inwardTxnNumber']=='INW-TEST-A-$STAMP' for t in d)")" "False"

HISTORY_SEARCH=$(curl -s "$BASE_URL/api/gate/transactions?activeOnly=false&vehicleNumber=MH04GT3312" -H "Authorization: Bearer $SECURITY_TOKEN")
check "completed vehicle found via vehicleNumber history search" "$(field "$HISTORY_SEARCH" "any(t['inwardTxnNumber']=='INW-TEST-A-$STAMP' for t in d)")" "True"

PENDING_EXIT=$(curl -s "$BASE_URL/api/gate/transactions/pending-exit?vehicleNumber=MH04GT3312" -H "Authorization: Bearer $SECURITY_TOKEN")
check "completed vehicle appears in pending-exit list" "$(field "$PENDING_EXIT" "any(t['id']==$ID for t in d)")" "True"

EXIT_RESULT=$(curl -s -X POST "$BASE_URL/api/gate/$ID/exit" -H "Authorization: Bearer $SECURITY_TOKEN" \
  -F "file=@$TMP_PHOTO;type=image/jpeg")
check "exit records gateOutTime" "$(field "$EXIT_RESULT" "d['gateOutTime'] is not None")" "True"
check "exit gate pass token format" "$(field "$EXIT_RESULT" "(d['gatePassToken'] or '').startswith('EXIT-') and len(d['gatePassToken'] or '') == 18")" "True"

PENDING_EXIT_AFTER=$(curl -s "$BASE_URL/api/gate/transactions/pending-exit?vehicleNumber=MH04GT3312" -H "Authorization: Bearer $SECURITY_TOKEN")
check "exited vehicle no longer in pending-exit list" "$(field "$PENDING_EXIT_AFTER" "any(t['id']==$ID for t in d)")" "False"

EXIT_AGAIN_STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE_URL/api/gate/$ID/exit" -H "Authorization: Bearer $SECURITY_TOKEN" \
  -F "file=@$TMP_PHOTO;type=image/jpeg")
check "second exit on same vehicle is rejected" "$EXIT_AGAIN_STATUS" "400"
echo

# ============================================================================
# INWARD 2 — PO-1002 / GJ01AX7790: damaged + short delivery -> exception flagged
# ============================================================================
echo "=== INWARD 2: damaged + short delivery (PO-1002, GJ01AX7790) ==="
JOB=$(curl -s -X POST "$BASE_URL/api/gate/checkin" -H "Authorization: Bearer $SECURITY_TOKEN" -H "Content-Type: application/json" \
  -d "{\"vehicleNumber\":\"GJ01AX7790\",\"inwardTxnNumber\":\"INW-TEST-B-$STAMP\",\"poNumber\":\"PO-1002\"}")
ID=$(field "$JOB" "d['id']")

ACTIVE_LIST=$(curl -s "$BASE_URL/api/gate/transactions?activeOnly=true" -H "Authorization: Bearer $SECURITY_TOKEN")
check "in-progress vehicle appears in active-only list" "$(field "$ACTIVE_LIST" "any(t['id']==$ID for t in d)")" "True"

EXIT_TOO_EARLY_STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE_URL/api/gate/$ID/exit" -H "Authorization: Bearer $SECURITY_TOKEN" \
  -F "file=@$TMP_PHOTO;type=image/jpeg")
check "exit rejected before inspection is complete" "$EXIT_TOO_EARLY_STATUS" "400"

curl -s -X POST "$BASE_URL/api/inward/$ID/claim" -H "Authorization: Bearer $SUP1_TOKEN" > /dev/null
curl -s -X POST "$BASE_URL/api/inward/$ID/dock-in" -H "Authorization: Bearer $SUP1_TOKEN" -H "Content-Type: application/json" \
  -d '{"bayName":"Bay-2"}' > /dev/null
curl -s -X POST "$BASE_URL/api/inward/$ID/photos" -H "Authorization: Bearer $SUP1_TOKEN" \
  -F "type=MaterialBefore" -F "file=@$TMP_PHOTO;type=image/jpeg" > /dev/null

LINES_JSON=$(field "$JOB" "json.dumps([{'purchaseOrderLineId': l['id'], 'receivedQty': float(l['expectedQty']) - 50, 'condition': 'Damaged', 'notes': 'Carton crushed, 50 units short'} for l in d['lines']])")
curl -s -X POST "$BASE_URL/api/inward/$ID/inspection" -H "Authorization: Bearer $SUP1_TOKEN" -H "Content-Type: application/json" \
  -d "{\"lines\":$LINES_JSON}" > /dev/null
RESULT=$(curl -s -X POST "$BASE_URL/api/inward/$ID/complete" -H "Authorization: Bearer $SUP1_TOKEN")
check "GRN hasExceptions" "$(field "$RESULT" "d['grn']['hasExceptions']")" "True"
echo

# ============================================================================
# INWARD 3 — PO-1003 / KA05MZ4521: excess on one line, mismatch on another
# ============================================================================
echo "=== INWARD 3: excess + mismatch (PO-1003, KA05MZ4521, supervisor2) ==="
JOB=$(curl -s -X POST "$BASE_URL/api/gate/checkin" -H "Authorization: Bearer $SECURITY_TOKEN" -H "Content-Type: application/json" \
  -d "{\"vehicleNumber\":\"KA05MZ4521\",\"inwardTxnNumber\":\"INW-TEST-C-$STAMP\",\"poNumber\":\"PO-1003\"}")
ID=$(field "$JOB" "d['id']")

curl -s -X POST "$BASE_URL/api/inward/$ID/claim" -H "Authorization: Bearer $SUP2_TOKEN" > /dev/null
curl -s -X POST "$BASE_URL/api/inward/$ID/dock-in" -H "Authorization: Bearer $SUP2_TOKEN" -H "Content-Type: application/json" \
  -d '{"bayName":"Bay-3"}' > /dev/null
curl -s -X POST "$BASE_URL/api/inward/$ID/photos" -H "Authorization: Bearer $SUP2_TOKEN" \
  -F "type=VehicleBefore" -F "file=@$TMP_PHOTO;type=image/jpeg" > /dev/null

LINES_JSON=$(field "$JOB" "json.dumps([{'purchaseOrderLineId': d['lines'][0]['id'], 'receivedQty': float(d['lines'][0]['expectedQty']) + 50, 'condition': 'Excess', 'notes': 'Supplier over-shipped'}, {'purchaseOrderLineId': d['lines'][1]['id'], 'receivedQty': d['lines'][1]['expectedQty'], 'condition': 'Mismatch', 'notes': 'Wrong washer size received'}])")
curl -s -X POST "$BASE_URL/api/inward/$ID/inspection" -H "Authorization: Bearer $SUP2_TOKEN" -H "Content-Type: application/json" \
  -d "{\"lines\":$LINES_JSON}" > /dev/null
RESULT=$(curl -s -X POST "$BASE_URL/api/inward/$ID/complete" -H "Authorization: Bearer $SUP2_TOKEN")
check "GRN hasExceptions" "$(field "$RESULT" "d['grn']['hasExceptions']")" "True"
echo

# ============================================================================
# OUTWARD 1 — Dispatch Plan vehicle DPOUT1: full load, exactly as ordered -> not partial
# ============================================================================
echo "=== OUTWARD 1: full load (Dispatch Plan, supervisor1) ==="
VEH="DPOUT1-$STAMP"
create_dispatch_plan_row "$VEH" "Hex Bolt M12" "HW-BOLT-M12" 100
JOB=$(curl -s -X POST "$BASE_URL/api/office/dispatch-plan/outward/$VEH/generate-picklist" -H "Authorization: Bearer $OFFICE_TOKEN" -H "Content-Type: application/json" -d '{}')
ID=$(field "$JOB" "d['id']")

# Regression check for the WarehouseId bug this session's Dispatch Plan bridge fixed:
# GeneratePickListAsync never used to set WarehouseId (only real gate check-in did), so a job
# generated by Office couldn't be seen or assigned by that same Office until a truck physically
# gated in. Confirm it shows up in Office's own list immediately, before any dock-in has happened.
OWN_LIST=$(curl -s "$BASE_URL/api/office/outward-jobs?activeOnly=true" -H "Authorization: Bearer $OFFICE_TOKEN")
check "freshly generated pick list visible in Office's own Outward Jobs list" "$(field "$OWN_LIST" "any(t['id']==$ID for t in d)")" "True"

curl -s -X POST "$BASE_URL/api/outward/$ID/claim" -H "Authorization: Bearer $SUP1_TOKEN" > /dev/null

CLAIM2_STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE_URL/api/outward/$ID/claim" -H "Authorization: Bearer $SUP2_TOKEN")
check "second supervisor's claim is rejected (already claimed)" "$CLAIM2_STATUS" "400"

curl -s -X POST "$BASE_URL/api/outward/$ID/dock-in" -H "Authorization: Bearer $SUP1_TOKEN" -H "Content-Type: application/json" \
  -d '{"vehicleNumber":"MH04GT3312","bayName":"Load-1"}' > /dev/null
curl -s -X POST "$BASE_URL/api/outward/$ID/photos" -H "Authorization: Bearer $SUP1_TOKEN" \
  -F "type=VehicleLoaded" -F "file=@$TMP_PHOTO;type=image/jpeg" > /dev/null

LOAD_LINES_JSON=$(field "$JOB" "json.dumps([{'dispatchOrderLineId': l['id'], 'loadedQty': l['orderedQty'], 'loadSequence': i+1, 'notes': None} for i, l in enumerate(d['lines'])])")
curl -s -X POST "$BASE_URL/api/outward/$ID/load-lines" -H "Authorization: Bearer $SUP1_TOKEN" -H "Content-Type: application/json" \
  -d "{\"lines\":$LOAD_LINES_JSON}" > /dev/null
curl -s -X POST "$BASE_URL/api/outward/$ID/confirm-dispatch-ready" -H "Authorization: Bearer $SUP1_TOKEN" > /dev/null
RESULT=$(curl -s -X POST "$BASE_URL/api/outward/$ID/complete" -H "Authorization: Bearer $SUP1_TOKEN")
check "dispatch note isPartial" "$(field "$RESULT" "d['dispatchNote']['isPartial']")" "False"
echo

# ============================================================================
# OUTWARD 2 — Dispatch Plan vehicle DPOUT2: physical stock short -> partial load, supervisor2
# ============================================================================
echo "=== OUTWARD 2: partial load, stock short (Dispatch Plan, supervisor2) ==="
VEH="DPOUT2-$STAMP"
create_dispatch_plan_row "$VEH" "Flat Washer M12" "HW-WASH-M12" 100
JOB=$(curl -s -X POST "$BASE_URL/api/office/dispatch-plan/outward/$VEH/generate-picklist" -H "Authorization: Bearer $OFFICE_TOKEN" -H "Content-Type: application/json" -d '{}')
ID=$(field "$JOB" "d['id']")

curl -s -X POST "$BASE_URL/api/outward/$ID/claim" -H "Authorization: Bearer $SUP2_TOKEN" > /dev/null
curl -s -X POST "$BASE_URL/api/outward/$ID/dock-in" -H "Authorization: Bearer $SUP2_TOKEN" -H "Content-Type: application/json" \
  -d '{"vehicleNumber":"GJ01AX7790","bayName":"Load-2"}' > /dev/null
curl -s -X POST "$BASE_URL/api/outward/$ID/photos" -H "Authorization: Bearer $SUP2_TOKEN" \
  -F "type=MaterialLoaded" -F "file=@$TMP_PHOTO;type=image/jpeg" > /dev/null

LOAD_LINES_JSON=$(field "$JOB" "json.dumps([{'dispatchOrderLineId': l['id'], 'loadedQty': float(l['orderedQty']) - 60, 'loadSequence': i+1, 'notes': 'Short on physical stock'} for i, l in enumerate(d['lines'])])")
curl -s -X POST "$BASE_URL/api/outward/$ID/load-lines" -H "Authorization: Bearer $SUP2_TOKEN" -H "Content-Type: application/json" \
  -d "{\"lines\":$LOAD_LINES_JSON}" > /dev/null
curl -s -X POST "$BASE_URL/api/outward/$ID/confirm-dispatch-ready" -H "Authorization: Bearer $SUP2_TOKEN" > /dev/null
RESULT=$(curl -s -X POST "$BASE_URL/api/outward/$ID/complete" -H "Authorization: Bearer $SUP2_TOKEN")
check "dispatch note isPartial" "$(field "$RESULT" "d['dispatchNote']['isPartial']")" "True"
echo

# ============================================================================
# OUTWARD 3 — Dispatch Plan vehicle DPOUT3: full load, two lines loaded in a deliberate
# front/back sequence
# ============================================================================
echo "=== OUTWARD 3: full load with load sequencing (Dispatch Plan, supervisor1) ==="
VEH="DPOUT3-$STAMP"
create_dispatch_plan_row "$VEH" "Flat Washer M12" "HW-WASH-M12" 80
create_dispatch_plan_row "$VEH" "Hex Bolt M12" "HW-BOLT-M12" 80
JOB=$(curl -s -X POST "$BASE_URL/api/office/dispatch-plan/outward/$VEH/generate-picklist" -H "Authorization: Bearer $OFFICE_TOKEN" -H "Content-Type: application/json" -d '{}')
ID=$(field "$JOB" "d['id']")
check "both Dispatch Plan lines carried into the pick list" "$(field "$JOB" "len(d['lines'])")" "2"

curl -s -X POST "$BASE_URL/api/outward/$ID/claim" -H "Authorization: Bearer $SUP1_TOKEN" > /dev/null
curl -s -X POST "$BASE_URL/api/outward/$ID/dock-in" -H "Authorization: Bearer $SUP1_TOKEN" -H "Content-Type: application/json" \
  -d '{"vehicleNumber":"KA05MZ4521","bayName":"Load-3"}' > /dev/null
curl -s -X POST "$BASE_URL/api/outward/$ID/photos" -H "Authorization: Bearer $SUP1_TOKEN" \
  -F "type=VehicleLoaded" -F "file=@$TMP_PHOTO;type=image/jpeg" > /dev/null

# Heavier washers loaded first (placed at the back), lighter bolts loaded last (placed at the front).
LOAD_LINES_JSON=$(field "$JOB" "json.dumps([{'dispatchOrderLineId': l['id'], 'loadedQty': l['orderedQty'], 'loadSequence': (1 if 'Washer' in l['productName'] else 2), 'notes': None} for l in d['lines']])")
curl -s -X POST "$BASE_URL/api/outward/$ID/load-lines" -H "Authorization: Bearer $SUP1_TOKEN" -H "Content-Type: application/json" \
  -d "{\"lines\":$LOAD_LINES_JSON}" > /dev/null
curl -s -X POST "$BASE_URL/api/outward/$ID/confirm-dispatch-ready" -H "Authorization: Bearer $SUP1_TOKEN" > /dev/null
RESULT=$(curl -s -X POST "$BASE_URL/api/outward/$ID/complete" -H "Authorization: Bearer $SUP1_TOKEN")
check "dispatch note isPartial" "$(field "$RESULT" "d['dispatchNote']['isPartial']")" "False"
echo

echo "============================================================"
echo "Summary: $PASS passed, $FAIL failed"
echo "============================================================"
[ "$FAIL" -eq 0 ]
