using Trag.Infrastructure.Git.Write;
using Trag.TestSupport;

namespace Trag.Infrastructure.UnitTests.Git.Write;

// The editor is chosen in Git's own order, and the environment is injected here so every source can be tested. The
// core.editor step runs against a real repository, because that value comes from Git itself.
public sealed class GitEditorResolverTests
{
    private static async Task<(string? Editor, string? Rejection)> ResolveAsync(GitFixture fixture, Dictionary<string, string?> environment) =>
        await new GitEditorResolver(new(fixture.Runner), key => environment.GetValueOrDefault(key)).ResolveAsync(fixture.Root, fixture.Token);

    private static Dictionary<string, string?> EveryVariable() =>
        new() { ["GIT_EDITOR"] = "from-git-editor", ["VISUAL"] = "from-visual", ["EDITOR"] = "from-editor" };

    // Git's order walked from the top: every variable set and resolved, then GIT_EDITOR taken away.
    private static async Task<Dictionary<string, string?>> WithoutGitEditorAsync(GitFixture fixture)
    {
        var all = EveryVariable();
        await ResolveAsync(fixture, all);
        all.Remove("GIT_EDITOR");
        return all;
    }

    // ...then core.editor set and resolved.
    private static async Task<Dictionary<string, string?>> WithCoreEditorAsync(GitFixture fixture)
    {
        var all = await WithoutGitEditorAsync(fixture);
        await fixture.GitAsync("config", "core.editor", "from-core-editor");
        await ResolveAsync(fixture, all);
        return all;
    }

    // ...then core.editor unset again and VISUAL resolved.
    private static async Task<Dictionary<string, string?>> WithoutCoreEditorAsync(GitFixture fixture)
    {
        var all = await WithCoreEditorAsync(fixture);
        await fixture.GitAsync("config", "--unset", "core.editor");
        await ResolveAsync(fixture, all);
        return all;
    }

    [Fact]
    public async Task WhenEverySourceIsSet_ShouldUseGitEditor()
    {
        using var fixture = await GitFixture.CreateAsync();
        var all = EveryVariable();
        Assert.Equal("from-git-editor", (await ResolveAsync(fixture, all)).Editor);
    }

    [Fact]
    public async Task WhenGitEditorIsNotSet_ShouldUseCoreEditorBeforeVisualAndEditor()
    {
        using var fixture = await GitFixture.CreateAsync();
        var all = await WithoutGitEditorAsync(fixture);
        await fixture.GitAsync("config", "core.editor", "from-core-editor");
        Assert.Equal("from-core-editor", (await ResolveAsync(fixture, all)).Editor);
    }

    [Fact]
    public async Task WhenCoreEditorIsUnsetToo_ShouldUseVisualBeforeEditor()
    {
        using var fixture = await GitFixture.CreateAsync();
        var all = await WithCoreEditorAsync(fixture);
        await fixture.GitAsync("config", "--unset", "core.editor");
        Assert.Equal("from-visual", (await ResolveAsync(fixture, all)).Editor);
    }

    [Fact]
    public async Task WhenOnlyEditorIsLeft_ShouldUseEditor()
    {
        using var fixture = await GitFixture.CreateAsync();
        var all = await WithoutCoreEditorAsync(fixture);
        all.Remove("VISUAL");
        Assert.Equal("from-editor", (await ResolveAsync(fixture, all)).Editor);
    }

    // Which editors need one is Domain's EditorCommands.
    [Fact]
    public async Task WhenTheEditorNeedsATerminal_ShouldRefuseItAndAskForAGraphicalOne()
    {
        using var fixture = await GitFixture.CreateAsync();
        var (resolved, rejection) = await ResolveAsync(fixture, new() { ["GIT_EDITOR"] = "\"C:\\Program Files\\Vim\\vim91\\vim.exe\"" });
        Assert.Null(resolved);
        Assert.Equal("The configured editor needs a terminal. Configure a graphical Git core.editor with its wait option.", rejection);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("code\0--wait")]
    public async Task WhenTheEditorValueIsBlankOrHasANul_ShouldRefuseItWithTheGeneralHelp(string editor)
    {
        using var fixture = await GitFixture.CreateAsync();
        var (resolved, rejection) = await ResolveAsync(fixture, new() { ["GIT_EDITOR"] = editor });
        Assert.Null(resolved);
        Assert.Equal("Configure a graphical Git core.editor with its wait option (for example, code --wait), then try again.", rejection);
    }

    [Fact]
    public async Task WhenNoEditorIsConfigured_ShouldRefuseWithTheSameGeneralHelp()
    {
        using var fixture = await GitFixture.CreateAsync();
        var (resolved, rejection) = await ResolveAsync(fixture, []);
        Assert.Null(resolved);
        Assert.Equal("Configure a graphical Git core.editor with its wait option (for example, code --wait), then try again.", rejection);
    }

    [Fact]
    public async Task WhenTheEditorIsGraphicalWithItsWaitOption_ShouldUseIt()
    {
        using var fixture = await GitFixture.CreateAsync();
        const string editor = "\"C:\\Program Files\\Microsoft VS Code\\Code.exe\" --wait";
        Assert.Equal(editor, (await ResolveAsync(fixture, new() { ["GIT_EDITOR"] = editor })).Editor);
    }
}
