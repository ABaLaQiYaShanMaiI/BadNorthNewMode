@echo off
rem Together fake peer (no Steam friend needed). Default = game is HOST; -AsHost = game is CLIENT.
rem Help: run-fake-peer.bat -Help     Args: -AsHost -Port 25599 -Name FakeTestPeer -All -ShowFrames -Once -IdleSeconds 60
cd /d "%~dp0"
echo === Together fake peer ===
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0mp-fake-peer.ps1" %*
echo.
pause
