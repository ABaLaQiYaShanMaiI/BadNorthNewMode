# 静态规则闸门（v1.7.3 / P3）：**不需要游戏 DLL**，可在 CI 跑。
# 把"踩过的坑"固化成机器检查（每条规则都对应一次真实事故或一条明确的架构约束）：
#   ① 空 `catch { }`            → 必须有 `// silent-ok` 说明"这是有意的静默降级"
#   ② 反射成员 `== null` 判空   → 游戏 mscorlib 2.0 没有 `MemberInfo.op_Equality` → 运行期 MissingMethodException（B 系列事故）
#      （作用域精确：只认"类级反射字段"与"同一方法内声明的反射局部"，避免把同名其它变量误判）
#   ③ `Debug.Log(`              → 游戏内统一走 `Util.Log/Warn/LogOnce`（便于按 cfg 开关）
#   ④ 硬编码盘符路径            → 一律从 `BadNorthDir` / cfg 来
#   ⑤ 热路径（Update/OnGUI/Draw*/Tick*）方法体里 new List/Dictionary/HashSet → 每帧分配；确有必要时写 `// alloc-ok`
#   ⑥ 纵向布局链丢返回值（`DrawXxx(x, inner, y);` 前必须写 `y =`）→ 本行与下一行同 y，后画的盖住先画的
#      （v1.7.4 实机事故：靠岸速度 4 格被操作按钮行整片盖住、点那里会误触发清场/强制胜利）
#   ⑦ 禁用清单（`tools\banned-api.txt`）：逐行扫全部 .cs，命中即拦（该行写 `// ok:` 可豁免）
#      —— 目前含 `IsPointerOverGameObject`（本游戏恒为真，会把我们的世界点击静默吞掉；v1.0.4 / v1.7.4 各栽一次）
#      与 `Array.Empty`（游戏 mscorlib 2.0 没有）。**加一条清单 = 永久拦截**，不再依赖"谁记得"。
#   ⑧ `DontDestroyOnLoad` 的常驻对象必须有 `static void Destroy()` → 插件卸载 / 热重载时才能清掉
#      （v1.7.6 专题：Ledger / Flotilla / Watchdog / Marker / RemoteCursor 曾经全都只创建不销毁，见 PROJECT_SPEC §11.14）
# 用法：.\tools\check-rules.ps1 -Root <仓库根>
param([Parameter(Mandatory = $true)][string]$Root)
$ErrorActionPreference = 'Stop'

# ---- 一行代码"忽略注释与字符串字面量"后的净花括号数（用于找方法体边界）----
function Get-BraceDelta($line) {
    $inStr = $false; $inChr = $false; $esc = $false; $net = 0
    for ($i = 0; $i -lt $line.Length; $i++) {
        $ch = $line[$i]
        if ($inStr -or $inChr) {
            if ($esc) { $esc = $false; continue }
            if ($ch -eq '\') { $esc = $true; continue }
            if ($inStr -and $ch -eq '"') { $inStr = $false; continue }
            if ($inChr -and $ch -eq "'") { $inChr = $false; continue }
            continue
        }
        if ($ch -eq '/' -and ($i + 1) -lt $line.Length -and $line[$i + 1] -eq '/') { break }
        if ($ch -eq '"') { $inStr = $true; continue }
        if ($ch -eq "'") { $inChr = $true; continue }
        if ($ch -eq '{') { $net++ } elseif ($ch -eq '}') { $net-- }
    }
    return $net
}

function Strip-Comment($line) {
    $inStr = $false; $inChr = $false; $esc = $false
    for ($i = 0; $i -lt $line.Length; $i++) {
        $ch = $line[$i]
        if ($inStr -or $inChr) {
            if ($esc) { $esc = $false; continue }
            if ($ch -eq '\') { $esc = $true; continue }
            if ($inStr -and $ch -eq '"') { $inStr = $false; continue }
            if ($inChr -and $ch -eq "'") { $inChr = $false; continue }
            continue
        }
        if ($ch -eq '/' -and ($i + 1) -lt $line.Length -and $line[$i + 1] -eq '/') { return $line.Substring(0, $i) }
        if ($ch -eq '"') { $inStr = $true; continue }
        if ($ch -eq "'") { $inChr = $true; continue }
    }
    return $line
}

$hotNames = '^(Update|OnGUI|LateUpdate|FixedUpdate|Draw[A-Za-z]*|Tick[A-Za-z]*)$'
$reflDecl = '(?:System\.Reflection\.)?(?:FieldInfo|PropertyInfo|MethodInfo|ConstructorInfo|EventInfo|ParameterInfo|MemberInfo)\s+([A-Za-z_]\w*)'
$sigRe = '^\s+(?:internal |public |private |protected |static |virtual |override |sealed )*[\w<>,\[\]\.]+\s+(\w+)\s*(?:<[^<>]*>)?\s*\('

$files = @(Get-ChildItem $Root -Filter *.cs -File)
if ($files.Count -eq 0) { Write-Host "[rules] 找不到任何 .cs（-Root 传错？）" -ForegroundColor Red; exit 1 }

# ---- 禁用清单（规则⑦）：`tools\banned-api.txt`，一行 `正则模式 | 原因`；文件不存在 = 清单为空 ----
$banned = New-Object System.Collections.ArrayList
$banFile = Join-Path $Root 'tools\banned-api.txt'
if (Test-Path $banFile) {
    foreach ($l in [System.IO.File]::ReadAllLines($banFile, [Text.Encoding]::UTF8)) {
        $t = $l.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith('#')) { continue }
        $parts = $t.Split('|')
        if ($parts.Count -lt 2) { continue }
        [void]$banned.Add([pscustomobject]@{ Pattern = $parts[0].Trim(); Reason = $parts[1].Trim() })
    }
}

$v = New-Object System.Collections.ArrayList
$scannedLines = 0
$hotMethods = 0

foreach ($f in $files) {
    $lines = [System.IO.File]::ReadAllLines($f.FullName, [Text.Encoding]::UTF8)
    $scannedLines += $lines.Count

    # ---- A. 方法区间（② 作用域判定 与 ⑤ 热路径 共用）----
    $methods = New-Object System.Collections.ArrayList
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -notmatch $sigRe) { continue }
        $open = -1
        for ($k = $i; $k -lt $lines.Count; $k++) {
            if ((Get-BraceDelta $lines[$k]) -gt 0) { $open = $k; break }
            if ($lines[$k] -match ';\s*$') { break }
        }
        if ($open -lt 0) { continue }

        $depth = 0; $end = -1
        for ($k = $open; $k -lt $lines.Count; $k++) {
            $depth += (Get-BraceDelta $lines[$k])
            if ($depth -eq 0) { $end = $k; break }
        }
        if ($end -lt 0) { continue }
        [void]$methods.Add(@{ Name = $Matches[1]; Start = $i; End = $end })
    }

    # ---- B. ② 反射成员集合：类级字段（全文件有效）+ 每个方法内的局部（仅该方法有效）----
    $reflFields = New-Object System.Collections.Generic.HashSet[string]
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $inMethod = $false
        foreach ($m in $methods) { if ($i -ge $m.Start -and $i -le $m.End) { $inMethod = $true; break } }
        if ($inMethod) { continue }
        $mm = [regex]::Match((Strip-Comment $lines[$i]), $reflDecl + '\s*(?:=|;)')
        if ($mm.Success) { [void]$reflFields.Add($mm.Groups[1].Value) }
    }
    $reflLocal = @{}
    foreach ($m in $methods) {
        $set = New-Object System.Collections.Generic.HashSet[string]
        foreach ($mm in [regex]::Matches((($lines[$m.Start..$m.End]) -join "`n"), $reflDecl)) { [void]$set.Add($mm.Groups[1].Value) }
        $reflLocal[$m.Start] = $set
    }

    # ---- C. 逐行检查 ①②③④ ----
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $raw = $lines[$i]
        $code = Strip-Comment $raw
        $lineNo = $i + 1

        # ① 空 catch（允许在 catch 那行或它上面两行写 // silent-ok）
        if ($code -match 'catch\s*\{\s*\}') {
            $marker = $false
            for ($k = [Math]::Max(0, $i - 2); $k -le $i; $k++) { if ($lines[$k] -match 'silent-ok') { $marker = $true } }
            if (-not $marker) { [void]$v.Add(("① 空 catch 没写 // silent-ok  " + $f.Name + ":" + $lineNo + "   " + $raw.Trim())) }
        }

        # ② 反射成员用 == null / != null 判空（作用域 = 类级字段 + 当前方法内的局部）
        if ($code -match '[!=]=\s*null') {
            $names = New-Object System.Collections.Generic.HashSet[string]
            foreach ($n in $reflFields) { [void]$names.Add($n) }
            foreach ($m in $methods) {
                if ($i -ge $m.Start -and $i -le $m.End) { foreach ($n in $reflLocal[$m.Start]) { [void]$names.Add($n) } }
            }
            foreach ($name in $names) {
                if ($code -match ('\b' + [regex]::Escape($name) + '\s*[!=]=\s*null')) {
                    if ($raw -notmatch '//\s*ok:') {
                        [void]$v.Add(("② 反射成员 $name 用 == null 判空（游戏 mscorlib 2.0 无 op_Equality，用 object.ReferenceEquals）  " + $f.Name + ":" + $lineNo + "   " + $raw.Trim()))
                    }
                    break
                }
            }
        }

        # ③ Debug.Log
        if ($code -match '\bDebug\.Log') {
            [void]$v.Add(("③ 用了 Debug.Log（应走 Util.Log/Warn）  " + $f.Name + ":" + $lineNo + "   " + $raw.Trim()))
        }

        # ④ 硬编码盘符路径（注释行不算）
        if ($raw -notmatch '^\s*//' -and $code -match '[A-Za-z]:\\') {
            [void]$v.Add(("④ 代码里出现硬编码盘符路径  " + $f.Name + ":" + $lineNo + "   " + $raw.Trim()))
        }

        # ⑥ 纵向布局链丢返回值：`DrawXxx(x, inner, y);` 必须以 `y =` 接住（丢 → 与下一行同 y，后画的盖住先画的）
        if ($code -match '^\s*Draw\w+\s*\(\s*x\s*,\s*inner\s*,\s*y\s*\)\s*;') {
            if ($raw -notmatch '//\s*layout-ok') {
                [void]$v.Add(("⑥ 纵向布局链丢了返回值（写成 y = DrawXxx(x, inner, y);；确有不接的理由写 // layout-ok）  " + $f.Name + ":" + $lineNo + "   " + $raw.Trim()))
            }
        }

        # ⑦ 禁用清单（tools\banned-api.txt；见文件头 ⑦）：一行一个模式，命中即拦（该行写 // ok: 可豁免）
        foreach ($ban in $banned) {
            if ($code -match $ban.Pattern) {
                if ($raw -notmatch '//\s*ok:') {
                    [void]$v.Add(("⑦ 命中禁用清单「" + $ban.Pattern + "」：" + $ban.Reason + "  " + $f.Name + ":" + $lineNo + "   " + $raw.Trim()))
                }
                break
            }
        }
    }

    # ---- D. ⑤ 热路径方法体里的分配 ----
    foreach ($m in $methods) {
        $name = $m.Name
        if ($name -notmatch $hotNames) { continue }
        $hotMethods++

        for ($k = $m.Start; $k -le $m.End; $k++) {
            if ($lines[$k] -match '//\s*alloc-ok') { continue }
            $code = Strip-Comment $lines[$k]
            if ($code -match 'new\s+List<') { [void]$v.Add(("⑤ 热路径 " + $name + " 里有 new List<（每帧分配；确有必要写 // alloc-ok）  " + $f.Name + ":" + ($k + 1) + "   " + $lines[$k].Trim())) }
            if ($code -match 'new\s+Dictionary<') { [void]$v.Add(("⑤ 热路径 " + $name + " 里有 new Dictionary<（每帧分配；确有必要写 // alloc-ok）  " + $f.Name + ":" + ($k + 1) + "   " + $lines[$k].Trim())) }
            if ($code -match 'new\s+HashSet<') { [void]$v.Add(("⑤ 热路径 " + $name + " 里有 new HashSet<（每帧分配；确有必要写 // alloc-ok）  " + $f.Name + ":" + ($k + 1) + "   " + $lines[$k].Trim())) }
        }
    }

    # ---- E. ⑧ 常驻对象必须有 Destroy()（用了 DontDestroyOnLoad 就必须能清）----
    # 场景：插件卸载 / 热重载 / 关游戏。只创建不销毁 ⇒ 辅助对象与运行时贴图永远留着（v1.7.6 专题，见 §11.14）。
    $text = [System.IO.File]::ReadAllText($f.FullName)
    if ($text -match 'DontDestroyOnLoad\s*\(') {
        if ($text -notmatch 'static\s+void\s+Destroy\s*\(') {
            [void]$v.Add(("⑧ 用了 DontDestroyOnLoad 但没有 static void Destroy()（卸载后清不掉；见 PROJECT_SPEC §11.14）  " + $f.Name))
        }
    }
}

Write-Host "[rules] 扫描 $($files.Count) 个 .cs / $scannedLines 行 / 热路径方法 $hotMethods 个 / 禁用清单 $($banned.Count) 条"
if ($v.Count -gt 0) {
    Write-Host "[rules] 违规 $($v.Count) 处：" -ForegroundColor Red
    $v | Sort-Object -Unique | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
    exit 1
}
Write-Host "[rules] 全部通过 OK" -ForegroundColor Green
exit 0

