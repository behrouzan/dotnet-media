namespace DotnetMedia.Core;

/// <summary>Stores media in a private local directory, publishing each object only after a complete write.</summary>
public sealed class LocalMediaStore : IMediaStore
{
    private readonly string root;

    /// <summary>Creates a local store. The root must be an absolute path outside the application directory.</summary>
    public LocalMediaStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (!Path.IsPathFullyQualified(rootDirectory))
            throw new ArgumentException("The storage root must be absolute.", nameof(rootDirectory));

        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        var app = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        if (root.Equals(app, StringComparison.OrdinalIgnoreCase) ||
            root.StartsWith(app + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The storage root must be outside the application directory.", nameof(rootDirectory));
    }

    /// <inheritdoc />
    public async Task<StoredMedia> SaveAsync(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(root);
        var key = Guid.NewGuid().ToString("N");
        var temporary = Path.Combine(root, "." + key + ".tmp");
        var destination = Path.Combine(root, key);
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await source.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination);
            return new StoredMedia(key, new FileInfo(destination).Length);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(Path.Combine(root, key), FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, FileOptions.Asynchronous);
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(Path.Combine(root, key));
        return Task.CompletedTask;
    }

    private static void ValidateKey(string key)
    {
        if (key is null || key.Length != 32 || !Guid.TryParseExact(key, "N", out _))
            throw new ArgumentException("Invalid storage key.", nameof(key));
    }
}
