# Builds the downloads into dist\:
#   AION2PetTracker-Setup-v<version>.exe       - the installer (needs Inno Setup 6.3+): the main download
#   AION2PetTracker.exe                        - the program as one portable file, game data inside, no .NET needed
#   AION2PetTracker-v<version>-win-x64.zip     - the portable exe with its licence
#   AION2PetTracker-v<version>-source.zip      - the source code (GPL-3.0: handed out alongside the program)
# -Stage runs one step, so CI can sign the exe before it goes into the installer and sign the installer after:
#   exe | installer | archives | all (default)
param([string]$Version = "", [ValidateSet("exe", "installer", "archives", "all")] [string]$Stage = "all")

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

# Inno Setup's compiler: on PATH or in its per-user / per-machine install folder.
function Find-Iscc {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($v in "7", "6") {
        foreach ($base in "$env:LOCALAPPDATA\Programs", $env:ProgramFiles, ${env:ProgramFiles(x86)}) {
            $path = Join-Path $base "Inno Setup $v\ISCC.exe"
            if ($base -and (Test-Path $path)) { return $path }
        }
    }
}

$project = Join-Path $root "src\AionMeter.App\AionMeter.App.csproj"
if (-not $Version) {
    $Version = (Select-Xml -Path $project -XPath "//Version").Node.InnerText | Select-Object -First 1
}
$dist = Join-Path $root "dist"
$exe = Join-Path $dist "AION2PetTracker.exe"
New-Item -ItemType Directory -Force $dist | Out-Null
$outputs = @()

if ($Stage -in "exe", "all") {
    $publishDir = Join-Path $dist ".publish"
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    # One self-contained exe. All managed code is loaded from inside it (covered by its signature); only Microsoft's
    # signed native WPF libraries are unpacked. The game data is embedded as resources (see DataFiles.cs).
    # No symbols; .NET's own messages only in English and Russian (%3B is MSBuild's escaped ";").
    dotnet publish $project -c Release -r win-x64 --self-contained true -o $publishDir `
        -p:DebugType=none -p:DebugSymbols=false -p:SatelliteResourceLanguages=en%3Bru `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
    $extra = Get-ChildItem $publishDir | Where-Object { $_.Name -ne "AION2PetTracker.exe" }
    if ($extra) { throw "publish left files next to the exe: $($extra.Name -join ', ')" }
    Copy-Item (Join-Path $publishDir "AION2PetTracker.exe") $exe -Force
    Remove-Item $publishDir -Recurse -Force
    $outputs += $exe
}

if ($Stage -in "installer", "all") {
    if (-not (Test-Path $exe)) { throw "Build the exe first (-Stage exe)" }
    $iscc = Find-Iscc
    if ($iscc) {
        & $iscc /Qp "/DAppVer=$Version" "/DAppExePath=$exe" "/O$dist" (Join-Path $root "installer\AION2PetTracker.iss")
        if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
        $outputs += Join-Path $dist "AION2PetTracker-Setup-v$Version.exe"
    }
    elseif ($Stage -eq "installer") { throw "Inno Setup not found (https://jrsoftware.org/isdl.php)" }
    else { Write-Warning "Inno Setup not found (https://jrsoftware.org/isdl.php): no installer this time." }
}

if ($Stage -in "archives", "all") {
    if (-not (Test-Path $exe)) { throw "Build the exe first (-Stage exe)" }
    $name = "AION2PetTracker-v$Version-win-x64"
    $portable = Join-Path $dist $name
    if (Test-Path $portable) { Remove-Item $portable -Recurse -Force }
    New-Item -ItemType Directory $portable | Out-Null
    Copy-Item $exe, (Join-Path $root "LICENSE") $portable
    New-Zip $portable (Join-Path $dist "$name.zip")
    Remove-Item $portable -Recurse -Force
    $outputs += Join-Path $dist "$name.zip"

    # Source: everything tracked by git (no recordings, build output or local tooling).
    $source = Join-Path $dist "AION2PetTracker-v$Version-source"
    if (Test-Path $source) { Remove-Item $source -Recurse -Force }
    git -C $root ls-files | ForEach-Object {
        $target = Join-Path $source $_
        New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
        Copy-Item (Join-Path $root $_) $target
    }
    New-Zip $source "$source.zip"
    Remove-Item $source -Recurse -Force
    $outputs += "$source.zip"
}

Get-Item $outputs | Select-Object Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
