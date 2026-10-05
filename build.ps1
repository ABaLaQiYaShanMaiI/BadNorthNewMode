#Requires -Version 5.1
<#
.SYNOPSIS
    编译 + 复制到 BepInEx/plugins + 哈希校验。
.DESCRIPTION
    1. 解析游戏目录 BadNorthDir（命令行 > 环境变量 > csproj 默认值）；
    2. dotnet build（-p:BadNorthDir 传入）；
    3. 把 bin\<Configuration>\net472\BadNorthNewMode.dll 复制到 <BadNorthDir>\BepInEx\plugins\
       （旧 DLL 先备份为 .bak_<时间戳>）；
    4. SHA256 校验源/目标一致，输出摘要。
.EXAMPLE
    .\build.ps1                          # Release，用环境变量或 csproj 默认游戏目录
    .\build.ps1 -BadNorthDir "D:\Games\Bad North" -Configuration Debug
    .\build.ps1 -SkipDeploy              # 只编译，不复制不校验
#>
param(
    [string]$BadNorthDir = $env:BadNorthDir,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipDeploy
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'BadNorthNewMode.csproj'
$dllName = 'BadNorthNewMode.dll'

# ---------- 1. 解析游戏目录 ----------
if (-not $BadNorthDir) {
    try {
        $xml = [xml](Get-Content $proj -Raw)
        $node = $xml.SelectSingleNode('//BadNorthDir')
        if ($node -and $node.'#text') { $BadNorthDir = $node.'#text'.Trim() }
    } catch { }
}
if (-not $BadNorthDir) { $BadNorthDir = 'D:\Steam\steamapps\common\BadNorth' }
if (-not (Test-Path $BadNorthDir)) {
    Write-Error "游戏目录不存在: $BadNorthDir`n  请用 -BadNorthDir <游戏根目录> 或设置环境变量 BadNorthDir。"
}
$managed = Join-Path $BadNorthDir 'BadNorth_Data\Managed'
$plugins = Join-Path $BadNorthDir 'BepInEx\plugins'
$core = Join-Path $BadNorthDir 'BepInEx\core\BepInEx.dll'
foreach ($need in @("$managed\Assembly-CSharp.dll", "$managed\UnityEngine.CoreModule.dll", $core)) {
    if (-not (Test-Path $need)) { Write-Error "缺少游戏引用，请确认 BadNorthDir 指向完整游戏安装: $need" }
}
Write-Host "[build] BadNorthDir = $BadNorthDir"

# ---------- 2. 编译 ----------
Push-Location $root
try {
    $output = & dotnet build $proj -c $Configuration -nologo -v minimal -p:BadNorthDir="$BadNorthDir" 2>&1
    $output | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) { throw "dotnet build 失败（exit=$LASTEXITCODE）" }
} finally { Pop-Location }

# 产物路径按 TFM 自动查找（net35 等），不写死
$builtDll = (Get-ChildItem (Join-Path $root "bin\$Configuration") -Recurse -Filter $dllName -ErrorAction SilentlyContinue |
    Select-Object -First 1 -ExpandProperty FullName)
if (-not $builtDll) { Write-Error "找不到编译产物: $root\bin\$Configuration\<tfm>\$dllName" }
# ---------- 3. 运行时 API 校验（构建闸门：防"净 net472 能编过、游戏 Mono 2.0 却跑不了"的 API）----------
$apiCheck = Join-Path $root 'tools\check-api.ps1'
if (Test-Path $apiCheck) {
    & $apiCheck -Dll $builtDll -GameDir $BadNorthDir
    if ($LASTEXITCODE -ne 0) { throw "运行时 API 校验失败（见上方 api-check 输出）" }
} else {
    Write-Host "[build] 未找到 tools\check-api.ps1，跳过运行时 API 校验" -ForegroundColor Yellow
}

# ---------- 3.5 本地化一致性校验（构建闸门：漏 key 会静默回退中文、占位符不一致会在 OnGUI 抛 FormatException）----------
$locCheck = Join-Path $root 'tools\check-loc.ps1'
if (Test-Path $locCheck) {
    & $locCheck -Root $root
    if ($LASTEXITCODE -ne 0) { throw "本地化校验失败（见上方 loc-check 输出）" }
} else {
    Write-Host "[build] 未找到 tools\check-loc.ps1，跳过本地化校验" -ForegroundColor Yellow
}

if ($SkipDeploy) {
    Write-Host "[build] SkipDeploy=true，仅编译+校验。产物: $builtDll"
    exit 0
}

# ---------- 4. 部署到 plugins ----------
if (-not (Test-Path $plugins)) { Write-Error "BepInEx/plugins 不存在: $plugins" }
$target = Join-Path $plugins $dllName
if (Test-Path $target) {
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $bak = "$target.bak_$stamp"
    Copy-Item $target $bak -Force
    Write-Host "[build] 旧 DLL 已备份: $bak"
}
Copy-Item $builtDll $target -Force

# ---------- 5. SHA256 校验 ----------
$hashSrc = (Get-FileHash $builtDll -Algorithm SHA256).Hash
$hashDst = (Get-FileHash $target -Algorithm SHA256).Hash
if ($hashSrc -ne $hashDst) { Write-Error "哈希校验失败: $builtDll != $target" }

$bytes = (Get-Item $target).Length
Write-Host "[build] 已部署: $target"
Write-Host "[build] SHA256 = $hashSrc ($bytes bytes) 哈希 MATCH OK"
