# Image processing engine decision (28 September 2026)

The next slice should use **Magick.NET**, subject to a pinned package and its transitive license review when that dependency is added. This slice deliberately has no image engine dependency.

| Option | Capability and deployment | License finding |
| --- | --- | --- |
| [ImageSharp](https://github.com/SixLabors/ImageSharp) | Managed .NET, JPEG/PNG/WebP support and image processing. | Its [split license](https://github.com/SixLabors/ImageSharp/blob/main/LICENSE) grants Apache 2.0 use only under listed criteria and calls for a commercial license otherwise. A general package for unknown SaaS consumers cannot assume every consumer meets those criteria. |
| [Magick.NET](https://github.com/dlemstra/Magick.NET) | .NET binding/distribution of ImageMagick with broad format, resize and orientation features; native binaries increase package size and require runtime-specific verification. | Its repository [license](https://github.com/dlemstra/Magick.NET/blob/main/License.txt) is Apache 2.0. ImageMagick's [license](https://imagemagick.org/script/license.php) is also published by its project. Review the exact selected binary package and notices before release. |
| [NetVips](https://github.com/kleisauke/net-vips) | Efficient streaming pipeline; requires native libvips binaries and platform packaging. | Binding is [MIT](https://github.com/kleisauke/net-vips/blob/master/LICENSE), while [libvips](https://github.com/libvips/libvips/blob/master/COPYING) has separate LGPL terms. Distribution obligations need a specific review before choosing its binary packages. |

Magick.NET is the current engineering choice because its published license is straightforward for a reusable commercial package and it supports the needed raster operations. Its many decoders must be restricted to the configured input allowlist. Decoder resource limits, frame count, metadata stripping, and native dependency deployment need explicit tests in the processing slice. This record describes the upstream license texts, not legal advice or a conclusion about every transitive binary.
