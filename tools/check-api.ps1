# 运行时 API 链接校验（构建闸门）：模组 DLL 里对 mscorlib / System / System.Core 的每个成员引用，
# 必须真的存在于【游戏自带】的同名程序集里——游戏 Mono 是 mscorlib 2.0.0.0，缺 .NET 4.x 的 API
# （典型：params 空数组被 Roslyn 优化成 Array.Empty<T>() → 运行期 MissingMethodException 且游戏内毫无反馈）。
param([Parameter(Mandatory=$true)][string]$Dll, [Parameter(Mandatory=$true)][string]$GameDir)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll')

# 程序集元数据里的"特性类型"引用不参与加载（CLR 仅反射时才解析），列为已知无害：
$KnownHarmless = @('System.Runtime.Versioning.TargetFrameworkAttribute')

$solver = New-Object Mono.Cecil.DefaultAssemblyResolver
$solver.AddSearchDirectory((Join-Path $GameDir 'BadNorth_Data\Managed'))
$solver.AddSearchDirectory((Join-Path $GameDir 'BepInEx\core'))
$solver.AddSearchDirectory((Join-Path $GameDir 'BepInEx\plugins'))

$rp = New-Object Mono.Cecil.ReaderParameters
$rp.AssemblyResolver = $solver
$rp.ReadSymbols = $false
$a = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll, $rp)

$checked = 0
$missing = New-Object System.Collections.ArrayList
foreach ($mr in $a.MainModule.GetMemberReferences()) {
    $dt = $mr.DeclaringType
    if ($null -eq $dt) { continue }
    $scope = $dt.Scope
    $asmName = $null
    if ($scope -is [Mono.Cecil.AssemblyNameReference]) { $asmName = $scope.Name }
    if ($asmName -ne 'mscorlib' -and $asmName -ne 'System' -and $asmName -ne 'System.Core') { continue }
    if ($KnownHarmless -contains $dt.FullName) { continue }
    $checked++

    try { $td = $dt.Resolve() } catch { $td = $null }
    if ($null -eq $td) { [void]$missing.Add("类型不存在: $($dt.FullName) [$asmName]"); continue }

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
$a.Dispose()

Write-Host "[api-check] 已检查 BCL 成员引用 $checked 个"
if ($missing.Count -gt 0) {
    Write-Host "[api-check] 以下成员在游戏运行时不存-> 会抛 MissingMethodException：" -ForegroundColor Red
    $missing | Sort-Object -Unique | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
    exit 1
}
Write-Host "[api-check] 全部存在，运行时 API 安全 OK" -ForegroundColor Green
exit 0
