using DotnetMedia.Core;
using ImageMagick;

namespace DotnetMedia.Imaging;

/// <summary>Validates a raster upload and stores an original plus named versions.</summary>
public sealed class ImageProcessor
{
    private readonly IMediaStore store;

    static ImageProcessor()
    {
        // ImageMagick limits are process-wide. These ceilings are independent of per-upload options.
        ResourceLimits.Memory = 256UL * 1024 * 1024;
        ResourceLimits.Disk = 0;
        ResourceLimits.ListLength = 16;
        ResourceLimits.Thread = 2;
        ResourceLimits.Width = 10_000;
        ResourceLimits.Height = 10_000;
    }

    /// <summary>Creates a processor using the supplied storage adapter.</summary>
    public ImageProcessor(IMediaStore store) => this.store = store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>Processes an untrusted stream. The caller retains ownership of the stream.</summary>
    public async Task<ProcessedImage> ProcessAsync(Stream input, ImageProcessingOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        var saved = new List<string>();
        try
        {
            var bytes = await ReadBoundedAsync(input, options.MaxInputBytes, cancellationToken);
            var format = DetectFormat(bytes);
            if (!options.AllowedInputFormats.Contains(format))
                throw new ImageProcessingException(ImageError.UnsupportedFormat, "Input format is not allowed by configuration.");
            if (IsAnimated(bytes, format))
                throw new ImageProcessingException(ImageError.AnimatedImage, "Animated images are not supported.");

            MagickImageInfo info;
            try { info = new MagickImageInfo(bytes); }
            catch (MagickException error) { throw new ImageProcessingException(ImageError.InvalidImage, "Invalid image data.", error); }
            if (FromMagickFormat(info.Format) != format)
                throw new ImageProcessingException(ImageError.InvalidImage, "Image signature and decoded format disagree.");
            if (info.Width == 0 || info.Height == 0 || info.Width > options.MaxWidth || info.Height > options.MaxHeight ||
                (long)info.Width * info.Height > options.MaxPixels)
                throw new ImageProcessingException(ImageError.DimensionsExceeded, "Image dimensions exceed configured limits.");

            cancellationToken.ThrowIfCancellationRequested();
            MagickImage image;
            try { image = new MagickImage(bytes); }
            catch (MagickException error) { throw new ImageProcessingException(ImageError.InvalidImage, "Invalid image data.", error); }
            using (image)
            {
            cancellationToken.ThrowIfCancellationRequested();
            if (FromMagickFormat(image.Format) != format || image.Width != info.Width || image.Height != info.Height)
                throw new ImageProcessingException(ImageError.InvalidImage, "Decoded image differs from its header.");
            var originalOrientation = image.Orientation;
            image.AutoOrient();
            image.Strip();

            ImageOutput original;
            if (options.OriginalMode == OriginalMode.PreserveIfPossible && originalOrientation is OrientationType.TopLeft or OrientationType.Undefined &&
                (options.OriginalMaxWidth == 0 || image.Width <= options.OriginalMaxWidth) &&
                (options.OriginalMaxHeight == 0 || image.Height <= options.OriginalMaxHeight))
                original = await SaveAsync("original", bytes, format, (int)image.Width, (int)image.Height, saved, cancellationToken);
            else
            {
                using var copy = image.Clone();
                ResizeContain(copy, options.OriginalMaxWidth == 0 ? (int)copy.Width : options.OriginalMaxWidth,
                    options.OriginalMaxHeight == 0 ? (int)copy.Height : options.OriginalMaxHeight);
                original = await EncodeAndSaveAsync("original", copy, options.OriginalFormat, options.OriginalQuality, saved, cancellationToken);
            }

            var variants = new List<ImageOutput>(options.Variants.Count);
            foreach (var variant in options.Variants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var copy = image.Clone();
                if (variant.Mode == ResizeMode.Cover) ResizeCover(copy, variant.Width, variant.Height);
                else ResizeContain(copy, variant.Width, variant.Height);
                variants.Add(await EncodeAndSaveAsync(variant.Name, copy, variant.Format, variant.Quality, saved, cancellationToken));
            }
            return new ProcessedImage(original, variants);
            }
        }
        catch
        {
            foreach (var key in saved)
            {
                try { await store.DeleteAsync(key, CancellationToken.None); }
                catch { /* Preserve the processing or storage failure. */ }
            }
            throw;
        }
    }

    private async Task<ImageOutput> EncodeAndSaveAsync(string name, IMagickImage<byte> image, ImageFormat format, int quality,
        List<string> saved, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        image.Quality = (uint)quality;
        using var output = new MemoryStream();
        image.Write(output, ToMagickFormat(format));
        cancellationToken.ThrowIfCancellationRequested();
        return await SaveAsync(name, output.ToArray(), format, (int)image.Width, (int)image.Height, saved, cancellationToken);
    }

    private async Task<ImageOutput> SaveAsync(string name, byte[] bytes, ImageFormat format, int width, int height,
        List<string> saved, CancellationToken cancellationToken)
    {
        var type = ContentType(format);
        using var source = new MemoryStream(bytes, writable: false);
        var stored = await store.SaveAsync(source, type, cancellationToken);
        saved.Add(stored.Key);
        return new ImageOutput(name, stored.Key, format, type, width, height, stored.Length);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream input, long max, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var count = await input.ReadAsync(chunk, token);
            if (count == 0) break;
            if (buffer.Length + count > max)
                throw new ImageProcessingException(ImageError.InputTooLarge, "Input exceeds the byte limit.");
            buffer.Write(chunk, 0, count);
        }
        return buffer.ToArray();
    }

    private static ImageFormat DetectFormat(byte[] data)
    {
        if (data.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff })) return ImageFormat.Jpeg;
        if (data.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ImageFormat.Png;
        if (data.Length >= 12 && data.AsSpan(0, 4).SequenceEqual("RIFF"u8) && data.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return ImageFormat.WebP;
        throw new ImageProcessingException(ImageError.UnsupportedFormat, "Only JPEG, PNG and WebP are supported; SVG is rejected.");
    }

    private static bool IsAnimated(byte[] bytes, ImageFormat format)
    {
        if (format == ImageFormat.Jpeg) return false;
        var offset = format == ImageFormat.Png ? 8 : 12;
        while (offset + 8 <= bytes.Length)
        {
            var length = format == ImageFormat.Png
                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4))
                : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            var type = format == ImageFormat.Png ? bytes.AsSpan(offset + 4, 4) : bytes.AsSpan(offset, 4);
            if (format == ImageFormat.Png && type.SequenceEqual("acTL"u8)) return true;
            if (format == ImageFormat.WebP && (type.SequenceEqual("ANIM"u8) || type.SequenceEqual("ANMF"u8) ||
                (type.SequenceEqual("VP8X"u8) && length > 0 && offset + 8 < bytes.Length && (bytes[offset + 8] & 2) != 0))) return true;
            var next = (long)offset + 8 + length + (format == ImageFormat.Png ? 4 : (length & 1));
            if (next > bytes.Length || next <= offset) break;
            offset = (int)next;
        }
        return false;
    }

    private static void ResizeContain(IMagickImage<byte> image, int width, int height)
    {
        var scale = Math.Min(1d, Math.Min((double)width / image.Width, (double)height / image.Height));
        if (scale >= 1) return;
        image.Resize(Math.Max(1u, (uint)Math.Round(image.Width * scale)), Math.Max(1u, (uint)Math.Round(image.Height * scale)));
    }

    private static void ResizeCover(IMagickImage<byte> image, int width, int height)
    {
        var scale = Math.Min(1d, Math.Max((double)width / image.Width, (double)height / image.Height));
        if (scale < 1)
            image.Resize(Math.Max(1u, (uint)Math.Ceiling(image.Width * scale)), Math.Max(1u, (uint)Math.Ceiling(image.Height * scale)));
        image.Crop(Math.Min(image.Width, (uint)width), Math.Min(image.Height, (uint)height), Gravity.Center);
    }

    private static ImageFormat FromMagickFormat(MagickFormat format) => format switch
    {
        MagickFormat.Jpeg => ImageFormat.Jpeg,
        MagickFormat.Png => ImageFormat.Png,
        MagickFormat.WebP => ImageFormat.WebP,
        _ => throw new ImageProcessingException(ImageError.UnsupportedFormat, "Decoded format is not allowed.")
    };

    private static MagickFormat ToMagickFormat(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => MagickFormat.Jpeg,
        ImageFormat.Png => MagickFormat.Png,
        ImageFormat.WebP => MagickFormat.WebP,
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private static string ContentType(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => "image/jpeg",
        ImageFormat.Png => "image/png",
        ImageFormat.WebP => "image/webp",
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    private static void ValidateOptions(ImageProcessingOptions options)
    {
        if (options.MaxInputBytes <= 0 || options.MaxInputBytes > 100_000_000 || options.MaxWidth is <= 0 or > 10_000 ||
            options.MaxHeight is <= 0 or > 10_000 ||
            options.MaxPixels <= 0 || options.MaxPixels > 100_000_000 || options.OriginalMaxWidth < 0 || options.OriginalMaxHeight < 0 ||
            options.OriginalMaxWidth > options.MaxWidth || options.OriginalMaxHeight > options.MaxHeight ||
            options.OriginalQuality is < 1 or > 100 || !Enum.IsDefined(options.OriginalMode) || !Enum.IsDefined(options.OriginalFormat) ||
            options.AllowedInputFormats is null || options.AllowedInputFormats.Count == 0 ||
            options.AllowedInputFormats.Any(format => !Enum.IsDefined(format)) ||
            options.Variants is null || options.Variants.Count > 20)
            throw new ArgumentException("Invalid image processing options.", nameof(options));
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variant in options.Variants)
            if (variant is null || string.IsNullOrWhiteSpace(variant.Name) || !names.Add(variant.Name) ||
                variant.Width <= 0 || variant.Height <= 0 || variant.Width > options.MaxWidth || variant.Height > options.MaxHeight ||
                (long)variant.Width * variant.Height > options.MaxPixels || variant.Quality is < 1 or > 100 ||
                !Enum.IsDefined(variant.Mode) || !Enum.IsDefined(variant.Format))
                throw new ArgumentException("Invalid image variant.", nameof(options));
    }
}
