# dotnet-media

An early, unpublished .NET 8 media library. `DotnetMedia.Core` holds storage contracts, `DotnetMedia.Storage.Local` implements private disk storage, and `DotnetMedia.Imaging` validates and processes raster uploads. Public names and eventual NuGet IDs are **provisional** pending consumer review.

## Current use

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
}, cancellationToken);
// result.Original and result.Variants contain keys, types, dimensions and byte lengths.
```

`ImageProcessor` ignores uploaded filenames and Content-Type headers. It accepts JPEG, PNG and WebP only, rejects SVG and animation, applies byte/dimension/pixel limits, corrects orientation, and cleans up earlier outputs if a later output fails. Reencoding strips metadata; preserving original bytes retains metadata. `SaveAsync` remains a lower-level storage API and does not itself validate image bytes.

For deployment, set the storage root to a private absolute directory outside the app and web root. Deny execution and direct web serving at the filesystem and server level; restrict permissions to the service identity. The constructor rejects paths inside `AppContext.BaseDirectory`, but it cannot enforce OS access rules or detect every deployment layout. User supplied filenames never become storage keys. Treat keys as opaque identifiers and authorize read/delete operations in the application.

Antimalware scanning is outside this package's first implementation. A future consumer can scan the bounded input before invoking the image pipeline and before any public publication. No scanner adapter or stub is included.

See [design](docs/design.md) and [image engine decision](docs/image-engine.md). Run `dotnet test DotnetMedia.slnx` to verify this slice.
