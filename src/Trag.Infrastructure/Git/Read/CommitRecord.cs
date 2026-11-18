using System.Text;
using Trag.Domain.History;
using Trag.Infrastructure.Git.Parsing;

namespace Trag.Infrastructure.Git.Read;

// One commit as Git prints it for history: six NUL-separated fields, always in this order, the author as .mailmap names
// them as the history page shows them. The row the list shows is clipped the way the history page clips it; the whole
// message stays on the record.
internal sealed record CommitRecord(HistoryCommit Summary, string Message)
{
    public const string Format = "--format=%H%x00%P%x00%aN%x00%aE%x00%aI%x00%B";
    public const int Fields = 6;

    // The same record with Git's check of its signature in front: the letter (%G?) and the signer (%GS).
    public static readonly string SignedFormat = "--format=%G?%x00%GS%x00" + Format["--format=".Length..];
    public const int SignedFields = Fields + 2;

    // One whole record as the record stream hands it out with all six fields. Where the fields end is decided by the
    // same parser the history page uses, so the two never disagree about the format.
    public static CommitRecord Parse(ReadOnlySpan<byte> record)
    {
        Span<Range> fields = stackalloc Range[Fields];
        HistoryRecordParser.Fields(record, fields);
        var message = record[fields[5]];
        return new(CommitSummary.Of(record[fields[0]], record[fields[1]], record[fields[2]], record[fields[3]], record[fields[4]], message),
            Encoding.UTF8.GetString(message));
    }

    public static (CommitRecord Record, CommitSignature Signature) ParseSigned(ReadOnlySpan<byte> record, string errors)
    {
        var letter = record.IndexOf((byte)0);
        var signer = letter + 1 + record[(letter + 1)..].IndexOf((byte)0);
        return (Parse(record[(signer + 1)..]), CommitSignature.FromGit(Encoding.UTF8.GetString(record[..letter]), Encoding.UTF8.GetString(record[(letter + 1)..signer]), errors));
    }
}
