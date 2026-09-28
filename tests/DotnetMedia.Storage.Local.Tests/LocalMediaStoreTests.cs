using DotnetMedia.Core;
using DotnetMedia.Storage.Local;

namespace DotnetMedia.Storage.Local.Tests;

public sealed class LocalMediaStoreTests
{
    [Fact]
    public async Task SaveReadDeleteRoundTrip()
    {
        var root = NewRoot();
        try
        {
            var store = new LocalMediaStore(root);
            var bytes = new byte[] { 1, 2, 3, 4 };
            var saved = await store.SaveAsync(new MemoryStream(bytes), "image/jpeg");
            Assert.Equal(bytes.Length, saved.Length);
            Assert.True(Guid.TryParseExact(saved.Key, "N", out _));
            var output = new MemoryStream();
            await using (var read = await store.OpenReadAsync(saved.Key))
                await read.CopyToAsync(output);
            Assert.Equal(bytes, output.ToArray());
            await store.DeleteAsync(saved.Key);
            await Assert.ThrowsAsync<FileNotFoundException>(() => store.OpenReadAsync(saved.Key));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FailedWriteLeavesNoObject()
    {
        var root = NewRoot();
        try
        {
            var store = new LocalMediaStore(root);
            var failure = new IOException("Simulated input failure");
            var caught = await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(new FailingStream(failure), "image/jpeg"));
            Assert.Same(failure, caught);
            Assert.Empty(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MultiSegmentPrefixRoundTripAndDelete()
    {
        var root = NewRoot();
        try
        {
            var store = new LocalMediaStore(root);
            var saved = await store.SaveAsync(new MemoryStream([1, 2, 3]), "image/png", keyPrefix: "shops/42/products");
            Assert.StartsWith("shops/42/products/", saved.Key);
            Assert.True(Guid.TryParseExact(saved.Key["shops/42/products/".Length..], "N", out _));
            Assert.True(File.Exists(Path.Combine(root, "shops", "42", "products", saved.Key.Split('/')[^1])));
            await using (var stream = await store.OpenReadAsync(saved.Key))
            {
                using var output = new MemoryStream();
                await stream.CopyToAsync(output);
                Assert.Equal(new byte[] { 1, 2, 3 }, output.ToArray());
            }
            await store.DeleteAsync(saved.Key);
            await Assert.ThrowsAsync<FileNotFoundException>(() => store.OpenReadAsync(saved.Key));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("/products")]
    [InlineData("products/")]
    [InlineData("shops//products")]
    [InlineData("products/../secret")]
    [InlineData("products/./secret")]
    [InlineData("C:/products")]
    [InlineData("products\\secret")]
    [InlineData("products:secret")]
    [InlineData(" products")]
    [InlineData("products/%2e%2e")]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PrN")]
    [InlineData("aUx")]
    [InlineData("nul")]
    [InlineData("COM1")]
    [InlineData("com9")]
    [InlineData("LPT1")]
    [InlineData("lPt9")]
    [InlineData("shops/42/CoM4/products")]
    public async Task RejectsInvalidPrefixes(string prefix)
    {
        var store = new LocalMediaStore(NewRoot());
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new MemoryStream([1]), "image/png", keyPrefix: prefix));
    }

    [Fact]
    public async Task SimilarNonReservedPrefixRemainsUsable()
    {
        var root = NewRoot();
        try
        {
            var store = new LocalMediaStore(root);
            var saved = await store.SaveAsync(new MemoryStream([7]), "image/png", keyPrefix: "shops/42/CONtent/COM10");
            Assert.StartsWith("shops/42/CONtent/COM10/", saved.Key);
            await using (var read = await store.OpenReadAsync(saved.Key))
                Assert.Equal(7, read.ReadByte());
            await store.DeleteAsync(saved.Key);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("00000000000000000000000000000000/../")]
    [InlineData("shops/../0123456789abcdef0123456789abcdef")]
    [InlineData("shops//0123456789abcdef0123456789abcdef")]
    [InlineData("shops\\42/0123456789abcdef0123456789abcdef")]
    [InlineData("shops/42/0123456789abcdef0123456789abcdeg")]
    [InlineData("/0123456789abcdef0123456789abcdef")]
    [InlineData("shops/NuL/0123456789abcdef0123456789abcdef")]
    public async Task RejectsUntrustedKeys(string key)
    {
        var store = new LocalMediaStore(NewRoot());
        await Assert.ThrowsAsync<ArgumentException>(() => store.OpenReadAsync(key));
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(key));
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "dotnet-media-tests", Guid.NewGuid().ToString("N"));

    private sealed class FailingStream : Stream
    {
        private readonly IOException failure;
        public FailingStream(IOException failure) => this.failure = failure;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw failure;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(failure);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
