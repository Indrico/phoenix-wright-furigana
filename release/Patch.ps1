param(
    [ValidateSet('Install', 'Uninstall')][string]$Mode = 'Install',
    [string]$GameDirectory = (Split-Path -Parent $PSScriptRoot)
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$staging = $null

function FileHash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function RequireHash([string]$Path, [string]$Expected) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf) -or (FileHash $Path) -ne $Expected) {
        throw "File is missing or differs from the supported build: $Path"
    }
}

try {
    $game = (Resolve-Path -LiteralPath $GameDirectory).Path
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'release-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $target = Join-Path $game 'PWAAT_Data/Managed/Assembly-CSharp.dll'
    $runtime = Join-Path $game 'PWAAT_Data/Managed/PwaatFurigana.dll'
    $readings = Join-Path $game 'PWAAT_Data/StreamingAssets/Furigana/readings.tsv'
    $backup = Join-Path $game 'furigana-mod/backups/Assembly-CSharp.dll'
    if (!(Test-Path -LiteralPath $target -PathType Leaf)) { throw 'Place furigana-mod beside PWAAT.exe, or pass -GameDirectory.' }
    if (Get-Process -Name PWAAT -ErrorAction SilentlyContinue) { throw 'Close the game before installing or uninstalling.' }
    $currentHash = FileHash $target
    if ($currentHash -ne $manifest.original_sha256 -and $currentHash -ne $manifest.patched_sha256) {
        throw 'Unsupported or modified Assembly-CSharp.dll. No files changed. Restore the original supported build first.'
    }

    if ($Mode -eq 'Uninstall') {
        if ($currentHash -eq $manifest.original_sha256) { Write-Host 'Original game is already installed.'; exit 0 }
        RequireHash $backup $manifest.original_sha256
        Copy-Item -LiteralPath $backup -Destination $target -Force
        RequireHash $target $manifest.original_sha256
        if ((Test-Path -LiteralPath $runtime) -and (FileHash $runtime) -eq $manifest.runtime_sha256) { Remove-Item -LiteralPath $runtime }
        if ((Test-Path -LiteralPath $readings) -and (FileHash $readings) -eq $manifest.readings_sha256) { Remove-Item -LiteralPath $readings }
        Write-Host 'Original game restored. Your backup is retained.'
        exit 0
    }

    foreach ($payload in $manifest.payloads) {
        RequireHash (Join-Path $PSScriptRoot $payload.path) $payload.sha256
    }
    foreach ($pair in @(@($runtime, $manifest.runtime_sha256), @($readings, $manifest.readings_sha256))) {
        if (Test-Path -LiteralPath $pair[0]) { RequireHash $pair[0] $pair[1] }
    }
    if (Test-Path -LiteralPath $backup) { RequireHash $backup $manifest.original_sha256 }
    if ($currentHash -eq $manifest.patched_sha256) {
        RequireHash $backup $manifest.original_sha256
        if ((Test-Path -LiteralPath $runtime) -and (Test-Path -LiteralPath $readings)) {
            Write-Host 'Furigana patch is already installed and verified.'; exit 0
        }
        $original = $backup
    } else { $original = $target }

    Write-Host 'Verifying Japanese game assets...'
    $assetFiles = @()
    foreach ($asset in $manifest.sources) {
        if ($asset.path -notmatch '^PWAAT_Data/StreamingAssets/GS[123]/scenario/[A-Za-z0-9_.-]+\.mdt$') { throw 'Invalid asset path in manifest.' }
        $path = Join-Path $game $asset.path
        RequireHash $path $asset.sha256
        $assetFiles += $path
    }
    $staging = Join-Path ([IO.Path]::GetTempPath()) ('pwaat-furigana-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging | Out-Null
    Add-Type -Path (Join-Path $PSScriptRoot 'GamePatch.cs') -IgnoreWarnings -WarningAction SilentlyContinue
    $cecil = Join-Path $PSScriptRoot 'payload/Mono.Cecil.dll'
    [Reflection.Assembly]::LoadFrom($cecil) | Out-Null
    $references = @([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { !$_.IsDynamic -and $_.Location } | ForEach-Object { $_.Location })
    # PowerShell 7 uses the mscorlib facade to consume Cecil's .NET Framework build.
    $facade = Join-Path $PSHOME 'ref/mscorlib.dll'
    if (Test-Path -LiteralPath $facade) { $references += $facade }
    Add-Type -Path (Join-Path $PSScriptRoot 'CecilPatch.cs') -ReferencedAssemblies $references
    $stagedAssembly = Join-Path $staging 'Assembly-CSharp.dll'
    $stagedReadings = Join-Path $staging 'readings.tsv'
    [PwaatCecilPatch]::Apply($original, (Join-Path $PSScriptRoot 'payload/PwaatFurigana.dll'), (Split-Path -Parent $target), $stagedAssembly)
    RequireHash $stagedAssembly $manifest.patched_sha256
    Write-Host 'Rebuilding offline readings from your local game scripts...'
    $count = [PwaatReleasePatch]::RebuildReadings([string[]]$assetFiles, (Join-Path $PSScriptRoot 'payload/readings-hashes.tsv.gz'), $stagedReadings)
    RequireHash $stagedReadings $manifest.readings_sha256

    # All inputs and staged outputs have passed verification before installation.
    New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force | Out-Null
    if (!(Test-Path -LiteralPath $backup)) {
        [IO.File]::WriteAllBytes($backup, [IO.File]::ReadAllBytes($original))
        RequireHash $backup $manifest.original_sha256
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $readings) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'payload/PwaatFurigana.dll') -Destination $runtime -Force
    Copy-Item -LiteralPath $stagedReadings -Destination $readings -Force
    Copy-Item -LiteralPath $stagedAssembly -Destination $target -Force
    RequireHash $target $manifest.patched_sha256
    RequireHash $runtime $manifest.runtime_sha256
    RequireHash $readings $manifest.readings_sha256
    Write-Host "Installed and verified $count Japanese reading entries. In Japanese Options, use Furigana ON/OFF, or press F8 in-game."
} catch {
    Write-Host ('Patch failed: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
} finally {
    if ($staging -and (Test-Path -LiteralPath $staging)) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
