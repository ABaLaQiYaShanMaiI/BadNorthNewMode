#Requires -Version 5.1
<#  Together 假同伴：没有 Steam 好友时，用它和你的游戏握手，验证本模组的"联机让路"。
    两种角色：
      · 默认（假客机）：游戏 F9 → 端口 25599 → 点【作为主机监听】→ 运行本脚本（游戏=主机）。
      · -AsHost（假主机）：先运行本脚本监听端口 → 游戏 F9 → IP 127.0.0.1 → 端口 → 点【连接主机】（游戏=客机）。
    协议：TCP 帧 = 4 字节大端长度 + 载荷；Hello/HelloAck 明文，握手后一切（含 Ping/Pong）包 SessionData；协议号 14。
    输出：默认只报关键事件 + 每 10 秒汇总；-All 逐条消息、-ShowFrames 帧头十六进制；全程写 %TEMP%\mp-fake-peer-<时间戳>.log。
    限制：不跑游戏，对端会停在"等待…"（预期）；不能用来验证真实战斗同步。 #>
param(
    [string]$TargetHost = '127.0.0.1',   # -AsHost 时不使用
    [int]$Port = 25599,
    [string]$Name = 'FakeTestPeer',
    [int]$IdleSeconds = 0,        # 0 = 跑到 Ctrl+C
    [int]$RetryDelaySeconds = 2,  # 断开/连不上时的重试间隔
    [int]$StallSeconds = 30,      # 读到一半卡住这么久才放弃本次连接
    [switch]$AsHost,              # 假主机模式：监听端口，等游戏点【连接主机】
    [switch]$Once,                # 只尝试一次，不自动重连
    [switch]$Quiet,               # 再降一级：连汇总也不打
    [switch]$All,                 # 逐条打印每条消息
    [switch]$ShowFrames,          # 打印帧头十六进制（隐含 -All）
    [string]$LogFile = '',        # 留空 = %TEMP%\mp-fake-peer-<时间戳>.log
    [Alias('h')][switch]$Help     # 打印用法后退出（run-fake-peer.bat -Help）
)
$ErrorActionPreference = 'Stop'

if ($Help) {
    Write-Host 'Together 假同伴工具（没有 Steam 好友时，用 TCP 直连验证联机让路）' -ForegroundColor Yellow
    Write-Host '  默认（假客机）：游戏 F9 → 端口 25599 → 点【作为主机监听】 → 再运行本脚本（游戏 = 主机）'
    Write-Host '  -AsHost（假主机）：先运行本脚本监听 → 游戏 F9 → IP 127.0.0.1 → 点【连接主机】（游戏 = 客机）'
    Write-Host '参数：'
    Write-Host '  -Port 25599        监听 / 连接端口（默认 25599，须与游戏 F9 里填的一致）'
    Write-Host '  -Name "假好友"     握手时上报的名字（默认 FakeTestPeer）'
    Write-Host '  -All               逐条打印每条消息（默认只报关键事件 + 每 10 秒汇总一行）'
    Write-Host '  -ShowFrames        打印帧头十六进制（隐含 -All）'
    Write-Host '  -Quiet             只报关键事件（连汇总也不打）'
    Write-Host '  -Once              只尝试一次连接，然后退出'
    Write-Host '  -IdleSeconds 60    跑满 60 秒自动退出（默认 0 = 一直等，Ctrl+C 结束）'
    Write-Host '  -LogFile <路径>    日志文件（默认 %TEMP%\mp-fake-peer-<时间戳>.log）'
    Write-Host '双击即用：run-fake-peer.bat（假同伴） / run-fake-host.bat（假主机）'
    exit 0
}

if ([string]::IsNullOrEmpty($LogFile)) {
    $LogFile = Join-Path $env:TEMP ('mp-fake-peer-{0:yyyyMMdd_HHmmss}.log' -f (Get-Date))
}
try { Start-Transcript -Path $LogFile -Force | Out-Null } catch { }

$script:Proto = 14
$script:All = [bool]$All -or [bool]$ShowFrames
$script:Quiet = [bool]$Quiet
$script:Stall = $StallSeconds
$script:Attempt = 0
$script:Own = [Guid]::NewGuid()
$script:Peer = [Guid]::Empty
$script:Handshook = $false
$script:In = 0; $script:Out = 0; $script:Bytes = 0
$script:Hinted = $false; $script:LastNotice = Get-Date
$script:IsHost = [bool]$AsHost
$script:PingSeq = 0
$script:Deadline = if ($IdleSeconds -gt 0) { (Get-Date).AddSeconds($IdleSeconds) } else { [DateTime]::MaxValue }

Write-Host "[peer] 日志：$LogFile" -ForegroundColor DarkGray
if ($AsHost) { Write-Host "[peer] 角色：假主机（监听 $Port，等游戏点【连接主机】）  名字 '$Name'" -ForegroundColor Yellow }
else { Write-Host "[peer] 角色：假客机（连 $TargetHost`:$Port）  名字 '$Name'（Ctrl+C 退出；未连上会每 $RetryDelaySeconds 秒重试）" -ForegroundColor Yellow }

function Get-MsgName([byte]$t) {
    switch ($t) {
        1 { 'Hello' } 2 { 'Ping' } 3 { 'Pong' } 4 { 'HelloAck' } 5 { 'SessionData' }
        10 { 'CampaignStart' } 11 { 'CampaignState' } 20 { 'SquadMove' } 21 { 'DeployReady' }
        22 { 'DeployCommit' } 23 { 'DeployFinished' } 24 { 'FirstWave' } 25 { 'PauseState' }
        26 { 'SlowIntent' } 27 { 'SlowState' } 28 { 'WaveBegin' } 30 { 'Snapshot' }
        31 { 'AgentRegistry' } 32 { 'AgentDeath' } 33 { 'BattleResult' } 40 { 'BattleBaseline' }
        41 { 'BattleReady' } 42 { 'ActorSpawn' } 43 { 'ActorDespawn' } 44 { 'ActorPose' }
        45 { 'VisualResources' } 50 { 'WorldSnapshot' } 51 { 'WorldEvent' } 52 { 'AbilityCommand' }
        53 { 'AbilityState' } 54 { 'CombatEvent' } 55 { 'BattleClock' } 56 { 'RaidManifest' }
        57 { 'ShipLifecycle' } 58 { 'ActorCensus' } 59 { 'SurfaceMark' } 60 { 'SceneDecoration' }
        61 { 'FireVisual' } 62 { 'CampaignControl' } 63 { 'CampaignReady' } 64 { 'AbilityVisual' }
        65 { 'OptionalVisualStop' } 66 { 'DeployRoster' } 67 { 'ActorPoses' } 68 { 'ProjectilePoses' }
        default { "未知($t)" }
    }
}

function Write-Frame($stream, [byte[]]$payload) {
    $n = $payload.Length
    $h = [byte[]]@([byte](($n -shr 24) -band 255), [byte](($n -shr 16) -band 255), [byte](($n -shr 8) -band 255), [byte]($n -band 255))
    $stream.Write($h, 0, 4); $stream.Write($payload, 0, $n); $stream.Flush()
    $script:Out++
}

function New-Bytes([scriptblock]$fill) {
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms, [System.Text.Encoding]::UTF8)
    try { & $fill $bw; $bw.Flush(); return ,$ms.ToArray() }   # ,$a：防 byte[] 被枚举成 Object[]（会命中 Write(bool) 只写 1 字节）
    finally { $bw.Dispose(); $ms.Dispose() }
}

# 读到正好 n 字节；$idleOk 时"暂时无数据"返回 $null；已开始收就不再因读超时中断，只按"零进展 $stall 秒"放弃
function Read-Exact($stream, [int]$n, [bool]$idleOk, [int]$stall) {
    $buf = New-Object byte[] $n
    $got = 0; $last = Get-Date
    while ($got -lt $n) {
        try { $r = $stream.Read($buf, $got, $n - $got) }
        catch [System.IO.IOException] {
            if ($got -eq 0 -and $idleOk) { return $null }
            if (((Get-Date) - $last).TotalSeconds -ge $stall) { throw ("接收卡住：{0} 秒无新数据" -f $stall) }
            continue
        }
        if ($r -le 0) { throw '对端已断开（游戏侧会话结束）' }
        $got += $r; $last = Get-Date
    }
    return $buf
}

function Read-Frame($stream) {
    $h = Read-Exact $stream 4 $true $script:Stall
    if ($null -eq $h) { return $null }
    $len = ([int]$h[0] -shl 24) -bor ([int]$h[1] -shl 16) -bor ([int]$h[2] -shl 8) -bor [int]$h[3]   # 先 [int]：byte 左移会被截回 byte
    if ($ShowFrames) { Write-Host ("[peer] 帧头 {0:X2} {1:X2} {2:X2} {3:X2} → {4} 字节" -f $h[0], $h[1], $h[2], $h[3], $len) -ForegroundColor Magenta }
    if ($len -le 0 -or $len -gt 1048576) { throw "帧长度非法: $len" }
    $script:Bytes += $len
    return (Read-Exact $stream $len $false $script:Stall)
}

function Wrap-Session([byte[]]$inner) {
    return New-Bytes { param($bw) $bw.Write([byte]5); $bw.Write($script:Own.ToByteArray()); $bw.Write($script:Peer.ToByteArray()); $bw.Write($inner) }
}
function Get-PingSeq($inner) { return [BitConverter]::ToInt32([byte[]]$inner, 1) }
function Send-Pong([int]$seq) {
    Write-Frame $script:Stream (Wrap-Session (New-Bytes { param($bw) $bw.Write([byte]3); $bw.Write($seq) }))
}
function Send-Hello() {
    Write-Frame $script:Stream (New-Bytes { param($bw)
        $bw.Write([byte]1); $bw.Write([int]$script:Proto); $bw.Write($script:Own.ToByteArray()); $bw.Write([bool]$script:IsHost); $bw.Write($Name) })
}
function Send-Ping() {
    $script:PingSeq++
    Write-Frame $script:Stream (Wrap-Session (New-Bytes { param($bw) $bw.Write([byte]2); $bw.Write([int]$script:PingSeq) }))
}
function Send-HelloAck() {
    Write-Frame $script:Stream (New-Bytes { param($bw)
        $bw.Write([byte]4); $bw.Write([int]$script:Proto); $bw.Write($script:Own.ToByteArray()); $bw.Write($script:Peer.ToByteArray()) })
}
function Reset-State {
    $script:Own = [Guid]::NewGuid(); $script:Peer = [Guid]::Empty; $script:PeerName = ''
    $script:Handshook = $false; $script:Stat = @{}; $script:LastReport = Get-Date
}

# 默认档的周期输出：每 10 秒（或强制）汇总一行"收了多少条 / 哪些消息类型"
function Write-Summary([bool]$force) {
    if ($script:Quiet) { return }
    if ($script:Stat.Count -eq 0) { $script:LastReport = Get-Date; return }
    if (-not $force -and ((Get-Date) - $script:LastReport).TotalSeconds -lt 10) { return }
    $script:LastReport = Get-Date
    $parts = @()
    foreach ($k in ($script:Stat.Keys | Sort-Object)) { $parts += ('{0}x{1}' -f $k, $script:Stat[$k]) }
    Write-Host ("[peer] 收 {0} 条 / {1} KB ：{2}" -f $script:In, [int]($script:Bytes / 1024), ($parts -join ', ')) -ForegroundColor DarkGray
    $script:Stat = @{}
}

function Invoke-Session($client) {
    $script:Stream = $client.GetStream()
    $script:Stream.ReadTimeout = 500               # 只用于定期回头打印，不因它中断
    $script:Stream.WriteTimeout = 10000
    $script:NextPing = (Get-Date).AddSeconds(2.5)
    $at = Get-Date
    Send-Hello
    try {
        while ((Get-Date) -lt $script:Deadline) {
            $payload = Read-Frame $script:Stream
            if ($null -eq $payload) {
                if (-not $script:Handshook -and ((Get-Date) - $at).TotalSeconds -ge 15) {
                    Write-Host "[peer] 连上但 15 秒没收到 Hello → 对端会话多半已结束（握手窗口只有 15 秒）" -ForegroundColor Red
                    break
                }
                if ($script:IsHost -and (Get-Date) -ge $script:NextPing) {     # 假主机要定期 Ping，否则游戏侧 30 秒无消息会断
                    $script:NextPing = (Get-Date).AddSeconds(2.5)
                    Send-Ping
                }
                Write-Summary $false
                continue
            }

            $script:In++
            $t = $payload[0]
            $ms = New-Object System.IO.MemoryStream(, $payload)
            $br = New-Object System.IO.BinaryReader($ms, [System.Text.Encoding]::UTF8)
            try {
                switch ($t) {
                    1 {
                        [void]$br.ReadByte(); $v = $br.ReadInt32(); $pnonce = New-Object Guid(, $br.ReadBytes(16))
                        $ih = $br.ReadBoolean(); $pname = $br.ReadString()
                        $script:Peer = $pnonce; $script:PeerName = $pname
                        if ($v -ne $script:Proto) { Write-Host "[peer] !! 协议号不一致（本地 $($script:Proto)，对端 $v）→ 双方 Together 版本不同" -ForegroundColor Red }
                        Send-HelloAck
                    }
                    4 {
                        [void]$br.ReadByte(); $v = $br.ReadInt32()
                        $a = New-Object Guid(, $br.ReadBytes(16)); $b = New-Object Guid(, $br.ReadBytes(16))
                        # 对端视角：HelloAck = [对端 nonce][自己 nonce]
                        $script:Handshook = ($v -eq $script:Proto) -and ($a -eq $script:Peer) -and ($b -eq $script:Own)
                        if ($script:Handshook) { Write-Host "[peer] ✅ 握手完成（对端 '$($script:PeerName)'）→ 游戏侧已进入联机态；按 F1 应被拒绝" -ForegroundColor Green }
                        else { Write-Host "[peer] !! HelloAck 校验失败（ver=$v）" -ForegroundColor Red }
                    }
                    5 {
                        if ($payload.Length -lt 34) { break }
                        [void]$br.ReadByte()
                        $g1 = New-Object Guid(, $br.ReadBytes(16)); $g2 = New-Object Guid(, $br.ReadBytes(16))
                        if ($g1 -ne $script:Peer -or $g2 -ne $script:Own) { break }      # 标识不匹配：丢弃
                        $inner = $br.ReadBytes([int]($ms.Length - $ms.Position))
                        $itype = $inner[0]
                        $nm = Get-MsgName $itype
                        $script:Stat[$nm] = 1 + [int]$script:Stat[$nm]
                        if ($itype -eq 2) { Send-Pong (Get-PingSeq $inner) }             # Ping → Pong（保活）
                        if ($script:All) {
                            $col = 'Yellow'; if ($itype -eq 2) { $col = 'DarkGray' }
                            Write-Host ("[peer] ← {0}（{1} 字节）" -f $nm, ($payload.Length - 33)) -ForegroundColor $col
                        }
                    }
                    default { if ($script:All) { Write-Host "[peer] ← 非会话消息 $(Get-MsgName $t)" -ForegroundColor DarkYellow } }
                }
            }
            finally { $br.Dispose(); $ms.Dispose() }
        }
    }
    catch {
        $raw = $_.Exception.Message
        if ($_.Exception.InnerException -ne $null) { $raw = $_.Exception.InnerException.Message }
        if ($raw -match 'Write|写入') { $raw = '写失败：对端已断开' }        # 断开时 PowerShell 会把整句异常拼出来，这里归一化
        elseif ($raw.Length -gt 60) { $raw = $raw.Substring(0, 60) }
        Write-Host ("[peer] 本次连接结束：{0}" -f $raw) -ForegroundColor DarkRed
    }
    finally { try { $client.Close() } catch { } }

    Write-Summary $true
    if ($script:Handshook) { Write-Host "[peer] 本次已握手（游戏侧曾进入联机态）" -ForegroundColor Green }
    else { Write-Host "[peer] 本次未完成握手" -ForegroundColor Yellow }
    Write-Host ("[peer] 本次：收 {0} 条 / {1} KB，发 {2} 条" -f $script:In, [int]($script:Bytes / 1024), $script:Out) -ForegroundColor DarkGray
}

# 假客机：主动连游戏（游戏 = 主机）
function Invoke-Attempt {
    $script:Attempt++; $script:IsHost = $false; Reset-State
    if ($script:All) { Write-Host "[peer] ===== 第 $script:Attempt 次尝试：连接 $TargetHost`:$Port =====" -ForegroundColor Yellow }
    $client = New-Object System.Net.Sockets.TcpClient
    $client.NoDelay = $true
    try { $client.Connect($TargetHost, $Port) }
    catch {
        if (-not $script:Hinted) {          # 只提示一次，之后静默重试（否则每 2 秒刷两行）
            $script:Hinted = $true
            $raw = $_.Exception.Message
            if ($_.Exception.InnerException -ne $null) { $raw = $_.Exception.InnerException.Message }
            if ($raw.Length -gt 70) { $raw = $raw.Substring(0, 70) }
            Write-Host "[peer] 连接失败：$raw" -ForegroundColor Red
            Write-Host "[peer]  → 先确认游戏 F9 点过【作为主机监听】、端口一致；本脚本会自动重试" -ForegroundColor Yellow
        }
        try { $client.Close() } catch { }
        return
    }
    $script:Hinted = $false
    Invoke-Session $client
}

# 假主机：监听端口，等游戏点【连接主机】（游戏 = 客机）
function Invoke-HostAttempt {
    $script:Attempt++; $script:IsHost = $true; Reset-State
    $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Any, $Port)
    $listener.Start()
    Write-Host "[peer] 假主机已监听 0.0.0.0`:$Port → 请在游戏 F9 里点【连接主机】（IP 127.0.0.1，端口 $Port）" -ForegroundColor Green
    while ((Get-Date) -lt $script:Deadline) {
        if ($listener.Pending()) {
            $client = $listener.AcceptTcpClient()
            $listener.Stop()
            $client.NoDelay = $true
            Write-Host "[peer] 游戏已连入，开始握手" -ForegroundColor Cyan
            Invoke-Session $client
            return
        }
        Start-Sleep -Milliseconds 200
    }
    $listener.Stop()
}


try {
    while ($true) {
        if ($AsHost) { Invoke-HostAttempt } else { Invoke-Attempt }
        if ($Once -or (Get-Date) -ge $script:Deadline) { break }
        if (((Get-Date) - $script:LastNotice).TotalSeconds -ge 30) {      # 重试期间每 30 秒才提醒一次
            $script:LastNotice = Get-Date
            if ($AsHost) { Write-Host "[peer] 仍在等待游戏连入（要立刻试：游戏里点【连接主机】，IP 127.0.0.1 / 端口 $Port）" -ForegroundColor DarkGray }
            else { Write-Host "[peer] 仍在重试（要立刻再连：游戏里点一次【作为主机监听】）" -ForegroundColor DarkGray }
        }
        Start-Sleep -Seconds $RetryDelaySeconds
    }
}
finally {
    Write-Host "[peer] 退出（共尝试 $script:Attempt 次）。日志：$LogFile" -ForegroundColor Yellow
    try { Stop-Transcript | Out-Null } catch { }
}


