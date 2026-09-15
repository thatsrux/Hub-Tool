param(
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')][string]$Version = '0.3.3',
    [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64',
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$hubRoot = Split-Path $PSScriptRoot -Parent
$hubArtifacts = Join-Path $hubRoot 'artifacts'
New-Item -ItemType Directory -Path $hubArtifacts -Force | Out-Null
foreach ($hubFlavor in @('portable', 'compact')) {
    $hubOutput = Join-Path $hubArtifacts "$Runtime-$hubFlavor-$Version"
    if (Test-Path -LiteralPath $hubOutput) {
        $hubResolvedOutput = [System.IO.Path]::GetFullPath($hubOutput)
        $hubResolvedArtifacts = [System.IO.Path]::GetFullPath($hubArtifacts) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $hubResolvedOutput.StartsWith($hubResolvedArtifacts, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Output must stay within artifacts' }
        Remove-Item -LiteralPath $hubResolvedOutput -Recurse -Force
    }
    $hubStandalone = if ($hubFlavor -eq 'portable') { 'true' } else { 'false' }
    $hubBuildRoot = Join-Path $hubRoot ".tools/package-build/$Version/$Runtime/$hubFlavor/"
    & $Dotnet publish (Join-Path $hubRoot 'src/HubTool/HubTool.csproj') -c Release -r $Runtime --self-contained $hubStandalone -p:BaseOutputPath=$hubBuildRoot -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=$hubStandalone -p:DebugType=None -p:Version=$Version -o $hubOutput
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item (Join-Path $hubRoot 'README.md'),(Join-Path $hubRoot 'LICENSE'),(Join-Path $hubRoot 'THIRD-PARTY-NOTICES.md') -Destination $hubOutput
    Copy-Item (Join-Path $hubRoot 'licenses') -Destination $hubOutput -Recurse -Force
    Copy-Item (Join-Path $hubRoot 'docs') -Destination $hubOutput -Recurse -Force
    $hubZip = Join-Path $hubArtifacts "HubTool-$Version-$Runtime-$hubFlavor.zip"
    Compress-Archive -Path "$hubOutput/*" -DestinationPath $hubZip -Force
    if ($hubFlavor -eq 'portable') {
        Copy-Item (Join-Path $hubOutput 'HubTool.exe') (Join-Path $hubArtifacts "HubTool-$Version-$Runtime.exe") -Force
        Copy-Item (Join-Path $hubOutput 'HubTool.exe') (Join-Path $hubRoot 'HubTool.exe') -Force
    } else {
        Copy-Item (Join-Path $hubOutput 'HubTool.exe') (Join-Path $hubArtifacts "HubTool-$Version-$Runtime-compact.exe") -Force
    }
}
$hubFiles = Get-ChildItem -LiteralPath $hubArtifacts -File | Where-Object { $_.Name -like "HubTool-$Version-$Runtime*" -and $_.Extension -in '.zip','.exe' }
$hubChecksums = $hubFiles | Sort-Object Name | ForEach-Object { "{0}  {1}" -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
$hubChecksums | Set-Content (Join-Path $hubArtifacts "SHA256SUMS-$Runtime.txt") -Encoding utf8
