namespace Trag.Infrastructure.Git.Processes;

// NUL terminated records read straight from a Git stream, a record at a time and never the whole output. A record that
// sits inside the buffer is handed out without a copy and is borrowed until the next call; one that spans buffer
// refills is assembled, still against the limit. Retain only selected records.
public sealed class GitRecordStream(Stream stream, int limit = 2 * 1024 * 1024)
{
    private readonly byte[] buffer = new byte[16 * 1024];
    private int offset, length;

    // A multi-field record is the bytes up to its last field's NUL: the fields in between keep their own NULs.
    public async ValueTask<ReadOnlyMemory<byte>?> ReadAsync(CancellationToken ct, int fields = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fields, 1);
        MemoryStream? partial = null;
        var remaining = fields;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (offset == length && !await FillAsync(ct).ConfigureAwait(false))
                {
                    if (partial?.Length > 0) throw new FormatException("Incomplete Git record.");
                    return null;
                }
                var end = EndOfRecord(ref remaining);
                var count = (end < 0 ? length : end) - offset;
                if ((partial?.Length ?? 0) + count > limit) throw new GitOutputLimitException();
                if (partial is null && end >= 0)
                {
                    var value = buffer.AsMemory(offset, count);
                    offset = end + 1;
                    return value;
                }
                partial ??= new();
                partial.Write(buffer, offset, count);
                offset += count;
                if (end < 0) continue;
                offset++;
                return partial.ToArray();
            }
        }
        finally { partial?.Dispose(); }
    }

    private async ValueTask<bool> FillAsync(CancellationToken ct)
    {
        length = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
        offset = 0;
        return length != 0;
    }

    // The index of the NUL that ends the record, counting down the fields still to pass, or -1 when the buffer runs
    // out first; the fields passed so far stay counted for the next buffer.
    private int EndOfRecord(ref int remaining)
    {
        var scan = offset;
        while (true)
        {
            var end = Array.IndexOf(buffer, (byte)0, scan, length - scan);
            if (end < 0) return -1;
            if (--remaining == 0) return end;
            scan = end + 1;
        }
    }
}
