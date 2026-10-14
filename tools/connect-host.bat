@echo off
rem Game as CLIENT: write Together cfg (JoinIp/Port), probe the host, then launch the game.
rem Help: connect-host.bat -Help     Example: connect-host.bat 192.168.1.23 25599 -NoLaunch  /  connect-host.bat -Show
cd /d "%~dp0"
echo === Bad North: game as CLIENT, connecting to HOST (Together TCP) ===
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0mp-client-connect.ps1" %*
echo.
pause
