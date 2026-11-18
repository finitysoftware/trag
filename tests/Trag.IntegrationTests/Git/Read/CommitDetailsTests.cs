using Trag.Domain.History;
using Trag.Infrastructure.Git.Read;
using Trag.TestSupport;

namespace Trag.IntegrationTests.Git.Read;

// One commit's details as Trag reads them from real Git: its message, the check of its signature and its files.
public sealed class CommitDetailsTests
{
    // Git waits for the program that checks an OpenPGP signature, which can wait on its agent or a key server for good; the
    // commit is still shown after the limit, without the check and saying it did not finish.
    [Fact]
    public async Task WhenGpgHangsWhileVerifyingTheSignature_ShouldShowTheDetailsAndSayTheCheckDidNotFinishAfterTheLimit()
    {
        using var f = await GitFixture.CreateAsync("main");
        await f.WriteAsync("a.txt", "a\n");
        await f.CommitAsync("Add");
        var tree = await f.GitAsync("rev-parse", "HEAD^{tree}");
        await File.WriteAllTextAsync(Path.Combine(f.Container, "signed-commit"), "tree " + tree + "\nauthor A <a@example.invalid> 1700000000 +0000\n"
            + "committer A <a@example.invalid> 1700000000 +0000\ngpgsig -----BEGIN PGP SIGNATURE-----\n \n iQEzBAABCAAdFiEE\n -----END PGP SIGNATURE-----\n\nSigned\n", f.Token);
        var signed = await f.GitAsync("hash-object", "-t", "commit", "-w", Path.Combine(f.Container, "signed-commit"));
        var gpg = Path.Combine(f.Container, "hanging-gpg.sh");
        await ShellScript.WriteAsync(gpg, "#!/bin/sh\nsleep 40\n", f.Token);
        await f.GitAsync("config", "gpg.program", gpg.Replace('\\', '/'));

        var details = await new CliHistoryReader(f.Runner).ReadCommitAsync(f.Root, signed, f.Token);

        Assert.Equal(("Signed\n", new CommitSignature(SignatureCheck.NotFinished, "")), (details.Message, details.Signature));
        Assert.Equal(["a.txt"], details.Files.Select(file => file.Path.Display));
    }
}
