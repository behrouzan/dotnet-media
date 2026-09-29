# dotnet-media

An early, unpublished .NET 8 media library. `DotnetMedia.Core` holds storage contracts, `DotnetMedia.Storage.Local` implements private disk storage, and `DotnetMedia.Imaging` validates and processes raster uploads. Public names and eventual NuGet IDs are **provisional** pending consumer review.

To build and consume the three `0.1.0-preview.1` packages from an unpublished local feed, see [Local NuGet preview](docs/LOCAL-NUGET-PREVIEW.md). Increase the shared version before producing changed package bytes; never replace a feed entry with the same ID and version.

## Current use

`keyPrefix` is optional per operation; omit it or pass `null`/`""` to save at the configured root. It is a logical, slash-separated namespace, not a filesystem path. Segments use ASCII letters, digits, `-` and `_`, starting with a letter or digit. Windows device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`) are rejected in every segment regardless of case, on every OS. The store generates each final filename and returns the complete key (for example `shops/42/products/<generated-id>`); keep that key in the consumer's own records for later reads and deletes. The package has no database, entity or migration.

```csharp
using DotnetMedia.Core;
using DotnetMedia.Storage.Local;
using DotnetMedia.Imaging;

IMediaStore store = new LocalMediaStore(@"D:\private-media");
await using var input = File.OpenRead("untrusted-upload");
var result = await new ImageProcessor(store).ProcessAsync(input, new ImageProcessingOptions
{
    MaxInputBytes = 10 * 1024 * 1024,
    MaxWidth = 6000,
    MaxHeight = 6000,
    MaxPixels = 24_000_000,
    OriginalMode = OriginalMode.Reencode,
    OriginalFormat = ImageFormat.Jpeg,
    OriginalMaxWidth = 1800,
    OriginalMaxHeight = 1800,
    Variants =
    [
        new("card", 640, 640, ResizeMode.Cover, ImageFormat.WebP, 82),
        new("thumb", 240, 240, ResizeMode.Contain, ImageFormat.Jpeg, 80)
    ]
}, cancellationToken, keyPrefix: "shops/42/products");
// result.Original and result.Variants contain complete keys, types, dimensions and byte lengths.
```

`ImageProcessor` ignores uploaded filenames and Content-Type headers. It accepts JPEG, PNG and WebP only, rejects SVG and animation, applies byte/dimension/pixel limits, corrects orientation, and cleans up earlier outputs if a later output fails. `PreserveIfPossible` retains input bytes **and metadata** only if input format matches `OriginalFormat`, orientation needs no correction and original size limits are met. Otherwise it reencodes to `OriginalFormat` and strips metadata. `SaveAsync` remains a lower-level storage API and does not itself validate image bytes.

The host must configure Magick.NET native `ResourceLimits` explicitly at startup. They affect the entire process; constructing `ImageProcessor` does not change them. See the [host configuration example](docs/design.md#proposed-upload-settings-and-flow) and choose native memory/disk/thread ceilings appropriate for the deployment. Per-upload byte, dimension and pixel limits remain in `ImageProcessor`.

For deployment, set the storage root to a private absolute directory outside the app and web root. Deny execution and direct web serving at the filesystem and server level; restrict permissions to the service identity. The constructor rejects paths inside `AppContext.BaseDirectory`, but it cannot enforce OS access rules or detect every deployment layout. User supplied filenames never become storage keys. Treat keys as opaque identifiers and authorize read/delete operations in the application.

Antimalware scanning is outside this package's first implementation. A future consumer can scan the bounded input before invoking the image pipeline and before any public publication. No scanner adapter or stub is included.

See [design](docs/design.md) and [image engine decision](docs/image-engine.md). Run `dotnet test DotnetMedia.slnx` to verify this slice.

The [ASP.NET Core sample](samples/DotnetMedia.Sample.Api/README.md) shows an HTTP multipart upload that produces an original and two named versions using configuration, plus a Development-only read route for local testing.
