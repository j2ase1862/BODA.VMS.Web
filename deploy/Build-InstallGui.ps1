<#
.SYNOPSIS
  Install-Web-Gui.cs → Install-Web-Gui.exe 빌드 (오프라인 설치 GUI 런처).

.DESCRIPTION
  Windows 내장 .NET Framework 컴파일러(csc.exe)를 사용 — .NET SDK 불필요.
  산출 exe 는 .NET Framework 4.x 대상이라 Windows 10/11 현장 PC 에서
  런타임 설치 없이 바로 실행된다.

  USB 에는 다음 3개를 담으면 됨:
    deploy\Install-Web-Gui.exe      ← 운영자가 더블클릭
    deploy\Install-Web-Offline.ps1
    publish\                        ← dotnet publish 산출물
#>

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$csc = Join-Path $env:windir "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    # 32bit fallback
    $csc = Join-Path $env:windir "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    Write-Error ".NET Framework csc.exe 를 찾지 못했습니다."
    exit 1
}

$src = Join-Path $ScriptDir "Install-Web-Gui.cs"
$out = Join-Path $ScriptDir "Install-Web-Gui.exe"

& $csc /nologo /target:winexe /out:"$out" `
    /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
    "$src"

if ($LASTEXITCODE -ne 0) {
    Write-Error "컴파일 실패 (exit $LASTEXITCODE)"
    exit 1
}

$size = [math]::Round((Get-Item $out).Length / 1KB, 1)
Write-Host "빌드 완료: $out ($size KB)" -ForegroundColor Green
