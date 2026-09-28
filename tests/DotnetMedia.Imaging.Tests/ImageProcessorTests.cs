using DotnetMedia.Core;
using DotnetMedia.Imaging;
using ImageMagick;

namespace DotnetMedia.Imaging.Tests;

public sealed class ImageProcessorTests
{
    [Theory]
    [InlineData(MagickFormat.Jpeg, ImageFormat.Jpeg, "image/jpeg")]
    [InlineData(MagickFormat.Png, ImageFormat.Png, "image/png")]
    [InlineData(MagickFormat.WebP, ImageFormat.WebP, "image/webp")]
    public async Task AcceptsActualRasterFormat(MagickFormat inputFormat, ImageFormat expected, string contentType)
    {
        var store = new RecordingStore();
        var result = await new ImageProcessor(store).ProcessAsync(new MemoryStream(CreateImage(8, 6, inputFormat)),
            new ImageProcessingOptions { OriginalFormat = expected });
        Assert.Equal(expected, result.Original.Format);
        Assert.Equal(contentType, result.Original.ContentType);
        Assert.Equal(contentType, store.ContentTypes.Single());
        Assert.Equal(8, result.Original.Width);
    }

    [Fact]
    public async Task MakesNamedContainAndCoverOutputsWithoutUpscale()
    {
        var result = await new ImageProcessor(new RecordingStore()).ProcessAsync(new MemoryStream(CreateImage(20, 10, MagickFormat.Png)),
            new ImageProcessingOptions { OriginalFormat = ImageFormat.Png, Variants =
            [
                new("contain", 8, 8, ResizeMode.Contain, ImageFormat.Jpeg),
                new("cover", 8, 8, ResizeMode.Cover, ImageFormat.WebP),
                new("large", 50, 50, ResizeMode.Contain, ImageFormat.Png)
            ] });
        Assert.Equal((8, 4), (result.Variants[0].Width, result.Variants[0].Height));
        Assert.Equal((8, 8), (result.Variants[1].Width, result.Variants[1].Height));
        Assert.Equal((20, 10), (result.Variants[2].Width, result.Variants[2].Height));
        Assert.Equal(new[] { "contain", "cover", "large" }, result.Variants.Select(x => x.Name));
    }

    [Fact]
    public async Task RejectsOversizeBytesAndPixels()
    {
        var bytes = CreateImage(20, 10, MagickFormat.Png);
        var processor = new ImageProcessor(new RecordingStore());
        var tooLarge = await Assert.ThrowsAsync<ImageProcessingException>(() => processor.ProcessAsync(new MemoryStream(bytes),
            new ImageProcessingOptions { MaxInputBytes = bytes.Length - 1 }));
        Assert.Equal(ImageError.InputTooLarge, tooLarge.Error);
        var tooManyPixels = await Assert.ThrowsAsync<ImageProcessingException>(() => processor.ProcessAsync(new MemoryStream(bytes),
            new ImageProcessingOptions { MaxPixels = 199 }));
        Assert.Equal(ImageError.DimensionsExceeded, tooManyPixels.Error);
    }

    [Fact]
    public async Task RejectsFakeCorruptAndSvg()
    {
        var processor = new ImageProcessor(new RecordingStore());
        var fake = await Assert.ThrowsAsync<ImageProcessingException>(() => processor.ProcessAsync(new MemoryStream("not an image"u8.ToArray()), new()));
        Assert.Equal(ImageError.UnsupportedFormat, fake.Error);
        var svg = await Assert.ThrowsAsync<ImageProcessingException>(() => processor.ProcessAsync(new MemoryStream("<svg/>"u8.ToArray()), new()));
        Assert.Equal(ImageError.UnsupportedFormat, svg.Error);
        var corrupt = await Assert.ThrowsAsync<ImageProcessingException>(() => processor.ProcessAsync(new MemoryStream([0xff, 0xd8, 0xff, 0x00]), new()));
        Assert.Equal(ImageError.InvalidImage, corrupt.Error);
    }

    [Fact]
    public async Task RespectsConfiguredInputFormats()
    {
        var processor = new ImageProcessor(new RecordingStore());
        var rejected = await Assert.ThrowsAsync<ImageProcessingException>(() => processor.ProcessAsync(
            new MemoryStream(CreateImage(8, 6, MagickFormat.Png)),
            new ImageProcessingOptions { AllowedInputFormats = [ImageFormat.Jpeg] }));
        Assert.Equal(ImageError.UnsupportedFormat, rejected.Error);
    }

    [Fact]
    public async Task RejectsValidAnimatedWebP()
    {
        using var frames = new MagickImageCollection();
        frames.Add(new MagickImage(MagickColors.Red, 8, 6) { AnimationDelay = 10 });
        frames.Add(new MagickImage(MagickColors.Blue, 8, 6) { AnimationDelay = 10 });
        using var output = new MemoryStream();
        frames.Write(output, MagickFormat.WebP);
        var bytes = output.ToArray();
        using (var decoded = new MagickImageCollection(bytes))
            Assert.Equal(2, decoded.Count);
        var error = await Assert.ThrowsAsync<ImageProcessingException>(() => new ImageProcessor(new RecordingStore()).ProcessAsync(
            new MemoryStream(bytes), new()));
        Assert.Equal(ImageError.AnimatedImage, error.Error);
    }

    [Fact]
    public async Task CorrectsOrientationAndReencodesOriginal()
    {
        using var image = new MagickImage(MagickColors.Red, 10, 5);
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.Orientation, (ushort)6);
        image.SetProfile(profile);
        image.Orientation = OrientationType.RightTop;
        using var encoded = new MemoryStream();
        image.Write(encoded, MagickFormat.Jpeg);
        using (var check = new MagickImage(encoded.ToArray()))
            Assert.Equal(OrientationType.RightTop, check.Orientation);
        var result = await new ImageProcessor(new RecordingStore()).ProcessAsync(new MemoryStream(encoded.ToArray()),
            new ImageProcessingOptions { OriginalMode = OriginalMode.PreserveIfPossible });
        Assert.Equal((5, 10), (result.Original.Width, result.Original.Height));
    }

    [Fact]
    public async Task DeletesEarlierOutputsIfLaterSaveFails()
    {
        var store = new RecordingStore { FailAtSave = 2 };
        var failure = await Assert.ThrowsAsync<IOException>(() => new ImageProcessor(store).ProcessAsync(
            new MemoryStream(CreateImage(10, 10, MagickFormat.Png)), new ImageProcessingOptions
            { Variants = [new("thumb", 5, 5, ResizeMode.Contain, ImageFormat.Png)] }));
        Assert.Equal("storage failed", failure.Message);
        Assert.Single(store.DeletedKeys);
        Assert.Empty(store.Objects);
    }

    [Fact]
    public async Task PreservesOriginalBytesWhenPolicyAllows()
    {
        using var image = new MagickImage(MagickColors.Red, 8, 6);
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.Artist, "test artist");
        image.SetProfile(profile);
        using var encoded = new MemoryStream();
        image.Write(encoded, MagickFormat.Jpeg);
        var bytes = encoded.ToArray();
        var store = new RecordingStore();
        var result = await new ImageProcessor(store).ProcessAsync(new MemoryStream(bytes), new ImageProcessingOptions
        { OriginalMode = OriginalMode.PreserveIfPossible, OriginalFormat = ImageFormat.Jpeg });
        Assert.Equal(bytes, store.Objects[result.Original.Key]);
        using var decoded = new MagickImage(store.Objects[result.Original.Key]);
        Assert.NotNull(decoded.GetExifProfile());
    }

    [Fact]
    public async Task DifferentOriginalFormatForcesReencodeAndStripsMetadata()
    {
        using var image = new MagickImage(MagickColors.Red, 8, 6);
        var profile = new ExifProfile();
        profile.SetValue(ExifTag.Artist, "test artist");
        image.SetProfile(profile);
        using var input = new MemoryStream();
        image.Write(input, MagickFormat.Jpeg);
        var store = new RecordingStore();
        var result = await new ImageProcessor(store).ProcessAsync(new MemoryStream(input.ToArray()), new ImageProcessingOptions
        { OriginalMode = OriginalMode.PreserveIfPossible, OriginalFormat = ImageFormat.Png });
        Assert.Equal(ImageFormat.Png, result.Original.Format);
        Assert.Equal("image/png", result.Original.ContentType);
        using var decoded = new MagickImage(store.Objects[result.Original.Key]);
        Assert.Null(decoded.GetExifProfile());
    }

    [Fact]
    public async Task CancellationBeforeReadDoesNotStoreAnything()
    {
        var store = new RecordingStore();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ImageProcessor(store).ProcessAsync(
            new MemoryStream(CreateImage(8, 6, MagickFormat.Png)), new(), cancellation.Token));
        Assert.Empty(store.Objects);
    }

    [Fact]
    public async Task FailedRollbackDoesNotHideStorageFailure()
    {
        var store = new RecordingStore { FailAtSave = 2, FailDelete = true };
        var failure = await Assert.ThrowsAsync<IOException>(() => new ImageProcessor(store).ProcessAsync(
            new MemoryStream(CreateImage(8, 6, MagickFormat.Png)), new ImageProcessingOptions
            { Variants = [new("thumb", 4, 4, ResizeMode.Contain, ImageFormat.Png)] }));
        Assert.Equal("storage failed", failure.Message);
        Assert.Single(store.DeletedKeys);
    }

    private static byte[] CreateImage(uint width, uint height, MagickFormat format)
    {
        using var image = new MagickImage(MagickColors.Red, width, height);
        using var output = new MemoryStream();
        image.Write(output, format);
        return output.ToArray();
    }

    private sealed class RecordingStore : IMediaStore
    {
        private int saves;
        public int FailAtSave { get; init; }
        public bool FailDelete { get; init; }
        public Dictionary<string, byte[]> Objects { get; } = new();
        public List<string> ContentTypes { get; } = new();
        public List<string> DeletedKeys { get; } = new();
        public async Task<StoredMedia> SaveAsync(Stream source, string contentType, CancellationToken cancellationToken = default)
        {
            if (++saves == FailAtSave) throw new IOException("storage failed");
            using var output = new MemoryStream();
            await source.CopyToAsync(output, cancellationToken);
            var key = Guid.NewGuid().ToString("N");
            Objects.Add(key, output.ToArray());
            ContentTypes.Add(contentType);
            return new StoredMedia(key, output.Length);
        }
        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(Objects[key]));
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(key);
            if (FailDelete) throw new IOException("cleanup failed");
            Objects.Remove(key);
            return Task.CompletedTask;
        }
    }
}
