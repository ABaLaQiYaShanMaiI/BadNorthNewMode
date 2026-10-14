# 整合包（BadNorthMOD-main）兼容性闸门：把"成员引用校验"的目标程序集，从【原版 Assembly-CSharp】
# 换成【整合包替换的那份 Assembly-CSharp】，并可选输出"包版 vs 原版"的元数据差异。
#
# 背景（2026-10-12 实测）：
#   - 原版 …\BadNorth_Data\Managed\Assembly-CSharp.dll  2,862,080 B  MVID 38edd33c-39d6-4f38-becf-d6ff5ef06747
#   - 包版 BadNorthMOD-main\MOD\Assembly-CSharp.dll    2,882,560 B  MVID 同上（= 就地编辑，不重编译）
#   ⇒ 包版是原版的**超集**（+32 类型 / +102 方法 / +111 字段），所以风险不在"成员消失"，
#     而在"被删/被改的成员"与"运行期行为叠加"。本脚本用来把前一半变成可自动化的构建闸门。
#
# 用法：
#   .\tools\check-pack-compat.ps1 -Dll <我们的 DLL> -GameDir <游戏根目录> -PackDll <包版 Assembly-CSharp.dll>
#   .\tools\check-pack-compat.ps1 ... -DiffPack            # 额外打印包版与原版的差异摘要
# 退出码：0 = 我们引用的成员在包版里全部存在；1 = 有缺失（会在运行时抛 MissingMethod/FieldException）
param(
    [Parameter(Mandatory = $true)][string]$Dll,
    [Parameter(Mandatory = $true)][string]$GameDir,
    [Parameter(Mandatory = $true)][string]$PackDll,
    [int]$DiffTop = 40,
    [switch]$DiffPack
)
$ErrorActionPreference = 'Stop'

Add-Type -Path (Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll')

# 把包版 Assembly-CSharp 放到独立目录（Cecil 解析器按**文件名**匹配），保证解析到的是包版而不是原版。
$stage = Join-Path $env:TEMP ('bnpack_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item $PackDll (Join-Path $stage 'Assembly-CSharp.dll') -Force

function New-Solver($order) {
    $s = New-Object Mono.Cecil.DefaultAssemblyResolver
    foreach ($d in $order) { if (Test-Path $d) { $s.AddSearchDirectory($d) } }
    return $s
}
function Read-Asm($path, $solver) {
    $rp = New-Object Mono.Cecil.ReaderParameters
    $rp.AssemblyResolver = $solver
    $rp.ReadSymbols = $false
    return [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path, $rp)
}

# ---------- 1. 成员引用校验（以包版为基准）----------
$solverOrder = @($stage, (Join-Path $GameDir 'BadNorth_Data\Managed'), (Join-Path $GameDir 'BepInEx\core'), (Join-Path $GameDir 'BepInEx\plugins'))
$asm = Read-Asm $Dll (New-Solver $solverOrder)

$checked = 0
$missing = New-Object System.Collections.ArrayList
$missingTypes = New-Object System.Collections.ArrayList
foreach ($mr in $asm.MainModule.GetMemberReferences()) {
    $dt = $mr.DeclaringType
    if ($null -eq $dt) { continue }
    $scope = $dt.Scope
    $asmName = $null
    if ($scope -is [Mono.Cecil.AssemblyNameReference]) { $asmName = $scope.Name }
    if ($asmName -ne 'Assembly-CSharp') { continue }   # 只校验游戏程序集；BCL/Unity 由 check-api.ps1 负责
    $checked++
    $td = $null
    try { $td = $dt.Resolve() } catch { $td = $null }
    if ($null -eq $td) { [void]$missingTypes.Add($dt.FullName); continue }
    if ($mr -is [Mono.Cecil.FieldReference]) {
        $hit = @($td.Fields | Where-Object { $_.Name -eq $mr.Name })
        if ($hit.Count -eq 0) { [void]$missing.Add("字段不存在: $($dt.FullName)::$($mr.Name)") }
    }
    elseif ($mr -is [Mono.Cecil.MethodReference]) {
        $pc = $mr.Parameters.Count
        $hit = @($td.Methods | Where-Object { $_.Name -eq $mr.Name -and $_.Parameters.Count -eq $pc })
        if ($hit.Count -eq 0) { [void]$missing.Add("方法不存在: $($dt.FullName)::$($mr.Name)($pc 参)") }
    }
}
Write-Host "[pack-compat] 对包版 Assembly-CSharp 检查成员引用 $checked 个（来自 $([IO.Path]::GetFileName($Dll))）"
if ($missingTypes.Count -gt 0) {
    Write-Host "[pack-compat] 类型不存在 $($missingTypes.Count) 个：" -ForegroundColor Red
    $missingTypes | Sort-Object -Unique | Select-Object -First $DiffTop | ForEach-Object { Write-Host "   类型: $_" -ForegroundColor Red }
}
if ($missing.Count -gt 0) {
    Write-Host "[pack-compat] 缺失成员 $($missing.Count) 个（运行期会抛 MissingMethod/MissingField）：" -ForegroundColor Red
    $missing | Sort-Object -Unique | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
} else {
    Write-Host "[pack-compat] 我们引用的 Assembly-CSharp 成员在包版里全部存在 OK" -ForegroundColor Green
}
$asm.Dispose()

# ---------- 2. 可选：包版 vs 原版 差异摘要 ----------
if ($DiffPack) {
    $vanilla = Read-Asm (Join-Path $GameDir 'BadNorth_Data\Managed\Assembly-CSharp.dll') (New-Solver $solverOrder)
    $pack = Read-Asm (Join-Path $stage 'Assembly-CSharp.dll') (New-Solver $solverOrder)
    if ($vanilla.MainModule.Mvid -eq $pack.MainModule.Mvid) {
        Write-Host "[pack-compat] MVID 相同（$($pack.MainModule.Mvid)）→ 包版是原版的就地编辑版" -ForegroundColor Yellow
    } else {
        Write-Host "[pack-compat] MVID 不同 → 包版是重新编译的独立版本，务必逐成员校验" -ForegroundColor Yellow
    }
    $vt = @{}; foreach ($t in $vanilla.MainModule.Types) { $vt[$t.FullName] = $t }
    $pt = @{}; foreach ($t in $pack.MainModule.Types) { $pt[$t.FullName] = $t }
    $added = @($pt.Keys | Where-Object { -not $vt.ContainsKey($_) } | Sort-Object)
    $removed = @($vt.Keys | Where-Object { -not $pt.ContainsKey($_) } | Sort-Object)
    Write-Host "[pack-compat] 包版新增类型 $($added.Count) 个 / 移除类型 $($removed.Count) 个"
    $added | Select-Object -First $DiffTop | ForEach-Object { Write-Host "   + $_" }
    if ($removed.Count -gt 0) { $removed | ForEach-Object { Write-Host "   - $_" -ForegroundColor Red } }

    $shared = @($pt.Keys | Where-Object { $vt.ContainsKey($_) })
    $fieldDelta = 0; $methodDelta = 0; $touched = New-Object System.Collections.ArrayList
    foreach ($name in $shared) {
        $a = $vt[$name]; $b = $pt[$name]
        $af = New-Object 'System.Collections.Generic.HashSet[string]'
        foreach ($x in $a.Fields) { [void]$af.Add($x.Name) }
        $bf = New-Object 'System.Collections.Generic.HashSet[string]'
        foreach ($x in $b.Fields) { [void]$bf.Add($x.Name) }
        $am = New-Object 'System.Collections.Generic.HashSet[string]'
        foreach ($x in $a.Methods) { [void]$am.Add($x.Name + '/' + $x.Parameters.Count) }
        $bm = New-Object 'System.Collections.Generic.HashSet[string]'
        foreach ($x in $b.Methods) { [void]$bm.Add($x.Name + '/' + $x.Parameters.Count) }
        $fa = 0; $ra = 0; $ma = 0; $mrb = 0
        foreach ($x in $bf) { if (-not $af.Contains($x)) { $fa++ } }
        foreach ($x in $af) { if (-not $bf.Contains($x)) { $ra++ } }
        foreach ($x in $bm) { if (-not $am.Contains($x)) { $ma++ } }
        foreach ($x in $am) { if (-not $bm.Contains($x)) { $mrb++ } }
        $fieldDelta += $fa + $ra; $methodDelta += $ma + $mrb
        if ($fa -or $ra -or $ma -or $mrb) {
            [void]$touched.Add([pscustomobject]@{ Type = $name; FldAdded = $fa; FldRemoved = $ra; MthAdded = $ma; MthRemoved = $mrb })
        }
    }
    Write-Host "[pack-compat] 共有类型里：字段增删 $fieldDelta 处 / 方法增删 $methodDelta 处，涉及 $($touched.Count) 个类型"
    $touched | Sort-Object -Property @{Expression={$_.FldRemoved + $_.MthRemoved};Descending=$true}, @{Expression={$_.FldAdded + $_.MthAdded};Descending=$true} |
        Select-Object -First $DiffTop | Format-Table -AutoSize | Out-String -Width 160 | Write-Host
    $vanilla.Dispose(); $pack.Dispose()
}

Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
if ($missing.Count -gt 0 -or $missingTypes.Count -gt 0) { exit 1 }
exit 0

