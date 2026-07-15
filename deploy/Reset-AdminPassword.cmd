@echo off
rem admin 비밀번호 리셋 — 더블클릭 실행용 래퍼 (UAC 자동 요청은 ps1 이 처리)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Reset-AdminPassword.ps1"
pause
