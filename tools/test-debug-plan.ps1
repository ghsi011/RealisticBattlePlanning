# Offline smoke test of the debug-plan deserialization path (mirrors
# DebugPlanLoader settings). Runs on .NET Framework via Windows PowerShell.
param(
    [string]$GameDir = 'C:\games\Steam\steamapps\common\Mount & Blade II Bannerlord',
    [string]$RepoDir = 'C:\github\RealisticBattlePlanning'
)

$ErrorActionPreference = 'Stop'

[void][Reflection.Assembly]::LoadFrom("$GameDir\bin\Win64_Shipping_Client\Newtonsoft.Json.dll")
[void][Reflection.Assembly]::LoadFrom("$RepoDir\src\bin\x64\Debug\RealisticBattlePlanning.dll")

$settings = New-Object Newtonsoft.Json.JsonSerializerSettings
$settings.Converters.Add((New-Object Newtonsoft.Json.Converters.StringEnumConverter))
$settings.MissingMemberHandling = [Newtonsoft.Json.MissingMemberHandling]::Error

$planType = [RealisticBattlePlanning.Planning.Model.BattlePlan]

function Parse([string]$json) {
    [Newtonsoft.Json.JsonConvert]::DeserializeObject($json, $planType, $settings)
}

# 1. The shipped sample must parse and survive validation.
$sample = Get-Content "$RepoDir\Module\ModuleData\rbp_debug_plan.json" -Raw
$plan = Parse $sample
$validation = [RealisticBattlePlanning.Planning.PlanValidator]::Validate($plan)
"sample: formations=$($plan.Formations.Count) anchors=$($plan.Anchors.Count) signals=$($plan.PlayerSignals.Count) errors=$($validation.Errors.Count) warnings=$($validation.Warnings.Count)"
$validation.Errors | ForEach-Object { "  ERROR: $_" }
$validation.Warnings | ForEach-Object { "  WARN:  $_" }
""
[RealisticBattlePlanning.Planning.PlanFormatter]::Describe($plan)
""

# 2. A typo'd property must fail loudly, with a readable message.
try {
    Parse '{ "formations": [ { "formation": "Infantry", "stagess": [] } ] }' | Out-Null
    "typo test: FAILED - no exception raised"
} catch {
    "typo test: OK - $($_.Exception.GetBaseException().Message)"
}

# 3. A bad enum value must fail loudly.
try {
    Parse '{ "formations": [ { "formation": "Imaginary", "stages": [] } ] }' | Out-Null
    "enum test: FAILED - no exception raised"
} catch {
    "enum test: OK - $($_.Exception.GetBaseException().Message)"
}

# 4. Validator catches structural problems the parser can't.
$bad = Parse '{ "formations": [ { "formation": "Infantry", "stages": [ { "when": [ { "type": "TimerElapsed" } ], "do": { "type": "MoveTo", "anchor": "nowhere" } } ] } ] }'
$badResult = [RealisticBattlePlanning.Planning.PlanValidator]::Validate($bad)
"validator test: errors=$($badResult.Errors.Count) (expect 2: missing seconds, undefined anchor)"
$badResult.Errors | ForEach-Object { "  $_" }
