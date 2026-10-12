# Builds the downloads:
#   dist\AION2PetTracker.exe                     - the program: one self-contained file, game data inside, no .NET needed
#   dist\AION2PetTracker-v<version>-win-x64.zip  - the same exe with LICENSE and INSTALL.txt (what the in-app updater fetches)
#   dist\AION2PetTracker-v<version>-source.zip   - the source code (GPL-3.0: hand it out alongside the program)
# Packet recordings (captures\) and build output never go into any of them.
param([string]$Version = "")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

# Zip a folder with "/" in entry names (Compress-Archive in Windows PowerShell writes "\", which some unzippers mangle).
function New-Zip([string]$folder, [string]$zipPath) {
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem $folder -Recurse -File | ForEach-Object {
            $entry = $_.FullName.Substring($folder.TrimEnd('\').Length + 1).Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $zip.Dispose()
    }
}

$project = Join-Path $root "src\AionMeter.App\AionMeter.App.csproj"
if (-not $Version) {
    $Version = (Select-Xml -Path $project -XPath "//Version").Node.InnerText | Select-Object -First 1
}
$dist = Join-Path $root "dist"
$name = "AION2PetTracker-v$Version-win-x64"
$stage = Join-Path $dist $name
New-Item -ItemType Directory -Force $dist | Out-Null
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

# One self-contained exe; the data folder rides inside and is unpacked next to the runtime on first start.
# No symbols; .NET's own messages only in English and Russian (%3B is MSBuild's escaped ";").
dotnet publish $project -c Release -r win-x64 --self-contained true -o $stage `
    -p:DebugType=none -p:DebugSymbols=false -p:SatelliteResourceLanguages=en%3Bru `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$exe = Join-Path $stage "AION2PetTracker.exe"
if (-not (Test-Path $exe)) { throw "publish produced no AION2PetTracker.exe" }
$extra = Get-ChildItem $stage | Where-Object { $_.Name -ne "AION2PetTracker.exe" }
if ($extra) { throw "publish left files next to the exe: $($extra.Name -join ', ')" }

Copy-Item (Join-Path $root "LICENSE"), (Join-Path $root "INSTALL.txt") $stage
$programZip = Join-Path $dist "$name.zip"
New-Zip $stage $programZip
$standalone = Join-Path $dist "AION2PetTracker.exe"
Copy-Item $exe $standalone -Force
$outputs = @($standalone, $programZip)

# Source: everything tracked by git (no recordings, build output or local tooling).
$sourceStage = Join-Path $dist "AION2PetTracker-v$Version-source"
if (Test-Path $sourceStage) { Remove-Item $sourceStage -Recurse -Force }
git -C $root ls-files | ForEach-Object {
    $target = Join-Path $sourceStage $_
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item (Join-Path $root $_) $target
}
$sourceZip = "$sourceStage.zip"
New-Zip $sourceStage $sourceZip
Remove-Item $sourceStage -Recurse -Force
$outputs += $sourceZip

Get-Item $outputs | Select-Object Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
