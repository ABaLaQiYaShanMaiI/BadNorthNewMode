#Requires -Version 5.1
<#
.SYNOPSIS
    让本机《Bad North》作为【客机】直连【主机】（Together 的 TCP 直连）：
    写 JoinIp/Port → 探活 → 启动游戏 → 给出 F9 步骤。
.DESCRIPTION
    1. 解析游戏目录（-BadNorthDir > 环境变量 > csproj 默认值 > D:\Steam\...）；
    2. 定位 BepInEx\config\com.linfeng.together.cfg（Together 的配置）；
    3. 备份 cfg（.bak_<时间戳>），只改写 [TCP] 段的 JoinIp / Port（可选 [Player] Name），
       其余内容（注释、行尾、编码）原样保留 —— UTF-8 无 BOM，不引入 BOM；
    4. TCP 探活主机端口（2 秒超时）→ 报告"可达 / 不可达"（不可达只警告，不阻断）；
    5. 默认按 Steam AppID 启动游戏（可 -NoLaunch）；游戏已在运行 → 提示需重启才生效并跳过启动；
    6. 打印客机操作步骤（F9 → IP/端口 → 连接主机）与主机侧前置条件。
    前置（主机侧）：进游戏 F9 → 端口一致 → 点【作为主机监听】，并放行防火墙。
    本脚本只动 Together 的 cfg 与启动游戏，不碰 winhttp/doorstop/游戏本体。
.PARAMETER HostIp
    主机 IP（局域网如 192.168.x.x；同机双开自测用 127.0.0.1）。省略则交互输入（回车 = 沿用 cfg 当前值）。
.PARAMETER Port
    TCP 端口；省略或 0 = 沿用 cfg 当前值（没有则 25599）。
.PARAMETER PlayerName
    可选：写 [Player] Name（留空字符串 = 使用 Steam 昵称）。不传该参数则不改这一行。
.PARAMETER BadNorthDir
    游戏根目录；默认 $env:BadNorthDir → 仓库 csproj 的 <BadNorthDir> → D:\Steam\steamapps\common\BadNorth。
.PARAMETER Show
    只显示当前配置（游戏目录 / JoinIp / Port / Name / 游戏是否在运行），不做任何修改、不启动游戏。
.PARAMETER NoLaunch
    只写配置，不启动游戏。
.PARAMETER DryRun
    演练：打印将要写入的值（含新旧对比），不落盘、不启动游戏。
.EXAMPLE
    .\tools\connect-host.bat                              # 交互输入主机 IP，写配置并启动游戏
    .\tools\connect-host.bat 192.168.1.23                # 指定主机 IP
    .\tools\connect-host.bat 192.168.1.23 25599 -NoLaunch
    .\tools\connect-host.bat -Show                        # 只看当前配置
#>
param(
    [Parameter(Position = 0)][string]$HostIp = '',
    [Parameter(Position = 1)][int]$Port = 0,
    [string]$PlayerName = '',
    [string]$BadNorthDir = $env:BadNorthDir,
    [switch]$Show,
    [switch]$NoLaunch,
    [switch]$DryRun,
    [switch]$Help      # 打印用法后退出（connect-host.bat -Help）
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if ($Help) {
    Write-Host '让本机《Bad North》作为【客机】直连【主机】（Together 的 TCP 直连）' -ForegroundColor Yellow
    Write-Host '用法：connect-host.bat [主机IP] [端口] [参数]'
    Write-Host '  1) 主机侧先进游戏 → F9 → 端口一致 → 点【作为主机监听】（并放行防火墙）'
    Write-Host '  2) 双击 connect-host.bat → 输入主机 IP（回车 = 沿用 cfg 当前值）'
    Write-Host '  3) 工具会写 cfg（JoinIp / Port）→ 探活主机 → 启动游戏'
    Write-Host '  4) 进游戏按 F9 → 点【连接主机】'
    Write-Host '参数：'
    Write-Host '  -HostIp / 第 1 个参数   主机 IP（同机自测用 127.0.0.1）'
    Write-Host '  -Port   / 第 2 个参数   端口（默认沿用 cfg，没有则 25599）'
    Write-Host '  -PlayerName "名字"      写 [Player] Name（留空 = Steam 昵称）'
    Write-Host '  -BadNorthDir <目录>     手动指定游戏根目录'
    Write-Host '  -Show                   只显示当前配置（不改、不启动）'
    Write-Host '  -NoLaunch               只写配置，不启动游戏'
    Write-Host '  -DryRun                 演练：只打印将要写入的值'
    Write-Host '示例：connect-host.bat 192.168.1.23 25599 -NoLaunch   /   connect-host.bat -Show'
    exit 0
}

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$cfgRel = 'BepInEx\config\com.linfeng.together.cfg'
$appIdHint = 688420                     # Steam AppID（Bad North）；下面会优先从 appmanifest 读真实值

function Say([string]$msg, [string]$color = 'Gray') { Write-Host "[client] $msg" -ForegroundColor $color }

# ---------- 1. 解析游戏目录 ----------
if (-not $BadNorthDir) {
    $proj = Join-Path $root 'BadNorthNewMode.csproj'
    if (Test-Path $proj) {
        try {
            $xml = [xml](Get-Content $proj -Raw)
            $node = $xml.SelectSingleNode('//BadNorthDir')
            if ($node -and $node.'#text') { $BadNorthDir = $node.'#text'.Trim() }
        } catch { }
    }
}
if (-not $BadNorthDir) { $BadNorthDir = 'D:\Steam\steamapps\common\BadNorth' }
if (-not (Test-Path $BadNorthDir)) {
    Write-Host "[client] 游戏目录不存在: $BadNorthDir" -ForegroundColor Red
    Write-Host "[client]  请用 -BadNorthDir <游戏根目录> 或设置环境变量 BadNorthDir。" -ForegroundColor Yellow
    exit 2
}

$cfgPath = Join-Path $BadNorthDir $cfgRel
if (-not (Test-Path $cfgPath)) {
    Write-Host "[client] 找不到 Together 配置: $cfgPath" -ForegroundColor Red
    Write-Host "[client]  → 先启动一次游戏、按 F9 打开联机窗口（Together 会生成该 cfg），再运行本工具。" -ForegroundColor Yellow
    exit 2
}
Say "游戏目录 = $BadNorthDir"
Say "联机配置 = $cfgPath"

# ---------- 2. 读 cfg（UTF-8 无 BOM + CRLF；行尾不一致时按主行尾重写并在写入前提示） ----------
$raw = [System.IO.File]::ReadAllText($cfgPath)
$nl = if ($raw.Contains("`r`n")) { "`r`n" } else { "`n" }
$lines = [System.Collections.Generic.List[string]]::new()
foreach ($l in ($raw -split "`r`n|`n", -1)) { [void]$lines.Add($l) }

$state = ''
$cur = @{ JoinIp = ''; Port = ''; Name = '' }
$idx = @{ JoinIp = -1; Port = -1; Name = -1 }
for ($i = 0; $i -lt $lines.Count; $i++) {
    $t = $lines[$i].Trim()
    if ($t.StartsWith('[') -and $t.EndsWith(']')) { $state = $t.Substring(1, $t.Length - 2).Trim(); continue }
    $m = [regex]::Match($lines[$i], '^([ \t]*)([A-Za-z_][A-Za-z0-9_]*)([ \t]*=[ \t]*)(.*)$')
    if (-not $m.Success) { continue }
    $k = $m.Groups[2].Value
    if ($state -eq 'TCP' -and $k -eq 'JoinIp') { $cur.JoinIp = $m.Groups[4].Value.Trim(); $idx.JoinIp = $i }
    elseif ($state -eq 'TCP' -and $k -eq 'Port') { $cur.Port = $m.Groups[4].Value.Trim(); $idx.Port = $i }
    elseif ($state -eq 'Player' -and $k -eq 'Name') { $cur.Name = $m.Groups[4].Value.Trim(); $idx.Name = $i }
}

$proc = Get-Process -Name 'BadNorth' -ErrorAction SilentlyContinue
$running = $proc -ne $null

# ---------- 3. -Show：只读展示 ----------
if ($Show) {
    Say ''
    Say '当前配置（Together 直连）' 'Cyan'
    Say ("  JoinIp    = {0}" -f $(if ($cur.JoinIp) { $cur.JoinIp } else { '(缺)' }))
    Say ("  Port      = {0}" -f $(if ($cur.Port) { $cur.Port } else { '(缺)' }))
    Say ("  Name      = {0}" -f $(if ($cur.Name) { $cur.Name } else { '(空 = Steam 昵称)' }))
    Say ''
    Say ("游戏进程  = {0}" -f $(if ($running) { "运行中（PID $($proc[0].Id)）—— 改配置需重启游戏才生效" } else { '未运行' }))
    Say ("配置文件  = {0}" -f $cfgPath)
    exit 0
}
# ---------- 4. 决定要写入的值 ----------
$newIp = $HostIp.Trim()
if (-not $newIp) {
    $dft = $cur.JoinIp
    if (-not $dft) { $dft = '127.0.0.1' }
    $newIp = (Read-Host "主机 IP（局域网如 192.168.1.23；同机自测 127.0.0.1）[$dft]").Trim()
    if (-not $newIp) { $newIp = $dft }
}
if ($newIp -match '\s') { Write-Host "[client] 主机 IP 不能含空格：'$newIp'" -ForegroundColor Red; exit 2 }

$curPort = 0
if ($cur.Port) { [void][int]::TryParse($cur.Port, [ref]$curPort) }
if ($Port -gt 0) { $newPort = $Port } elseif ($curPort -gt 0) { $newPort = $curPort } else { $newPort = 25599 }
if ($newPort -lt 1 -or $newPort -gt 65535) { Write-Host "[client] 端口超出范围（1~65535）：$newPort" -ForegroundColor Red; exit 2 }

$setName = $PSBoundParameters.ContainsKey('PlayerName')
$newName = $PlayerName.Trim()

# ---------- 5. 生成新内容（只动 [TCP] 的 JoinIp/Port；缺行则在段首补齐；其余原样保留） ----------
function Get-SectionHeaderIndex([System.Collections.Generic.List[string]]$l, [string]$name) {
    for ($i = 0; $i -lt $l.Count; $i++) {
        if ($l[$i].Trim() -eq "[$name]") { return $i }
    }
    return -1
}

$changed = [System.Collections.Generic.List[string]]::new()

# JoinIp
if ($idx.JoinIp -ge 0) {
    if ($cur.JoinIp -ne $newIp) {
        $m = [regex]::Match($lines[$idx.JoinIp], '^([ \t]*)([A-Za-z_][A-Za-z0-9_]*)([ \t]*=[ \t]*)(.*)$')
        $lines[$idx.JoinIp] = $m.Groups[1].Value + 'JoinIp' + $m.Groups[3].Value + $newIp
        [void]$changed.Add("JoinIp: $($cur.JoinIp) → $newIp")
    }
} else {
    $h = Get-SectionHeaderIndex $lines 'TCP'
    if ($h -lt 0) { Write-Host "[client] cfg 缺少 [TCP] 段（先启动一次游戏按 F9，让 Together 生成完整配置）" -ForegroundColor Red; exit 2 }
    $lines.Insert($h + 1, "JoinIp = $newIp")
    $idx.JoinIp = $h + 1
    [void]$changed.Add("JoinIp: (缺) → $newIp（已补进 [TCP]）")
}

# Port
$portText = $newPort.ToString([System.Globalization.CultureInfo]::InvariantCulture)
if ($idx.Port -ge 0) {
    if ($cur.Port -ne $portText) {
        $m = [regex]::Match($lines[$idx.Port], '^([ \t]*)([A-Za-z_][A-Za-z0-9_]*)([ \t]*=[ \t]*)(.*)$')
        $lines[$idx.Port] = $m.Groups[1].Value + 'Port' + $m.Groups[3].Value + $portText
        [void]$changed.Add("Port: $($cur.Port) → $portText")
    }
} else {
    $h = Get-SectionHeaderIndex $lines 'TCP'
    if ($h -lt 0) { Write-Host "[client] cfg 缺少 [TCP] 段" -ForegroundColor Red; exit 2 }
    $lines.Insert($h + 1, "Port = $portText")
    $idx.Port = $h + 1
    [void]$changed.Add("Port: (缺) → $portText（已补进 [TCP]）")
}

# Name（仅当显式传了 -PlayerName）
if ($setName) {
    if ($idx.Name -ge 0) {
        if ($cur.Name -ne $newName) {
            $m = [regex]::Match($lines[$idx.Name], '^([ \t]*)([A-Za-z_][A-Za-z0-9_]*)([ \t]*=[ \t]*)(.*)$')
            $lines[$idx.Name] = $m.Groups[1].Value + 'Name' + $m.Groups[3].Value + $newName
            [void]$changed.Add("Name: $($cur.Name) → $newName")
        }
    } else {
        $h = Get-SectionHeaderIndex $lines 'Player'
        if ($h -ge 0) {
            $lines.Insert($h + 1, "Name = $newName")
            [void]$changed.Add("Name: (缺) → $newName（已补进 [Player]）")
        } else {
            Say '[Player] 段不存在 → 跳过 -PlayerName' 'Yellow'
        }
    }
}

$newText = $lines -join $nl

# ---------- 6. 计划输出 / 演练 ----------
Say ''
Say '连接计划' 'Cyan'
Say ("  客机（本机） : JoinIp = {0} , Port = {1}" -f $newIp, $newPort)
if ($changed.Count -gt 0) { foreach ($c in $changed) { Say ("  配置改动     : {0}" -f $c) } }
else { Say '  配置改动     : 无（当前值已相同，不写文件）' }

if ($DryRun) {
    Say ''
    Say '[演练] 未写入任何文件、未启动游戏。-DryRun 检查通过。' 'Green'
    exit 0
}

# ---------- 7. 备份 + 写入（UTF-8 无 BOM；行尾与原文一致） ----------
if ($changed.Count -gt 0) {
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $bak = "$cfgPath.bak_$stamp"
    Copy-Item $cfgPath $bak -Force
    Say "已备份旧配置: $bak"

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($cfgPath, $newText, $utf8NoBom)

    # 回读校验：目标值必须真的落盘；写坏/引入 BOM 就立刻从备份还原
    # 注意：cfg 是 CRLF，(?m)$ 只匹配 \n 之前 → 必须允许行尾的 \r，否则会误判"校验失败"
    $verify = [System.IO.File]::ReadAllText($cfgPath)
    $okIp = [regex]::IsMatch($verify, '(?m)^[ \t]*JoinIp[ \t]*=[ \t]*' + [regex]::Escape($newIp) + '[ \t]*\r?$')
    $okPort = [regex]::IsMatch($verify, '(?m)^[ \t]*Port[ \t]*=[ \t]*' + $portText + '[ \t]*\r?$')
    if (-not $okIp -or -not $okPort) {
        Copy-Item $bak $cfgPath -Force
        Write-Host '[client] 写入校验失败 → 已从备份还原，未改动配置。' -ForegroundColor Red
        exit 3
    }
    if (([System.IO.File]::ReadAllBytes($cfgPath))[0] -eq 0xEF) {
        Copy-Item $bak $cfgPath -Force
        Write-Host '[client] 写入引入了 BOM → 已从备份还原' -ForegroundColor Red
        exit 3
    }
    Say '配置文件已更新（UTF-8 无 BOM；注释与行尾原样保留）' 'Green'
} else {
    Say '配置文件无需修改' 'Green'
}

# ---------- 8. 探活主机端口（不可达只警告、不阻断：主机可能还没点"作为主机监听"） ----------
function Test-TcpPort([string]$target, [int]$port, [int]$timeoutMs) {
    $c = New-Object System.Net.Sockets.TcpClient
    try {
        $c.NoDelay = $true
        $ar = $c.BeginConnect($target, $port, $null, $null)
        if (-not $ar.AsyncWaitHandle.WaitOne($timeoutMs)) { return $false }
        $c.EndConnect($ar)
        return $true
    } catch { return $false }
    finally { try { $c.Close() } catch { } }
}

Say ''
Say "探活 $newIp`:$newPort ..." 'Cyan'
if (Test-TcpPort $newIp $newPort 2000) {
    Say '端口可达：主机已在监听 —— 进游戏点【连接主机】即可' 'Green'
} else {
    Say '端口不可达（2 秒超时）：按下面顺序排查（别急着断定失败）' 'Yellow'
    Say '  1) 主机侧必须先：进游戏 → F9 → 端口填同一个 → 点【作为主机监听】（TCP 上限 1 个客机）'
    Say '  2) 主机侧防火墙放行 BadNorth.exe / 该 TCP 端口；同机自测请填 127.0.0.1'
    Say '  3) IP 是否同网段（看主机 ipconfig 的 IPv4；不要用 169.254.*）'
    Say '  4) TCP 直连的超时是 10 秒：主机刚点完监听可稍等 1~2 秒再去点【连接主机】'
}

# ---------- 9. 启动游戏（Steam；游戏已在运行则跳过并提醒重启） ----------
if ($running) {
    Say ''
    Say "游戏正在运行（PID $($proc[0].Id)）：JoinIp/Port 在游戏启动时读取，需【重启】后才生效。" 'Yellow'
    Say '  → 现在可直接在游戏里 F9 手填 IP/端口，或重启游戏后再运行本工具。' 'Yellow'
} elseif ($NoLaunch) {
    Say ''
    Say '按 -NoLaunch：未启动游戏。进游戏后按 F9 → 点【连接主机】。' 'Yellow'
} else {
    $appId = 0
    $steamApps = Split-Path -Parent $BadNorthDir
    $leaf = Split-Path -Leaf $BadNorthDir
    try {
        foreach ($acf in (Get-ChildItem $steamApps -Filter 'appmanifest_*.acf' -ErrorAction SilentlyContinue)) {
            $c = Get-Content $acf.FullName -Raw
            if ($c -match ('"installdir"\s+"' + [regex]::Escape($leaf) + '"')) {
                $m = [regex]::Match($c, '"appid"\s+"(\d+)"')
                if ($m.Success) { $appId = [int]$m.Groups[1].Value }
                break
            }
        }
    } catch { }
    if ($appId -le 0) { $appId = $appIdHint }

    Say ''
    if ($appId -gt 0) {
        Say "启动游戏：steam://rungameid/$appId" 'Green'
        Start-Process "steam://rungameid/$appId"
    } else {
        $exe = Join-Path $BadNorthDir 'BadNorth.exe'
        Say "未取到 AppID → 直接启动 $exe（需 Steam 在运行）" 'Yellow'
        Start-Process $exe
    }
}

# ---------- 10. 客机操作步骤 ----------
Say ''
Say '客机操作（本机）' 'Cyan'
Say '  1) 进游戏（主菜单或战局都行）按 F9 打开联机窗口'
Say "  2) 端口填 $newPort 、IP 填 $newIp（cfg 已写好，界面里就是这两个值）"
Say '  3) 点【连接主机】；状态面板出现方式/状态/延迟即已握手'
Say '  4) 客机禁改战役（选岛/存档/读档/升级被 Together 挡住）—— 等主机选岛'
Say ''
Say '主机侧前置：F9 → 同端口 → 点【作为主机监听】；只支持 2 人 / 1 个客机。'
Say '版本要求：双方 Together 版本与协议号（14）一致，否则报"协议不兼容"。'
exit 0

