using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuestMapStudio;
using QuestMapStudio.Core;

AppBuilder.Configure<App>().UseSkia().WithInterFont()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
const string serial = "edit-fixture";
using var temp = new FixtureDirectory();
var data = new LocalData(temp.Path);
data.Settings.LastSerial = serial;
data.SaveSettings();

var maps = Enumerable.Range(0, 85).Select(i => new MapEntry {
    Title = $"Track {i:000}", Folder = MapInstaller.Destination + $"/map-{i}", Artist = $"Artist {i}",
    Mapper = "Mapper", Bpm = 138, Duration = 166, MapCode = i.ToString("x"),
    GameHash = i.ToString("x40"), ModifiedUnix = 1000 + i
}).ToList();
data.SaveMaps(serial, maps);

var playlist = PlaylistDocument.Parse(JsonSerializer.Serialize(new {
    playlistTitle = "Long playlist", songs = Enumerable.Range(15, 45).Select(i => new {
        songName = maps[i].Title, hash = maps[i].GameHash
    })
}));
playlist.RemotePath = QuestRepository.Playlists + "/long.bplist";
data.SaveDraft(serial, playlist);
var playlistIds = new List<string> { playlist.LocalId };
for (int i = 0; i < 13; i++)
{
    var other = PlaylistDocument.New();
    other.Title = "Playlist " + i;
    other.RemotePath = QuestRepository.Playlists + $"/{i}.bplist";
    data.SaveDraft(serial, other);
    playlistIds.Add(other.LocalId);
}
data.SaveOrderDraft(serial, new PlaylistOrderDraft([], playlistIds.ToArray()));

var window = new MainWindow(data) { Width = 1060, Height = 660 };
window.Show();
void Layout()
{
    Dispatcher.UIThread.RunJobs();
    window.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
}
void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + label);
    Console.WriteLine("PASS " + label);
}
IEnumerable<T> Controls<T>() where T : Visual => window.GetVisualDescendants().OfType<T>();
T Named<T>(string name) where T : Control => Controls<T>().Single(c => AutomationProperties.GetName(c) == name);
Point At(Visual visual, double x, double y) => visual.TranslatePoint(new Point(x, y), window)!.Value;
void Press(Point point) { window.MouseDown(point, MouseButton.Left, RawInputModifiers.None); Layout(); }
void Move(Point point) { window.MouseMove(point, RawInputModifiers.LeftMouseButton); Layout(); }
void Release(Point point) { window.MouseUp(point, MouseButton.Left, RawInputModifiers.None); Layout(); }
void Click(Control control)
{
    var point = At(control, control.Bounds.Width / 2, control.Bounds.Height / 2);
    Press(point); Release(point);
}
ScrollViewer Viewer(Control control) => control.GetVisualDescendants().OfType<ScrollViewer>().First();
ScrollBar Vertical(ScrollViewer viewer) => viewer.GetVisualDescendants().OfType<ScrollBar>()
    .First(bar => bar.Orientation == Avalonia.Layout.Orientation.Vertical && ReferenceEquals(bar.TemplatedParent, viewer));
ListBoxItem Item(ListBox list, string label) => list.GetVisualDescendants().OfType<ListBoxItem>()
    .First(item => item.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == label));
void EnsureClearance(ScrollBar bar, Control button, string name)
{
    Check(bar.IsEffectivelyVisible && !bar.AllowAutoHide, name + " scrollbar remains visible");
    Check(At(button, button.Bounds.Width, 0).X <= At(bar, 0, 0).X - 1, name + " content clears scrollbar");
}
Layout();
var library = Named<ListBox>("Map library");
var mapViewer = Viewer(library);
var bar = Vertical(mapViewer);
EnsureClearance(bar, Item(library, "Track 000").GetVisualDescendants().OfType<CheckBox>().Single(), "Maps");
library.ScrollIntoView(62);
Layout();
Check(mapViewer.Offset.Y > 500, "fixture scrolls away from first map");
var sort = Named<Button>("Sort maps");
var titleSort = ((MenuFlyout)sort.Flyout!).Items.OfType<MenuItem>().First();
for (int i = 0; i < 2; i++)
{
    titleSort.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
    Layout();
}
Check(mapViewer.Offset.Y == 0 && library.ItemsSource!.Cast<MapEntry>().First().Title == "Track 084" &&
    Item(library, "Track 084").IsEffectivelyVisible, "changing sort scrolls to the new first map");

var clear = Named<CheckBox>("Clear checked maps");
Check(clear.IsChecked == false && !clear.IsEnabled, "clear checkbox starts empty");
foreach (var title in new[] { "Track 084", "Track 082", "Track 080" })
    Click(Item(library, title).GetVisualDescendants().OfType<CheckBox>().Single());
Check(clear.IsChecked == true && clear.IsEnabled, "clear checkbox reflects partial selection");
Click(clear);
Check(clear.IsChecked == false && !clear.IsEnabled && Item(library, "Track 082").GetVisualDescendants().OfType<CheckBox>().Single().IsChecked == false,
    "clear checkbox resets every checked track");

Click(Item(library, "Track 084").GetVisualDescendants().OfType<CheckBox>().Single());
library.ScrollIntoView(60);
Layout();
double scrolledOffset = mapViewer.Offset.Y;
var visibleMap = library.GetVisualDescendants().OfType<ListBoxItem>().First(item => item.DataContext is MapEntry);
Click(clear);
Check(scrolledOffset > 500 && mapViewer.Offset.Y == scrolledOffset &&
      visibleMap.IsAttachedToVisualTree() && !clear.IsEnabled,
    "clearing an off-screen selection keeps the current scroll position and items");
library.ScrollIntoView(0);
mapViewer.Offset = default;
Layout();

var header = Controls<Button>().Single(b => Equals(ToolTip.GetTip(b), "Long playlist"));
Click(header);
var tracks = Named<ListBox>("Playlist tracks");
var trackViewer = Viewer(tracks);
var trackBar = Vertical(trackViewer);
var first = Item(tracks, "Track 015");
EnsureClearance(trackBar, first.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Remove from playlist"), "Playlist tracks");
header = Controls<Button>().Single(b => Equals(ToolTip.GetTip(b), "Long playlist"));
var outer = header.GetVisualAncestors().OfType<ScrollViewer>().First();
EnsureClearance(Vertical(outer), Controls<Button>().First(b => b.Content as string == "⋯"), "Playlist headers");
Check(!first.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.Contains("BPM") == true || text.Text?.StartsWith('#') == true),
    "playlist tracks omit BPM and map code");
Check(At(tracks, 0, 0).X > At(header, 0, 0).X, "playlist tracks are inset beneath header");

foreach (var title in new[] { "Track 084", "Track 082", "Track 080" })
    Click(Item(library, title).GetVisualDescendants().OfType<CheckBox>().Single());
var starting = Item(library, "Track 082");
var drop = At(first, 110, 8);
Press(At(starting, 110, 24));
Move(drop);
var stack = Named<ListBox>("Dragged map stack");
var preview = Named<Border>("Dragged map card");
Check(preview.Opacity is > 0 and < 1 && Math.Abs(At(stack, 0, 0).Y - drop.Y) < 1.1,
    "group preview stays translucent with the leading map at the pointer");
Check(stack.ItemsSource!.Cast<MapEntry>().Select(m => m.Title).SequenceEqual(["Track 084", "Track 082", "Track 080"]),
    "group preview contains all selected cards in drop order");
var positions = stack.GetVisualDescendants().OfType<ListBoxItem>().OrderBy(row => At(row, 0, 0).Y).ToArray();
Check(positions.Length == 3 && Math.Abs(At(positions[0], 0, positions[0].Bounds.Height).Y - At(positions[1], 0, 0).Y) < 0.1,
    "group preview renders contiguous rows");
Release(drop);
Layout();
Check(tracks.ItemCount == 48 && data.LoadDrafts(serial).Single(p => p.LocalId == playlist.LocalId).Pending,
    "drop adds all selected maps to the pending playlist");
var undo = Named<Button>("Undo last edit");
var redo = Named<Button>("Redo last edit");
var apply = Controls<Button>().Single(b => b.Content as string == "Apply");
Check(undo.IsEnabled && !redo.IsEnabled && At(undo, 0, 0).X < At(redo, 0, 0).X && At(redo, 0, 0).X < At(apply, 0, 0).X,
    "history arrows are before Apply");
Click(undo);
Check(tracks.ItemCount == 45 && redo.IsEnabled && !data.LoadDrafts(serial).Single(p => p.LocalId == playlist.LocalId).Pending,
    "Undo restores pre-drop draft and state");
Click(redo);
Check(tracks.ItemCount == 48 && undo.IsEnabled && !redo.IsEnabled, "Redo restores group addition");
var top = Item(tracks, "Track 084");
Click(top.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Remove from playlist"));
Check(tracks.ItemCount == 47, "track removal remains functional");
Click(undo);
Check(tracks.ItemCount == 48, "Undo restores removed playlist track");

var selectAll = Controls<CheckBox>().Single(c => Equals(ToolTip.GetTip(c), "Check all visible maps"));
Click(selectAll);
var largeGroupStart = Item(library, "Track 084");
Press(At(largeGroupStart, 110, 25));
Move(new Point(620, 620));
var largeStack = Named<ListBox>("Dragged map stack");
Check(largeStack.ItemCount == 85 && Math.Abs(At(largeStack, 0, 0).Y - 620) < 1.1 &&
    At(largeStack, 0, largeStack.Bounds.Height).Y > window.ClientSize.Height,
    "large stack extends below the viewport while the leading card follows the pointer");
Release(new Point(620, 620));
Check(tracks.ItemCount == 48, "dropping large selection outside the playlist leaves the draft intact");
Click(clear);

Button PlaylistButton(string name) => Controls<Button>().Single(b => Equals(ToolTip.GetTip(b), name));
var outerPresenter = outer.GetVisualDescendants().OfType<ScrollContentPresenter>()
    .First(presenter => ReferenceEquals(presenter.TemplatedParent, outer));
var viewportTop = At(outerPresenter, 0, 0).Y;
outer.Offset = new Vector(0, Math.Max(0, outer.Extent.Height - outer.Viewport.Height));
Layout();
var lastPlaylist = PlaylistButton("Playlist 12");
Check(At(lastPlaylist, 0, 0).Y > viewportTop + 150, "last playlist starts near the bottom of the panel");
Click(lastPlaylist);
lastPlaylist = PlaylistButton("Playlist 12");
Check(Math.Abs(At(lastPlaylist, 0, 0).Y - viewportTop) < 1.1 &&
    Math.Abs(outer.Offset.Y - lastPlaylist.GetVisualAncestors().OfType<Grid>().First(grid => grid.Tag is PlaylistDocument).Bounds.Top) < 1.1,
    "opening the last empty playlist scrolls its header to the top");
Check(data.LoadOrderDraft(serial)!.LocalIds.SequenceEqual(playlistIds), "opening a playlist preserves the saved order");
outer.Offset = default;
Layout();
Click(PlaylistButton("Long playlist"));
Check(Math.Abs(At(PlaylistButton("Long playlist"), 0, 0).Y - viewportTop) < 1.1 && Named<ListBox>("Playlist tracks").ItemCount == 48,
    "opening another playlist scrolls to its header and shows its tracks");

if (args is ["--capture", var file])
{
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using var frame = window.CaptureRenderedFrame();
    frame?.Save(file);
}
window.Close();

sealed class FixtureDirectory : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("qms8-ui-").FullName;
    public void Dispose() => Directory.Delete(Path, true);
}
