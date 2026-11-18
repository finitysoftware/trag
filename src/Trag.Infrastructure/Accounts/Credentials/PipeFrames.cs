using System.Buffers.Binary;

namespace Trag.Infrastructure.Accounts.Credentials;

public static class PipeFrames
{
    public const int MaximumLength = 16_384;
    public static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        if (bytes.Length > MaximumLength) throw new InvalidDataException("IPC frame too large.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
    public static async Task<byte[]> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 0 or > MaximumLength) throw new InvalidDataException("Invalid IPC frame size.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        return bytes;
    }
}
