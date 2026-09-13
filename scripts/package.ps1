param(
    [string]$Version = '0.2.0',
    [string]$Runtime = 'win-x64',
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$hubRoot = Split-Path $PSScriptRoot -Parent
$hubArtifacts = Join-Path $hubRoot 'artifacts'
New-Item -ItemType Directory -Path $hubArtifacts -Force | Out-Null
foreach ($hubFlavor in @('portable', 'compact')) {
    $hubOutput = Join-Path $hubArtifacts "$Runtime-$hubFlavor-$Version"
    $hubStandalone = if ($hubFlavor -eq 'portable') { 'true' } else { 'false' }
    & $Dotnet publish (Join-Path $hubRoot 'HubTool/HubTool.csproj') -c Release -r $Runtime --self-contained $hubStandalone -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:Version=$Version -o $hubOutput
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item (Join-Path $hubRoot 'README.md'),(Join-Path $hubRoot 'LICENSE'),(Join-Path $hubRoot 'THIRD-PARTY-NOTICES.md') -Destination $hubOutput
    Copy-Item (Join-Path $hubRoot 'licenses') -Destination $hubOutput -Recurse -Force
    Copy-Item (Join-Path $hubRoot 'docs') -Destination $hubOutput -Recurse -Force
    $hubZip = Join-Path $hubArtifacts "HubTool-$Version-$Runtime-$hubFlavor.zip"
    Compress-Archive -Path "$hubOutput/*" -DestinationPath $hubZip -Force
    if ($hubFlavor -eq 'portable') {
        Copy-Item (Join-Path $hubOutput 'HubTool.exe') (Join-Path $hubArtifacts "HubTool-$Version-$Runtime.exe") -Force
    }
}
$hubFiles = Get-ChildItem -LiteralPath $hubArtifacts -File | Where-Object { $_.Name -like "HubTool-$Version-$Runtime*" -and $_.Extension -in '.zip','.exe' }
$hubChecksums = $hubFiles | Sort-Object Name | ForEach-Object { "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
$hubChecksums | Set-Content (Join-Path $hubArtifacts "SHA256SUMS-$Runtime.txt") -Encoding utf8
