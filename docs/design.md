# Design and delivery plan

## Boundaries

- `DotnetMedia.Core` (provisional) owns storage contracts and storage-independent models. `DotnetMedia.Storage.Local` owns the local implementation. `DotnetMedia.Imaging` owns validation, decode, orientation, original processing and named variants.
- Commerce owns the gallery: its ten-image cap, order, primary image selection, admin authorization, and association with products. The media library receives one file and settings, returning independent objects and metadata.
- An ASP.NET Core example will stream a raw multipart upload into the service. It will require no browser-side crop or compression.
- A future S3-compatible adapter can implement `IMediaStore`; it is not part of this slice.

## Storage request metadata

`IMediaStore.SaveAsync` takes a required `contentType` alongside the bytes. The image pipeline will determine it from validated, encoded output. A future S3-compatible adapter needs it when writing an object so reads can carry the correct HTTP Content-Type; keeping it only in a gallery database would force the adapter or serving layer to recover it separately. The local store ignores this value because its files are private and have no HTTP headers. Width, height, variant name, encoder settings and product association belong in image results or the consumer's metadata, not the storage request. No general metadata bag is added without a concrete use case.

If temporary-file cleanup itself fails after an upload error, the local store preserves the upload error. Operations should monitor and remove abandoned `.tmp` files in a private storage directory; a reliable cleanup-failure test needs a deterministic filesystem fault mechanism and is not included in this slice.

## Proposed upload settings and flow

Current settings: maximum input bytes, width, height and pixels; a configurable decoded input subset of JPEG, PNG and WebP; an original policy (`PreserveIfPossible` or `Reencode`); and named variants with width, height, contain/cover resize, output format and quality. The policy does not use a file-size threshold. Names and package IDs remain provisional until actual consumer review.

The pipeline will count bytes while accepting the upload, identify and decode actual content, reject disallowed formats and limits, normalize EXIF orientation, then apply the original policy and encode each variant. It will write only validated outputs, return each key, width, height, format and byte count, and delete already written outputs if a later write fails. Cancellation passes through every stage. A caller can then record product-gallery metadata in its own transaction; if that transaction fails, it should delete the returned media objects.

Animated images and SVG are rejected in the initial pipeline. This includes animated WebP; no silent first-frame flattening. An explicit animation policy would require a later design review. SVG is not a raster photo and will not be decoded through this pipeline.

The upload is read into bounded memory, and the byte limit is checked during each read. Signature and animation checks precede Magick.NET header inspection; width, height and pixel count are checked before full decode. Native ImageMagick limits are process-wide: the processor sets memory to 256 MiB, disk pixel cache to zero, list length to 16, thread count to two and maximum width/height to 10,000. Per-request limits are stricter when configured. These process-wide settings can affect other Magick.NET users in the same host; this needs API/hosting review. A separate host process and OS memory/CPU limits are recommended for untrusted high-volume uploads. Cancellation is checked during input reads and between synchronous native stages; it cannot interrupt a native decode or encode already in progress.

Reencoded outputs have profiles and metadata stripped after auto-orientation. `PreserveIfPossible` retains original bytes and their metadata when the orientation and size already comply, so choose it only when that retention is acceptable. Invalid input is reported with an `ImageProcessingException` category; storage and native runtime errors propagate. Cleanup runs with an uncancelled token and preserves the original error if deletion fails. A storage adapter must avoid publishing a partial object when `SaveAsync` throws, since the processor has no key to clean up in that case.

## Review checkpoints

1. This branch: core contract, working local store, tests and decision record.
2. This branch: image validation and processing with named variants, cleanup tests and XML docs.
3. ASP.NET Core sample, end-to-end failure cases and deployment guidance.

Before a package ID or public API is finalized, review names against actual Commerce consumption. Do not publish to NuGet until that consumption and review are complete.
