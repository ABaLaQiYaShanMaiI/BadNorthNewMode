# BadNorthTogether 静态闸门（无游戏、无真人双端）
# 检查：程序集/插件版本、关键类型与回滚/门控代码是否还在。
# 用法：.\tools\check-badnorthtogether.ps1 -GameDir 'D:\Steam\steamapps\common\BadNorth'
param(
    [string]$GameDir = 'D:\Steam\steamapps\common\BadNorth',
    [string]$Dll = (Join-Path $PSScriptRoot '..\bin\Release\BadNorthTogether.dll')
)

$ErrorActionPreference = 'Stop'
$fail = New-Object System.Collections.Generic.List[string]

function Check($condition, $message) {
    if ($condition) {
        Write-Host ("[PASS] " + $message)
    } else {
        Write-Host ("[FAIL] " + $message)
        $script:fail.Add($message)
    }
}

$dll = (Resolve-Path $Dll -ErrorAction SilentlyContinue)
Check ($null -ne $dll) "DLL 存在：$Dll"
if ($null -eq $dll) { exit 1 }

$cecil = Join-Path $GameDir 'BepInEx\core\Mono.Cecil.dll'
Check (Test-Path $cecil) "Mono.Cecil 存在：$cecil"
if (-not (Test-Path $cecil)) { exit 1 }

Add-Type -Path $cecil
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($dll)

Check ($asm.Name.Version.ToString() -eq '0.6.14.0') "程序集版本 = 0.6.14.0（实际 $($asm.Name.Version)）"

$pluginType = $asm.MainModule.Types | Where-Object { $_.Name -eq 'Plugin' } | Select-Object -First 1
Check ($null -ne $pluginType) "找到 Together.Plugin"
if ($pluginType) {
    $attr = $pluginType.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' } | Select-Object -First 1
    Check ($null -ne $attr) "找到 BepInPlugin"
    if ($attr) {
        $args = @($attr.ConstructorArguments | ForEach-Object { [string]$_.Value })
        Check ($args[0] -eq 'com.linfeng.together') "插件 GUID = com.linfeng.together"
        Check ($args[1] -eq 'BadNorthTogether') "插件显示名 = BadNorthTogether"
        Check ($args[2] -eq '0.6.14') "插件版本 = 0.6.14"
    }
}

$typeNames = @($asm.MainModule.Types | ForEach-Object { $_.FullName })
Check ($typeNames -contains 'Together.HostRaidExternal') "存在 Together.HostRaidExternal"
Check ($typeNames -contains 'Together.BadNorthTogetherFeatures') "存在 Together.BadNorthTogetherFeatures"

$session = $asm.MainModule.Types | Where-Object { $_.FullName -eq 'Together.Session' } | Select-Object -First 1
Check ($null -ne $session -and ($session.Properties | Where-Object { $_.Name -eq 'PeerSupportsExternalWaves' })) "Session.PeerSupportsExternalWaves 存在"

$raidIdentity = $asm.MainModule.Types | Where-Object { $_.FullName -eq 'Together.RaidIdentity' } | Select-Object -First 1
Check ($null -ne $raidIdentity -and ($raidIdentity.Methods | Where-Object { $_.Name -eq 'RegisterExternal' })) "RaidIdentity.RegisterExternal 存在"
Check ($null -ne $raidIdentity -and ($raidIdentity.Methods | Where-Object { $_.Name -eq 'UnregisterWave' })) "RaidIdentity.UnregisterWave 存在"

$manifest = $asm.MainModule.Types | Where-Object { $_.FullName -eq 'Together.HostRaidManifest' } | Select-Object -First 1
Check ($null -ne $manifest -and ($manifest.Methods | Where-Object { $_.Name -eq 'TryAdopt' })) "HostRaidManifest.TryAdopt 存在"
$asm.Dispose()

$src = Join-Path $PSScriptRoot '..\Together\Together\HostRaidManifest.cs'
Check (Test-Path $src) "源码存在：$src"
if (Test-Path $src) {
    $text = Get-Content $src -Raw
    Check ($text -notmatch 'Budget\s*=\s*0\s*,') "CaptureExternal 不再写 Budget=0"
    Check ($text -match 'Budget\s*=\s*Mathf\.Clamp\(wave\.bounty,\s*1,\s*32767\)') "CaptureExternal 使用 Mathf.Clamp(wave.bounty,1,32767)"
    Check ($text -match 'RegisterExternal\(wave, candidate\)') "TryAdopt 使用 RegisterExternal"
    Check ($text -match 'UnregisterWave\(wave\)') "TryAdopt 失败路径调用 UnregisterWave"
    Check ($text -match 'SafeFloat\(') "外部波浮点字段走 SafeFloat"
}

$external = Join-Path $PSScriptRoot '..\Together\Together\HostRaidExternal.cs'
Check (Test-Path $external) "源码存在：$external"
if (Test-Path $external) {
    $text = Get-Content $external -Raw
    Check ($text -match 'PeerSupportsExternalWaves') "HostRaidExternal 有对端能力门控"
    Check ($text -match 'held\[i\]\.Wave') "HostRaidExternal 有重复接管保护"
}

$steam = Join-Path $PSScriptRoot '..\Together\Together\Net\SteamTransport.cs'
Check (Test-Path $steam) "源码存在：$steam"
if (Test-Path $steam) {
    $text = Get-Content $steam -Raw
    Check ($text -match 'together_build') "SteamTransport 写入 together_build"
    Check ($text -match 'together_external_waves') "SteamTransport 写入 together_external_waves"
    Check ($text -match 'PeerSupportsExternalWaves') "SteamTransport 刷新 PeerSupportsExternalWaves"
}

if ($fail.Count -gt 0) {
    Write-Host ""
    Write-Host ("[FAIL] " + $fail.Count + " 项未通过")
    exit 1
}
Write-Host ""
Write-Host "[PASS] BadNorthTogether 静态闸门全部通过"
