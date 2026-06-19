# BODA.VMS.Web 로컬 라이브 교체 (관리자 권한 필요)
# 서비스 중지 → publish 산출물을 C:\WINDOWS\system32\publish 로 미러 복사
# (appsettings.Production.json 보존) → 서비스 재시작. 결과는 로그 파일에 기록.
$ErrorActionPreference = "Stop"
$log = "D:\Project\BODA.VMS.Web\deploy-live.log"
$src = "D:\Project\BODA.VMS.Web\publish"
$dst = "C:\WINDOWS\system32\publish"
$svc = "BodaVmsWeb"

function Log($m) { $line = "{0}  {1}" -f (Get-Date -Format "HH:mm:ss"), $m; Add-Content -Path $log -Value $line -Encoding UTF8 }

Set-Content -Path $log -Value "=== deploy-live start ===" -Encoding UTF8
try {
    Log "Stopping service $svc..."
    Stop-Service -Name $svc -Force
    Start-Sleep -Seconds 2
    Log "Service stopped."

    Log "Mirroring $src -> $dst (preserve appsettings.Production.json)..."
    robocopy $src $dst /MIR /NJH /NJS /NDL /NFL /NC /NS /NP /XF "appsettings.Production.json" | Out-Null
    $rc = $LASTEXITCODE
    Log "robocopy exit code = $rc (0-7 = success)"

    Log "Starting service $svc..."
    Start-Service -Name $svc
    Start-Sleep -Seconds 3
    $s = Get-Service -Name $svc
    Log "Service status = $($s.Status)"
    Log "=== deploy-live done ==="
}
catch {
    Log "ERROR: $($_.Exception.Message)"
    Log "=== deploy-live FAILED ==="
}
