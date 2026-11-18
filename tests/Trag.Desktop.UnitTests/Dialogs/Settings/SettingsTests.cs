using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Trag.Application.Live;
using Trag.Application.Updates;
using Trag.Desktop.Dialogs;
using Trag.Desktop.Dialogs.Settings;
using Trag.Desktop.Views;
using Trag.Domain.Accounts;
using Trag.Domain.Presentation.Text;
using Trag.Domain.Updates;
using Trag.TestSupport;
using Trag.ViewModels.Updates;
using Trag.Desktop.UnitTests.Dialogs.Settings;

namespace Trag.Desktop.UnitTests.SettingsHost.Settings;

public sealed class SettingsTests
{
    [AvaloniaFact]
    public async Task WhenSettingsIsChosenUnderMore_ShouldOpenSettingsLazilyWithProviderCardsInBothThemes()
    {
        var service = Service();
        var h = new AppHarness(); await using var model = h.Shell();
        var owner = new MainWindow { DataContext = model, SettingsHost = { AccountService = service } }; owner.Show(); AppHarness.Layout(owner);
        Assert.Null(owner.SettingsHost.ActiveSettings);
        var settings = await ChooseSettingsUnderMore(owner);
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            settings.RequestedThemeVariant = theme; settings.Width = 740; settings.Height = 530; settings.UpdateLayout();
            var github = settings.Descendant<Button>("AddGitHubButton")!; var gitlab = settings.Descendant<Button>("AddGitLabButton")!;
            Assert.True(github.IsEffectivelyVisible); Assert.True(gitlab.IsEffectivelyVisible);
            Assert.True(github.Bounds.Width > 50); Assert.True(settings.Descendant<ScrollViewer>("AccountsScroll")!.Bounds.Height > 100);
        }
        settings.Close();
        owner.Close();
    }
    [AvaloniaFact]
    public async Task WhenAGitLabTokenIsTypedAndSettingsClose_ShouldMaskItAndClearTheEditor()
    {
        var service = Service();
        var h = new AppHarness(); await using var model = h.Shell();
        var owner = new MainWindow { DataContext = model, SettingsHost = { AccountService = service } }; owner.Show(); AppHarness.Layout(owner);
        var settings = await ChooseSettingsUnderMore(owner);
        ShowInBothThemes(settings);
        settings.Model.AddGitLabCommand.Execute(null); settings.UpdateLayout();
        Assert.True(settings.Descendant<TextBox>("GitLabTokenEditor")!.IsEffectivelyVisible);
        Assert.Equal('●', settings.Descendant<TextBox>("GitLabTokenEditor")!.PasswordChar);
        settings.Descendant<TextBox>("GitLabTokenEditor")!.Text = "synthetic-token";
        settings.Close(); Assert.Null(owner.SettingsHost.ActiveSettings);
        Assert.Equal("", settings.Descendant<TextBox>("GitLabTokenEditor")!.Text);
        owner.Close();
    }
    // Remove on an account's row turns into Cancel and an accent Confirm on that row alone; Cancel keeps the account,
    // Confirm removes it and keeps the others.
    [AvaloniaFact]
    public async Task WhenAnAccountRemovalIsRequested_ShouldConfirmItOnTheAccountsRowAndKeepTheOtherAccounts()
    {
        var service = Service(); var window = SettingsHarness.Window(service); window.Show(); await window.Initialization; AppHarness.Layout(window);
        var (github, gitlab) = (window.GetVisualDescendants().OfType<GitHubAccountCard>().Single(), window.GetVisualDescendants().OfType<GitLabAccountCard>().Single());
        static Button? Shown(Control card, string content) => card.GetVisualDescendants().OfType<Button>().SingleOrDefault(button => Equals(button.Content, content) && button.IsEffectivelyVisible);

        SettingsHarness.Click(window, Shown(github, "Remove")!);

        Assert.Null(Shown(github, "Remove")); Assert.Contains("accent", Shown(github, "Confirm")!.Classes);
        Assert.NotNull(Shown(gitlab, "Remove")); Assert.Null(Shown(gitlab, "Confirm"));
        SettingsHarness.Click(window, Shown(github, "Cancel")!);
        Assert.Null(Shown(github, "Confirm")); Assert.Equal(2, service.Accounts.Count);
        SettingsHarness.Click(window, Shown(github, "Remove")!);
        SettingsHarness.Click(window, Shown(github, "Confirm")!);
        await AppHarness.Until(() => service.Accounts.Count == 1 && window.Model.IsIdle, "the removal");
        Assert.Equal(AccountProvider.GitLab, Assert.Single(service.Accounts).Provider);
        window.Close();
    }
    [AvaloniaFact]
    public async Task WhenTheAccountCardsShow_ShouldBindTheModelCommandsWithReplaceTokenOpeningTheEntryOnTheAccountsServer()
    {
        var service = Service(); var window = SettingsHarness.Window(service); window.Show(); await window.Initialization; AppHarness.Layout(window);
        var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
        Assert.Equal(2, buttons.Count(button => Equals(button.Content, "Check connection") && ReferenceEquals(button.Command, window.Model.VerifyAccountCommand)));
        Assert.Equal(2, buttons.Count(button => Equals(button.Content, "Remove") && ReferenceEquals(button.Command, window.Model.RequestRemovalCommand)));
        var replace = Assert.Single(buttons, button => Equals(button.Content, "Replace token"));
        Assert.Same(window.Model.ReplaceTokenCommand, replace.Command); Assert.Same(service.Accounts[1], replace.CommandParameter);
        replace.Command!.Execute(replace.CommandParameter); AppHarness.Layout(window);
        Assert.True(window.Model.ShowGitLabEntry); Assert.Equal("https://gitlab.com/", window.Model.GitLabAddress); Assert.True(window.Model.MemoryOnly);
        Assert.True(window.Descendant<TextBox>("GitLabTokenEditor")!.IsFocused);
        window.Close();
    }
    // Escape closes Settings as Done does; the GitLab entry open over it takes the first Escape, as every dialog does.
    [AvaloniaFact]
    public async Task WhenEscapeIsPressed_ShouldCloseSettingsOnceNoDialogIsOpenOverIt()
    {
        var window = SettingsHarness.Window(Service()); window.Show(); await window.Initialization; AppHarness.Layout(window);
        var closed = false; window.Closed += (_, _) => closed = true;
        SettingsHarness.Click(window, window.Descendant<Button>("AddGitLabButton")!);
        Assert.True(window.Descendant<DialogHost>("GitLabEntryOverlay")!.IsKeyboardFocusWithin);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); AppHarness.Layout(window);

        Assert.False(window.Model.ShowGitLabEntry); Assert.False(closed);
        window.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null); AppHarness.Layout(window);
        Assert.False(closed);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); AppHarness.Layout(window);
        Assert.True(closed);
    }
    // A control under Settings that answers Escape itself gets it first: an open list closes, Settings stays.
    [AvaloniaFact]
    public async Task WhenEscapeIsPressedInAnOpenDropDown_ShouldCloseOnlyTheList()
    {
        var layout = new LayoutPreferences(new FakeLayoutStore(), new StatusBarState());
        var window = SettingsHarness.Window(Service(), layout); window.Show(); await window.Initialization; AppHarness.Layout(window);
        var closed = false; window.Closed += (_, _) => closed = true;
        SettingsHarness.Choose(window, "AppearanceSection");
        var selector = window.Descendant<ComboBox>("ThemeSelector")!;
        SettingsHarness.Click(window, selector);
        Assert.True(selector.IsDropDownOpen);

        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); AppHarness.Layout(window);

        Assert.False(selector.IsDropDownOpen); Assert.False(closed);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); AppHarness.Layout(window);
        Assert.True(closed);
        await layout.SaveCompletion;
    }
    [AvaloniaFact]
    public async Task WhenAnyWindowShows_ShouldCarryTheAppIcon()
    {
        var main = new MainWindow(); main.Show();
        var settings = SettingsHarness.Window(Service()); settings.Show(); await settings.Initialization;
        // One WindowIcon from the Window style in App.axaml, so a future dialog gets it without code of its own.
        Assert.NotNull(main.Icon); Assert.Same(main.Icon, settings.Icon); Assert.NotNull(main.Descendant<Image>("TitleIcon")!.Source);
        settings.Close(); main.Close();
    }
    [AvaloniaFact]
    public async Task WhenAnotherAutomaticFetchIntervalIsChosen_ShouldSaveItAndSayItOnTheFetchButton()
    {
        var h = new AppHarness(); await using var model = h.Shell();
        var owner = new MainWindow { DataContext = model, SettingsHost = { AccountService = Service(), AutoFetch = model.AutoFetch } }; owner.Show(); AppHarness.Layout(owner);
        Assert.Equal("Fetch (Ctrl+Shift+F) · Trag also fetches every minute", ToolTip.GetTip(owner.Descendant<Control>("FetchButton")!));
        // The window is modal: the call completes when it closes, so the test goes on with the window it opened.
        var showing = owner.SettingsHost.ShowSettingsAsync();
        var settings = owner.SettingsHost.ActiveSettings!; await settings.Initialization; AppHarness.Layout(settings);
        var interval = settings.Descendant<ComboBox>("AutoFetchInterval")!;
        Assert.True(interval.IsEffectivelyVisible);
        Assert.Equal(4, interval.ItemCount); Assert.Equal("Every minute", ((Domain.Presentation.Text.AutoFetchOption)interval.SelectedItem!).Label);
        interval.SelectedIndex = 2; AppHarness.Layout(settings);
        Assert.Equal(5, model.Layout.AutoFetchMinutes);
        Assert.Equal("Fetch (Ctrl+Shift+F) · Trag also fetches every 5 minutes", ToolTip.GetTip(owner.Descendant<Control>("FetchButton")!));
        settings.Close(); await showing; owner.Close();
    }

    // Settings opens on Integrations; a click on About shows the version of the running build in place of the accounts
    // and their status line.
    [AvaloniaFact]
    public async Task WhenAboutIsChosen_ShouldShowTheRunningVersionInPlaceOfTheAccounts()
    {
        var window = SettingsHarness.Window(Service()); window.Show(); await window.Initialization; AppHarness.Layout(window);
        var accounts = window.Descendant<ScrollViewer>("AccountsScroll")!; var status = window.Descendant<TextBlock>("AccountStatus")!;
        var about = window.FindControl<AboutPage>("About")!;
        Assert.True(accounts.IsEffectivelyVisible); Assert.True(status.IsEffectivelyVisible); Assert.False(about.IsEffectivelyVisible);

        SettingsHarness.Choose(window, "AboutSection");

        Assert.False(accounts.IsEffectivelyVisible); Assert.False(status.IsEffectivelyVisible); Assert.True(about.IsEffectivelyVisible);
        var version = window.Descendant<TextBlock>("AppVersion")!;
        var running = typeof(SettingsWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Equal(AboutText.Version(running), version.Text);
        window.Close();
    }

    // About names the folder crash logs go to; Open folder makes it when no crash has made it yet, and shows it.
    [AvaloniaFact]
    public async Task WhenAboutHasACrashLogFolder_ShouldNameItAndShowIt()
    {
        var folder = Path.Combine(Path.GetTempPath(), "trag-crash-logs-" + Guid.NewGuid().ToString("N")); string? shown = null;
        var window = SettingsHarness.Window(Service()); var about = window.FindControl<AboutPage>("About")!;
        about.CrashLogs = folder; about.Reveal = path => shown = path;
        window.Show(); await window.Initialization; AppHarness.Layout(window);
        SettingsHarness.Choose(window, "AboutSection");
        Assert.Equal(folder, window.Descendant<TextBlock>("CrashLogsPath")!.Text);
        try
        {
            SettingsHarness.Click(window, window.Descendant<Button>("OpenCrashLogs")!);

            Assert.Equal(folder, shown); Assert.True(Directory.Exists(folder));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder); }
        window.Close();
    }

    // A file where the folder should be cannot become the folder: Open folder shows nothing and the window stays.
    [AvaloniaFact]
    public async Task WhenTheCrashLogFolderCannotBeMade_ShouldShowNothingAndKeepTheWindow()
    {
        var file = Path.Combine(Path.GetTempPath(), "trag-crash-logs-" + Guid.NewGuid().ToString("N")); string? shown = null;
        await File.WriteAllTextAsync(file, "in the way");
        var window = SettingsHarness.Window(Service()); var about = window.FindControl<AboutPage>("About")!;
        about.CrashLogs = file; about.Reveal = path => shown = path;
        window.Show(); await window.Initialization; AppHarness.Layout(window);
        SettingsHarness.Choose(window, "AboutSection");
        try
        {
            SettingsHarness.Click(window, window.Descendant<Button>("OpenCrashLogs")!);

            Assert.Null(shown);
        }
        finally { File.Delete(file); }
        window.Close();
    }

    // About opens Trag's own license, which ships beside it, in the program the system has for text.
    [AvaloniaFact]
    public async Task WhenTheLicenseIsAskedFor_ShouldOpenTheFileBesideTrag()
    {
        string? opened = null;
        var window = SettingsHarness.Window(Service()); window.FindControl<AboutPage>("About")!.Open = path => opened = path;
        window.Show(); await window.Initialization; AppHarness.Layout(window);
        SettingsHarness.Choose(window, "AboutSection");

        SettingsHarness.Click(window, window.Descendant<Button>("OpenLicense")!);

        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "LICENSE.txt"), opened); Assert.True(File.Exists(opened));
        window.Close();
    }

    // About opens the third-party notices that ship beside Trag, in the program the system has for text.
    [AvaloniaFact]
    public async Task WhenTheNoticesAreAskedFor_ShouldOpenTheFileBesideTrag()
    {
        string? opened = null;
        var window = SettingsHarness.Window(Service()); window.FindControl<AboutPage>("About")!.Open = path => opened = path;
        window.Show(); await window.Initialization; AppHarness.Layout(window);
        SettingsHarness.Choose(window, "AboutSection");

        SettingsHarness.Click(window, window.Descendant<Button>("OpenNotices")!);

        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt"), opened); Assert.True(File.Exists(opened));
        window.Close();
    }

    // About asks for an update only when the user does; a newer release turns Update into Download and says so beside
    // the version.
    [AvaloniaFact]
    public async Task WhenTheUserChecksAndANewerReleaseIsOut_ShouldSayItBesideTheVersionAndOfferTheDownload()
    {
        var checker = new FakeUpdateChecker { Answer = UpdateOffer.From(true, "1.1.0", "https://downloads.finitysoftware.io/trag/1.1.0") };
        var window = SettingsHarness.Window(Service());
        window.FindControl<AboutPage>("About")!.Update = new UpdateViewModel(new UpdateModel(checker, "1.0.0"));
        window.Show(); await window.Initialization; AppHarness.Layout(window);
        SettingsHarness.Choose(window, "AboutSection");
        var (update, download, status) = (window.Descendant<Button>("CheckForUpdate")!, window.Descendant<Button>("DownloadUpdate")!, window.Descendant<TextBlock>("UpdateStatus")!);
        Assert.True(update.IsEffectivelyVisible); Assert.False(download.IsEffectivelyVisible); Assert.False(status.IsEffectivelyVisible);

        SettingsHarness.Click(window, update); AppHarness.Layout(window);

        Assert.False(update.IsEffectivelyVisible); Assert.True(download.IsEffectivelyVisible);
        Assert.Equal(("·", "There is a new update! Version 1.1.0"), (window.Descendant<TextBlock>("UpdateSeparator")!.Text, status.Text));
        Assert.True(status.IsEffectivelyVisible);
        window.Close();
    }

    // A probe asks no update service, so About offers no check.
    [AvaloniaFact]
    public async Task WhenNoUpdateServiceIsGiven_ShouldOfferNoCheck()
    {
        var window = SettingsHarness.Window(Service()); window.Show(); await window.Initialization; AppHarness.Layout(window);
        SettingsHarness.Choose(window, "AboutSection");
        Assert.False(window.Descendant<Button>("CheckForUpdate")!.IsEffectivelyVisible); Assert.False(window.Descendant<Button>("DownloadUpdate")!.IsEffectivelyVisible);
        window.Close();
    }

    // Finity Software's name at the foot of About opens its site.
    [AvaloniaFact]
    public async Task WhenFinitySoftwaresNameIsClicked_ShouldOpenItsSiteInTheBrowser()
    {
        Uri? opened = null;
        var window = SettingsHarness.Window(Service()); window.FindControl<AboutPage>("About")!.OpenBrowser = uri => { opened = uri; return true; };
        window.Show(); await window.Initialization; AppHarness.Layout(window);
        SettingsHarness.Choose(window, "AboutSection");

        SettingsHarness.Click(window, window.Descendant<Button>("FinitySoftwareLink")!);

        Assert.Equal(new Uri("https://finitysoftware.io/"), opened);
        window.Close();
    }

    // Finity Software's name ends where the line under the page ends and sits five pixels above it.
    [AvaloniaFact]
    public async Task WhenAboutIsShown_ShouldEndFinitySoftwaresNameWithTheLineFiveAboveIt()
    {
        var window = SettingsHarness.Window(Service()); window.Show(); await window.Initialization; AppHarness.Layout(window);
        SettingsHarness.Choose(window, "AboutSection"); AppHarness.Layout(window);

        var (name, line) = (Edges(window, "FinitySoftwareLink"), Edges(window, "SettingsFooterLine"));

        Assert.Equal(line.Right, name.Right, tolerance: 0.5); Assert.Equal(5, line.Top - name.Bottom, tolerance: 0.5);
        window.Close();
    }

    // Finity Software's name is quiet until the pointer is on it; then it turns to the accent, under a hand.
    [AvaloniaFact]
    public async Task WhenThePointerIsOnFinitySoftwaresName_ShouldTurnItToTheAccent()
    {
        var window = SettingsHarness.Window(Service()); window.Show(); await window.Initialization; AppHarness.Layout(window);
        SettingsHarness.Choose(window, "AboutSection");
        var link = window.Descendant<Button>("FinitySoftwareLink")!;
        var accent = window.FindResource(window.ActualThemeVariant, "AccentBrush");
        Assert.NotSame(accent, link.GetBaseValue(TemplatedControl.ForegroundProperty).GetValueOrDefault()); Assert.NotNull(link.Cursor);

        ((IPseudoClasses)link.Classes).Add(":pointerover"); AppHarness.Layout(window);

        Assert.Same(accent, link.GetBaseValue(TemplatedControl.ForegroundProperty).GetValueOrDefault());
        window.Close();
    }

    // The host hands About the update service the composition gave it, and the check asks with the running version.
    [AvaloniaFact]
    public async Task WhenTheHostHasAnUpdateService_ShouldOfferTheCheckAndAskWithTheRunningVersion()
    {
        var h = new AppHarness(); await using var model = h.Shell();
        var checker = new FakeUpdateChecker();
        var owner = new MainWindow { DataContext = model, SettingsHost = { AccountService = Service(), Updates = checker } }; owner.Show(); AppHarness.Layout(owner);
        var settings = await ChooseSettingsUnderMore(owner);
        SettingsHarness.Choose(settings, "AboutSection");

        SettingsHarness.Click(settings, settings.Descendant<Button>("CheckForUpdate")!); AppHarness.Layout(settings);

        var running = typeof(SettingsWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Equal([VersionNumber.Of(running)], checker.Versions);
        Assert.Equal("Could not check for updates.", settings.Descendant<TextBlock>("UpdateStatus")!.Text);
        settings.Close(); owner.Close();
    }

    // Settings opened with Ctrl+, from the commit message gives the keyboard back to the message when it closes.
    [AvaloniaFact]
    public async Task WhenSettingsWasOpenedFromTheCommitEditorWithCtrlComma_ShouldGiveFocusBackToTheEditor()
    {
        var h = new AppHarness();
        await using var model = h.Shell();
        var window = h.Window(model);
        await model.Session.OpenRepositoryAsync("fixture"); AppHarness.Layout(window);
        window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, null); AppHarness.Layout(window);
        Assert.Equal("CommitMessageEditor", (window.FocusManager!.GetFocusedElement() as Control)?.Name);

        window.KeyPress(Key.OemComma, RawInputModifiers.Control, PhysicalKey.Comma, null);
        await AppHarness.Until(() => window.SettingsHost.ActiveSettings is not null, "Ctrl+, settings");
        window.SettingsHost.ActiveSettings!.Close(); AppHarness.Layout(window);

        await AppHarness.Until(() => (window.FocusManager!.GetFocusedElement() as Control)?.Name == "CommitMessageEditor", "the focus back in the commit message");
        window.Close();
    }

    // A probe that keeps no files keeps no crash logs either, so About offers none.
    [AvaloniaFact]
    public async Task WhenThereIsNoCrashLogFolder_ShouldOfferNone()
    {
        var h = new AppHarness(); await using var model = h.Shell();
        var owner = new MainWindow { DataContext = model, SettingsHost = { AccountService = Service() } }; owner.Show(); AppHarness.Layout(owner);
        var settings = await ChooseSettingsUnderMore(owner);

        SettingsHarness.Choose(settings, "AboutSection");

        Assert.False(settings.Descendant<Button>("OpenCrashLogs")!.IsEffectivelyVisible);
        settings.Close(); owner.Close();
    }

    // The host hands Settings the crash-log folder the composition gave it, and the shell's ways to show a folder and open
    // a file.
    [AvaloniaFact]
    public async Task WhenSettingsOpens_ShouldGetTheCrashLogFolderAndTheShellFromItsHost()
    {
        var h = new AppHarness(); await using var model = h.Shell();
        var folder = Path.Combine(Path.GetTempPath(), "trag-crash-logs");
        var owner = new MainWindow { DataContext = model, SettingsHost = { AccountService = Service(), CrashLogs = folder } }; owner.Show(); AppHarness.Layout(owner);

        var settings = await ChooseSettingsUnderMore(owner);

        var about = settings.FindControl<AboutPage>("About")!;
        Assert.Equal(folder, about.CrashLogs); Assert.NotNull(about.Reveal); Assert.NotNull(about.Open);
        settings.Close(); owner.Close();
    }

    // Appearance is a section of its own: a click on it shows the theme in place of the accounts.
    [AvaloniaFact]
    public async Task WhenAppearanceIsChosen_ShouldShowTheThemeInPlaceOfTheAccounts()
    {
        var layout = new LayoutPreferences(new FakeLayoutStore(), new StatusBarState());
        var window = SettingsHarness.Window(Service(), layout); window.Show(); await window.Initialization; AppHarness.Layout(window);
        var accounts = window.Descendant<ScrollViewer>("AccountsScroll")!; var appearance = window.FindControl<AppearanceCard>("Appearance")!;
        Assert.True(accounts.IsEffectivelyVisible); Assert.False(appearance.IsEffectivelyVisible);

        SettingsHarness.Choose(window, "AppearanceSection");

        Assert.False(accounts.IsEffectivelyVisible); Assert.True(appearance.IsEffectivelyVisible);
        window.Close(); await layout.SaveCompletion;
    }

    // Without the layout there is no theme to choose, so the section is not offered.
    [AvaloniaFact]
    public async Task WhenThereIsNoLayout_ShouldOfferNoAppearanceSection()
    {
        var window = SettingsHarness.Window(Service()); window.Show(); await window.Initialization; AppHarness.Layout(window);
        Assert.False(window.Descendant<RadioButton>("AppearanceSection")!.IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task WhenThereIsNoAutomaticFetch_ShouldShowNoCardInSettings()
    {
        var window = SettingsHarness.Window(Service()); window.Show(); await window.Initialization; AppHarness.Layout(window);
        Assert.False(window.FindControl<AutoFetchCard>("AutoFetch")!.IsVisible);
        window.Close();
    }

    // A real click on More opens its menu; Settings in it asks the catalog, and the host opens the window.
    private static async Task<SettingsWindow> ChooseSettingsUnderMore(MainWindow owner)
    {
        var more = owner.Descendant<Button>("MoreButton")!;
        var centre = more.TranslatePoint(new Point(more.Bounds.Width / 2, more.Bounds.Height / 2), owner)!.Value;
        owner.MouseDown(centre, MouseButton.Left); owner.MouseUp(centre, MouseButton.Left); AppHarness.Layout(owner);
        var item = ((MenuFlyout)more.Flyout!).Items.OfType<MenuItem>().Single(item => item.Name == "SettingsMenuItem");
        item.Command!.Execute(null); AppHarness.Layout(owner);
        var settings = owner.SettingsHost.ActiveSettings!; await settings.Initialization;
        return settings;
    }

    // The theme test's steps: both themes at the size the cards are checked in, so the token entry opens in Dark as
    // it always did.
    private static void ShowInBothThemes(SettingsWindow settings)
    {
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            settings.RequestedThemeVariant = theme; settings.Width = 740; settings.Height = 530; settings.UpdateLayout();
        }
    }

    // One saved account and one that only lives for this session, so both storage lines appear on the cards.
    private static Rect Edges(Window window, string name)
    {
        var control = window.Descendant<Control>(name)!;
        return new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
    }

    private static FakeAccountService Service() => new()
    {
        Accounts =
        {
            new("one", AccountProvider.GitHub, "1", "alice", new("https://api.github.com/"), new("https://github.com/"), true, DateTimeOffset.UtcNow, null),
            new("two", AccountProvider.GitLab, "2", "bob", new("https://gitlab.com/api/v4/"), new("https://gitlab.com/"), false, DateTimeOffset.UtcNow, null)
        }
    };
}
