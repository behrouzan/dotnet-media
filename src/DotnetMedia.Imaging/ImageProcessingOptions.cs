namespace DotnetMedia.Imaging;

/// <summary>Supported raster formats.</summary>
public enum ImageFormat
{
    /// <summary>JPEG.</summary>
    Jpeg,
    /// <summary>PNG.</summary>
    Png,
    /// <summary>WebP.</summary>
    WebP
}

/// <summary>How the original output is handled.</summary>
public enum OriginalMode
{
    /// <summary>Keep input bytes only when format matches <see cref="ImageProcessingOptions.OriginalFormat"/>, orientation needs no correction, and size complies. Preserved bytes retain metadata; otherwise the image is reencoded and metadata is stripped.</summary>
    PreserveIfPossible,
    /// <summary>Encode again and remove metadata.</summary>
    Reencode
}

/// <summary>How a variant fits its target box.</summary>
public enum ResizeMode
{
    /// <summary>Fit inside the target without cropping.</summary>
    Contain,
    /// <summary>Fill the target using a centered crop.</summary>
    Cover
}

/// <summary>One named image output.</summary>
/// <param name="Name">Unique name within the upload.</param>
/// <param name="Width">Target width in pixels.</param>
/// <param name="Height">Target height in pixels.</param>
/// <param name="Mode">Contain or center crop.</param>
/// <param name="Format">Encoded output format.</param>
/// <param name="Quality">Encoder quality from 1 to 100.</param>
public sealed record ImageVariant(string Name, int Width, int Height, ResizeMode Mode, ImageFormat Format, int Quality = 85);

/// <summary>Limits and requested outputs for one upload.</summary>
public sealed class ImageProcessingOptions
{
    /// <summary>Maximum encoded input bytes, enforced while reading.</summary>
    public long MaxInputBytes { get; init; } = 10 * 1024 * 1024;
    /// <summary>Maximum decoded input width.</summary>
    public int MaxWidth { get; init; } = 8000;
    /// <summary>Maximum decoded input height.</summary>
    public int MaxHeight { get; init; } = 8000;
    /// <summary>Maximum decoded input pixels.</summary>
    public long MaxPixels { get; init; } = 40_000_000;
    /// <summary>Allowed decoded input formats, restricted to JPEG, PNG and WebP.</summary>
    public IReadOnlyCollection<ImageFormat> AllowedInputFormats { get; init; } = [ImageFormat.Jpeg, ImageFormat.Png, ImageFormat.WebP];
    /// <summary>Original output mode. Reencoding strips metadata.</summary>
    public OriginalMode OriginalMode { get; init; } = OriginalMode.Reencode;
    /// <summary>Required original format. A different input format forces reencoding even with <see cref="OriginalMode.PreserveIfPossible"/>.</summary>
    public ImageFormat OriginalFormat { get; init; } = ImageFormat.Jpeg;
    /// <summary>Quality used when the original is reencoded.</summary>
    public int OriginalQuality { get; init; } = 85;
    /// <summary>Maximum original output width; zero keeps the input width.</summary>
    public int OriginalMaxWidth { get; init; }
    /// <summary>Maximum original output height; zero keeps the input height.</summary>
    public int OriginalMaxHeight { get; init; }
    /// <summary>Named variants generated after the original.</summary>
    public IReadOnlyList<ImageVariant> Variants { get; init; } = Array.Empty<ImageVariant>();
}

/// <summary>A validated output and its storage location.</summary>
public sealed record ImageOutput(string Name, string Key, ImageFormat Format, string ContentType, int Width, int Height, long Length);

/// <summary>The original and requested named outputs.</summary>
public sealed record ProcessedImage(ImageOutput Original, IReadOnlyList<ImageOutput> Variants);

/// <summary>Stable validation failure categories.</summary>
public enum ImageError
{
    /// <summary>The encoded byte limit was exceeded.</summary>
    InputTooLarge,
    /// <summary>The actual format is outside the allowlist.</summary>
    UnsupportedFormat,
    /// <summary>The input contains animation.</summary>
    AnimatedImage,
    /// <summary>The content could not be decoded as a valid raster image.</summary>
    InvalidImage,
    /// <summary>The dimensions or pixel count exceed configured limits.</summary>
    DimensionsExceeded
}

/// <summary>Indicates a rejected image upload.</summary>
public sealed class ImageProcessingException : Exception
{
    /// <summary>Creates a categorized image validation error.</summary>
    public ImageProcessingException(ImageError error, string message, Exception? innerException = null) : base(message, innerException) => Error = error;
    /// <summary>The validation failure category.</summary>
    public ImageError Error { get; }
}
