namespace DotnetMedia.Core;

/// <summary>Stores opaque media bytes under server-generated keys.</summary>
public interface IMediaStore
{
    /// <summary>Saves one stream with its validated MIME type and returns its generated key and byte length. The input stream is not disposed.</summary>
    /// <remarks>The caller must determine the MIME type from validated content, not a request header or filename.</remarks>
    Task<StoredMedia> SaveAsync(Stream source, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Opens a stored object for reading. The caller disposes the returned stream.</summary>
    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Deletes an object. Deleting an absent object is allowed.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>The opaque key and actual byte length of a saved object.</summary>
/// <param name="Key">Server-generated storage key.</param>
/// <param name="Length">Number of bytes saved.</param>
public sealed record StoredMedia(string Key, long Length);
