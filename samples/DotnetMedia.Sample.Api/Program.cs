using DotnetMedia.Core;
using DotnetMedia.Imaging;
using DotnetMedia.Sample.Api;
using DotnetMedia.Storage.Local;
using ImageMagick;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
var settings = SampleSettings.Load(builder.Configuration);

// Native ImageMagick limits affect the entire process. The host applies them explicitly at startup.
ResourceLimits.Memory = settings.NativeMemoryBytes;
ResourceLimits.Disk = settings.NativeDiskBytes;
ResourceLimits.ListLength = settings.NativeListLength;
ResourceLimits.Thread = settings.NativeThreads;
ResourceLimits.Width = (uint)settings.Image.MaxWidth;
ResourceLimits.Height = (uint)settings.Image.MaxHeight;

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = checked(settings.Image.MaxInputBytes + 1024 * 1024));
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = settings.Image.MaxInputBytes);
builder.Services.AddSingleton(settings.Image);
builder.Services.AddSingleton<IMediaStore>(new LocalMediaStore(settings.StorageRoot));
builder.Services.AddSingleton<ImageProcessor>();

var app = builder.Build();

// These unauthenticated sample routes are available only for local Development testing.
if (app.Environment.IsDevelopment())
{
app.MapPost("/images", async (HttpRequest request, ImageProcessor processor, ImageProcessingOptions imageOptions, CancellationToken token) =>
{
    if (!request.HasFormContentType)
        return Results.Problem("Expected multipart/form-data with a file field.", statusCode: StatusCodes.Status415UnsupportedMediaType);

    IFormCollection form;
    try { form = await request.ReadFormAsync(token); }
    catch (InvalidDataException error)
    {
        var tooLarge = error.Message.Contains("length limit", StringComparison.OrdinalIgnoreCase);
        return Results.Problem(tooLarge ? "Multipart input exceeds the configured limit." : "Malformed multipart input.",
            statusCode: tooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status400BadRequest);
    }

    var file = form.Files.GetFile("file");
    if (file is null)
        return Results.Problem("A multipart file field named 'file' is required.", statusCode: StatusCodes.Status400BadRequest);
    if (file.Length > imageOptions.MaxInputBytes)
        return Results.Problem("Input exceeds the configured byte limit.", statusCode: StatusCodes.Status413PayloadTooLarge);

    await using var input = file.OpenReadStream();
    try
    {
        var result = await processor.ProcessAsync(input, imageOptions, token);
        return Results.Ok(result);
    }
    catch (ImageProcessingException error)
    {
        var status = error.Error switch
        {
            ImageError.InputTooLarge => StatusCodes.Status413PayloadTooLarge,
            ImageError.UnsupportedFormat => StatusCodes.Status415UnsupportedMediaType,
            _ => StatusCodes.Status422UnprocessableEntity
        };
        return Results.Problem(error.Message, statusCode: status, extensions: new Dictionary<string, object?> { ["imageError"] = error.Error.ToString() });
    }
});

    app.MapGet("/media/{key}", async (string key, IMediaStore store, CancellationToken token) =>
    {
        try { return Results.File(await store.OpenReadAsync(key, token), "application/octet-stream"); }
        catch (ArgumentException) { return Results.BadRequest(); }
        catch (FileNotFoundException) { return Results.NotFound(); }
    });
}

app.Run();

/// <summary>Entry point used by the sample's HTTP integration tests.</summary>
public partial class Program;
