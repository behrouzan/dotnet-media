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

    [Theory]
    [InlineData("../secret")]
    [InlineData("00000000000000000000000000000000/../")]
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
