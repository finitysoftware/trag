using System.Security.AccessControl;
using System.Security.Principal;
using Trag.TestSupport;
using Trag.Application.Ports.Catalog;
using Trag.Domain.Presentation.Text;
using Trag.Infrastructure.Git.Catalog;
using Trag.Infrastructure.Git.Processes;
using Trag.Infrastructure.Platform.Storage;

namespace Trag.IntegrationTests.Git.Catalog;

// Every clone Trag turns down before or during Git, with the sentence the picker shows. A refused clone never leaves a
// folder behind that the caller did not ask for.
public sealed class CloneRefusalTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "trag-fixture-" + Guid.NewGuid().ToString("N"));
    private readonly CancellationTokenSource timeout = new(TimeSpan.FromSeconds(120));
    private readonly IRepositoryCatalog catalog;

    public CloneRefusalTests()
    {
        Directory.CreateDirectory(root);
        catalog = new RepositoryCatalogFacade(new GitProcessRunner(), new RepositoryCatalogStore(Path.Combine(root, "repositories.json")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--upload-pack=calc.exe")]
    [InlineData("https://example.invalid/repo.git\nfetch")]
    public async Task WhenTheAddressIsOneGitMustNotSee_ShouldRefuseTheCloneBeforeGitRuns(string address)
    {
        var result = await catalog.CloneAsync(new(address, Path.Combine(root, "target")), timeout.Token);
        Assert.False(result.Succeeded);
        Assert.Equal("The repository address is not valid.", result.Message);
        Assert.False(Directory.Exists(Path.Combine(root, "target")));
    }

    [Fact]
    public async Task WhenTheAddressIsLongerThanTheLimit_ShouldRefuseTheCloneBeforeGitRuns()
    {
        var result = await catalog.CloneAsync(new("https://example.invalid/" + new string('x', 2048), Path.Combine(root, "target")), timeout.Token);
        Assert.False(result.Succeeded);
        Assert.Equal("The repository address is not valid.", result.Message);
    }

    [Fact]
    public async Task WhenTheCloneTargetIsBlank_ShouldAskForALocalFolder()
    {
        var source = await SourceAsync();
        Assert.Equal("Enter a local folder for the clone.", (await catalog.CloneAsync(new(source, "  "), timeout.Token)).Message);
    }

    [Fact]
    public async Task WhenAFileExistsAtTheCloneTarget_ShouldAskForAFolder()
    {
        var source = await AfterABlankTargetAsync();
        var file = Path.Combine(root, "occupied");
        await File.WriteAllTextAsync(file, "not a folder\n", timeout.Token);
        Assert.Equal("A file already exists at this path. Choose a folder.", (await catalog.CloneAsync(new(source, file), timeout.Token)).Message);
    }

    [Fact]
    public async Task WhenTheCloneTargetFolderIsNotEmpty_ShouldAskForAnEmptyOneAndKeepItsFiles()
    {
        var source = await AfterAnOccupiedTargetAsync();
        var used = Directory.CreateDirectory(Path.Combine(root, "used")).FullName;
        await File.WriteAllTextAsync(Path.Combine(used, "keep.txt"), "mine\n", timeout.Token);
        Assert.Equal("Choose an empty or new folder for the clone.", (await catalog.CloneAsync(new(source, used), timeout.Token)).Message);
        Assert.True(File.Exists(Path.Combine(used, "keep.txt")));
    }

    [Fact]
    public async Task WhenGitCannotBeStarted_ShouldSayGitWasNotFoundAndLeaveNoFolder()
    {
        var withoutGit = new RepositoryCatalogFacade(new GitProcessRunner(executable: new() { Path = Path.Combine(root, "missing", "git.exe") }),
            new RepositoryCatalogStore(Path.Combine(root, "repositories.json")));
        var target = Path.Combine(root, "clones", "without-git");

        var result = await withoutGit.CloneAsync(new("https://example.invalid/repo.git", target), timeout.Token);

        Assert.Equal((false, SessionText.Error(Domain.Errors.RepositoryError.GitMissing)), (result.Succeeded, result.Message));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public async Task WhenGitCannotOpenTheAddress_ShouldFailWithTheDetailsBelowAndLeaveNoFolder()
    {
        var target = Path.Combine(root, "clones", "missing-source");
        var result = await catalog.CloneAsync(new(Path.Combine(root, "no-such-repository"), target), timeout.Token);
        Assert.False(result.Succeeded);
        Assert.Equal("Git could not clone this repository. Review the details below, then retry.", result.Message);
        // Git says it in its own words, which differ between its builds; the details carry them, naming the source.
        Assert.Contains("fatal:", result.Details, StringComparison.Ordinal);
        Assert.Contains("no-such-repository", result.Details, StringComparison.Ordinal);
        Assert.False(Directory.Exists(target));
    }

    // A folder the user may not write in (a system folder, a share, a folder of another account): Git cannot create the
    // clone there and says "Permission denied", which is no refused sign-in.
    [Fact]
    public async Task WhenTheParentFolderRefusesWriting_ShouldSayGitCouldNotCreateTheFolder()
    {
        if (!OperatingSystem.IsWindows()) return;
        var source = await SourceAsync();
        var parent = Directory.CreateDirectory(Path.Combine(root, "read-only"));
        var denied = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.CreateDirectories | FileSystemRights.CreateFiles,
            AccessControlType.Deny);
        var security = parent.GetAccessControl();
        security.AddAccessRule(denied);
        parent.SetAccessControl(security);
        try
        {
            var result = await catalog.CloneAsync(new(source, Path.Combine(parent.FullName, "clone")), timeout.Token);

            Assert.Equal((false, "Git could not create the folder for the clone. Choose another folder, then retry."), (result.Succeeded, result.Message));
            Assert.Contains("could not create work tree dir", result.Details, StringComparison.Ordinal);
        }
        finally
        {
            security.RemoveAccessRule(denied);
            parent.SetAccessControl(security);
        }
    }

    [Fact]
    public async Task WhenTheCloneIsCancelledAsGitStarts_ShouldSayItWasCanceled()
    {
        var source = await SourceAsync();
        using var cancel = new CancellationTokenSource();
        var result = await catalog.CloneAsync(new(source, Path.Combine(root, "clones", "canceled"), Progress: new CancelOnFirstReport(cancel)), cancel.Token);
        Assert.False(result.Succeeded);
        Assert.Equal("Clone canceled.", result.Message);
    }

    // Cancelled while Git writes the files, the clone takes back what Git wrote: a folder it made goes, an empty one that
    // was there stays empty, so the same clone can start again at once.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenTheCloneIsCancelledWhileGitWritesTheFiles_ShouldLeaveTheTargetAsItWasSoTheSameCloneCanStartAgain(bool existed)
    {
        var source = await ManyFilesSourceAsync();
        var target = Path.Combine(root, "clones", "canceled-midway");
        if (existed) Directory.CreateDirectory(target);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var watching = Task.CompletedTask;
        var started = new OnReport(_ => watching = CancelOnceFilesAppearAsync(target, cancel));

        var result = await catalog.CloneAsync(new(source, target, Progress: started), cancel.Token);
        await watching;

        Assert.Equal(CloneText.Canceled, result.Message);
        Assert.Equal(existed, Directory.Exists(target));
        Assert.True(!existed || !Directory.EnumerateFileSystemEntries(target).Any());
    }

    private static async Task CancelOnceFilesAppearAsync(string target, CancellationTokenSource cancel)
    {
        while (!Directory.Exists(target) || !Directory.EnumerateFiles(target).Any()) await Task.Delay(5, cancel.Token);
        await cancel.CancelAsync();
    }

    private sealed class OnReport(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    // A source whose one commit holds 20,000 files of one blob: made in a moment, and slow to check out.
    private async Task<string> ManyFilesSourceAsync()
    {
        var source = await SourceAsync();
        var runner = new GitProcessRunner();
        var blob = System.Text.Encoding.ASCII.GetString((await runner.RunAsync(source, ["hash-object", "-w", "--stdin"], timeout.Token,
            standardInput: "same\n"u8.ToArray())).StandardOutput).Trim();
        var index = string.Concat(Enumerable.Range(0, 20_000).Select(i => $"100644 {blob}\tmany/file-{i:00000}.txt\n"));
        Assert.Equal(0, (await runner.RunAsync(source, ["update-index", "--index-info"], timeout.Token, standardInput: System.Text.Encoding.ASCII.GetBytes(index))).ExitCode);
        Assert.Equal(0, (await runner.RunAsync(source, ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "-m", "Many"], timeout.Token)).ExitCode);
        return source;
    }

    // Git reports "Cloning repository…" before it starts; cancelling from there cancels the run itself, not the request.
    private sealed class CancelOnFirstReport(CancellationTokenSource source) : IProgress<string>
    {
        public void Report(string value) => source.Cancel();
    }

    private async Task<string> SourceAsync()
    {
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        await catalog.InitializeAsync(source, timeout.Token);
        var runner = new GitProcessRunner();
        await File.WriteAllTextAsync(Path.Combine(source, "file.txt"), "content\n", timeout.Token);
        Assert.Equal(0, (await runner.RunAsync(source, ["add", "--", "file.txt"], timeout.Token)).ExitCode);
        Assert.Equal(0, (await runner.RunAsync(source, ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "-m", "Initial"], timeout.Token)).ExitCode);
        return source;
    }

    // The blank target was refused first.
    private async Task<string> AfterABlankTargetAsync()
    {
        var source = await SourceAsync();
        await catalog.CloneAsync(new(source, "  "), timeout.Token);
        return source;
    }

    // The blank target, then a file at the target, were refused first.
    private async Task<string> AfterAnOccupiedTargetAsync()
    {
        var source = await AfterABlankTargetAsync();
        var file = Path.Combine(root, "occupied");
        await File.WriteAllTextAsync(file, "not a folder\n", timeout.Token);
        await catalog.CloneAsync(new(source, file), timeout.Token);
        return source;
    }

    // A server that takes the connection and then says nothing (a stalled proxy, a host behind a dead VPN): fetch, pull
    // and push stop after the quiet limit and say so, and a clone has to as well, or the dialog waits until the user
    // presses Cancel. The clone here gets a quiet limit of two seconds, as SyncQuietTests gives a fetch.
    [Fact]
    public async Task WhenTheServerAcceptsTheConnectionAndNeverAnswers_ShouldStopTheCloneByItselfAfterTheQuietLimit()
    {
        var quiet = new RepositoryCatalogFacade(new GitProcessRunner(), new RepositoryCatalogStore(Path.Combine(root, "repositories.json")),
            quietLimit: TimeSpan.FromSeconds(2));
        using var silent = new SilentServer();
        using var patience = new CancellationTokenSource(TimeSpan.FromMinutes(1));

        var result = await quiet.CloneAsync(new(silent.Url("silent.git"), Path.Combine(root, "target")), patience.Token);

        Assert.False(patience.IsCancellationRequested, "The clone ran until the test canceled it: " + result.Message);
        Assert.Equal((false, CloneText.TimedOut), (result.Succeeded, result.Message));
        Assert.False(Directory.Exists(Path.Combine(root, "target")));
        Assert.True(silent.Connected.IsCompleted);
    }

    public void Dispose()
    {
        timeout.Cancel();
        timeout.Dispose();
        FixtureDirectory.Delete(root, "trag-fixture-");
    }
}
