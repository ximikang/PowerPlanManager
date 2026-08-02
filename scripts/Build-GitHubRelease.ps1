param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('x86', 'x64', 'ARM64')]
    [string]$Platform,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [ValidateSet('Portable', 'Minimal')]
    [string]$Variant = 'Portable'
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

$variantName = $Variant.ToLowerInvariant()
$publishRoot = Join-Path $repoRoot "artifacts\portable\$variantName\$runtimeIdentifier"
$releaseRoot = Join-Path $repoRoot 'artifacts\release'
$archivePath = Join-Path $releaseRoot "PowerPlanManager-$safeVersion-$runtimeIdentifier-$variantName.zip"

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

$isSelfContained = $Variant -eq 'Portable'
dotnet publish $projectPath `
    -c Release `
    -p:Platform=$Platform `
    -r $runtimeIdentifier `
    --self-contained $isSelfContained `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=$isSelfContained `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -p:Version=$safeVersion `
    -o $publishRoot
if ($LASTEXITCODE -ne 0) {
    throw "$Variant publish failed for $Platform."
}

$keptLanguages = @('en-us', 'zh-cn')
$languageDirectoryPattern = '^[a-z]{2,3}(?:-[a-z0-9]+){1,2}$'
Get-ChildItem -LiteralPath $publishRoot -Directory | Where-Object {
    $_.Name -match $languageDirectoryPattern -and $keptLanguages -notcontains $_.Name.ToLowerInvariant()
} | ForEach-Object {
    Remove-Item -LiteralPath $_.FullName -Recurse -Force
}

if ($Variant -eq 'Minimal') {
    $requirementsPath = Join-Path $repoRoot 'docs\release\MINIMAL-REQUIREMENTS.txt'
    Copy-Item -LiteralPath $requirementsPath -Destination (Join-Path $publishRoot 'MINIMAL-REQUIREMENTS.txt')
}

Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $archivePath -Force
Write-Output "$Variant release: $archivePath"
