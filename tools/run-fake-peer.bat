@echo off
rem ---------------------------------------------------------------------------
rem  Together 假同伴一键启动（用于没有 Steam 好友时，单独测试"房主"）
rem  A) 假客机（默认）：先在游戏里按 F9 -> 端口 25599 -> 点【作为主机监听】，然后双击本文件。
rem  B) 假主机：先双击本文件并带参数 -AsHost（即 run-fake-peer.bat -AsHost），
rem     然后在游戏 F9 里填 IP 127.0.0.1、端口 25599，点【连接主机】（游戏当客机）。
rem  其他参数：-Port 25599 -Name "假好友" -All（逐条消息）-ShowFrames（帧头十六进制）
rem ---------------------------------------------------------------------------
cd /d "%~dp0"
echo === Together fake peer (no Steam friend needed) ===
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0mp-fake-peer.ps1" %*
echo.
pause
