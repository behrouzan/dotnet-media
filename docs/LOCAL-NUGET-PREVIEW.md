# Local NuGet preview

These package IDs and public API names are provisional. This preview is for local development and testing; it is not published to nuget.org.

From the repository root in PowerShell, build a feed **outside the repository**:

```powershell
$feed = Join-Path $env:LOCALAPPDATA 'dotnet-media-preview-feed'
$version = '0.1.0-preview.1'
New-Item -ItemType Directory -Force -Path $feed | Out-Null
if (Get-ChildItem -LiteralPath $feed -Filter "*.$version.nupkg") {
    throw "Version $version already exists in the feed. Increase the version; do not overwrite packages."
}
dotnet restore DotnetMedia.slnx
dotnet build DotnetMedia.slnx --configuration Release --no-restore
dotnet test DotnetMedia.slnx --configuration Release --no-build --no-restore
dotnet pack src/DotnetMedia.Core/DotnetMedia.Core.csproj --configuration Release --no-build --no-restore --output $feed
dotnet pack src/DotnetMedia.Storage.Local/DotnetMedia.Storage.Local.csproj --configuration Release --no-build --no-restore --output $feed
dotnet pack src/DotnetMedia.Imaging/DotnetMedia.Imaging.csproj --configuration Release --no-build --no-restore --output $feed
Get-ChildItem -LiteralPath $feed -Filter "*.$version.nupkg"
```

The pack command produces exactly `DotnetMedia.Core`, `DotnetMedia.Storage.Local`, and `DotnetMedia.Imaging`. The sample and test projects are marked non-packable. Package files are also ignored by Git if one is accidentally created under the checkout. Keep one immutable set of package bytes per version; a rebuild with changed content requires increasing `Version` in `src/Directory.Build.props` (for example, `0.1.0-preview.2`) and building into a fresh feed. Do not replace an existing version: NuGet caches packages by ID and version.

An independent .NET 8 consumer can reference only the required packages:

```powershell
$consumer = Join-Path $env:TEMP 'dotnet-media-preview-consumer'
New-Item -ItemType Directory -Force -Path $consumer | Out-Null
dotnet new console --framework net8.0 --name DotnetMediaPreviewConsumer --output $consumer
$project = Join-Path $consumer 'DotnetMediaPreviewConsumer.csproj'
dotnet add $project package DotnetMedia.Imaging --version $version --no-restore
dotnet add $project package DotnetMedia.Storage.Local --version $version --no-restore
$feedXml = [System.Security.SecurityElement]::Escape($feed)
$config = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear />
  <add key="dotnet-media-preview" value="$feedXml" />
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
</packageSources></configuration>
"@
Set-Content -LiteralPath (Join-Path $consumer 'NuGet.Config') -Value $config
dotnet restore $project --configfile (Join-Path $consumer 'NuGet.Config')
dotnet list $project package --include-transitive
```

`DotnetMedia.Core` should arrive transitively from both packages. `DotnetMedia.Imaging` also requires `Magick.NET-Q8-AnyCPU` 14.17.2 and its transitive managed/native dependencies; the official NuGet source in this example supplies those packages. In an offline environment, a separate local feed containing the upstream `.nupkg` files can replace nuget.org. This repo's README and sample API show how to configure processing and private storage. The host must set process-wide ImageMagick resource limits explicitly.

Before any public release, review package IDs and API names with a real consumer, select and record the library's own license, inspect the exact native notices and distribution obligations for Magick.NET and its bundled components, and test supported runtimes. This preview does not establish legal clearance for public distribution.
