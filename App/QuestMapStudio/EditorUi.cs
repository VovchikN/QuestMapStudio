using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuestMapStudio.Core;

namespace QuestMapStudio;

public sealed partial class MainWindow
{
    private static readonly IBrush PlaylistTrackBackground = Brush.Parse("#111B2B");
    private const double TrackCoverSize = 30;
    private readonly Border playlistEndSpace = new() { IsVisible = false, IsHitTestVisible = false };
    private readonly CheckBox clearSelection = new() { Width = 24, MinWidth = 24, Padding = new Thickness(0) };
    private Button? undoButton;
    private Button? redoButton;
    private const int HistoryLimit = 20;
    private readonly List<EditorState> undo = [];
    private readonly List<EditorState> redo = [];
    private readonly Dictionary<PlaylistDocument, DraftState> cachedDrafts = [];

    private sealed record DraftState(string Id, string Text, string? RemotePath, string? RemoteDigest, bool Pending, bool PendingDelete)
    {
        public static DraftState From(PlaylistDocument doc) => new(doc.LocalId, Encoding.UTF8.GetString(doc.Bytes()),
            doc.RemotePath, doc.RemoteDigest, doc.Pending, doc.PendingDelete);

        public PlaylistDocument Restore()
        {
            var doc = PlaylistDocument.Parse(Text);
            doc.LocalId = Id;
            doc.RemotePath = RemotePath;
            doc.RemoteDigest = RemoteDigest;
            doc.Pending = Pending;
            doc.PendingDelete = PendingDelete;
            return doc;
        }
    }

    private sealed record EditorState(DraftState[] Drafts, PlaylistOrderDraft? Order, string[] Queue,
        Dictionary<string, string> Errors, string? OpenPlaylist);

    private DraftState Snapshot(PlaylistDocument doc)
    {
        if (!cachedDrafts.TryGetValue(doc, out var snapshot))
            cachedDrafts.Add(doc, snapshot = DraftState.From(doc));
        return snapshot;
    }

    private EditorState CaptureEditor() => new(playlists.Select(Snapshot).ToArray(), pendingOrder,
        importQueue.ToArray(), new Dictionary<string, string>(archiveErrors, StringComparer.OrdinalIgnoreCase), current?.LocalId);

    private void RememberEdit(EditorState? before = null)
    {
        undo.Add(before ?? CaptureEditor());
        if (undo.Count > HistoryLimit) undo.RemoveAt(0);
        redo.Clear();
        UpdateHistoryButtons();
    }

    private void ResetEditorHistory()
    {
        undo.Clear();
        redo.Clear();
        cachedDrafts.Clear();
        UpdateHistoryButtons();
    }

    private void UpdateHistoryButtons()
    {
        if (undoButton != null) undoButton.IsEnabled = !Busy && undo.Count != 0;
        if (redoButton != null) redoButton.IsEnabled = !Busy && redo.Count != 0;
    }

    private void StepEditor(bool forward)
    {
        var source = forward ? redo : undo;
        var destination = forward ? undo : redo;
        if (Busy || source.Count == 0) return;
        var previous = source[^1];
        var before = CaptureEditor();
        var existing = playlists.ToDictionary(doc => doc.LocalId);
        var previousIds = previous.Drafts.Select(draft => draft.Id).ToHashSet(StringComparer.Ordinal);
        var restored = new List<PlaylistDocument>(previous.Drafts.Length);
        foreach (var snapshot in previous.Drafts)
        {
            if (existing.TryGetValue(snapshot.Id, out var unchanged) && Snapshot(unchanged) == snapshot)
            {
                restored.Add(unchanged);
                continue;
            }
            var doc = snapshot.Restore();
            data.SaveDraft(profile, doc);
            restored.Add(doc);
        }
        foreach (var doc in playlists.Where(doc => !previousIds.Contains(doc.LocalId)))
            data.DeleteDraft(profile, doc);
        data.SaveOrderDraft(profile, previous.Order);
        data.SaveQueue(previous.Queue);

        playlists.Clear();
        playlists.AddRange(restored);
        cachedDrafts.Clear();
        for (int i = 0; i < restored.Count; i++) cachedDrafts.Add(restored[i], previous.Drafts[i]);
        pendingOrder = previous.Order;
        importQueue.Clear();
        importQueue.AddRange(previous.Queue);
        archiveErrors.Clear();
        foreach (var (file, error) in previous.Errors) archiveErrors.Add(file, error);
        current = restored.Find(doc => doc.LocalId == previous.OpenPlaylist);
        source.RemoveAt(source.Count - 1);
        destination.Add(before);
        RenderPlaylists();
        UpdateQueue();
        Report(forward ? "Edit redone." : "Edit undone.");
    }

    private Button HistoryButton(string glyph, string name, bool forward)
    {
        var button = Button(glyph, () => { StepEditor(forward); return Task.CompletedTask; });
        button.MinWidth = 34;
        button.FontSize = 19;
        button.Padding = new Thickness(5, 0);
        ToolTip.SetTip(button, name);
        AutomationProperties.SetName(button, name);
        button.IsEnabled = false;
        return button;
    }

    private static void ConfigureScrollBar(Control control)
    {
        ScrollViewer.SetHorizontalScrollBarVisibility(control, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(control, ScrollBarVisibility.Auto);
        ScrollViewer.SetAllowAutoHide(control, false);
    }

    private void RefreshCheckedMapCards()
    {
        // Preserve the current items and scroll position when only check marks change.
        foreach (var item in mapList.GetVisualDescendants().OfType<ListBoxItem>())
        {
            if (item.DataContext is not MapEntry map) continue;
            var check = item.GetVisualDescendants().OfType<CheckBox>().FirstOrDefault(box => box.Tag is Border);
            if (check?.Tag is not Border card) continue;
            bool selected = checkedMaps.Contains(map.Folder);
            check.IsChecked = selected;
            card.Background = selected ? Marked : Card;
        }
        UpdateMapCount();
    }

    private void ResetMapScroll()
    {
        mapList.SelectedIndex = -1;
        Dispatcher.UIThread.Post(() =>
        {
            if (mapList.ItemCount != 0) mapList.ScrollIntoView(0);
            if (mapList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } scroller)
                scroller.Offset = default;
        }, DispatcherPriority.Loaded);
    }

    private MapEntry[] MapsForDrag(MapEntry map) => checkedMaps.Contains(map.Folder) ? CheckedInListOrder() : [map];

    private void PositionPlaylistHeader(Control header, bool alignToTop, double savedOffset)
    {
        UpdateLayout();
        var last = playlistCards.Children[^2];
        double spaceBelow = header.Bounds.Top + playlistScroll.Viewport.Height - last.Bounds.Bottom;
        playlistEndSpace.IsVisible = spaceBelow > 0.5;
        playlistEndSpace.Height = Math.Max(0, spaceBelow - playlistCards.Spacing);
        UpdateLayout();
        playlistScroll.Offset = new Vector(0, alignToTop ? header.Bounds.Top : savedOffset);
    }

    private Control MapDragStack(MapEntry[] selected)
    {
        if (selected.Length == 1) return MapCard(selected[0]);

        var cards = new ListBox
        {
            ItemsSource = selected,
            Background = Brushes.Transparent,
            MaxHeight = ClientSize.Height
        };
        ScrollViewer.SetVerticalScrollBarVisibility(cards, ScrollBarVisibility.Hidden);
        ScrollViewer.SetHorizontalScrollBarVisibility(cards, ScrollBarVisibility.Disabled);
        var items = new Style(selector => selector.OfType<ListBoxItem>());
        items.Setters.Add(new Setter(TemplatedControl.PaddingProperty, new Thickness(0)));
        items.Setters.Add(new Setter(MinHeightProperty, 0.0));
        cards.Styles.Add(items);
        cards.ItemTemplate = new FuncDataTemplate<MapEntry>((map, _) =>
        {
            if (map == null) return new Border();
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8, Height = 30 };
            row.Children.Add(MapCover(map, 24));
            Place(row, Label(map.Title, 12, true), 1);
            return new Border { Background = Marked, BorderBrush = Stroke,
                BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(7, 0), Child = row };
        });
        AutomationProperties.SetName(cards, "Dragged map stack");
        return cards;
    }
}
