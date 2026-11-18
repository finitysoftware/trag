using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Trag.Domain.Diff;
using Trag.Domain.Presentation;

namespace Trag.Desktop.Controls.Diff;

// The diff control: a virtualized list of rows drawn by DiffRowControl, unified or split. The place in the rows is a
// DiffAnchor that follows the same content into the other mode and into a re-read; other content starts at the top.
// The arithmetic lives in DiffViewport.
public sealed partial class DiffView : UserControl
{
    public static readonly StyledProperty<DiffPresentation> DocumentProperty = AvaloniaProperty.Register<DiffView, DiffPresentation>(nameof(Document), DiffPresentation.Empty);
    public static readonly StyledProperty<int> ModeProperty = AvaloniaProperty.Register<DiffView, int>(nameof(Mode), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> SplitRatioProperty = AvaloniaProperty.Register<DiffView, double>(nameof(SplitRatio), 0.5, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> HorizontalOffsetProperty = AvaloniaProperty.Register<DiffView, double>(nameof(HorizontalOffset));
    public static readonly StyledProperty<HunkOffer> OfferProperty = AvaloniaProperty.Register<DiffView, HunkOffer>(nameof(Offer), HunkOffer.None);
    public static readonly StyledProperty<ICommand?> HunkCommandProperty = AvaloniaProperty.Register<DiffView, ICommand?>(nameof(HunkCommand));
    public static readonly StyledProperty<ICommand?> DiscardHunkCommandProperty = AvaloniaProperty.Register<DiffView, ICommand?>(nameof(DiscardHunkCommand));
    public static readonly DirectProperty<DiffView, bool> IsSplitProperty = AvaloniaProperty.RegisterDirect<DiffView, bool>(nameof(IsSplit), view => view.IsSplit);
    public DiffPresentation Document { get => GetValue(DocumentProperty); set => SetValue(DocumentProperty, value); }
    public int Mode { get => GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public bool IsSplit => Mode == (int)DiffLayout.Split;
    public double SplitRatio { get => GetValue(SplitRatioProperty); set => SetValue(SplitRatioProperty, value); }
    public double HorizontalOffset { get => GetValue(HorizontalOffsetProperty); set => SetValue(HorizontalOffsetProperty, value); }
    // What the shown diff offers to take apart from its file and the commands that take and discard; no offer, no button.
    public HunkOffer Offer { get => GetValue(OfferProperty); set => SetValue(OfferProperty, value); }
    public ICommand? HunkCommand { get => GetValue(HunkCommandProperty); set => SetValue(HunkCommandProperty, value); }
    public ICommand? DiscardHunkCommand { get => GetValue(DiscardHunkCommandProperty); set => SetValue(DiscardHunkCommandProperty, value); }
    public double GutterWidth => Viewport.GutterWidth;
    // The selected unified line: in split mode a row is a pair, so the line the user chose is kept rather than derived.
    public int SelectedSource { get; private set; } = -1;
    public int TopSource => Viewport.TopSource(Scroller?.Offset.Y ?? 0);
    public object RowIdentity { get; private set; } = new();
    private DiffViewport Viewport => new(Document, IsSplit);
    private ScrollViewer? Scroller => Rows.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
    // The place kept for the content on screen, and the placement waiting for the new rows to be laid out.
    private DiffAnchor? anchor;
    private (DiffPlacement Place, bool Focus)? pending;
    private double rowViewportWidth;
    private bool attached;
    private bool placing;
    // Where the pointer is over the rows, for the block it points at.
    private double? pointer;

    public DiffView()
    {
        InitializeComponent();
        Rows.ItemTemplate = new FuncDataTemplate<DiffRowData>((data, _) => new DiffRowControl(this, data!));
        // A row is drawn, not a text, so a screen reader is given the line it shows.
        Rows.ContainerPrepared += (_, e) => AutomationProperties.SetName(e.Container, Document.Lines[Viewport.ToSource(e.Index)].Text);
        Rows.SelectionChanged += (_, _) =>
        {
            if (!placing && Rows.SelectedIndex >= 0) SelectedSource = Viewport.ToSource(Rows.SelectedIndex);
            PlaceHunk();
        };
        // The area, not the rows: the pointer over the action still points at its block.
        RowsArea.PointerMoved += (_, e) =>
        {
            pointer = e.GetPosition(Rows).Y;
            PlaceHunk();
        };
        RowsArea.PointerExited += (_, _) =>
        {
            pointer = null;
            PlaceHunk();
        };
        Rows.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => PlaceHunk());
        // Filled before the list opens its menu, which stays shut when there is nothing to offer.
        Rows.AddHandler(ContextRequestedEvent, (_, e) => e.Handled = !FillMenu(), RoutingStrategies.Tunnel);
        Rows.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (Rows.ItemCount == 0 || e.KeyModifiers != KeyModifiers.None) return;
            if (DiffViewport.KeyTarget(e.Key, Rows.SelectedIndex, Rows.ItemCount, DiffViewport.PageSize(Rows.Bounds.Height)) is not { } target) return;
            Rows.SelectedIndex = Math.Clamp(target, 0, Rows.ItemCount - 1);
            Rows.ScrollIntoView(Rows.SelectedIndex);
            Rows.Focus(NavigationMethod.Directional);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);
        SizeChanged += (_, _) => UpdateHorizontalRange();
        Rows.LayoutUpdated += (_, _) =>
        {
            if (Rows.GetRealizedContainers().FirstOrDefault() is not { } row || row.Bounds.Width <= 0) return;
            if (Math.Abs(row.Bounds.Width - rowViewportWidth) < 0.5) return;
            rowViewportWidth = row.Bounds.Width;
            SideHeaders.Margin = new(0, 0, Math.Max(0, Rows.Bounds.Width - rowViewportWidth), 0);
            UpdateHorizontalRange();
        };
        // Rows exist only while the view is in the tree; the place is remembered across being detached.
        DetachedFromVisualTree += (_, _) =>
        {
            Remember(Viewport);
            attached = false;
            pending = null;
            Rows.ItemsSource = null;
        };
        AttachedToVisualTree += (_, _) =>
        {
            attached = true;
            ShowRows(keep: true);
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (Rows is null) return;
        if (change.Property == ModeProperty)
        {
            var wasSplit = change.GetOldValue<int>() == (int)DiffLayout.Split;
            RaisePropertyChanged(IsSplitProperty, wasSplit, IsSplit);
            Remember(new DiffViewport(Document, wasSplit));
            ShowRows(keep: true);
        }
        else if (change.Property == DocumentProperty)
        {
            var shown = change.GetOldValue<DiffPresentation>();
            Remember(new DiffViewport(shown, IsSplit));
            ShowRows(keep: Document.Source is { } source && source == shown.Source);
        }
        else if (change.Property == OfferProperty) PlaceHunk();
        else if (change.Property == SplitRatioProperty || change.Property == HorizontalOffsetProperty)
        {
            UpdateHorizontalRange();
            foreach (var row in Rows.GetVisualDescendants().OfType<DiffRowControl>()) row.InvalidateVisual();
        }
    }

    // The place in the rows on screen. With no rows there is nothing to remember, so the place of content being read
    // again stays until that content arrives.
    private void Remember(DiffViewport shown)
    {
        if (Rows.ItemCount == 0) return;
        anchor = DiffAnchor.Capture(shown, Scroller?.Offset.Y ?? 0, SelectedSource);
    }

    // The rows for the current document and mode. A kept place is found in them; otherwise the view starts over at the
    // top with nothing selected and no horizontal scroll. The rows being replaced release their glyphs at once.
    private void ShowRows(bool keep)
    {
        if (!attached) return;
        if (!keep)
        {
            anchor = null;
            SetCurrentValue(HorizontalOffsetProperty, 0);
        }
        var focus = keep && Rows.IsKeyboardFocusWithin;
        foreach (var row in Rows.GetVisualDescendants().OfType<DiffRowControl>()) row.Release();
        var viewport = Viewport;
        var place = anchor?.Locate(viewport) ?? DiffPlacement.Start;
        RowIdentity = new();
        placing = true;
        try
        {
            Rows.ItemsSource = new DiffRowItems(RowIdentity, viewport.RowCount, IsSplit);
            Rows.SelectedIndex = viewport.ToRow(place.Selected);
        }
        finally { placing = false; }
        SelectedSource = place.Selected;
        UpdateHorizontalRange();
        // The offset applies once the new rows are laid out; a newer replacement before then takes this one's place.
        pending = (place, focus);
        Dispatcher.UIThread.Post(ApplyPending, DispatcherPriority.Loaded);
    }

    private void ApplyPending()
    {
        if (pending is not { } next) return;
        pending = null;
        Rows.UpdateLayout();
        if (Scroller is { } scroll) scroll.Offset = new(0, next.Place.VerticalOffset);
        if (next.Focus) Rows.Focus(NavigationMethod.Directional);
        PlaceHunk();
    }

    // The actions sit beside the first row of what the offer's button takes while it is in view, a plus to stage or a
    // minus to unstage, and the discard only where the offer has one.
    private void PlaceHunk()
    {
        var button = Offer.Button(Document.Lines, Chosen(), Pointed());
        var top = button is null ? null : Viewport.ActionTop(new(button.Lines[0], button.Lines[^1]), Scroller?.Offset.Y ?? 0, Rows.Bounds.Height);
        HunkButton.IsVisible = top is not null;
        DiscardHunkButton.IsVisible = top is not null && button!.CanDiscard;
        if (top is not { } at) return;
        HunkActions.Margin = new(0, at + 1, 18, 0);
        HunkIcon.Kind = Offer.Icon;
        ToolTip.SetTip(HunkButton, button!.Take);
        AutomationProperties.SetName(HunkButton, button.Take);
        ToolTip.SetTip(DiscardHunkButton, button.Discard);
        AutomationProperties.SetName(DiscardHunkButton, button.Discard);
        HunkButton.CommandParameter = DiscardHunkButton.CommandParameter = new HunkChoice(Document.Lines, button.Lines);
    }

    private IReadOnlyList<int> Chosen() => Viewport.ChangesIn(Rows.Selection.SelectedIndexes);

    private DiffBlock? Pointed() =>
        pointer is { } y ? DiffBlocks.At(Document.Lines, Viewport.ToSource(DiffViewport.TopRow((Scroller?.Offset.Y ?? 0) + y))) : null;

    // The offer's menu, a line between taking and discarding; false when it has nothing to offer.
    private bool FillMenu()
    {
        var menu = (MenuFlyout)Rows.ContextFlyout!;
        menu.Items.Clear();
        var items = Offer.Menu(Document.Lines, Chosen(), Pointed());
        var taking = items.Count(item => !item.Discards);
        foreach (var item in items)
        {
            if (item.Discards && menu.Items.Count == taking) menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = item.Header, Command = item.Discards ? DiscardHunkCommand : HunkCommand, CommandParameter = new HunkChoice(Document.Lines, item.Lines),
                Icon = item.Discards ? new LineIcon { Kind = "discard" } : null });
        }
        return menu.Items.Count != 0;
    }

    private void UpdateHorizontalRange()
    {
        var (viewport, maximum) = Viewport.HorizontalRange(rowViewportWidth, Bounds.Width, SplitRatio, DiffRowControl.CellWidth);
        HorizontalBar.ViewportSize = viewport;
        HorizontalBar.Maximum = maximum;
        HorizontalBar.LargeChange = viewport;
        if (HorizontalOffset > maximum) SetCurrentValue(HorizontalOffsetProperty, maximum);
    }
}
