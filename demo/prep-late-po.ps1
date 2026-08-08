<#
.SYNOPSIS
    Recording-only prep script for the demo video's "late PO" beat (§17 of the locked spec's
    single Demo Scenario: "แสดง Late PO 1,000 kg แต่ไม่นับใน Eligible Incoming" / "แสดง PO-DEMO-001
    Late 4 วัน"). NOT part of the deployment pipeline and NOT the canonical seed - this is a
    temporary, throwaway row pair you insert right before recording and remove right after.

.DESCRIPTION
    The canonical seed deliberately does NOT include a late Incoming PO for RM-001 - a genuinely
    late PO there would flip the locked DashboardTests.Kpi_counts_match_the_canonical_seed
    assertion (LatePurchaseOrderCount == 0) and several other locked figures (see
    docs/00_Project_Status.md's "Seed follow-up acceptance evidence" section for the full
    analysis). This script creates ONE throwaway Purchase Request + Incoming Purchase Order
    (PO-DEMO-001, 1,000 kg, 4 days late, for RM-001) directly via sqlcmd, matching the locked
    scenario's exact numbers, then removes both cleanly afterward - the same
    insert-for-verification-then-delete pattern already used for live checks in Days 7, 12, and
    13 of this project's own build.

.PARAMETER Cleanup
    Remove the rows this script inserted. Safe to run even if nothing was inserted.

.PARAMETER ConnectionString
    Defaults to the same LocalDB target used everywhere else in this repo.

.EXAMPLE
    .\demo\prep-late-po.ps1              # insert before recording
    .\demo\prep-late-po.ps1 -Cleanup     # remove after recording
#>
param(
    [switch]$Cleanup,
    [string]$Server = '(localdb)\MSSQLLocalDB',
    [string]$Database = 'AI_Factory_CommandCenter'
)

$ErrorActionPreference = 'Stop'

if ($Cleanup) {
    $sql = @'
DECLARE @PoId BIGINT = (SELECT Id FROM IncomingPurchaseOrders WHERE PurchaseOrderNumber = 'PO-DEMO-001');
DECLARE @PrId BIGINT = (SELECT Id FROM PurchaseRequests WHERE RequestNumber = 'PR-DEMO-LATE-001');

DELETE FROM IncomingPurchaseOrderItems WHERE IncomingPurchaseOrderId = @PoId;
DELETE FROM IncomingPurchaseOrders WHERE Id = @PoId;
DELETE FROM PurchaseRequestItems WHERE PurchaseRequestId = @PrId;
DELETE FROM PurchaseRequests WHERE Id = @PrId;

SELECT
    (SELECT COUNT(*) FROM PurchaseRequests WHERE RequestNumber = 'PR-DEMO-LATE-001') AS RemainingPR,
    (SELECT COUNT(*) FROM IncomingPurchaseOrders WHERE PurchaseOrderNumber = 'PO-DEMO-001') AS RemainingPO;
'@
    Write-Host "Removing the temporary late PO (PO-DEMO-001) and its source PR..." -ForegroundColor Cyan
}
else {
    $sql = @'
IF EXISTS (SELECT 1 FROM PurchaseRequests WHERE RequestNumber = 'PR-DEMO-LATE-001')
BEGIN
    PRINT 'PR-DEMO-LATE-001 already exists - run with -Cleanup first if you want to reset it.';
    RETURN;
END

DECLARE @PlanId BIGINT = (SELECT Id FROM ProductionPlans WHERE PlanNumber = 'PP-DEMO-001');
DECLARE @PlannerId BIGINT = (SELECT Id FROM AspNetUsers WHERE UserName = 'planner.demo');
DECLARE @ManagerId BIGINT = (SELECT Id FROM AspNetUsers WHERE UserName = 'manager.demo');
DECLARE @RM001Id BIGINT = (SELECT Id FROM RawMaterials WHERE Code = 'RM-001');
-- Anchored off the REAL current date, not the seed's frozen T: Development/Production run on
-- TimeProvider.System (wall-clock time), so "days late" is computed against actual today. Using
-- T here would make the on-screen delay drift by however many days have passed since the seed
-- was created - anchoring off today keeps it exactly "4 days late" no matter when this runs.
DECLARE @Today DATETIME2 = CAST(CAST(SYSUTCDATETIME() AS DATE) AS DATETIME2);
DECLARE @LateDate DATETIME2 = DATEADD(DAY, -4, @Today);

INSERT INTO PurchaseRequests (RequestNumber, SourceProductionPlanId, Status, RequestedByUserId, RequestedDate, ApprovedByUserId, ApprovedDate, CreatedAt)
VALUES ('PR-DEMO-LATE-001', @PlanId, 'Approved', @PlannerId, @LateDate, @ManagerId, @LateDate, @LateDate);

DECLARE @PrId BIGINT = SCOPE_IDENTITY();

INSERT INTO PurchaseRequestItems (PurchaseRequestId, RawMaterialId, RequestedQuantity, ExpectedDate)
VALUES (@PrId, @RM001Id, 1000, @LateDate);

INSERT INTO IncomingPurchaseOrders (PurchaseOrderNumber, PurchaseRequestId, ExpectedDate, Status, CreatedAt)
VALUES ('PO-DEMO-001', @PrId, @LateDate, 'Open', @LateDate);

DECLARE @PoId BIGINT = SCOPE_IDENTITY();

INSERT INTO IncomingPurchaseOrderItems (IncomingPurchaseOrderId, RawMaterialId, OrderedQuantity, ReceivedQuantity)
VALUES (@PoId, @RM001Id, 1000, 0);

SELECT 'PO-DEMO-001' AS PurchaseOrderNumber, @LateDate AS ExpectedDate, DATEDIFF(DAY, @LateDate, @Today) AS DaysLate;
'@
    Write-Host "Inserting the temporary late PO (PO-DEMO-001, 1,000 kg, 4 days late) for RM-001..." -ForegroundColor Cyan
}

$tempFile = [System.IO.Path]::GetTempFileName() + '.sql'
try {
    Set-Content -Path $tempFile -Value $sql -Encoding UTF8
    # -b makes sqlcmd exit non-zero on a T-SQL error, not only on a connection failure.
    # Without it (and without the check below) a failed run still printed the green
    # "Done." message, which is exactly how a silently-empty seed goes unnoticed.
    sqlcmd -S $Server -d $Database -I -b -i $tempFile
    $sqlcmdExit = $LASTEXITCODE
}
finally {
    Remove-Item -Path $tempFile -Force -ErrorAction SilentlyContinue
}

if ($sqlcmdExit -ne 0) {
    throw @"
sqlcmd failed with exit code $sqlcmdExit against '$Server' - nothing was changed.

If this was a connection timeout, LocalDB's '(localdb)\MSSQLLocalDB' alias can go stale
while the SQL Server process is still running. Check the real state with:
    SqlLocalDB.exe info MSSQLLocalDB
and if a LOCALDB#* named pipe exists, connect through it directly:
    .\demo\prep-late-po.ps1 -Server 'np:\\.\pipe\LOCALDB#XXXXXXXX\tsql\query'
"@
}

if ($Cleanup) {
    Write-Host "Cleanup complete. The canonical seed is back to its normal state." -ForegroundColor Green
}
else {
    Write-Host "Done. Reload the Material Shortage screen for RM-001 - PO-DEMO-001 should appear under Late Purchase Orders, excluded from Eligible Incoming." -ForegroundColor Green
    Write-Host "After recording, run: .\demo\prep-late-po.ps1 -Cleanup" -ForegroundColor Yellow
}
