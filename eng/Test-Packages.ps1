#requires -Version 7.0
[CmdletBinding()]
param(
  [string] $PackageDirectory,
  [string] $ExpectedCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-DotNet {
    & dotnet @args | Out-Host

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet failed with exit code $LASTEXITCODE."
    }
}

function Read-ZipText {
    param(
        [IO.Compression.ZipArchive] $Archive,
        [string] $Name
    )

    $entry = $Archive.GetEntry($Name)

    if ($null -eq $entry -or $entry.Length -eq 0) {
        throw "Missing or empty package entry: $Name"
    }

    $reader = [IO.StreamReader]::new($entry.Open())

    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}

function Assert-ZipEntry {
    param(
        [IO.Compression.ZipArchive] $Archive,
        [string] $Name
    )

    $entry = $Archive.GetEntry($Name)

    if ($null -eq $entry -or $entry.Length -eq 0) {
        throw "Missing or empty package entry: $Name"
    }
}

$root = Split-Path $PSScriptRoot -Parent
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()

if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $versionDirectory = Join-Path $root "artifacts/packages/v$version"

    if (!(Test-Path -LiteralPath $versionDirectory -PathType Container)) {
        throw "No package builds found for version $version. Run Build.ps1 first."
    }

    $latestDirectory = Get-ChildItem -LiteralPath $versionDirectory -Directory |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -eq $latestDirectory) {
        throw "No package attempts found in $versionDirectory."
    }

    $PackageDirectory = $latestDirectory.FullName
}

if (!(Test-Path -LiteralPath $PackageDirectory -PathType Container)) {
    throw "Package directory does not exist: $PackageDirectory"
}

# Resolve explicit relative paths before changing locations or writing
# the package source into the generated NuGet configuration.
$PackageDirectory = (Resolve-Path -LiteralPath $PackageDirectory).ProviderPath

# Values describe direct dependencies within the PingTrace package family.
# Keep the release package list explicit, but derive its dependency graph.
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

# Match references by resolved project path, rather than filename alone.
$projectsByPath = @{}

foreach ($library in $libraries) {
    $projectPath = (
        Resolve-Path -LiteralPath (
            Join-Path $root "src/$library/$library.csproj"
        )
    ).Path

    $projectsByPath[$projectPath] = $library
}

$dependencies = [ordered]@{}

Push-Location $root

try {
    foreach ($library in $libraries) {
        $projectPath = (
            Resolve-Path -LiteralPath (
                Join-Path $root "src/$library/$library.csproj"
            )
        ).Path

        # Capture stdout directly: Invoke-DotNet sends it to Out-Host.
        $output = @(& dotnet list $projectPath reference)

        if ($LASTEXITCODE -ne 0) {
            throw "Cannot list project references for $library."
        }

        $references = @(
            foreach ($line in $output) {
                $reference = $line.Trim()

                # Ignore headings without depending on their language.
                if ($reference -notmatch '\.csproj$') {
                    continue
                }

                $reference = $reference.Replace(
                    '\',
                    [IO.Path]::DirectorySeparatorChar
                )

                # Relative references are relative to the owning project.
                $referencePath = [IO.Path]::GetFullPath(
                    $reference,
                    (Split-Path $projectPath -Parent)
                )

                $referencePath = (
                    Resolve-Path -LiteralPath $referencePath
                ).Path

                if (!$projectsByPath.ContainsKey($referencePath)) {
                    throw (
                        "$library references a project outside the release " +
                        "package list: $referencePath"
                    )
                }

                $projectsByPath[$referencePath]
            }
        )

        $dependencies[$library] = @(
            $references | Sort-Object -Unique
        )
    }
}
finally {
    Pop-Location
}

$packages = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.nupkg')
$symbols = @(Get-ChildItem -LiteralPath $PackageDirectory -Filter '*.snupkg')

if ($packages.Count -ne $dependencies.Count -or
    $symbols.Count -ne $dependencies.Count) {
    throw "Expected eight .nupkg and eight .snupkg files; found $($packages.Count) and $($symbols.Count)."
}

$releaseUrl = "https://github.com/patware/Patware.PingTrace/releases/tag/v$version"

foreach ($library in $dependencies.Keys) {
    $id = "Patware.$library"
    $packagePath = Join-Path $PackageDirectory "$id.$version.nupkg"
    $symbolPath = Join-Path $PackageDirectory "$id.$version.snupkg"

    if (!(Test-Path -LiteralPath $packagePath -PathType Leaf) -or
        !(Test-Path -LiteralPath $symbolPath -PathType Leaf)) {
        throw "Missing package or symbols for $id $version."
    }

    Write-Host "Inspecting $id"

    $archive = [IO.Compression.ZipFile]::OpenRead($packagePath)

    try {
        $nuspecs = @($archive.Entries | Where-Object FullName -Like '*.nuspec')

        if ($nuspecs.Count -ne 1) {
            throw "$id must contain exactly one nuspec."
        }

        [xml] $nuspec = Read-ZipText $archive $nuspecs[0].FullName
        $metadata = $nuspec.SelectSingleNode(
            "/*[local-name()='package']/*[local-name()='metadata']"
        )

        if ([string]$metadata.id -cne $id -or
            [string]$metadata.version -cne $version) {
            throw "Unexpected package identity in $packagePath."
        }

        if ([string]$metadata.releaseNotes -cne
            "Release and upgrade notes: $releaseUrl") {
            throw "Incorrect release-note link in $id."
        }

        if ([string]$metadata.license.InnerText -ne 'MIT' -or
            [string]$metadata.license.type -ne 'expression') {
            throw "Expected the MIT license expression in $id."
        }

        if ([string]$metadata.repository.url -ne
            'https://github.com/patware/Patware.PingTrace') {
            throw "Incorrect repository URL in $id."
        }

        if ($ExpectedCommit -and
            [string]$metadata.repository.commit -ne $ExpectedCommit) {
            throw "Incorrect source commit in $id."
        }

        if ([string]$metadata.readme -ne 'README.md') {
            throw "Incorrect README metadata in $id."
        }

        $null = Read-ZipText $archive 'README.md'
        Assert-ZipEntry $archive "lib/net10.0/$library.dll"
        Assert-ZipEntry $archive "lib/net10.0/$library.xml"

        $internalDependencies = @(
            $metadata.SelectNodes(".//*[local-name()='dependency']") |
                Where-Object { $_.GetAttribute('id').StartsWith('Patware.PingTrace.') }
        )

        $actualIds = @(
            $internalDependencies |
                ForEach-Object { $_.GetAttribute('id') } |
                Sort-Object -Unique
        )

        $expectedIds = @(
            $dependencies[$library] |
                ForEach-Object { "Patware.$_" } |
                Sort-Object
        )

        if (($actualIds -join '|') -cne ($expectedIds -join '|')) {
            throw "Incorrect internal dependency set in $id."
        }

        foreach ($dependency in $internalDependencies) {
            $range = $dependency.GetAttribute('version') -replace '\s', ''

            # NuGet may serialize a minimum version as either form below.
            # An exact-version range is also acceptable.
            if ($range -notin @($version, "[$version,)", "[$version]")) {
                throw "Incorrect dependency version in ${id}: $($dependency.OuterXml)"
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    $archive = [IO.Compression.ZipFile]::OpenRead($symbolPath)

    try {
        $nuspecs = @($archive.Entries | Where-Object FullName -Like '*.nuspec')

        if ($nuspecs.Count -ne 1) {
            throw "$id symbols must contain exactly one nuspec."
        }

        [xml] $nuspec = Read-ZipText $archive $nuspecs[0].FullName
        $metadata = $nuspec.SelectSingleNode(
            "/*[local-name()='package']/*[local-name()='metadata']"
        )

        if ([string]$metadata.id -cne $id -or
            [string]$metadata.version -cne $version) {
            throw "Unexpected symbol-package identity for $id."
        }

        Assert-ZipEntry $archive "lib/net10.0/$library.pdb"
    }
    finally {
        $archive.Dispose()
    }
}

$smokeDirectory = Join-Path $root (
    'artifacts/smoke/' + [Guid]::NewGuid().ToString('N')
)

New-Item -ItemType Directory -Path $smokeDirectory | Out-Null

# Stop the repository's build properties and targets from flowing into
# the generated consumer. It should behave like an external application.
Set-Content (Join-Path $smokeDirectory 'Directory.Build.props') `
    '<Project />' -Encoding utf8

Set-Content (Join-Path $smokeDirectory 'Directory.Build.targets') `
    '<Project />' -Encoding utf8

$references = foreach ($library in $dependencies.Keys) {
    '    <PackageReference Include="Patware.{0}" Version="[{1}]" />' -f $library, $version
}

$projectText = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
$($references -join "`n")
  </ItemGroup>
</Project>
"@

Set-Content (Join-Path $smokeDirectory 'Smoke.csproj') `
    $projectText -Encoding utf8

$escapedFeed = [Security.SecurityElement]::Escape($PackageDirectory)

$configText = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$escapedFeed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="local">
      <package pattern="Patware.PingTrace.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
  <fallbackPackageFolders>
    <clear />
  </fallbackPackageFolders>
</configuration>
"@

Set-Content (Join-Path $smokeDirectory 'NuGet.Config') `
    $configText -Encoding utf8

$programText = Get-Content (Join-Path $root 'eng/consumer.program.cs') -Raw

$assemblyNames = @($dependencies.Keys) | ConvertTo-Json -Compress
$programText = $programText.Replace('__ASSEMBLIES__', $assemblyNames)

Set-Content (Join-Path $smokeDirectory 'Program.cs') `
    $programText -Encoding utf8

Push-Location $smokeDirectory

try {
    Invoke-DotNet restore 'Smoke.csproj' `
        --configfile 'NuGet.Config' `
        --packages (Join-Path $smokeDirectory 'cache') `
        --no-http-cache

    Invoke-DotNet build 'Smoke.csproj' `
        --configuration Release `
        --no-restore

    Invoke-DotNet run `
        --project 'Smoke.csproj' `
        --configuration Release `
        --no-build `
        --no-restore
}
finally {
    Pop-Location
}

[pscustomobject]@{
    Version          = $version
    PackageDirectory = $PackageDirectory
    PackageCount     = $packages.Count
    SymbolCount      = $symbols.Count
    SmokeDirectory   = $smokeDirectory
}
