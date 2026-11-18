using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using Trag.Domain.Accounts;
using Trag.Domain.Diff;
using Trag.Domain.Errors;
using Trag.Domain.Merge;
using Trag.Domain.Naming;
using Trag.Domain.Rebase;
using Trag.Domain.Status;
using Trag.Domain.Sync;
using Trag.Infrastructure.Accounts.Credentials;
using Trag.Infrastructure.Accounts.Providers;
using Trag.Infrastructure.Git.Parsing;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Git.Read;
using Trag.Infrastructure.UnitTests.Accounts;

namespace Trag.Infrastructure.UnitTests.Git.Parsing;

// One parser as Trag reaches it: the entry point its caller uses, fed bytes as they arrive from Git, a pipe or a provider,
// and the exceptions the callers handle as a refusal. A string parser gets the bytes decoded leniently, invalid UTF-8 as
// replacement characters, so the decoding is not what is tested. No target allows the ArgumentException family.
internal sealed record ParserFuzzTarget(Action<byte[]> Read, Type[] Allowed, byte[][] Samples)
{
    public bool Allows(Exception error) => Allowed.Any(type => type.IsInstanceOfType(error));
}

// The valid samples are only ever appended to, so that a seed recorded for a sample keeps naming the same input.
internal static class ParserFuzzTargets
{
    private const string First = "0c98993c3923e3f8b15c6cdc7fc63dada39bfed2";
    private const string Second = "968803de2d6ae0667a52c6eb1b736aef4088c64b";
    private const string Third = "f12a8e6bac11659a85bcc53073bcbf6b3743e5ce";
    private const string Oid = "4d7a214614ab2935c943f9e0ff69d22eadbb8f32b1258daaa5e2ca24d17e2393";
    private const string OtherOid = "2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae";

    private static readonly Type[] Format = [typeof(FormatException)];
    private static readonly Type[] Account = [typeof(AccountException)];
    private static readonly Type[] Nothing = [];
    private static readonly Uri GitHubApi = new("https://api.github.com/");
    private static readonly Uri GitLabApi = new("https://gitlab.example/api/v4/");

    public static readonly IReadOnlyDictionary<string, ParserFuzzTarget> All = new Dictionary<string, ParserFuzzTarget>
    {
        ["Status"] = new(bytes => StatusParser.Parse(bytes), Format,
        [
            Sample("# branch.oid " + Third + "\0# branch.head main\0# branch.upstream origin/main\0"
                + "1 .M N... 100644 100644 100644 " + First + " " + First + " a.txt\0"
                + "2 RM N... 100644 100644 100644 " + Second + " " + Second + " R100 d.txt\0b.txt\0"
                + "u UU N... 100644 100644 100644 100644 " + First + " " + Second + " " + Third + " conflict.txt\0"
                + "1 .M SCMU 160000 160000 160000 " + First + " " + First + " sub\0? c.txt\0! bin/\0"),
        ]),
        ["RawDiff"] = new(bytes => FileDiffParser.Metadata(bytes), Format,
        [
            Sample(":100644 100644 " + First + " " + Second + " M\0a.txt\0:000000 100644 " + new string('0', 40) + " " + Third + " A\0new file.txt\0"),
        ]),
        ["Patch"] = new(bytes => LfsPointer.Sides(FileDiffParser.Patch([], bytes, false).Lines), Nothing,
        [
            Sample("diff --git a/a.txt b/a.txt\nindex 0c98993..968803d 100644\n--- a/a.txt\n+++ b/a.txt\n@@ -1,3 +1,4 @@\n one\n-two\r\n+TWO\r\n three\n\n+four\n\\ No newline at end of file\n"),
            Sample("diff --git a/b.txt b/b.txt\ndeleted file mode 100644\nindex 968803d..0000000\n--- a/b.txt\n+++ /dev/null\n@@ -1,2 +0,0 @@\n-one\n-two\n"
                + "diff --git a/d.txt b/d.txt\nnew file mode 100644\nindex 0000000..968803d\n--- /dev/null\n+++ b/d.txt\n@@ -0,0 +1,2 @@\n+one\n+two\n"),
            Sample("diff --git a/big.bin b/big.bin\nindex 1a2b3c4..5d6e7f8 100644\n--- a/big.bin\n+++ b/big.bin\n@@ -1,3 +1,3 @@\n version https://git-lfs.github.com/spec/v1\n"
                + "-oid sha256:" + Oid + "\n-size 12\n+oid sha256:" + OtherOid + "\n+size 13\n"),
            Sample("diff --git a/run.sh b/run.sh\nold mode 100644\nnew mode 100755\ndiff --git a/x.png b/x.png\nindex 1a2b3c4..5d6e7f8 100644\nBinary files a/x.png and b/x.png differ\n"),
            // From the mutation pass (2.7.3): Git's "\ No newline at end of file" with no row before it.
            Sample("\\ No newline at end of file\n-a\n"),
        ]),
        ["NewContent"] = new(bytes => FileDiffParser.NewContent([], bytes, false, false), Nothing,
        [
            Sample("one\r\ntwo\rthree\n\nfour"),
        ]),
        ["HistoryRecord"] = new(bytes =>
        {
            foreach (var query in new[] { "č", "e", "" }) new HistoryRecordFilter(query, 0).Match(bytes);
        }, Format,
        [
            Sample(Third + "\0" + Second + " " + First + "\0Ana\0ana@example.com\02023-11-14T23:13:20+01:00\0HEAD -> refs/heads/main, refs/remotes/origin/main, tag: refs/tags/v1"
                + "\0Ana Č\0ana@example.com\0Third\r\n\nBody\n"),
            Sample(First + "\0\0Bo\0bo@example.com\02023-11-14T23:13:20+01:00\0\0Bo\0bo@example.com\0First\n"),
        ]),
        ["CommitRecord"] = new(bytes => CommitRecord.Parse(bytes), Format,
        [
            Sample(Third + "\0" + Second + " " + First + "\0Ana\0ana@example.com\02023-11-14T23:13:20+01:00\0Third\n\nBody\n"),
        ]),
        ["SignedCommitRecord"] = new(bytes => CommitRecord.ParseSigned(bytes, ""), Format,
        [
            Sample("G\0Ana <ana@example.com>\0" + Third + "\0" + Second + "\0Ana\0ana@example.com\02023-11-14T23:13:20+01:00\0Third\n\nBody\n"),
        ]),
        ["CommitRefs"] = new(bytes => CommitRefParser.Parse(bytes), Nothing,
        [
            Sample("HEAD -> refs/heads/main, refs/remotes/origin/main, refs/remotes/origin/HEAD, tag: refs/tags/v1.0, refs/heads/feature/č, refs/stash"),
            Sample("HEAD, tag: refs/tags/v2"),
        ]),
        ["Blame"] = new(bytes => BlamePorcelain.Parse(bytes), Format,
        [
            Sample(First + " 1 1 1\nauthor Ana\nauthor-mail <ana@example.com>\nauthor-time 1700000000\nauthor-tz +0100\ncommitter Ana\ncommitter-mail <ana@example.com>\n"
                + "committer-time 1700000000\ncommitter-tz +0100\nsummary First\nboundary\nfilename a.txt\n\tone\n"
                + Second + " 2 2 1\nauthor Bo\nauthor-mail <bo@example.com>\nauthor-time 1793339651\nauthor-tz +0100\ncommitter Bo\ncommitter-mail <bo@example.com>\n"
                + "committer-time 1793339651\ncommitter-tz +0100\nsummary Second\nprevious " + First + " a.txt\nfilename a.txt\n\tTWO\n"
                + First + " 3 3 1\n\tthree\r\n"
                + Second + " 4 4 1\n\tfour\n"
                + Third + " 5 5 1\nauthor Ana\nauthor-mail <ana@example.com>\nauthor-time 1793339651\nauthor-tz +0100\ncommitter Ana\ncommitter-mail <ana@example.com>\n"
                + "committer-time 1793339651\ncommitter-tz +0100\nsummary Third\nprevious " + Second + " a.txt\nfilename \"b\\\"c.txt\"\n\tfive\n"),
        ]),
        ["BranchRefs"] = new(bytes =>
        {
            foreach (var row in Text(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries)) BranchRefParser.Parse(row);
        }, Format,
        [
            Sample("refs/heads/main\0" + Third + "\0refs/remotes/origin/main\0\0*\nrefs/heads/feature\0" + Second + "\0\0\0 \n"
                + "refs/remotes/origin/HEAD\0" + Third + "\0\0refs/remotes/origin/main\0 \nrefs/remotes/origin/main\0" + Third + "\0\0\0 \n"),
        ]),
        ["RemoteRefs"] = new(bytes =>
        {
            foreach (var row in Text(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries)) RemoteRefParser.Parse(row);
        }, Format,
        [
            Sample("refs/remotes/origin/HEAD\0" + Third + "\0refs/remotes/origin/main\nrefs/remotes/origin/main\0" + Third + "\0\n"),
        ]),
        ["StashList"] = new(bytes =>
        {
            foreach (var entry in StashListParser.Parse(Text(bytes))) _ = entry.Title;
        }, Format,
        [
            Sample(First + "\0WIP on main: f12a8e6 Third\01793339665\n" + Second + "\0On feature: work in progress\01793264400\n"),
        ]),
        ["Shortlog"] = new(bytes => ShortlogParser.Parse(Text(bytes)), Format,
        [
            Sample("     2\tAna <ana@example.com>\n     1\tBo [ops] <bo@example.com>\n"),
        ]),
        ["GitConfig"] = new(bytes =>
        {
            var configuration = new GitConfiguration(GitConfigParser.Parse(bytes));
            _ = (configuration.LfsInstalled, configuration.Signing, configuration.Flag("core.sparsecheckout"),
                configuration.RemoteNames().ToArray(), configuration.TransportOptions("origin"), configuration.Values("remote.origin.fetch"));
        }, Format,
        [
            Sample("user.name\nAna\0remote.origin.url\nhttps://example.com/team/repo.git\0remote.origin.fetch\n+refs/heads/*:refs/remotes/origin/*\0"
                + "remote.origin.proxy\nhttp://proxy.example:8080\0commit.gpgsign\ntrue\0gpg.format\nssh\0filter.lfs.process\ngit-lfs filter-process\0core.sparsecheckout\0"),
        ]),
        ["GitVersion"] = new(bytes => GitVersion.Parse(GitBytes.FirstLine(bytes)), [typeof(RepositoryException)],
        [
            Sample("git version 2.55.0.windows.3\n"),
            Sample("git version 2.39.3 (Apple Git-146)\n"),
        ]),
        ["Records"] = new(bytes => ReadRecords(bytes, 1), [typeof(FormatException), typeof(GitOutputLimitException)],
        [
            Sample("100644 " + First + " 0\ta.txt\0160000 " + Second + " 0\tsub\0"),
        ]),
        ["FieldRecords"] = new(bytes => ReadRecords(bytes, HistoryRecordFilter.Fields), [typeof(FormatException), typeof(GitOutputLimitException)],
        [
            Sample(Third + "\0" + Second + "\0Ana\0ana@example.com\02023-11-14T23:13:20+01:00\0HEAD -> refs/heads/main\0Ana\0ana@example.com\0Third\n\0"
                + First + "\0\0Bo\0bo@example.com\02023-11-14T23:13:20+01:00\0\0Bo\0bo@example.com\0First\n\0"),
        ]),
        ["LfsPaths"] = new(bytes => LfsFilterRecords.PathsAsync(new MemoryStream(bytes), CancellationToken.None).GetAwaiter().GetResult(), Format,
        [
            Sample("100644 " + First + " 0\ta.txt\0100644 " + Second + " 0\tbig.bin\0160000 " + Third + " 0\tsub\0"),
        ]),
        ["LfsAttributes"] = new(bytes => LfsFilterRecords.CountLfsAsync(new MemoryStream(bytes), CancellationToken.None).GetAwaiter().GetResult(), Format,
        [
            Sample("a.txt\0filter\0unspecified\0big.bin\0filter\0lfs\0"),
        ]),
        ["ConflictDocument"] = new(bytes =>
        {
            if (ConflictDocument.Parse(Text(bytes)) is not { } document) return;
            document.Parts();
            for (var block = 0; block < document.Blocks.Count; block++)
                foreach (var choice in Enum.GetValues<ConflictChoice>()) document.Take(block, choice);
        }, Nothing,
        [
            Sample("start\r\n<<<<<<< HEAD\r\nmine\r\n||||||| base\r\nbase\r\n=======\r\ntheirs\r\n>>>>>>> feature\r\nmiddle\n<<<<<<< HEAD\nx\n=======\ny\n>>>>>>> other\nend"),
        ]),
        ["RewriteTodo"] = new(bytes => RewriteTodo.Parse(Text(bytes)), Nothing,
        [
            Sample("pick 0c98993 # First\nfixup -C 968803d # Second\nexec git commit --amend --allow-empty --no-verify --cleanup=verbatim -F '/tmp/m/1.txt'\n"
                + "reword f12a8e6 Third\nlabel onto\nupdate-ref refs/heads/topic\n\n# Rebase 0c98993..f12a8e6 onto 0c98993 (3 commands)\n"),
        ]),
        ["MergeMessages"] = new(bytes =>
        {
            var text = Text(bytes);
            MergeMessages.Clean(text);
            MergeMessages.IncomingName(new RepositoryMerge(null, null, text, "", ""));
        }, Nothing,
        [
            Sample("Merge branch 'feature' into main\n\n# Conflicts:\n#\ta.txt\n#\tb.txt\n"),
            Sample("Merge remote-tracking branch 'origin/main'\r\n\r\n# ------------------------ >8 ------------------------\r\n# Do not modify or remove the line above.\r\n"
                + "# Everything below it will be ignored.\r\n#\r\n# Conflicts:\r\n#\tc.txt\r\n"),
            Sample("# This is a combination of 2 commits.\n# This is the 1st commit message:\n\nFirst\n\n# This is the commit message #2:\n\nSecond\n"),
        ]),
        ["Refspec"] = new(bytes =>
        {
            var lines = Text(bytes).Split('\n', 2);
            var mapping = RemoteRefspecMapping.Parse(lines[0], "origin");
            mapping?.SourceFor(lines[^1]);
            mapping?.DestinationFor(lines[^1]);
        }, Nothing,
        [
            Sample("+refs/heads/*:refs/remotes/origin/*\nrefs/remotes/origin/feature/x"),
            Sample("refs/heads/main:refs/remotes/origin/main\nrefs/heads/main"),
        ]),
        ["LfsPointer"] = new(bytes => LfsPointer.Parse(GitBytes.Text(bytes).Split('\n')), Nothing,
        [
            Sample("version https://git-lfs.github.com/spec/v1\noid sha256:" + Oid + "\nsize 12345\n"),
        ]),
        ["AskPass"] = new(AskPass, [typeof(InvalidDataException), typeof(EndOfStreamException), typeof(JsonException)],
        [
            Frame("{\"OperationId\":\"operation-1\",\"Prompt\":\"Username for 'https://example.com/team/repo.git': \"}"),
            Frame("{\"OperationId\":\"operation-1\",\"Prompt\":\"Password for 'https://alice@example.com/team/repo.git': \"}"),
        ]),
        ["GitHubRepositories"] = new(bytes => Provider(bytes, client => client.Repositories(AccountProvider.GitHub, GitHubApi, "token", CancellationToken.None)), Account,
        [
            Sample("[{\"full_name\":\"team/repo\",\"clone_url\":\"https://github.com/team/repo.git\",\"html_url\":\"https://github.com/team/repo\",\"default_branch\":\"main\","
                + "\"private\":true,\"pushed_at\":\"2026-10-01T12:00:00Z\",\"updated_at\":\"2026-10-02T12:00:00Z\"}]"),
        ]),
        ["GitLabRepositories"] = new(bytes => Provider(bytes, client => client.Repositories(AccountProvider.GitLab, GitLabApi, "token", CancellationToken.None)), Account,
        [
            Sample("[{\"path_with_namespace\":\"group/repo\",\"http_url_to_repo\":\"https://gitlab.example/group/repo.git\",\"web_url\":\"https://gitlab.example/group/repo\","
                + "\"default_branch\":\"main\",\"visibility\":\"internal\",\"last_activity_at\":\"2026-10-01T12:00:00.000Z\"}]"),
        ]),
        ["GitHubOwners"] = new(bytes => Provider(bytes, client => client.Owners(AccountProvider.GitHub, GitHubApi, "token", CancellationToken.None)), Account,
        [
            Sample("[{\"login\":\"team\",\"id\":7},{\"login\":\"other.org\",\"id\":8}]"),
        ]),
        ["GitLabOwners"] = new(bytes => Provider(bytes, client => client.Owners(AccountProvider.GitLab, GitLabApi, "token", CancellationToken.None)), Account,
        [
            Sample("[{\"id\":12,\"full_path\":\"group/sub\"},{\"id\":13,\"full_path\":\"other\"}]"),
        ]),
        ["GitHubIdentity"] = new(bytes => Provider(bytes, client => client.IdentityAsync(AccountProvider.GitHub, GitHubApi, "token", CancellationToken.None)), Account,
        [
            Sample("{\"id\":7,\"login\":\"alice\"}"),
        ]),
        ["GitLabIdentity"] = new(bytes => Provider(bytes, client => client.IdentityAsync(AccountProvider.GitLab, GitLabApi, "token", CancellationToken.None)), Account,
        [
            Sample("{\"id\":2,\"username\":\"bob\"}"),
        ]),
        ["GitHubDeviceCode"] = new(DeviceCodeOf, Account,
        [
            Sample("{\"device_code\":\"3584d83530557fdd1f46af8289938c8ef79f9dc5\",\"user_code\":\"WDJB-MJHT\",\"verification_uri\":\"https://github.com/login/device\","
                + "\"expires_in\":900,\"interval\":5}"),
        ]),
    };

    private static byte[] Sample(string text) => Encoding.UTF8.GetBytes(text);

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    private static void ReadRecords(byte[] bytes, int fields)
    {
        var records = new GitRecordStream(new MemoryStream(bytes), 1024);
        while (records.ReadAsync(CancellationToken.None, fields).AsTask().GetAwaiter().GetResult() is not null) { }
    }

    // A request as the askpass helper sends it: the frame's length, then the request as JSON.
    private static byte[] Frame(string json)
    {
        var request = Encoding.UTF8.GetBytes(json);
        var frame = new byte[4 + request.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, request.Length);
        request.CopyTo(frame, 4);
        return frame;
    }

    // What AskPassPipeServer does with one exchange: the frame, the request in it, and whether it may be answered.
    private static void AskPass(byte[] bytes)
    {
        var frame = PipeFrames.ReadAsync(new MemoryStream(bytes), CancellationToken.None).GetAwaiter().GetResult();
        if (JsonSerializer.Deserialize(frame, CredentialJsonContext.Default.CredentialRequest) is { } request)
            new CredentialAuthorizer(new("https://example.com/team/repo.git"), "alice", "secret"u8.ToArray(), "operation-1", CancellationToken.None).Authorize(request);
    }

    // The provider answers every request with the same body, read the way the account service reads it.
    private static void Provider(byte[] body, Func<ProviderClient, Task> read)
    {
        using var client = new ProviderClient(Answering(body));
        read(client).GetAwaiter().GetResult();
    }

    private static void DeviceCodeOf(byte[] body)
    {
        using var http = new ProviderHttpClient(Answering(body));
        var response = new GitHubTokenClient(http, TimeProvider.System).OAuthAsync("https://github.com/login/device/code", [], CancellationToken.None).GetAwaiter().GetResult();
        DeviceCode.Parse(response, DateTimeOffset.UnixEpoch);
    }

    private static AccountServiceTests.Handler Answering(byte[] body) =>
        new((_, _) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
}
