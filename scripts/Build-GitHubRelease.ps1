param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('x86', 'x64', 'ARM64')]
    [string]$Platform,

    [Parameter(Mandatory = $true)]
    [string]$Version
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\PowerManager.App\PowerManager.App.csproj'
$runtimeIdentifier = switch ($Platform) {
    'x86' { 'win-x86' }
    'x64' { 'win-x64' }
    'ARM64' { 'win-arm64' }
}

$safeVersion = ($Version -replace '^v', '') -replace '[^0-9A-Za-z.-]', '-'
if ([string]::IsNullOrWhiteSpace($safeVersion)) {
    throw 'Version must contain at least one letter or number.'
}

$publishRoot = Join-Path $repoRoot "artifacts\portable\$runtimeIdentifier"
$releaseRoot = Join-Path $repoRoot 'artifacts\release'
$archivePath = Join-Path $releaseRoot "PowerPlanManager-$safeVersion-$runtimeIdentifier.zip"

$resolvedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
$resolvedPublishRoot = [System.IO.Path]::GetFullPath($publishRoot)
if (-not $resolvedPublishRoot.StartsWith($resolvedArtifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Publish directory is outside the repository artifacts directory: $resolvedPublishRoot"
}

if (Test-Path -LiteralPath $resolvedPublishRoot) {
    Remove-Item -LiteralPath $resolvedPublishRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

dotnet publish $projectPath `
    -c Release `
    -p:Platform=$Platform `
    -r $runtimeIdentifier `
    --self-contained true `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -p:Version=$safeVersion `
    -o $publishRoot
if ($LASTEXITCODE -ne 0) {
    throw "Portable publish failed for $Platform."
}

Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $archivePath -Force
Write-Output "Portable release: $archivePath"
