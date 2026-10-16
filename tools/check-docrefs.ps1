# 文档引用闸门（v1.7.3 / P3）：`.cs` 注释里写了 80+ 处 `§N` / `§N.M`（如 `见 §4 坑表`、`§5/T30`、`§11.3`）。
# 这些引用是本项目的知识锚点 —— md 一旦调整章节编号就会集体腐烂，而人手改注释几乎必然漏。
# 本闸门把"代码注释里的 § 引用"与"md 里真实存在的标题编号"对齐（不需要游戏 DLL，可在 CI 跑）。
# 用法：.\tools\check-docrefs.ps1 -Root <仓库根>
param([Parameter(Mandatory = $true)][string]$Root)
$ErrorActionPreference = 'Stop'

$mdFiles = @(Get-ChildItem $Root -Filter *.md -File)
if ($mdFiles.Count -eq 0) { Write-Host "[docrefs] 找不到任何 .md（-Root 传错？）" -ForegroundColor Red; exit 1 }

$headings = New-Object System.Collections.Generic.HashSet[string]
foreach ($md in $mdFiles) {
    foreach ($l in [System.IO.File]::ReadAllLines($md.FullName, [Text.Encoding]::UTF8)) {
        $m = [regex]::Match($l, '^#{1,4}\s*(\d+(?:\.\d+)?)')
        if ($m.Success) { [void]$headings.Add($m.Groups[1].Value) }
    }
}

$csFiles = @(Get-ChildItem $Root -Filter *.cs -File)
$totalRefs = 0
$missing = New-Object System.Collections.ArrayList
foreach ($cs in $csFiles) {
    $lineNo = 0
    foreach ($l in [System.IO.File]::ReadAllLines($cs.FullName, [Text.Encoding]::UTF8)) {
        $lineNo++
        foreach ($m in [regex]::Matches($l, '§(\d+(?:\.\d+)?)')) {
            $key = $m.Groups[1].Value
            $totalRefs++
            if (-not $headings.Contains($key)) {
                [void]$missing.Add(("§" + $key + "  " + $cs.Name + ":" + $lineNo + "   " + $l.Trim()))
            }
        }
    }
}

Write-Host "[docrefs] .cs 里的 § 引用 $totalRefs 处；md 标题编号 $($headings.Count) 个（来自 $($mdFiles.Count) 个 md）"
if ($missing.Count -gt 0) {
    Write-Host "[docrefs] 找不到对应标题的引用 $($missing.Count) 处（md 改了章节？）：" -ForegroundColor Red
    $missing | Sort-Object -Unique | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
    exit 1
}
Write-Host "[docrefs] 全部引用都能在 md 里找到对应标题 OK" -ForegroundColor Green
exit 0
