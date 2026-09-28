# Design and delivery plan

## Boundaries

- `DotnetMedia.Core` (provisional) owns storage contracts and storage-independent models. `DotnetMedia.Storage.Local` owns the local implementation. `DotnetMedia.Imaging` owns validation, decode, orientation, original processing and named variants.
- Commerce owns the gallery: its ten-image cap, order, primary image selection, admin authorization, and association with products. The media library receives one file and settings, returning independent objects and metadata.
- The ASP.NET Core sample accepts a raw multipart upload and passes its stream to the image service. It requires no browser-side crop or compression.
- A future S3-compatible adapter can implement `IMediaStore`; it is not part of this slice.

## Storage request metadata

`IMediaStore.SaveAsync` also accepts an optional per-operation `keyPrefix`. `ImageProcessor.ProcessAsync` forwards the same prefix to every output. This is a portable logical namespace (`products`, `sliders`, `shops/42/products`), not a physical directory contract; a future object store can use it as an object-key prefix. Null or empty selects the root. `MediaKeyPrefix.Validate` rejects empty segments, absolute paths, dot segments, backslashes, characters outside the narrow ASCII segment grammar, and Windows device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`) case-insensitively in every segment on every OS. Similar ordinary names such as `CONtent` and `COM10` remain valid. Local storage returns `prefix/<generated-guid>`; the complete returned key is required for read/delete. The caller stores keys and output metadata in its own persistence model. No gallery or database type belongs in this package.

The local adapter checks existing root/prefix directories and object paths for symbolic links or reparse points before operations. This is a best-effort check, **not** protection against a concurrent actor swapping a directory or file between the check and the filesystem call. Parent directories above the configured root are also assumed trusted. Deploy with a dedicated private root whose directory tree cannot be modified by untrusted users, restrictive service-account permissions, no direct web serving and OS/server rules that deny executing stored files. The constructor's outside-app-directory check does not configure those permissions.

`IMediaStore.SaveAsync` takes a required `contentType` alongside the bytes. The image pipeline will determine it from validated, encoded output. A future S3-compatible adapter needs it when writing an object so reads can carry the correct HTTP Content-Type; keeping it only in a gallery database would force the adapter or serving layer to recover it separately. The local store ignores this value because its files are private and have no HTTP headers. Width, height, variant name, encoder settings and product association belong in image results or the consumer's metadata, not the storage request. No general metadata bag is added without a concrete use case.

If temporary-file cleanup itself fails after an upload error, the local store preserves the upload error. Operations should monitor and remove abandoned `.tmp` files in a private storage directory; a reliable cleanup-failure test needs a deterministic filesystem fault mechanism and is not included in this slice.

## Proposed upload settings and flow

Current settings: maximum input bytes, width, height and pixels; a configurable decoded input subset of JPEG, PNG and WebP; an original policy (`PreserveIfPossible` or `Reencode`); and named variants with width, height, contain/cover resize, output format and quality. The policy does not use a file-size threshold. Names and package IDs remain provisional until actual consumer review.

The pipeline will count bytes while accepting the upload, identify and decode actual content, reject disallowed formats and limits, normalize EXIF orientation, then apply the original policy and encode each variant. It will write only validated outputs, return each key, width, height, format and byte count, and delete already written outputs if a later write fails. Cancellation passes through every stage. A caller can then record product-gallery metadata in its own transaction; if that transaction fails, it should delete the returned media objects.

Animated images and SVG are rejected in the initial pipeline. This includes animated WebP; no silent first-frame flattening. An explicit animation policy would require a later design review. SVG is not a raster photo and will not be decoded through this pipeline.

The upload is read into bounded memory, and the byte limit is checked during each read. Signature and animation checks precede Magick.NET header inspection; width, height and pixel count are checked before full decode. The processor does not change ImageMagick's process-wide `ResourceLimits`. The host must set native limits explicitly at startup, before processing images or sharing Magick.NET with other components. For example, an upload-only host can set:

```csharp
using ImageMagick;

ResourceLimits.Memory = 256UL * 1024 * 1024;
ResourceLimits.Disk = 512UL * 1024 * 1024;
ResourceLimits.ListLength = 16;
ResourceLimits.Thread = 2;
ResourceLimits.Width = 10_000;
ResourceLimits.Height = 10_000;
```

These values are deployment examples, not library defaults. The host owns their sizing for concurrency and other Magick.NET users, protects the native temporary directory, and should use OS/container memory, disk and CPU limits for untrusted high-volume uploads. Per-request byte, dimension and pixel limits remain enforced in the processor. Cancellation is checked during input reads and between synchronous native stages; it cannot interrupt a native decode or encode already in progress.

Reencoded outputs have profiles and metadata stripped after auto-orientation. `PreserveIfPossible` retains original bytes and their metadata only when input format equals `OriginalFormat`, orientation needs no correction and size complies. Any mismatch forces reencoding to `OriginalFormat` and strips metadata. Choose preservation only when metadata retention is acceptable. Invalid input is reported with an `ImageProcessingException` category; storage and native runtime errors propagate. Cleanup runs with an uncancelled token and preserves the original error if deletion fails. A storage adapter must avoid publishing a partial object when `SaveAsync` throws, since the processor has no key to clean up in that case.

## Review checkpoints

1. This branch: core contract, working local store, tests and decision record.
2. This branch: image validation and processing with named variants, cleanup tests and XML docs.
3. This branch: ASP.NET Core sample, HTTP failure cases and deployment guidance.

Before a package ID or public API is finalized, review names against actual Commerce consumption. Do not publish to NuGet until that consumption and review are complete.
