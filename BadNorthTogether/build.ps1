# BadNorthTogether（Together 0.6.13 打补丁版）构建脚本
# 用 Roslyn csc 直接编译，引用游戏自带的 net35 程序集（不使用 dotnet SDK 的 net472/net8 目标框架，
# 否则生成的程序集会引用 4.x mscorlib，Unity Mono 2.0 加载不了）。
param(
    [string]$BadNorthDir = 'D:\Steam\steamapps\common\BadNorth',
    [string]$Configuration = 'Release',
    [switch]$Deploy
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$managed = Join-Path $BadNorthDir 'BadNorth_Data\Managed'
$core = Join-Path $BadNorthDir 'BepInEx\core'
$plugins = Join-Path $BadNorthDir 'BepInEx\plugins'

# ---- 1. 找 Roslyn csc ----
$csc = $null
$sdkRoot = 'C:\Program Files\dotnet\sdk'
if (Test-Path $sdkRoot)
{
    $csc = Get-ChildItem $sdkRoot -Directory |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'Roslyn\bincore\csc.dll' } |
        Where-Object { Test-Path $_ } |
        Select-Object -First 1
}
if (-not $csc) { throw '找不到 Roslyn csc.dll（需要 .NET SDK）' }

# ---- 2. 引用 ----
$refNames = @(
    'mscorlib.dll',
    'System.dll',
    'System.Core.dll',
    'Assembly-CSharp.dll',
    'UnityEngine.dll',
    'UnityEngine.CoreModule.dll',
    'UnityEngine.ParticleSystemModule.dll',
    'UnityEngine.AnimationModule.dll',
    'UnityEngine.AudioModule.dll',
    'UnityEngine.PhysicsModule.dll',
    'UnityEngine.UI.dll',
    'UnityEngine.IMGUIModule.dll',
    'UnityEngine.TerrainModule.dll',
    'Fabric.Core.dll'
)
$refs = @()
foreach ($n in $refNames)
{
    $p = Join-Path $managed $n
    if (Test-Path $p) { $refs += $p }
}
foreach ($n in @('BepInEx.dll', '0Harmony.dll'))
{
    $p = Join-Path $core $n
    if (Test-Path $p) { $refs += $p }
    else { throw "缺少引用：$p" }
}

# ---- 3. 源文件 ----
$src = Get-ChildItem (Join-Path $root 'Together') -Recurse -Filter *.cs |
    Sort-Object FullName |
    ForEach-Object { $_.FullName }
if ($src.Count -lt 100) { throw "源文件数量异常：$($src.Count)" }

# ---- 4. 编译 ----
$outDir = Join-Path $root "bin\$Configuration"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
# 程序集名保持 Together（所有反射 / FindAssembly("Together") 不变），只把文件发布成 BadNorthTogether.dll
$out = Join-Path $outDir 'Together.dll'
$publish = Join-Path $outDir 'BadNorthTogether.dll'
Remove-Item $out -Force -ErrorAction SilentlyContinue
Remove-Item $publish -Force -ErrorAction SilentlyContinue

$cscArgs = @(
    $csc, '-nologo', '-noconfig', '-nostdlib+', '-target:library', '-platform:x86', '-unsafe+',
    '-langversion:latest', '-deterministic', '-optimize+', '-define:TRACE', "-out:$out"
)
$cscArgs += $refs | ForEach-Object { "-r:$_" }
$cscArgs += $src

& dotnet @cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc 编译失败：$LASTEXITCODE" }

$hash = (Get-FileHash $out -Algorithm SHA256).Hash
Copy-Item $out $publish -Force
Write-Host ("[build] {0}  {1} bytes  SHA256={2}" -f $publish, (Get-Item $out).Length, $hash)

# ---- 5. 部署（可选）----
if ($Deploy)
{
    # 同名程序集只能存在一份：旧 Together.dll 必须移走，否则 BepInEx 会加载两份 Together
    $old = Join-Path $plugins 'Together.dll'
    if (Test-Path $old)
    {
        $bak = $old + '.bak_' + (Get-Date -Format 'yyyyMMdd_HHmmss')
        Move-Item $old $bak -Force
        Write-Host "[build] 旧 Together.dll 已备份: $bak"
    }
    $dst = Join-Path $plugins 'BadNorthTogether.dll'
    if (Test-Path $dst) { Copy-Item $dst ($dst + '.bak_' + (Get-Date -Format 'yyyyMMdd_HHmmss')) }
    Copy-Item $publish $dst -Force
    Write-Host "[build] 已部署: $dst"
}
