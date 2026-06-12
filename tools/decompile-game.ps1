# Decompiles the Bannerlord assemblies we reference/patch into a sibling
# source tree for grepping (see AGENTS.md "When in doubt"). Re-run after a
# game patch. Requires the ilspycmd global dotnet tool:
#   dotnet tool install -g ilspycmd
param(
    [string]$GameDir = 'C:\games\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$OutDir = 'C:\github\bannerlord-decompiled'
)

$ErrorActionPreference = 'Stop'

$mainBin = Join-Path $GameDir 'bin\Win64_Shipping_Client'
$nativeBin = Join-Path $GameDir 'Modules\Native\bin\Win64_Shipping_Client'
$sandboxBin = Join-Path $GameDir 'Modules\SandBox\bin\Win64_Shipping_Client'
$sandboxCoreBin = Join-Path $GameDir 'Modules\SandBoxCore\bin\Win64_Shipping_Client'
$customBattleBin = Join-Path $GameDir 'Modules\CustomBattle\bin\Win64_Shipping_Client'

# (bin dir, assembly name) — the set that matters for mission/order/UI work.
$assemblies = @(
    @($mainBin, 'TaleWorlds.MountAndBlade'),
    @($mainBin, 'TaleWorlds.MountAndBlade.ViewModelCollection'),
    @($mainBin, 'TaleWorlds.CampaignSystem'),
    @($mainBin, 'TaleWorlds.CampaignSystem.ViewModelCollection'),
    @($mainBin, 'TaleWorlds.Core'),
    @($mainBin, 'TaleWorlds.Core.ViewModelCollection'),
    @($mainBin, 'TaleWorlds.Library'),
    @($mainBin, 'TaleWorlds.Engine'),
    @($mainBin, 'TaleWorlds.InputSystem'),
    @($mainBin, 'TaleWorlds.ScreenSystem'),
    @($mainBin, 'TaleWorlds.ObjectSystem'),
    @($mainBin, 'TaleWorlds.ModuleManager'),
    @($mainBin, 'TaleWorlds.Localization'),
    @($mainBin, 'TaleWorlds.GauntletUI'),
    @($mainBin, 'TaleWorlds.GauntletUI.Data'),
    @($mainBin, 'TaleWorlds.GauntletUI.PrefabSystem'),
    @($nativeBin, 'TaleWorlds.MountAndBlade.View'),
    @($nativeBin, 'TaleWorlds.MountAndBlade.GauntletUI'),
    @($sandboxBin, 'SandBox'),
    @($sandboxBin, 'SandBox.View'),
    @($sandboxBin, 'SandBox.GauntletUI'),
    @($sandboxBin, 'SandBox.ViewModelCollection'),
    @($sandboxCoreBin, 'SandBoxCore'),
    @($customBattleBin, 'TaleWorlds.MountAndBlade.CustomBattle')
)

New-Item -ItemType Directory -Force $OutDir | Out-Null

$version = ([xml](Get-Content (Join-Path $GameDir 'Modules\Native\SubModule.xml'))).Module.Version.value
@"
Decompiled Bannerlord sources for API reference. DO NOT EDIT; regenerate with
RealisticBattlePlanning\tools\decompile-game.ps1 after game patches.
Game version: $version
Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm')
"@ | Set-Content (Join-Path $OutDir 'VERSION.txt') -Encoding utf8

foreach ($entry in $assemblies) {
    $bin, $name = $entry
    $dll = Join-Path $bin "$name.dll"
    if (-not (Test-Path $dll)) {
        Write-Warning "skipped (not found): $dll"
        continue
    }
    $target = Join-Path $OutDir $name
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    New-Item -ItemType Directory -Force $target | Out-Null
    Write-Host "decompiling $name ..."
    & ilspycmd $dll -p -o $target -r $mainBin -r $nativeBin -r $sandboxBin -r $sandboxCoreBin
    if ($LASTEXITCODE -ne 0) { Write-Warning "ilspycmd exited $LASTEXITCODE for $name" }
}

Write-Host "done -> $OutDir"
