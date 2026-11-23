param(
    [string] $ExpectedVersion = '0.1.0-preview.1'
)

$ErrorActionPreference = 'Stop'

$libraryProject = Join-Path $PSScriptRoot '../TasollerPlus/TasollerPlus.csproj'
$tempRoot = (Get-Item -LiteralPath ([System.IO.Path]::GetTempPath())).FullName
$testDirectory = (New-Item -ItemType Directory -Path (Join-Path $tempRoot "TasollerPlus-PackageTest-$([Guid]::NewGuid().ToString('N'))")).FullName

try {
    $feed = Join-Path $testDirectory 'feed'
    $packages = Join-Path $testDirectory 'packages'
    $consumerProject = Join-Path $testDirectory 'PackageConsumer.csproj'
    $version = [System.Security.SecurityElement]::Escape($ExpectedVersion)

    dotnet pack $libraryProject --configuration Release --output $feed --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Packing TasollerPlus failed.' }

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="TasollerPlus" Version="[$version]" />
  </ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $consumerProject -Encoding UTF8

    @'
using System;
using TasollerPlus;

using var device = new TasollerPlusDevice();
if (device.Status != TasollerPlusConnectionStatus.Disconnected || device.TryGetLatestInput(out _))
{
    throw new InvalidOperationException("A new TasollerPlusDevice must start disconnected with no input.");
}
'@ | Set-Content -LiteralPath (Join-Path $testDirectory 'Program.cs') -Encoding UTF8

    dotnet restore $consumerProject --source $feed --packages $packages --nologo
    if ($LASTEXITCODE -ne 0) { throw "Restoring TasollerPlus $ExpectedVersion from the local package failed." }

    dotnet run --project $consumerProject --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Running the package consumer failed.' }

    $restoredPackage = Get-ChildItem -LiteralPath (Join-Path $packages 'tasollerplus') -Directory
    $libraryAssembly = Join-Path $restoredPackage.FullName 'lib/netstandard2.1/TasollerPlus.dll'
    if (!(Test-Path -LiteralPath $libraryAssembly -PathType Leaf)) {
        throw 'The package is missing lib/netstandard2.1/TasollerPlus.dll.'
    }

    $manifest = [xml](Get-Content -LiteralPath (Join-Path $restoredPackage.FullName 'tasollerplus.nuspec') -Raw)
    $license = $manifest.package.metadata.license
    if ($license.type -ne 'expression' -or $license.InnerText -ne 'MIT') {
        throw 'The package license expression must be MIT.'
    }

    Write-Output "Verified TasollerPlus $ExpectedVersion with a local .NET 10 consumer."
}
finally {
    $resolvedTestDirectory = (Resolve-Path -LiteralPath $testDirectory).ProviderPath
    if ($resolvedTestDirectory -eq $testDirectory -and (Split-Path -Parent $resolvedTestDirectory) -eq $tempRoot) {
        Remove-Item -LiteralPath $resolvedTestDirectory -Recurse -Force
    }
}
