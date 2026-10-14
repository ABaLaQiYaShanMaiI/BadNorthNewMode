@echo off
rem Together fake HOST: the script listens, the game joins as CLIENT.
rem Help: run-fake-host.bat -Help     Args: -Port 25599 -Name FakeTestPeer -All -ShowFrames -Once -IdleSeconds 60
cd /d "%~dp0"
echo === Together fake HOST (the game joins as CLIENT) ===
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0mp-fake-peer.ps1" -AsHost %*
echo.
pause
