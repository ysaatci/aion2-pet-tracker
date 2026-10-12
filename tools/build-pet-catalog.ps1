# Builds data/pets.json (pet id -> name, genus, souls to unlock, NPC codes that drop its soul) from MetaBot.GG's
# AION 2 mount pages, which are read from the Global client's data. Data credit: https://metabot.gg/en/aion-2/mounts
# Usage: powershell -File tools/build-pet-catalog.ps1 [-DelayMs 1500]
param([int]$DelayMs = 1500)

$ErrorActionPreference = 'Stop'
$base = 'https://metabot.gg'
$agent = 'aion2-pet-tracker/0.1 (personal tool; credits metabot.gg)'
$out = Join-Path $PSScriptRoot '..\data\pets.json'

function Get-Page([string]$path) {
    for ($try = 1; $try -le 3; $try++) {
        try { return (Invoke-WebRequest -UseBasicParsing -UserAgent $agent -Uri ($base + $path) -TimeoutSec 60).Content }
        catch { Write-Warning "$path attempt ${try}: $($_.Exception.Message)"; Start-Sleep -Seconds (5 * $try) }
    }
    throw "Could not fetch $path"
}

$list = Get-Page '/en/aion-2/mounts'
$rows = [regex]::Matches($list, 'data-a2-k="own:m:(\d+)"><a [^>]*href="/en/aion-2/mounts/([a-z0-9-]+)"')
$pets = [ordered]@{}
foreach ($m in $rows) { if (-not $pets.Contains($m.Groups[1].Value)) { $pets[$m.Groups[1].Value] = $m.Groups[2].Value } }
Write-Host "$($pets.Count) pets in the list"

$catalog = [ordered]@{}
$n = 0
foreach ($id in $pets.Keys) {
    $slug = $pets[$id]
    $n++
    Start-Sleep -Milliseconds $DelayMs
    $html = Get-Page "/en/aion-2/mounts/$slug"
    $name = if ($html -match '<title>(.+?) Mount') { [Net.WebUtility]::HtmlDecode($Matches[1]) } else { $slug }
    $genus = if ($html -match ' is an? (\w+) mount in AION 2') { $Matches[1] } else { $null }
    $unlock = if ($html -match 'You need (\d+) souls? to unlock') { [int]$Matches[1] } else { $null }
    $npcs = [regex]::Matches($html, 'drop-(\d{7})') | ForEach-Object { [int]$_.Groups[1].Value } | Sort-Object -Unique
    $catalog[$id] = [ordered]@{ name = $name; slug = $slug; genus = $genus; unlockSouls = $unlock; npcs = @($npcs) }
    Write-Host ("[{0,3}/{1}] {2} {3,-28} {4,-8} npcs={5}" -f $n, $pets.Count, $id, $name, $genus, @($npcs).Count)
}

$doc = [ordered]@{
    _source = 'MetaBot.GG AION 2 mounts, https://metabot.gg/en/aion-2/mounts'
    _built = (Get-Date -Format 'yyyy-MM-dd')
    pets = $catalog
}
[IO.File]::WriteAllText([IO.Path]::GetFullPath($out), ($doc | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding $false))
Write-Host "Wrote $out"
