namespace DotnetMedia.Core;

/// <summary>Validates portable, logical storage prefixes independently of any filesystem.</summary>
public static class MediaKeyPrefix
{
    /// <summary>Validates an optional slash-separated prefix. Null or empty means the storage root.</summary>
    /// <remarks>Each segment is 1–64 ASCII letters, digits, hyphens or underscores, and must begin with a letter or digit. At most 16 segments and 512 characters are accepted.</remarks>
    public static void Validate(string? keyPrefix)
    {
        if (string.IsNullOrEmpty(keyPrefix)) return;
        if (keyPrefix.Length > 512) throw new ArgumentException("Storage prefix is too long.", nameof(keyPrefix));
        var segments = keyPrefix.Split('/');
        if (segments.Length > 16 || segments.Any(segment => segment.Length is < 1 or > 64 ||
            !IsAlphanumeric(segment[0]) || segment.Any(character => !IsAlphanumeric(character) && character is not '-' and not '_')))
            throw new ArgumentException("Storage prefix must contain safe slash-separated segments.", nameof(keyPrefix));
    }

    private static bool IsAlphanumeric(char value) => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
}
