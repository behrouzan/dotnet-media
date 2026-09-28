using System.Net;
using System.Net.Http.Json;
using DotnetMedia.Core;
using DotnetMedia.Imaging;
using ImageMagick;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace DotnetMedia.Sample.Api.Tests;

public sealed class SampleApiTests
{
    [Fact]
    public async Task UploadProducesOriginalAndReadableNamedVersions()
    {
        using var factory = new SampleFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/images", Form(CreatePng()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProcessedImage>();
        Assert.NotNull(result);
        Assert.Equal(2, result.Variants.Count);
        Assert.Equal(ImageFormat.Jpeg, result.Original.Format);
        Assert.Equal("image/jpeg", result.Original.ContentType);
        Assert.Equal(new[] { "card", "thumb" }, result.Variants.Select(x => x.Name));
        Assert.Equal(new[] { ImageFormat.WebP, ImageFormat.Jpeg }, result.Variants.Select(x => x.Format));
        foreach (var output in new[] { result.Original }.Concat(result.Variants))
        {
            Assert.True(output.Length > 0);
            Assert.True(output.Width > 0 && output.Height > 0);
            Assert.True(Guid.TryParseExact(output.Key, "N", out _));
            using var read = await client.GetAsync("/media/" + output.Key);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var decoded = new MagickImage(await read.Content.ReadAsByteArrayAsync());
            Assert.Equal(output.Width, (int)decoded.Width);
            Assert.Equal(output.Height, (int)decoded.Height);
        }
    }

    [Fact]
    public async Task InvalidImageIsRejectedWithoutStoredFiles()
    {
        using var factory = new SampleFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/images", Form("not an image"u8.ToArray()));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(Directory.GetFiles(factory.Root));
    }

    [Fact]
    public async Task MissingFileFieldIsBadRequest()
    {
        using var factory = new SampleFactory();
        using var client = factory.CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("x"), "other");
        using var response = await client.PostAsync("/images", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadAndReadRoutesAreUnavailableOutsideDevelopment()
    {
        using var factory = new SampleFactory(environment: "Production");
        using var client = factory.CreateClient();
        using var upload = await client.PostAsync("/images", Form(CreatePng()));
        using var read = await client.GetAsync("/media/0123456789abcdef0123456789abcdef");
        Assert.Equal(HttpStatusCode.NotFound, upload.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Empty(Directory.GetFiles(factory.Root));
    }

    [Fact]
    public async Task ExcessiveInputAndDimensionsAreRejected()
    {
        using var tooLargeFactory = new SampleFactory(new Dictionary<string, string?> { ["MediaSample:Image:MaxInputBytes"] = "128" });
        using var tooLargeClient = tooLargeFactory.CreateClient();
        using var tooLarge = await tooLargeClient.PostAsync("/images", Form(new byte[256]));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        Assert.Empty(Directory.GetFiles(tooLargeFactory.Root));

        using var pixelsFactory = new SampleFactory(new Dictionary<string, string?>
        {
            ["MediaSample:Image:MaxInputBytes"] = "10485760",
            ["MediaSample:Image:MaxPixels"] = "100",
            ["MediaSample:Image:Variants:0:Width"] = "8",
            ["MediaSample:Image:Variants:0:Height"] = "8",
            ["MediaSample:Image:Variants:1:Width"] = "4",
            ["MediaSample:Image:Variants:1:Height"] = "4"
        });
        using var pixelsClient = pixelsFactory.CreateClient();
        using var pixels = await pixelsClient.PostAsync("/images", Form(CreatePng()));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, pixels.StatusCode);
        Assert.Empty(Directory.GetFiles(pixelsFactory.Root));
    }

    [Fact]
    public async Task FailedVariantSaveRemovesEarlierOutputAndReturnsServerError()
    {
        using var factory = new SampleFactory(failSecondSave: true);
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/images", Form(CreatePng()));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(Directory.GetFiles(factory.Root));
    }

    [Fact]
    public void InvalidStartupConfigurationFailsClearly()
    {
        using var factory = new SampleFactory(new Dictionary<string, string?> { ["MediaSample:Image:MaxWidth"] = "0" });
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("MediaSample:Image", error.ToString());
    }

    private static MultipartFormDataContent Form(byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", "untrusted.bin");
        return form;
    }

    private static byte[] CreatePng()
    {
        using var image = new MagickImage(MagickColors.Red, 20, 10);
        using var output = new MemoryStream();
        image.Write(output, MagickFormat.Png);
        return output.ToArray();
    }

    private sealed class SampleFactory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<string, string?> previousEnvironment = new();
        private readonly bool failSecondSave;
        private readonly string environment;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "dotnet-media-sample-tests", Guid.NewGuid().ToString("N"));

        public SampleFactory(IDictionary<string, string?>? overrides = null, bool failSecondSave = false, string environment = "Development")
        {
            this.failSecondSave = failSecondSave;
            this.environment = environment;
            Directory.CreateDirectory(Root);
            var values = new Dictionary<string, string?>(overrides ?? new Dictionary<string, string?>())
            { ["MediaSample:StorageRoot"] = Root };
            foreach (var (key, value) in values)
            {
                var environmentKey = key.Replace(":", "__");
                previousEnvironment[environmentKey] = Environment.GetEnvironmentVariable(environmentKey);
                Environment.SetEnvironmentVariable(environmentKey, value);
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            if (failSecondSave)
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IMediaStore>();
                    services.AddSingleton<IMediaStore>(new FailingStore(new DotnetMedia.Storage.Local.LocalMediaStore(Root)));
                });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            foreach (var (key, value) in previousEnvironment)
                Environment.SetEnvironmentVariable(key, value);
            if (disposing && Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class FailingStore(IMediaStore inner) : IMediaStore
    {
        private int saves;
        public Task<StoredMedia> SaveAsync(Stream source, string contentType, CancellationToken cancellationToken = default) =>
            Interlocked.Increment(ref saves) == 2
                ? Task.FromException<StoredMedia>(new IOException("simulated storage failure"))
                : inner.SaveAsync(source, contentType, cancellationToken);
        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) => inner.OpenReadAsync(key, cancellationToken);
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => inner.DeleteAsync(key, cancellationToken);
    }
}
