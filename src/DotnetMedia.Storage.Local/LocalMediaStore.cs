using DotnetMedia.Core;

namespace DotnetMedia.Storage.Local;

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
    public async Task<StoredMedia> SaveAsync(Stream source, string contentType, CancellationToken cancellationToken = default, string? keyPrefix = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        MediaKeyPrefix.Validate(keyPrefix);
        cancellationToken.ThrowIfCancellationRequested();
        var directory = ResolveDirectory(keyPrefix, create: true);
        var filename = Guid.NewGuid().ToString("N");
        var key = string.IsNullOrEmpty(keyPrefix) ? filename : keyPrefix + "/" + filename;
        var temporary = Path.Combine(directory, "." + filename + ".tmp");
        var destination = Path.Combine(directory, filename);
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
            try { File.Delete(temporary); }
            catch (IOException) { /* Preserve the original upload failure. */ }
            catch (UnauthorizedAccessException) { /* Preserve the original upload failure. */ }
            throw;
        }
    }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolveKey(key);
        cancellationToken.ThrowIfCancellationRequested();
        RejectLink(path);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 81920, FileOptions.Asynchronous);
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolveKey(key);
        cancellationToken.ThrowIfCancellationRequested();
        RejectLink(path);
        File.Delete(path);
        return Task.CompletedTask;
    }

    private string ResolveKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Invalid storage key.", nameof(key));
        var separator = key.LastIndexOf('/');
        var prefix = separator < 0 ? null : key[..separator];
        if (separator == 0) throw new ArgumentException("Invalid storage key.", nameof(key));
        var filename = key[(separator + 1)..];
        MediaKeyPrefix.Validate(prefix);
        if (filename.Length != 32 || filename.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("Invalid storage key.", nameof(key));
        return Path.Combine(ResolveDirectory(prefix, create: false), filename);
    }

    private string ResolveDirectory(string? prefix, bool create)
    {
        if (create) Directory.CreateDirectory(root);
        RejectLink(root);
        var directory = root;
        if (string.IsNullOrEmpty(prefix)) return directory;
        foreach (var segment in prefix.Split('/'))
        {
            directory = Path.Combine(directory, segment);
            if (create) Directory.CreateDirectory(directory);
            RejectLink(directory);
        }
        return directory;
    }

    private static void RejectLink(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Storage paths must not contain symbolic links or reparse points.");
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }
}
