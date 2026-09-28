# ASP.NET Core sample

This standalone .NET 8 API accepts an unprocessed multipart image in a field named `file`. It uses `ImageProcessor` to create the original plus the `card` and `thumb` variants configured in `appsettings.json`. The client does not need to crop or compress the image. `POST /images` returns each output's key, format, Content-Type, width, height and byte length.

Set `MediaSample__StorageRoot` to an absolute private directory outside the app and web root. The sample intentionally leaves it empty in `appsettings.json` and fails startup with a clear error until it is configured. For example, in PowerShell:

```powershell
$env:MediaSample__StorageRoot = 'D:\private-media-demo'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project samples/DotnetMedia.Sample.Api --no-launch-profile --urls http://localhost:5055
```

In another terminal, upload an ordinary JPEG, PNG or WebP file and use a key from the JSON response to read it:

```powershell
curl.exe -F "file=@C:\temp\photo.jpg" http://localhost:5055/images
curl.exe -o output.bin http://localhost:5055/media/PASTE_OUTPUT_KEY
```

The read route is enabled **only in Development**. It is unauthenticated and serves `application/octet-stream` because the local store does not persist MIME metadata. It is solely for trying this sample. A production consumer must authorize each read against its own records and return the recorded output Content-Type. Do not expose the storage directory through static files or `wwwroot`. Use a private `ASPNETCORE_TEMP` directory for multipart buffering in production deployments.

`MediaSample:Image` configures upload byte, width, height and pixel limits, allowed input formats, original policy and named variants. The sample validates the configuration at startup. Multipart parsing has the configured byte limit; Kestrel has a slightly larger request limit for multipart framing. `ImageProcessor` enforces the byte limit again as it reads the file. Validation errors return 400, 413, 415 or 422 as appropriate. Storage and native failures remain server errors.

`Program.cs` applies `MediaSample:Native*` to Magick.NET `ResourceLimits` at startup. These settings are **process-wide**, affect other Magick.NET users in the same host and must be sized for the host's memory, disk and concurrency. The sample is an upload-only host; the library does not change these limits itself. Use a private native temporary directory and OS/container resource limits in a deployment that handles untrusted uploads. Antimalware scanning and gallery authorization remain outside this sample.
