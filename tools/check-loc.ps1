# 本地化一致性校验（构建闸门）：`Loc.cs` 的 key = 简中原文，调用点用 `Loc.T/F("原文"[, 参数])` 查表。
# 以下四类错误肉眼几乎发现不了，但都会在玩家侧出问题，所以放进构建：
#   ① 调用点用了表里没有的 key   → 静默显示中文（英文玩家看到中文，且永远不会报错）
#   ② 表里有但没有任何调用点     → 死词条（改文案时容易漏改）
#   ③ key 与英文的 {n} 集合不一致 → string.Format 抛 FormatException（在 OnGUI 里会炸掉整帧）
#   ④ 表内重复 key                → Dictionary 初始化抛 ArgumentException（插件直接加载失败）
# 用法：.\tools\check-loc.ps1 -Root <仓库根>
param([Parameter(Mandatory=$true)][string]$Root)
$ErrorActionPreference = 'Stop'

$loc = Join-Path $Root 'Loc.cs'
if (-not (Test-Path $loc)) { Write-Host "[loc-check] 找不到 $loc" -ForegroundColor Red; exit 1 }

$dict = [System.IO.File]::ReadAllText($loc)
$reEntry = '\{\s*"((?:[^"\\]|\\.)*)",\s*"((?:[^"\\]|\\.)*)"\s*\}'
$reCall = 'Loc\.(?:T|F)\(\s*"((?:[^"\\]|\\.)*)"'
$reIndex = '{(\d+)'

$keys = New-Object System.Collections.Generic.List[string]
$vals = New-Object System.Collections.Generic.List[string]
foreach ($m in [regex]::Matches($dict, $reEntry)) {
    $keys.Add($m.Groups[1].Value)
    $vals.Add($m.Groups[2].Value)
}

# 收集调用点（Loc.cs 自身不算）
$used = New-Object System.Collections.Generic.HashSet[string]
Get-ChildItem $Root -Filter *.cs -File | Where-Object { $_.Name -ne 'Loc.cs' } | ForEach-Object {
    $text = [System.IO.File]::ReadAllText($_.FullName)
    foreach ($m in [regex]::Matches($text, $reCall)) { [void]$used.Add($m.Groups[1].Value) }
}

$seen = New-Object System.Collections.Generic.HashSet[string]
$missing = New-Object System.Collections.ArrayList
$unused = New-Object System.Collections.ArrayList
$dupes = New-Object System.Collections.ArrayList
$phBad = New-Object System.Collections.ArrayList
$withPh = 0

for ($i = 0; $i -lt $keys.Count; $i++) {
    $k = $keys[$i]
    if (-not $seen.Add($k)) { [void]$dupes.Add($k) }
    if (-not $used.Contains($k)) { [void]$unused.Add($k) }

    $kr = (([regex]::Matches($k, $reIndex)) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique) -join ','
    $vr = (([regex]::Matches($vals[$i], $reIndex)) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique) -join ','
    if ($kr -ne '') { $withPh++ }
    if ($kr -ne $vr) { [void]$phBad.Add("[$kr] vs [$vr]  " + $k) }
}
foreach ($k in $used) { if (-not $seen.Contains($k)) { [void]$missing.Add($k) } }

Write-Host "[loc-check] 词条 $($keys.Count)（含占位符 $withPh 条）；调用点 key $($used.Count) 个"
# 假阳性防线：正则一旦失效会"恒过"，这里用"应该有大量占位符词条"做自检
if ($keys.Count -gt 0 -and $withPh -eq 0) {
    Write-Host "[loc-check] 警告：一条占位符都没解析到，正则可能已失效（本次校验结果不可信）" -ForegroundColor Yellow
}

$fail = $false
if ($dupes.Count -gt 0) { Write-Host "[loc-check] 表内重复 key（Dictionary 初始化会抛）：" -ForegroundColor Red; $dupes | Sort-Object -Unique | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }; $fail = $true }
if ($missing.Count -gt 0) { Write-Host "[loc-check] 调用点用了但表里没有（会静默显示中文）：" -ForegroundColor Red; $missing | Sort-Object -Unique | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }; $fail = $true }
if ($unused.Count -gt 0) { Write-Host "[loc-check] 表里有但没人用（死词条）：" -ForegroundColor Red; $unused | Sort-Object -Unique | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }; $fail = $true }
if ($phBad.Count -gt 0) { Write-Host "[loc-check] {n} 占位符与英文不一致（会抛 FormatException）：" -ForegroundColor Red; $phBad | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }; $fail = $true }

if ($fail) { exit 1 }
Write-Host "[loc-check] 全部一致，本地化安全 OK" -ForegroundColor Green
exit 0
