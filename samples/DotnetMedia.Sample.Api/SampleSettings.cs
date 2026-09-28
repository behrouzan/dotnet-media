using DotnetMedia.Imaging;

namespace DotnetMedia.Sample.Api;

internal sealed class SampleSettings
{
    public string StorageRoot { get; set; } = string.Empty;
    public ImageProcessingOptions Image { get; set; } = new();
    public ulong NativeMemoryBytes { get; set; }
    public ulong NativeDiskBytes { get; set; }
    public uint NativeListLength { get; set; }
    public uint NativeThreads { get; set; }

    public static SampleSettings Load(IConfiguration configuration)
    {
        SampleSettings settings;
        try { settings = configuration.GetSection("MediaSample").Get<SampleSettings>() ?? throw new InvalidOperationException("Missing MediaSample configuration."); }
        catch (Exception error) when (error is not InvalidOperationException)
        {
            throw new InvalidOperationException("Invalid MediaSample configuration: unable to bind values.", error);
        }

        var image = settings.Image;
        if (string.IsNullOrWhiteSpace(settings.StorageRoot) || !Path.IsPathFullyQualified(settings.StorageRoot))
            throw new InvalidOperationException("MediaSample:StorageRoot must be an absolute private path outside the application directory.");
        try { _ = new DotnetMedia.Storage.Local.LocalMediaStore(settings.StorageRoot); }
        catch (ArgumentException error) { throw new InvalidOperationException("MediaSample:StorageRoot must be outside the application directory.", error); }
        if (settings.NativeMemoryBytes == 0 || settings.NativeDiskBytes == 0 || settings.NativeListLength == 0 || settings.NativeThreads == 0)
            throw new InvalidOperationException("MediaSample native limits must all be positive.");
        if (image is null || image.MaxInputBytes is <= 0 or > 100_000_000 || image.MaxWidth is <= 0 or > 10_000 ||
            image.MaxHeight is <= 0 or > 10_000 || image.MaxPixels is <= 0 or > 100_000_000 ||
            image.OriginalMaxWidth < 0 || image.OriginalMaxHeight < 0 || image.OriginalMaxWidth > image.MaxWidth ||
            image.OriginalMaxHeight > image.MaxHeight || image.OriginalQuality is < 1 or > 100 ||
            !Enum.IsDefined(image.OriginalMode) || !Enum.IsDefined(image.OriginalFormat) ||
            image.AllowedInputFormats is null || image.AllowedInputFormats.Count == 0 ||
            image.AllowedInputFormats.Any(format => !Enum.IsDefined(format)) || image.Variants is null || image.Variants.Count > 20)
            throw new InvalidOperationException("MediaSample:Image contains invalid limits, format or original settings.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in image.Variants)
            if (variant is null || string.IsNullOrWhiteSpace(variant.Name) || !names.Add(variant.Name) ||
                variant.Width <= 0 || variant.Height <= 0 || variant.Width > image.MaxWidth || variant.Height > image.MaxHeight ||
                (long)variant.Width * variant.Height > image.MaxPixels || variant.Quality is < 1 or > 100 ||
                !Enum.IsDefined(variant.Mode) || !Enum.IsDefined(variant.Format))
                throw new InvalidOperationException("MediaSample:Image:Variants contains an invalid or duplicate variant.");
        return settings;
    }
}
