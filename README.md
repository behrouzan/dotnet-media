# dotnet-media

An early, unpublished .NET 8 media library. This branch contains the first buildable slice: an opaque storage contract and a private local disk implementation. Its public names, project names and eventual NuGet ID are **provisional** and need consumer review before publication.

## Current use

```csharp
IMediaStore store = new LocalMediaStore(@"D:\private-media");
await using var input = File.OpenRead("validated-image.jpg");
StoredMedia item = await store.SaveAsync(input, cancellationToken);
await using Stream output = await store.OpenReadAsync(item.Key, cancellationToken);
await store.DeleteAsync(item.Key, cancellationToken);
```

`SaveAsync` stores bytes as supplied. It does **not** validate or process images, so an application must not expose these objects publicly yet. The final upload service will decode and validate input before storage, then generate the original and named variants and clean up all saved objects if any step fails.

For deployment, set the storage root to a private absolute directory outside the app and web root. Deny execution and direct web serving at the filesystem and server level; restrict permissions to the service identity. The constructor rejects paths inside `AppContext.BaseDirectory`, but it cannot enforce OS access rules or detect every deployment layout. User supplied filenames never become storage keys. Treat keys as opaque identifiers and authorize read/delete operations in the application.

Antimalware scanning is outside this package's first implementation. A future consumer can scan the bounded input before invoking the image pipeline and before any public publication. No scanner adapter or stub is included.

See [design](docs/design.md) and [image engine decision](docs/image-engine.md). Run `dotnet test DotnetMedia.slnx` to verify this slice.
