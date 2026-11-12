#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-DotNet {
    & dotnet @args | Out-Host

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE."
    }
}

$root = $PSScriptRoot
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()

if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Invalid release version: '$version'."
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $attempt = [Guid]::NewGuid().ToString('N')
    $OutputDirectory = "artifacts/packages/v$version/$attempt"
}

$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory, $root)

if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Output directory already exists: $OutputDirectory"
}

$libraries = @(
    'PingTrace.Core'
    'PingTrace.Runtime'
    'PingTrace.AspNetCore'
    'PingTrace.Adapter.HttpClient'
    'PingTrace.Adapter.EntityFramework'
    'PingTrace.Adapter.MicrosoftGraph'
    'PingTrace.Adapter.ActiveDirectory'
    'PingTrace.Adapter.RedisCache'
)

$testProjects = @(
    Get-ChildItem (Join-Path $root 'tests') -Recurse -Filter '*.csproj' |
        Sort-Object FullName
)

if ($testProjects.Count -eq 0) {
    throw 'No library test projects were found.'
}

Push-Location $root

try {
    $commit = & git rev-parse HEAD

    if ($LASTEXITCODE -ne 0) {
        throw 'Cannot determine the source commit.'
    }

    $properties = @(
        '-p:ContinuousIntegrationBuild=true'
        "-p:Version=$version"
        "-p:RepositoryCommit=$commit"
    )

    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null

    foreach ($library in $libraries) {
        Write-Host "`nBuilding $library"

        $project = Join-Path $root "src/$library/$library.csproj"

        Invoke-DotNet build $project `
            --configuration Release `
            --no-incremental `
            @properties
    }

    foreach ($project in $testProjects) {
        Write-Host "`nTesting $($project.BaseName)"

        Invoke-DotNet test `
            --project $project.FullName `
            --configuration Release `
            @properties `
            -- --minimum-expected-tests 1
    }


    foreach ($library in $libraries) {
        Write-Host "`nPacking $library"

        $project = Join-Path $root "src/$library/$library.csproj"

        Invoke-DotNet pack $project `
            --configuration Release `
            --no-build `
            --no-restore `
            --output $OutputDirectory `
            @properties
    }

    # The success stream contains only this path.
    # Callers can capture it while build/test output remains visible.
    Write-Output $OutputDirectory
}
finally {
    Pop-Location
}
