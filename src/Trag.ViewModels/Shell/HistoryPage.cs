using CommunityToolkit.Mvvm.Input;
using Trag.Application.Live;
using Trag.ViewModels.Branches;
using Trag.ViewModels.CherryPick;
using Trag.ViewModels.Files;
using Trag.ViewModels.History;
using Trag.ViewModels.Rebase;
using Trag.ViewModels.Reset;
using Trag.ViewModels.Revert;
using Trag.ViewModels.Stash;
using Trag.ViewModels.Tags;

namespace Trag.ViewModels.Shell;

// What the history and search view binds (see FileStatusPage).
public sealed record HistoryPage(HistoryViewModel History, GoToCommitViewModel GoTo, StashViewModel Stash, BranchesViewModel Branches,
    TagsViewModel Tags, RevertViewModel Revert, CherryPickViewModel CherryPick, ResetViewModel Reset, RewriteViewModel Rewrite, BlameViewModel Blame, FileActionsViewModel Files, LayoutPreferences Layout, IRelayCommand Cancel);
