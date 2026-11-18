using Trag.Application.Diff;
using Trag.Desktop.Composition;
using Trag.Application.Ports.Catalog;
using Trag.Application.Ports.Git;
using Trag.Application.Ports.Storage;
using Trag.Application.Ports.Threading;
using Trag.Application.Ports.Workspace;
using Trag.Application.Workspace;
using Trag.Domain.Status;
using Trag.ViewModels.Shell;

namespace Trag.TestSupport;

// The one way to build a shell over the real RepositoryWorkspace and the shared fakes: a test sets the fakes it cares
// about, asks for the shell and drives it. The App tests derive the window and layout helpers from this class.
public class ShellHarness
{
    public FakeRepositoryReader Reader { get; } = new();
    public FakeGitWriter Writer { get; } = new();
    public FakeHistoryReader History { get; } = new();
    public FakeBranchReader Branches { get; } = new() { Local = { "main" } };
    public FakeStashReader Stashes { get; } = new();
    public FakeGitSync Sync { get; } = new();
    public FakeFileDiffReader Diff { get; } = new();
    public FakeRepositoryWatcher Watcher { get; } = new();
    public FakeRepositoryCatalog Catalog { get; } = new();
    public FakeLastRepositoryStore Store { get; } = new();
    public FakeLayoutStore Preferences { get; } = new();
    public FakeGitInstallations Git { get; } = new();
    public FakeGitChoiceStore GitChoice { get; } = new();
    // Inline by default; a test that has to hold a UI hop sets a QueuedDispatcher, the App tests the real one.
    public IViewDispatcher Dispatcher { get; set; } = new ImmediateViewDispatcher();
    // The session's clock; a test that tells or schedules by time sets a ManualTime.
    public TimeProvider Time { get; set; } = TimeProvider.System;

    // watch hands the workspace the fake watcher so a test can push live changes; diffBuilder replaces the presentation
    // builder for a test that holds a highlight.
    public ShellViewModel Shell(bool watch = false, bool withDiff = true, IDiffPresentationBuilder? diffBuilder = null) =>
        ShellFactory.Compose(Catalog, Store, Git, new RepositoryWorkspace(Reader, watch ? Watcher : null, withDiff ? Diff : null, Writer, History, Sync, Branches, Stashes),
            Dispatcher, diffBuilder, Preferences, Time, GitChoice);

    // A shell over any workspace, for a test that brings its own: the parts it leaves out are fakes with nothing in them.
    public static ShellViewModel Compose(IRepositoryWorkspace? workspace = null, ILastRepositoryStore? settings = null, IViewDispatcher? dispatcher = null,
        IDiffPresentationBuilder? diffBuilder = null, ILayoutStore? layout = null, IRepositoryCatalog? catalog = null, TimeProvider? time = null,
        IGitInstallations? git = null, IGitChoiceStore? gitChoice = null) =>
        ShellFactory.Compose(catalog ?? new FakeRepositoryCatalog(), settings ?? new FakeLastRepositoryStore(), git ?? new FakeGitInstallations(), workspace, dispatcher,
            diffBuilder, layout, time, gitChoice);

    public RepositorySnapshot Synced(params FileStatus[] files) =>
        Snapshots.Create(files: files, remotes: [Snapshots.Remote()], trackingRemote: "origin", upstream: "origin/main");

    // For a flow the test cannot await through a completion task (the live watcher loop, a window closing): the
    // condition is polled until it holds, with a deadline that names what was waited for.
    public static async Task Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Timed out waiting for " + what);
            await Task.Delay(20);
        }
    }
}
