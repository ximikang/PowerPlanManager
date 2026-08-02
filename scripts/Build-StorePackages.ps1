param(
    [switch]$StoreUpload
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src\PowerManager.App\PowerManager.App.csproj'
$profiles = @(
    'Properties\PublishProfiles\Store-x86.pubxml',
    'Properties\PublishProfiles\Store-x64.pubxml',
    'Properties\PublishProfiles\Store-ARM64.pubxml'
)

if ($StoreUpload) {
    $vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswherePath)) {
        throw 'Visual Studio Installer was not found. Install Visual Studio Windows application development and MSIX components first.'
    }

    $msbuildPath = & $vswherePath -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
    if (-not $msbuildPath) {
        throw 'Visual Studio MSBuild was not found. Install the Windows application development workload first.'
    }

    foreach ($profile in $profiles) {
        & $msbuildPath $projectPath /restore /t:Publish /p:PublishProfile=$profile /p:GenerateAppxPackageOnBuild=true /verbosity:minimal
        if ($LASTEXITCODE -ne 0) {
            throw "Store package build failed for $profile."
        }
    }
}
else {
    foreach ($profile in $profiles) {
        dotnet clean $projectPath -p:PublishProfile=$profile -p:GenerateAppxPackageOnBuild=true --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            throw "Clean failed for $profile."
        }

        dotnet publish $projectPath `
            -p:PublishProfile=$profile `
            -p:UapAppxPackageBuildMode=SideloadOnly `
            -p:AppxSymbolPackageEnabled=false `
            -p:GenerateAppxPackageOnBuild=true `
            --verbosity minimal
        if ($LASTEXITCODE -ne 0) {
            throw "MSIX build failed for $profile."
        }
    }
}

Write-Output "Packages: $(Join-Path $repoRoot 'src\PowerManager.App\AppPackages')"
