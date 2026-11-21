using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuestMapStudio.Core;

namespace QuestMapStudio;

public sealed partial class MainWindow : Window
{
	private sealed record CardDrag(IPointer Pointer, Point Start, Vector Grip, double Width, Border? Source, MapEntry? Map, EntryRow? Track, PlaylistDocument? Playlist);

	private enum MapSort
	{
		Title,
		New,
		Date,
		Level
	}

	private sealed class ImportProgress
	{
		public Dictionary<string, string?> InstalledFolders { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

		public HashSet<string> KnownHashes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		public Dictionary<string, string> KnownCodes { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public Dictionary<string, string> AlreadyThere { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public int Installed { get; set; }
	}

	private sealed record EntryRow(PlaylistDocument Owner, int Index, JsonNode Node, string Name, MapEntry? Map, bool Missing);

	private readonly Canvas dragLayer = new Canvas
	{
		IsHitTestVisible = false,
		ClipToBounds = true
	};

	private readonly Border dropMarker = new Border
	{
		Height = 2.0,
		ZIndex = 1,
		Background = Accent,
		IsVisible = false
	};

	private readonly ScrollViewer playlistScroll = new ScrollViewer();

	private readonly DispatcherTimer dragScroll = new DispatcherTimer
	{
		Interval = TimeSpan.FromMilliseconds(25L)
	};

	private Border? playlistTarget;

	private CardDrag? cardDrag;

	private Border? dragCard;

	private MapEntry[]? draggedMaps;

	private Point dragPosition;

	private static readonly IBrush Ink = Brush.Parse("#101322");

	private static readonly IBrush Panel = Brush.Parse("#191D30");

	private static readonly IBrush Card = Brush.Parse("#222941");

	private static readonly IBrush Stroke = Brush.Parse("#384360");

	private static readonly IBrush Muted = Brush.Parse("#ACB9D3");

	private static readonly IBrush Accent = Brush.Parse("#77DBF8");

	private static readonly IBrush Marked = Brush.Parse("#263F5C");

	private readonly ListBox mapList = new ListBox
	{
		Background = Brushes.Transparent
	};

	private readonly ListBox entries = new ListBox
	{
		Background = Brushes.Transparent,
		MaxHeight = 374.0
	};

	private readonly StackPanel playlistCards = new StackPanel
	{
		Spacing = 7.0
	};

	private readonly ContentControl expanded = new ContentControl();

	private readonly TextBlock queueSummary = new TextBlock
	{
		TextWrapping = TextWrapping.Wrap,
		Foreground = Muted
	};

	private readonly TextBlock pendingText = new TextBlock
	{
		Foreground = Muted,
		VerticalAlignment = VerticalAlignment.Center
	};

	private readonly ListBox importList = new ListBox
	{
		Background = Brushes.Transparent
	};

	private readonly TextBox search = new TextBox
	{
		Watermark = "Search maps"
	};

	private readonly TextBlock mapCount = new TextBlock
	{
		Foreground = Muted
	};

	private readonly CheckBox markVisible = new CheckBox
	{
		MinWidth = 24.0
	};

	private Button? deleteChecked;

	private readonly StackPanel dropZoneContent = new StackPanel
	{
		Spacing = 10.0
	};

	private readonly Button addSelected = new Button
	{
		Content = "Add checked maps",
		IsEnabled = false
	};

	private MapSort sortMode;

	private bool reverse;

	private bool explicitlySorted;

	private readonly HashSet<MapEntry> checkedDuration = new HashSet<MapEntry>();

	private bool durationCacheChanged;

	private readonly Dictionary<string, Bitmap> thumbnails = new Dictionary<string, Bitmap>(StringComparer.Ordinal);

	private readonly Dictionary<string, Task<Bitmap?>> pendingThumbnails = new Dictionary<string, Task<Bitmap>>(StringComparer.Ordinal);

	private readonly SemaphoreSlim coverReads = new SemaphoreSlim(3);

	private bool windowClosed;

	private static readonly HttpClient beatSaverHttp = new HttpClient(new HttpClientHandler
	{
		AllowAutoRedirect = false
	})
	{
		DefaultRequestHeaders = 
		{
			UserAgent = 
			{
				new ProductInfoHeaderValue("QuestMapStudio", "1.0.0")
			}
		}
	};

	private readonly LocalData data;

	private readonly ComboBox devices = new ComboBox
	{
		MinWidth = 185.0,
		MaxWidth = 245.0,
		PlaceholderText = "Connect a Quest"
	};

	private readonly TextBlock connection = new TextBlock
	{
		Text = "Offline",
		VerticalAlignment = VerticalAlignment.Center
	};

	private readonly TextBlock status = new TextBlock
	{
		Text = "Connect a Quest to sync your library.",
		TextWrapping = TextWrapping.Wrap
	};

	private readonly ProgressBar progress = new ProgressBar
	{
		IsIndeterminate = true,
		IsVisible = false,
		Height = 2.0
	};

	private readonly Button cancelButton = new Button
	{
		Content = "Cancel",
		IsVisible = false
	};

	private readonly Button applyButton = new Button
	{
		Content = "Apply",
		MinWidth = 99.0,
		IsEnabled = false
	};

	private readonly Grid toolbar = new Grid
	{
		ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"),
		ColumnSpacing = 8.0
	};

	private readonly List<MapEntry> maps = new List<MapEntry>();

	private readonly List<PlaylistDocument> playlists = new List<PlaylistDocument>();

	private readonly List<ImportBatch> imports = new List<ImportBatch>();

	private readonly List<string> importQueue = new List<string>();

	private readonly Dictionary<string, string> archiveErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> checkedMaps = new HashSet<string>(StringComparer.Ordinal);

	private readonly Grid workspace;

	private PlaylistDocument? current;

	private CancellationTokenSource? operation;

	private string profile = "local";

	private string? activeSerial;

	private string[] nativeOrder = Array.Empty<string>();

	private bool orderLoaded;

	private PlaylistOrderDraft? pendingOrder;

	private readonly HttpClient beatSaverClient;

	private Button? undoOrder;

	private bool Busy => operation != null;

	private string AdbPath
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(data.Settings.AdbPath))
			{
				return data.Settings.AdbPath;
			}
			return Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe");
		}
	}

	private void WireCardDrag()
	{
		dragLayer.Children.Add(dropMarker);
		AddHandler(InputElement.PointerMovedEvent, CardPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
		AddHandler(InputElement.PointerReleasedEvent, CardPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
		PointerCaptureLost += (object? _, PointerCaptureLostEventArgs _) =>
		{
			EndCardDrag();
		};
		Deactivated += (object? _, EventArgs _) =>
		{
			EndCardDrag();
		};
		dragScroll.Tick += (object? _, EventArgs _) =>
		{
			ScrollWhileDragging();
		};
	}

	private void RegisterCardDrag(ListBox list)
	{
		list.AddHandler(InputElement.PointerPressedEvent, (object? _, PointerPressedEventArgs e) =>
		{
			if (!Busy && !(cardDrag != null))
			{
				PlaylistDocument playlistDocument = current;
				if (playlistDocument != null && !playlistDocument.PendingDelete && e.Pointer.Type == PointerType.Mouse && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && Ancestor<Button>(e.Source) == null)
				{
					ListBoxItem listBoxItem = Ancestor<ListBoxItem>(e.Source);
					if (listBoxItem != null)
					{
						MapEntry map = ((listBoxItem.DataContext is MapEntry { Error: null } mapEntry) ? mapEntry : null);
						EntryRow entryRow = listBoxItem.DataContext as EntryRow;
						if (map != null || !(entryRow == null))
						{
							Point position = e.GetPosition(dragLayer);
							Border border = listBoxItem.GetVisualDescendants().OfType<Border>().FirstOrDefault((Border border2) => border2.Child is Grid && border2.Background == (entryRow != null ? PlaylistTrackBackground : map != null && checkedMaps.Contains(map.Folder) ? Marked : Card));
							Rect rect = BoundsInDragLayer((Visual)(((object)border) ?? ((object)listBoxItem)));
							cardDrag = new CardDrag(Grip: new Vector(Math.Clamp(position.X - rect.X, 0.0, rect.Width), Math.Clamp(position.Y - rect.Y, 0.0, rect.Height)), Pointer: e.Pointer, Start: position, Width: rect.Width, Source: border, Map: map, Track: entryRow, Playlist: null);
							e.Pointer.Capture(this);
							e.Handled = true;
						}
					}
				}
			}
		}, RoutingStrategies.Tunnel);
	}

	private void RegisterPlaylistDrag(Button button, Border card, PlaylistDocument playlist)
	{
		button.AddHandler(InputElement.PointerPressedEvent, (object? _, PointerPressedEventArgs e) =>
		{
			if (!Busy && !(cardDrag != null) && orderLoaded && e.Pointer.Type == PointerType.Mouse && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			{
				Point position = e.GetPosition(dragLayer);
				Rect rect = BoundsInDragLayer(card);
				cardDrag = new CardDrag(e.Pointer, position, new Vector(position.X - rect.X, position.Y - rect.Y), rect.Width, card, null, null, playlist);
				e.Pointer.Capture(this);
				e.Handled = true;
			}
		}, RoutingStrategies.Tunnel);
	}

	private void CardPointerMoved(object? sender, PointerEventArgs e)
	{
		CardDrag cardDrag = this.cardDrag;
		if ((object)cardDrag == null || cardDrag.Pointer != e.Pointer)
		{
			return;
		}
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			EndCardDrag();
			return;
		}
		dragPosition = e.GetPosition(dragLayer);
		if (dragCard == null && Math.Abs(dragPosition.X - cardDrag.Start.X) + Math.Abs(dragPosition.Y - cardDrag.Start.Y) < 7.0)
		{
			return;
		}
		if (dragCard == null)
		{
			PlaylistDocument playlist = cardDrag.Playlist;
			Control control;
			if (playlist == null)
			{
				EntryRow track = cardDrag.Track;
				if (track != null) control = PlaylistTrack(track);
                else
                {
                    draggedMaps = MapsForDrag(cardDrag.Map!);
                    control = MapDragStack(draggedMaps);
                }
			}
			else
			{
				control = PlaylistHeader(playlist, interactive: false);
			}
			Control child = control;
			dragCard = new Border
			{
				Width = Math.Max(140.0, cardDrag.Width),
				Child = child,
				BorderBrush = Accent,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(8.0),
				Background = Panel,
				BoxShadow = new BoxShadows(new BoxShadow
				{
					Blur = 16.0,
					OffsetY = 3.0,
					Color = Colors.Black
				}),
				Opacity = cardDrag.Map != null ? 0.60 : 0.96
			};
			AutomationProperties.SetName(dragCard, (cardDrag.Playlist == null) ? "Dragged map card" : "Dragged playlist card");
			dragLayer.Children.Add(dragCard);
            if (cardDrag.Source != null)
			{
				cardDrag.Source.Opacity = 0.35;
			}
			dragScroll.Start();
		}
		Canvas.SetLeft(dragCard, dragPosition.X - cardDrag.Grip.X);
        double top = cardDrag.Map != null ? dragPosition.Y - dragCard.BorderThickness.Top : dragPosition.Y - cardDrag.Grip.Y;
        Canvas.SetTop(dragCard, top);
		UpdateDropMarker();
		e.Handled = true;
	}

	private async void CardPointerReleased(object? sender, PointerReleasedEventArgs e)
	{
		CardDrag cardDrag = this.cardDrag;
		if ((object)cardDrag == null || cardDrag.Pointer != e.Pointer || e.InitialPressMouseButton != MouseButton.Left)
		{
			return;
		}
		MapEntry[]? selectedMaps = draggedMaps;
		Point position = e.GetPosition(dragLayer);
		(int, double, Rect)? tuple = ((dragCard != null) ? DropSlot(position) : (((int, double, Rect)?)null));
		int num;
		if (dragCard == null)
		{
			Border source = cardDrag.Source;
			if (source != null)
			{
				num = (BoundsInDragLayer(source).Contains(position) ? 1 : 0);
				goto IL_00b1;
			}
		}
		num = 0;
		goto IL_00b1;
		IL_00b1:
		bool flag = (byte)num != 0;
		EndCardDrag();
		e.Handled = true;
		try
		{
			PlaylistDocument playlist = cardDrag.Playlist;
			if (playlist != null)
			{
				if (tuple.HasValue)
				{
					MovePlaylist(playlist, tuple.GetValueOrDefault().Item1);
				}
				else if (flag)
				{
					TogglePlaylist(playlist);
				}
			}
			else
			{
				if (!tuple.HasValue)
				{
					return;
				}
				(int Index, double Y, Rect Visible) slot = tuple.GetValueOrDefault();
				PlaylistDocument playlistDocument = current;
				if (playlistDocument == null || playlistDocument.PendingDelete)
				{
					return;
				}
				EntryRow track = cardDrag.Track;
				if ((object)track != null && track.Owner == playlistDocument)
				{
					int num2 = playlistDocument.Songs.IndexOf(track.Node);
					MoveTrack(playlistDocument, num2, (slot.Index > num2) ? (slot.Index - 1) : slot.Index);
					return;
				}
				MapEntry map = cardDrag.Map;
				if (map != null)
				{
					MapEntry[] selected = selectedMaps ?? MapsForDrag(map);
					await Run((CancellationToken ct) => AddMaps(selected, ct, slot.Index));
				}
			}
		}
		catch (Exception ex)
		{
			Report(ex.Message);
		}
	}

	private void EndCardDrag()
	{
		IPointer pointer = cardDrag?.Pointer;
		if (dragCard != null)
		{
			Border border = cardDrag?.Source;
			if (border != null)
			{
				border.Opacity = 1.0;
			}
		}
		cardDrag = null;
		draggedMaps = null;
		dragScroll.Stop();
		if (dragCard != null)
		{
			dragLayer.Children.Remove(dragCard);
		}
		dragCard = null;
		dropMarker.IsVisible = false;
		if (playlistTarget != null)
		{
			playlistTarget.BorderBrush = Stroke;
		}
		if (pointer?.Captured == this)
		{
			pointer.Capture(null);
		}
	}

	private Rect BoundsInDragLayer(Visual visual)
	{
		Point? point = visual.TranslatePoint(default, dragLayer);
		if (point.HasValue)
		{
			Point valueOrDefault = point.GetValueOrDefault();
			return new Rect(valueOrDefault, visual.Bounds.Size);
		}
		return default;
	}

	private Rect ScrollBounds(ScrollViewer viewer)
	{
		ScrollContentPresenter scrollContentPresenter = viewer.GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault();
		return BoundsInDragLayer((Visual)(((object)scrollContentPresenter) ?? ((object)viewer)));
	}

	private ScrollViewer? TrackScroller()
	{
		return entries.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
	}

	private (int Index, double Y, Rect Visible)? DropSlot(Point point)
	{
		Rect outer = ScrollBounds(playlistScroll);
		if (cardDrag?.Playlist != null)
		{
			if (!outer.Contains(point))
			{
				return null;
			}
			(int, Rect)[] array = (from header in playlistCards.Children.OfType<Grid>()
				where header.Tag is PlaylistDocument
				select (Index: playlists.IndexOf((PlaylistDocument)header.Tag), Bounds: BoundsInDragLayer(header)) into header
				where header.Bounds.Bottom > outer.Top && header.Bounds.Top < outer.Bottom
				select header).ToArray();
			if (array.Length == 0)
			{
				return null;
			}
			(int, Rect)[] array2 = array;
			for (int num = 0; num < array2.Length; num++)
			{
				var (item, rect) = array2[num];
				if (point.Y < rect.Center.Y)
				{
					return (item, Math.Max(outer.Top, rect.Top), outer);
				}
			}
			var (num2, rect2) = array[^1];
			return (num2 + 1, Math.Min(outer.Bottom, rect2.Bottom), outer);
		}
		if (playlistTarget != null)
		{
			PlaylistDocument playlistDocument = current;
			if (playlistDocument != null && !playlistDocument.PendingDelete && playlistTarget.IsAttachedToVisualTree())
			{
				Rect item2 = BoundsInDragLayer(playlistTarget).Intersect(outer);
				if (item2.Width <= 0.0 || !item2.Contains(point))
				{
					return null;
				}
				ScrollViewer scrollViewer = TrackScroller();
				if (scrollViewer == null || entries.ItemCount == 0)
				{
					return (0, item2.Bottom - 5.0, item2);
				}
				Rect list = ScrollBounds(scrollViewer).Intersect(outer);
				(EntryRow, Rect)[] array3 = (from listBoxItem in entries.GetVisualDescendants().OfType<ListBoxItem>()
					where listBoxItem.DataContext is EntryRow
					select ((EntryRow)listBoxItem.DataContext, BoundsInDragLayer(listBoxItem)) into row
					where row.Item2.Bottom > list.Top && row.Item2.Top < list.Bottom
					orderby row.Item1.Index
					select row).ToArray();
				if (array3.Length == 0)
				{
					return null;
				}
				(EntryRow, Rect)[] array4 = array3;
				for (int num = 0; num < array4.Length; num++)
				{
					var (entryRow, rect3) = array4[num];
					if (point.Y < rect3.Center.Y)
					{
						return (entryRow.Index, Math.Max(list.Top, rect3.Top), list);
					}
				}
				var (entryRow2, rect4) = array3[^1];
				return (entryRow2.Index + 1, Math.Min(list.Bottom, rect4.Bottom), list);
			}
		}
		return null;
	}

	private void UpdateDropMarker()
	{
		(int, double, Rect)? tuple = DropSlot(dragPosition);
		dropMarker.IsVisible = tuple.HasValue;
		if (playlistTarget != null)
		{
			playlistTarget.BorderBrush = ((tuple.HasValue && cardDrag?.Playlist == null) ? Accent : Stroke);
		}
		if (tuple.HasValue)
		{
			(int, double, Rect) valueOrDefault = tuple.GetValueOrDefault();
			dropMarker.Width = Math.Max(0.0, valueOrDefault.Item3.Width - 10.0);
			Canvas.SetLeft(dropMarker, valueOrDefault.Item3.Left + 5.0);
			Canvas.SetTop(dropMarker, valueOrDefault.Item2 - 1.0);
		}
	}

	private void ScrollWhileDragging()
	{
		if (cardDrag == null || dragCard == null)
		{
			return;
		}
		Rect rect = ScrollBounds(playlistScroll);
		if (rect.Contains(dragPosition))
		{
			ScrollViewer scrollViewer = ((cardDrag.Playlist == null) ? TrackScroller() : null);
			if (scrollViewer != null && ScrollAtEdge(scrollViewer, ScrollBounds(scrollViewer).Intersect(rect), dragPosition))
			{
				UpdateDropMarker();
			}
			else if (ScrollAtEdge(playlistScroll, rect, dragPosition))
			{
				UpdateDropMarker();
			}
			else if (dropMarker.IsVisible)
			{
				UpdateDropMarker();
			}
		}
	}

	private static bool ScrollAtEdge(ScrollViewer viewer, Rect visible, Point pointer)
	{
		if (visible.Height <= 0.0 || !visible.Contains(pointer))
		{
			return false;
		}
		double num = Math.Min(38.0, visible.Height / 3.0);
		double num2 = Math.Clamp((visible.Top + num - pointer.Y) / num, 0.0, 1.0);
		double num3 = (Math.Clamp((pointer.Y - visible.Bottom + num) / num, 0.0, 1.0) - num2) * 22.0;
		Vector offset = viewer.Offset;
		double num4 = Math.Clamp(offset.Y + num3, 0.0, Math.Max(0.0, viewer.Extent.Height - viewer.Viewport.Height));
		if (Math.Abs(num4 - offset.Y) < 0.01)
		{
			return false;
		}
		viewer.Offset = new Vector(offset.X, num4);
		return true;
	}

	private static TextBlock Label(string? text, double size = 14.0, bool bold = false)
	{
		return new TextBlock
		{
			Text = text,
			FontSize = size,
			FontWeight = (bold ? FontWeight.DemiBold : FontWeight.Normal),
			VerticalAlignment = VerticalAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis
		};
	}

	private static Border Surface(Control child, Thickness padding)
	{
		return new Border
		{
			Child = child,
			Padding = padding,
			Background = Panel,
			BorderBrush = Stroke,
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(10.0)
		};
	}

	private static Grid Dock(Control heading, Control body)
	{
		Grid grid = new Grid
		{
			RowDefinitions = new RowDefinitions("Auto,*"),
			RowSpacing = 10.0,
			Children = { heading }
		};
		Grid.SetRow(body, 1);
		grid.Children.Add(body);
		return grid;
	}

	private static void CompactItems(ListBox list)
	{
		Style style = new Style((Selector? selector) => selector.OfType<ListBoxItem>());
		style.Setters.Add(new Setter(TemplatedControl.PaddingProperty, new Thickness(4.0, 3.0, 10.0, 3.0)));
		list.Styles.Add(style);
	}

	private Control MapsPane()
	{
		CompactItems(mapList);
		ConfigureScrollBar(mapList);
		AutomationProperties.SetName(mapList, "Map library");
		Button sort = new Button
		{
			Content = "⇅",
			FontSize = 17.0,
			MinWidth = 32.0
		};
		ToolTip.SetTip(sort, "Sort maps: a-z");
		AutomationProperties.SetName(sort, "Sort maps");
		MenuFlyout menuFlyout = new MenuFlyout();
		(MapSort, string, string)[] array = new (MapSort, string, string)[4]
		{
			(MapSort.Title, "a-z", "Map title"),
			(MapSort.New, "new", "Latest queued import, then file date"),
			(MapSort.Date, "data", "Info.dat modification date"),
			(MapSort.Level, "lvl", "Highest difficulty")
		};
		for (int i = 0; i < array.Length; i++)
		{
			(MapSort, string, string) tuple = array[i];
			MapSort mode = tuple.Item1;
			string item = tuple.Item2;
			string description = tuple.Item3;
			MenuItem menuItem = new MenuItem
			{
				Header = item
			};
			ToolTip.SetTip(menuItem, description);
			menuItem.Click += (object? _, RoutedEventArgs _) =>
			{
				reverse = explicitlySorted && sortMode == mode && !reverse;
				explicitlySorted = true;
				sortMode = mode;
				MapSort mapSort = mode;
				bool flag = (uint)(mapSort - 1) <= 1u;
				bool flag2 = flag != reverse;
				sort.Content = (flag2 ? "↓" : "↑");
				ToolTip.SetTip(sort, description + (flag2 ? " · Descending" : " · Ascending"));
				FilterMaps();
				ResetMapScroll();
			};
			menuFlyout.Items.Add(menuItem);
		}
		sort.Flyout = menuFlyout;
		Grid grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("*,Auto")
		};
		grid.Children.Add(Label("Maps", 21.0, bold: true));
		AutomationProperties.SetName(clearSelection, "Clear checked maps");
        ToolTip.SetTip(clearSelection, "Clear all checked maps");
        clearSelection.Click += (_, _) =>
        {
            if (checkedMaps.Count == 0) return;
            checkedMaps.Clear();
            RefreshCheckedMapCards();
        };
        Place(grid, Row(clearSelection, sort), 1);
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 7.0
		};
		stackPanel.Children.Add(grid);
		stackPanel.Children.Add(search);
		ToolTip.SetTip(markVisible, "Check all visible maps");
		markVisible.Click += (object? _, RoutedEventArgs _) =>
		{
			IEnumerable? itemsSource = mapList.ItemsSource;
			foreach (MapEntry item2 in ((itemsSource != null) ? itemsSource.OfType<MapEntry>() : null) ?? Array.Empty<MapEntry>())
			{
				if (markVisible.IsChecked == true && item2.Error == null)
				{
					checkedMaps.Add(item2.Folder);
				}
				else
				{
					checkedMaps.Remove(item2.Folder);
				}
			}
			RefreshCheckedMapCards();
		};
		deleteChecked = Button("Delete checked…", DeleteCheckedMaps);
		stackPanel.Children.Add(Row(markVisible, mapCount, deleteChecked));
		search.TextChanged += (object? _, TextChangedEventArgs _) =>
		{
			FilterMaps();
		};
		mapList.ItemTemplate = new FuncDataTemplate<MapEntry>((MapEntry map, INameScope _) => (map != null) ? MapCard(map) : new Border());
		RegisterCardDrag(mapList);
		return Surface(Dock(stackPanel, mapList), new Thickness(11.0));
	}

	private Control MapCard(MapEntry map)
	{
		Border card = new Border
		{
			Background = (checkedMaps.Contains(map.Folder) ? Marked : Card),
			BorderBrush = Stroke,
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(7.0),
			Padding = new Thickness(7.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 4.0)
		};
		Grid grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("24,44,*"),
			ColumnSpacing = 7.0
		};
		CheckBox check = new CheckBox
		{
			MinWidth = 22.0,
			Width = 22.0,
			Padding = new Thickness(0.0),
			IsEnabled = (map.Error == null),
			IsChecked = checkedMaps.Contains(map.Folder),
			Tag = card,
			VerticalAlignment = VerticalAlignment.Center
		};
		check.Click += (object? _, RoutedEventArgs _) =>
		{
			if (check.IsChecked == true)
			{
				checkedMaps.Add(map.Folder);
			}
			else
			{
				checkedMaps.Remove(map.Folder);
			}
			card.Background = ((check.IsChecked == true) ? Marked : Card);
			UpdateMapCount();
		};
		grid.Children.Add(check);
		Place(grid, MapCover(map, 44), 1);
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 3.0
		};
		TextBlock textBlock = Label(map.Title, 14.0, bold: true);
		textBlock.TextWrapping = TextWrapping.Wrap;
		textBlock.TextTrimming = TextTrimming.None;
		stackPanel.Children.Add(textBlock);
		string[] array = new string[2] { map.Artist, null };
		string mapper = map.Mapper;
		array[1] = ((mapper != null && mapper.Length > 0) ? ("Mapped by " + map.Mapper) : "");
		string text = string.Join(" · ", array.Where((string x) => !string.IsNullOrWhiteSpace(x)));
		if (text.Length > 0)
		{
			stackPanel.Children.Add(Meta(text));
		}
		TextBlock metadata = Meta(TrackMetrics(map));
		TextBlock textBlock2 = metadata;
		string? text2 = metadata.Text;
		textBlock2.IsVisible = text2 != null && text2.Length > 0;
		if (!map.Duration.HasValue && map.AudioFile != null)
		{
			metadata.AttachedToVisualTree += async (object? _, VisualTreeAttachmentEventArgs _) =>
			{
				await TryReadDuration(map);
				metadata.Text = TrackMetrics(map);
				metadata.IsVisible = metadata.Text.Length > 0;
			};
		}
		stackPanel.Children.Add(metadata);
		WrapPanel wrapPanel = new WrapPanel
		{
			ItemSpacing = 4.0,
			LineSpacing = 3.0
		};
		foreach (IGrouping<string, Difficulty> item2 in from d in map.Difficulties
			group d by DifficultyLabel(d.Name) into g
			orderby Rank(g.Key)
			select g)
		{
			(string, string) tuple = item2.Key switch
			{
				"Easy" => ("#143B37", "#8CE7CC"), 
				"Normal" => ("#193B57", "#9EDAFF"), 
				"Hard" => ("#4B341B", "#FFCF85"), 
				"Expert" => ("#513040", "#FFA7C5"), 
				"Expert+" => ("#3B2B61", "#DEC2FF"), 
				_ => ("#30384B", "#D5DFF2"), 
			};
			Border item = new Border
			{
				Background = Brush.Parse(tuple.Item1),
				Padding = new Thickness(5.0, 1.0),
				CornerRadius = new CornerRadius(3.0),
				Child = new TextBlock
				{
					Text = item2.Key,
					FontSize = 10.0,
					Foreground = Brush.Parse(tuple.Item2)
				}
			};
			wrapPanel.Children.Add(item);
		}
		if (wrapPanel.Children.Count > 0)
		{
			stackPanel.Children.Add(wrapPanel);
		}
		StackPanel stackPanel2 = new StackPanel
		{
			Spacing = 3.0,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(12.0, 0.0, 0.0, 0.0)
		};
		AddMapMods(stackPanel2, "Required mods", map.Difficulties.SelectMany((Difficulty d) => d.Requirements));
		AddMapMods(stackPanel2, "Recommended mods", map.Difficulties.SelectMany((Difficulty d) => d.Suggestions));
		string error = map.Error;
		if (error != null)
		{
			stackPanel.Children.Add(Meta(error));
		}
		Place(grid, stackPanel, 2);
		if (stackPanel2.Children.Count > 0)
		{
			grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(0.8, GridUnitType.Star)));
			Place(grid, stackPanel2, 3);
		}
		card.Child = grid;
		return card;
	}

	private static void AddMapMods(StackPanel info, string label, IEnumerable<string> mods)
	{
		string text = string.Join(", ", mods.Distinct(StringComparer.OrdinalIgnoreCase));
		if (text.Length != 0)
		{
			TextBlock textBlock = Meta(label + ": " + text);
			textBlock.TextWrapping = TextWrapping.Wrap;
			textBlock.TextTrimming = TextTrimming.None;
			textBlock.TextAlignment = TextAlignment.Right;
			info.Children.Add(textBlock);
		}
	}

	private static TextBlock Meta(string text)
	{
		TextBlock textBlock = Label(text, 11.0);
		textBlock.Foreground = Muted;
		return textBlock;
	}

	private static string TrackMetrics(MapEntry map)
	{
		List<string> list = new List<string>();
		double? duration = map.Duration;
		if (duration.HasValue)
		{
			double valueOrDefault = duration.GetValueOrDefault();
			if (valueOrDefault > 0.0 && valueOrDefault < 86400.0)
			{
				list.Add(FormatDuration(map.Duration.Value));
			}
		}
		duration = map.Bpm;
		if (duration.HasValue)
		{
			list.Add(duration.GetValueOrDefault().ToString("0.##", CultureInfo.InvariantCulture) + " BPM");
		}
		string mapCode = map.MapCode;
		if (mapCode != null)
		{
			list.Add("#" + mapCode);
		}
		return string.Join(" · ", list);
	}

	private async Task TryReadDuration(MapEntry map)
	{
		if (map.Duration.HasValue || activeSerial == null || profile != activeSerial || map.AudioFile == null || !checkedDuration.Add(map))
		{
			return;
		}
		string extension = Path.GetExtension(map.AudioFile);
		if (!extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".egg", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".opus", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		string path = AdbClient.Q(map.Folder + "/" + map.AudioFile);
		string serial = activeSerial;
		await coverReads.WaitAsync();
		try
		{
			if (windowClosed || activeSerial != serial || !maps.Contains(map))
			{
				return;
			}
			using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10L));
			AdbClient adb = new AdbClient(AdbPath, serial, timeout.Token);
			byte[] head = await adb.Run(new _003C_003Ez__ReadOnlyArray<string>(new string[3]
			{
				"shell",
				"-T",
				"head -c 4096 " + path
			}), 4096);
			byte[] array = await adb.Run(new _003C_003Ez__ReadOnlyArray<string>(new string[3]
			{
				"shell",
				"-T",
				"tail -c 131072 " + path
			}), 131072);
			double? num = OggDuration.Read(head, array);
			if (num.HasValue)
			{
				double valueOrDefault = num.GetValueOrDefault();
				if (maps.Contains(map))
				{
					map.Duration = valueOrDefault;
					durationCacheChanged = true;
				}
			}
		}
		catch (Exception ex) when ((ex is IOException || ex is OperationCanceledException || ex is Win32Exception) ? true : false)
		{
		}
		finally
		{
			coverReads.Release();
		}
	}

	private static string FormatDuration(double seconds)
	{
		TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);
		if (!(timeSpan.TotalHours >= 1.0))
		{
			return timeSpan.ToString("m\\:ss");
		}
		return timeSpan.ToString("h\\:mm\\:ss");
	}

	private static string DifficultyLabel(string name)
	{
		switch (name.ToUpperInvariant())
		{
		case "EASY":
			return "Easy";
		case "NORMAL":
			return "Normal";
		case "HARD":
			return "Hard";
		case "EXPERT":
			return "Expert";
		case "EXPERTPLUS":
		case "EXPERT+":
			return "Expert+";
		default:
			return name;
		}
	}

	private static int Rank(string name)
	{
		return name switch
		{
			"Easy" => 1, 
			"Normal" => 2, 
			"Hard" => 3, 
			"Expert" => 4, 
			"Expert+" => 5, 
			_ => 0, 
		};
	}

	private void FilterMaps()
	{
		string query = search.Text?.Trim() ?? "";
		IEnumerable<MapEntry> source = maps.Where((MapEntry m) =>
		{
			if (query.Length != 0)
			{
				InlineArray6<string> buffer = default;
				buffer[0] = m.Title;
				buffer[1] = m.Artist;
				buffer[2] = m.Mapper;
				buffer[3] = m.MapCode;
				buffer[4] = m.RequiredText;
				buffer[5] = m.SuggestedText;
				return string.Join(" ", (ReadOnlySpan<string?>)buffer).Contains(query, StringComparison.CurrentCultureIgnoreCase);
			}
			return true;
		});
		StringComparer currentCultureIgnoreCase = StringComparer.CurrentCultureIgnoreCase;
		Dictionary<string, long> recentlyImported;
		if (sortMode == MapSort.Title)
		{
			mapList.ItemsSource = (reverse ? source.OrderByDescending((MapEntry m) => m.Title, currentCultureIgnoreCase) : source.OrderBy((MapEntry m) => m.Title, currentCultureIgnoreCase)).ToArray();
		}
		else
		{
			recentlyImported = new Dictionary<string, long>(StringComparer.Ordinal);
			if (sortMode == MapSort.New)
			{
				foreach (ImportBatch item in imports.OrderByDescending((ImportBatch batch) => batch.Time))
				{
					string[] folders = item.Folders;
					foreach (string key in folders)
					{
						recentlyImported.TryAdd(key, item.Time.ToUnixTimeSeconds());
					}
				}
			}
			IOrderedEnumerable<(MapEntry, long?)> source2 = from m in source
				select (Map: m, Key: Value(m)) into x
				orderby !x.Key.HasValue
				select x;
			MapSort mapSort = sortMode;
			bool flag = (uint)(mapSort - 1) <= 1u;
			IOrderedEnumerable<(MapEntry, long?)> source3 = ((flag != reverse) ? source2.ThenByDescending(((MapEntry Map, long? Key) x) => x.Key) : source2.ThenBy(((MapEntry Map, long? Key) x) => x.Key));
			mapList.ItemsSource = source3.ThenBy(((MapEntry Map, long? Key) x) => x.Map.Title, currentCultureIgnoreCase).Select(((MapEntry Map, long? Key) x) => x.Map).ToArray();
		}
		UpdateMapCount();
		long? Value(MapEntry m)
		{
			long value;
			return sortMode switch
			{
				MapSort.New => recentlyImported.TryGetValue(m.Folder, out value) ? new long?(value) : m.ModifiedUnix, 
				MapSort.Date => m.ModifiedUnix, 
				_ => (m.Difficulties.Count == 0) ? ((long?)null) : new long?(m.Difficulties.Max((Difficulty d) => Rank(DifficultyLabel(d.Name)))), 
			};
		}
	}

	private void UpdateMapCount()
	{
		IEnumerable? itemsSource = mapList.ItemsSource;
		MapEntry[] array = (((itemsSource != null) ? itemsSource.OfType<MapEntry>() : null) ?? Array.Empty<MapEntry>()).Where((MapEntry m) => m.Error == null).ToArray();
		markVisible.IsChecked = array.Length != 0 && array.All((MapEntry m) => checkedMaps.Contains(m.Folder));
		mapCount.Text = $"{mapList.ItemCount} shown · {checkedMaps.Count} checked";
		clearSelection.IsChecked = checkedMaps.Count > 0;
		clearSelection.IsEnabled = checkedMaps.Count > 0;
		if (deleteChecked != null)
		{
			deleteChecked.IsEnabled = activeSerial != null && checkedMaps.Count > 0;
		}
		addSelected.IsEnabled = current != null && !current.PendingDelete && checkedMaps.Count > 0;
	}

	private async Task DeleteCheckedMaps()
	{
		if (activeSerial == null)
		{
			return;
		}
		MapEntry[] selected = CheckedInListOrder();
		bool flag = selected.Length == 0;
		if (!flag)
		{
			flag = !(await Confirm($"Permanently delete {selected.Length} checked map(s) from Quest? " + "Playlist entries for these maps will stay in their playlists."));
		}
		if (flag)
		{
			return;
		}
		await Run(async (CancellationToken ct) =>
		{
			AdbClient client = Client(ct);
			await new PlaylistDevice(client).Check();
			QuestRepository repo = new QuestRepository(client);
			string[] roots = QuestRepository.DefaultRoots.Concat(data.Settings.ExtraRoots).ToArray();
			int deleted = 0;
			int failed = 0;
			try
			{
				MapEntry[] array = selected;
				foreach (MapEntry map in array)
				{
					ct.ThrowIfCancellationRequested();
					try
					{
						await repo.DeleteMap(map, roots);
						maps.Remove(map);
						checkedMaps.Remove(map.Folder);
						deleted++;
					}
					catch (IOException ex)
					{
						failed++;
						Report(map.Title + " — " + ex.Message);
						await client.CheckConnection();
					}
				}
			}
			finally
			{
				data.SaveMaps(profile, maps);
				FilterMaps();
				RefreshTracks();
			}
			Report($"Deleted {deleted} map(s) from Quest" + ((failed == 0) ? "." : $" · Failed: {failed}. Refresh to verify remaining files."));
		});
	}

	private Control ImportPane()
	{
		StackPanel heading = new StackPanel
		{
			Spacing = 10.0,
			Children = 
			{
				(Control)Label("Add", 21.0, bold: true),
				(Control)Button("Choose files…", PickImports),
				(Control)queueSummary
			}
		};
		dropZoneContent.Children.Add(new TextBlock
		{
			Text = "Drop map ZIPs or playlist files here",
			TextWrapping = TextWrapping.Wrap,
			Foreground = Muted,
			HorizontalAlignment = HorizontalAlignment.Center,
			TextAlignment = TextAlignment.Center,
			Margin = new Thickness(4.0, 12.0)
		});
		importList.ItemTemplate = new FuncDataTemplate<string>((string path, INameScope _) =>
		{
			if (path != null)
			{
				TextBlock textBlock = new TextBlock
				{
					Text = Path.GetFileName(path),
					TextWrapping = TextWrapping.Wrap,
					FontSize = 11.0
				};
				Button button = Button("×", () =>
				{
					RememberEdit();
					importQueue.Remove(path);
					archiveErrors.Remove(path);
					data.SaveQueue(importQueue);
					UpdateQueue();
					return Task.CompletedTask;
				});
				ToolTip.SetTip(button, "Remove from queue");
				Grid grid = new Grid
				{
					ColumnDefinitions = new ColumnDefinitions("*,Auto"),
					Children = { (Control)textBlock }
				};
				Place(grid, button, 1);
				ToolTip.SetTip(grid, archiveErrors.TryGetValue(path, out string value) ? value : path);
				if (value != null)
				{
					textBlock.Foreground = Brushes.Salmon;
				}
				return grid;
			}
			return new Border();
		});
		Border zone = new Border
		{
			Background = Ink,
			CornerRadius = new CornerRadius(7.0),
			BorderBrush = Stroke,
			BorderThickness = new Thickness(1.0),
			Child = Dock(dropZoneContent, importList)
		};
		DragDrop.SetAllowDrop(zone, value: true);
		zone.AddHandler(DragDrop.DragOverEvent, (object? _, DragEventArgs e) =>
		{
			bool flag = !Busy && e.DataTransfer.Contains(DataFormat.File);
			zone.BorderBrush = (flag ? Accent : Stroke);
			e.DragEffects = (flag ? DragDropEffects.Copy : DragDropEffects.None);
			e.Handled = true;
		});
		zone.AddHandler(DragDrop.DragLeaveEvent, (object? _, DragEventArgs _) =>
		{
			zone.BorderBrush = Stroke;
		});
		zone.AddHandler(DragDrop.DropEvent, (object? _, DragEventArgs e) =>
		{
			zone.BorderBrush = Stroke;
			if (!Busy && e.DataTransfer.Contains(DataFormat.File))
			{
				try
				{
					Enqueue((e.DataTransfer.TryGetFiles() ?? Array.Empty<IStorageItem>()).Select((IStorageItem f) => f.TryGetLocalPath()).OfType<string>());
				}
				catch (Exception ex)
				{
					Report(ex.Message);
				}
				e.Handled = true;
			}
		});
		return Surface(Dock(heading, zone), new Thickness(10.0));
	}

	private async Task PickImports()
	{
		IReadOnlyList<IStorageFile> readOnlyList = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Choose maps or playlists",
			AllowMultiple = true,
			FileTypeFilter = new _003C_003Ez__ReadOnlySingleElementList<FilePickerFileType>(new FilePickerFileType("Maps and playlists")
			{
				Patterns = new _003C_003Ez__ReadOnlyArray<string>(new string[4] { "*.zip", "*.bplist", "*.json", "*.blist" })
			})
		});
		if (readOnlyList.Count > 0)
		{
			Enqueue(readOnlyList.Select((IStorageFile f) => f.TryGetLocalPath()).OfType<string>());
		}
	}

	private void Enqueue(IEnumerable<string> paths)
	{
		int count = importQueue.Count;
		EditorState? before = null;
		foreach (string path in paths)
		{
			bool flag;
			switch (Path.GetExtension(path).ToLowerInvariant())
			{
			case ".zip":
			case ".bplist":
			case ".json":
			case ".blist":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag && File.Exists(path) && !importQueue.Contains(path, StringComparer.OrdinalIgnoreCase))
			{
				before ??= CaptureEditor();
				importQueue.Add(path);
			}
		}
		if (before != null) RememberEdit(before);
		data.SaveQueue(importQueue);
		UpdateQueue();
		Report((importQueue.Count == count) ? "No new map or playlist files were added." : $"{importQueue.Count - count} file(s) queued. Click Apply to install.");
	}

	private void UpdateQueue()
	{
		importList.ItemsSource = importQueue.ToArray();
		queueSummary.Text = ((importQueue.Count == 0) ? "" : $"{importQueue.Count} queued");
		UpdatePending();
	}

	private void UpdatePending()
	{
		int num = Enumerable.Count(playlists, (PlaylistDocument p) => p.Pending || p.PendingDelete) + ((pendingOrder != null) ? 1 : 0);
		if (undoOrder != null)
		{
			undoOrder.IsVisible = pendingOrder != null;
		}
		applyButton.IsEnabled = !Busy && num + importQueue.Count > 0;
		UpdateHistoryButtons();
		pendingText.Text = ((num + importQueue.Count > 0) ? $"{importQueue.Count} queued · {num} playlist changes" : "");
	}

	private static T? Ancestor<T>(object? visual) where T : Visual
	{
		Visual obj = visual as Visual;
		if (obj == null)
		{
			return null;
		}
		return obj.GetSelfAndVisualAncestors().OfType<T>().FirstOrDefault();
	}

	private Control MapCover(MapEntry? map, int size = 42)
	{
		Image image = new Image
		{
			Width = size,
			Height = size,
			Stretch = Stretch.UniformToFill
		};
		if (map != null)
		{
			image.AttachedToVisualTree += async (object? _, VisualTreeAttachmentEventArgs _) =>
			{
				try
				{
					Image image2 = image;
					image2.Source = await GetMapCover(map);
				}
				catch (Exception)
				{
				}
			};
		}
		return new Border
		{
			Width = size,
			Height = size,
			Background = Brushes.DarkSlateGray,
			VerticalAlignment = VerticalAlignment.Center,
			ClipToBounds = true,
			CornerRadius = new CornerRadius(4.0),
			Child = image
		};
	}

	private Task<Bitmap?> GetMapCover(MapEntry map)
	{
		string coverFile = map.CoverFile;
		if (coverFile.Length == 0)
		{
			return Task.FromResult<Bitmap>(null);
		}
		string text = profile;
		string text2 = map.Folder + "/" + coverFile;
		string local = data.CoverPath(text, text2);
		if (thumbnails.TryGetValue(local, out Bitmap value))
		{
			return Task.FromResult(value);
		}
		if (pendingThumbnails.TryGetValue(local, out Task<Bitmap> value2))
		{
			return value2;
		}
		bool flag = activeSerial == text;
		string adbPath = AdbPath;
		Task<Bitmap> task = LoadMapCover(local, text2, flag ? text : null, adbPath);
		pendingThumbnails[local] = task;
		task.ContinueWith((Task<Bitmap?> _) =>
		{
			Dispatcher.UIThread.Post(() =>
			{
				pendingThumbnails.Remove(local);
			});
		}, TaskScheduler.Default);
		return task;
	}

	private async Task<Bitmap?> LoadMapCover(string local, string remote, string? serial, string executable)
	{
		await coverReads.WaitAsync();
		try
		{
			Bitmap bitmap = await Task.Run(async () =>
			{
				if (File.Exists(local))
				{
					try
					{
						using FileStream stream = File.OpenRead(local);
						return new Bitmap(stream);
					}
					catch (ArgumentException)
					{
						File.Delete(local);
					}
				}
				if (serial == null)
				{
					return (Bitmap)null;
				}
				using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(18L));
				string original = Path.Combine(Path.GetTempPath(), "qms-cover-" + Guid.NewGuid().ToString("N"));
				string staging = local + "." + Guid.NewGuid().ToString("N") + ".tmp";
				try
				{
					await new AdbClient(executable, serial, timeout.Token).PullFile(remote, original, 8388608L);
					byte[] array = TrackArtwork.CreateThumbnail(original);
					using MemoryStream preview = new MemoryStream(array);
					Bitmap bitmap2 = new Bitmap(preview);
					try
					{
						Directory.CreateDirectory(Path.GetDirectoryName(local));
						await File.WriteAllBytesAsync(staging, array, timeout.Token);
						File.Move(staging, local, overwrite: true);
						return bitmap2;
					}
					catch
					{
						bitmap2.Dispose();
						throw;
					}
				}
				finally
				{
					File.Delete(original);
					File.Delete(staging);
				}
			});
			if (windowClosed)
			{
				bitmap?.Dispose();
				return null;
			}
			if (bitmap != null)
			{
				thumbnails[local] = bitmap;
			}
			return bitmap;
		}
		finally
		{
			coverReads.Release();
		}
	}

	private static byte[]? PlaylistCoverBytes(PlaylistDocument doc)
	{
		string text = MapEntry.S(doc.Root, "imageString", MapEntry.S(doc.Root, "image"));
		int num = text.IndexOf("base64,", StringComparison.Ordinal);
		if (num >= 0)
		{
			text = text.Substring(num + 7);
		}
		int length = text.Length;
		if ((length > 3145728 || length == 0) ? true : false)
		{
			return null;
		}
		try
		{
			return Convert.FromBase64String(text);
		}
		catch (FormatException)
		{
			return null;
		}
	}

	private Control PlaylistThumbnail(PlaylistDocument doc)
	{
		Image image = new Image
		{
			Width = 36.0,
			Height = 36.0,
			Stretch = Stretch.UniformToFill
		};
		byte[] array = PlaylistCoverBytes(doc);
		if (array != null)
		{
			try
			{
				string key = "playlist:" + QuestRepository.Digest(array);
				if (!thumbnails.TryGetValue(key, out Bitmap value))
				{
					using MemoryStream stream = new MemoryStream(array);
					value = (thumbnails[key] = Bitmap.DecodeToWidth(stream, 72));
				}
				image.Source = value;
			}
			catch (Exception)
			{
			}
		}
		return image;
	}

	private async Task InstallQueuedMap(string path, string? code, string? expectedHash, ImportProgress state, MapInstaller installer, Action<string> progress, CancellationToken ct)
	{
		MapArchive archive = await Task.Run(() => MapArchive.Inspect(path, ct), ct);
		string hash;
		try
		{
			hash = await Task.Run(() => archive.GameHash(path, ct), ct);
		}
		catch (Exception ex) when (((Func<bool>)delegate
		{
			// Could not convert BlockContainer to single expression
			bool flag = expectedHash == null;
			if (flag)
			{
				flag = ((ex is InvalidDataException || ex is IOException || ex is ArgumentException || ex is JsonException) ? true : false);
			}
			return flag;
		}).Invoke())
		{
			hash = null;
		}
		if (expectedHash != null && !expectedHash.Equals(hash, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The downloaded map does not match the playlist's BeatSaver hash.");
		}
		if (hash != null && state.KnownHashes.Contains(hash))
		{
			state.AlreadyThere.TryAdd(hash, archive.Title);
			if (code != null)
			{
				state.KnownCodes[code] = hash;
			}
			return;
		}
		if ((await installer.Install(path, archive, progress, ct)).StartsWith("Already installed:", StringComparison.Ordinal))
		{
			state.AlreadyThere.TryAdd(hash ?? archive.ContentHash, archive.Title);
		}
		else
		{
			state.Installed++;
			state.InstalledFolders[MapInstaller.InstalledFolder(archive)] = code;
		}
		if (hash != null)
		{
			state.KnownHashes.Add(hash);
			if (code != null)
			{
				state.KnownCodes[code] = hash;
			}
		}
	}

	private async Task ImportQueuedPlaylist(string path, ImportProgress state, MapInstaller installer, PlaylistStore store, CancellationToken ct)
	{
		if (new FileInfo(path).Length > 8388608)
		{
			throw new IOException("Playlist exceeds 8 MiB.");
		}
		byte[] array = await File.ReadAllBytesAsync(path, ct);
		PlaylistDocument playlist = PlaylistDocument.Parse(Encoding.UTF8.GetString(array).TrimStart('\ufeff'));
		playlist.LocalId = "import" + QuestRepository.Digest(array).Substring(0, 32);
		BeatSaverMaps api = new BeatSaverMaps(beatSaverClient);
		Dictionary<string, (string Name, List<JsonObject> Songs, string Hash, string Code)> missing = new Dictionary<string, (string, List<JsonObject>, string, string)>(StringComparer.OrdinalIgnoreCase);
		foreach (JsonObject item in playlist.Songs.OfType<JsonObject>())
		{
			string text = MapEntry.S(item, "songName", MapEntry.S(item, "name", "Unnamed map"));
			string text2 = SongHash(item);
			string text3 = MapEntry.S(item, "key", MapEntry.S(item, "id", MapEntry.S(item, "bsr")));
			string text4 = MapEntry.S(item, "levelid", MapEntry.S(item, "levelId"));
			if (text2.Length == 0 && !BeatSaverMaps.IsCode(text3) && text4.Length != 0 && !text4.StartsWith("custom_level_", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			if (text2.Length == 0 && BeatSaverMaps.IsCode(text3) && state.KnownCodes.TryGetValue(text3, out string value))
			{
				text2 = value;
			}
			if (text2.Length != 0 && state.KnownHashes.Contains(text2))
			{
				state.AlreadyThere.TryAdd(text2, text);
				SetSongReference(item, text2);
				continue;
			}
			string key;
			if (text2.Length != 0)
			{
				key = text2;
			}
			else
			{
				key = (BeatSaverMaps.IsCode(text3) ? ("code:" + text3) : ("unknown:" + text));
			}
			if (!missing.TryGetValue(key, out (string, List<JsonObject>, string, string) value2))
			{
				value2 = (text, new List<JsonObject>(), text2, text3);
				missing.Add(key, value2);
			}
			value2.Item2.Add(item);
		}
		int downloaded = 0;
		int processed = 0;
		List<string> errors = new List<string>();
		foreach (var entry in missing.Values)
		{
			ct.ThrowIfCancellationRequested();
			processed++;
			string prefix = $"{playlist.Title} · {downloaded}/{missing.Count} downloaded · map {processed}/{missing.Count} · {entry.Name}";
			Report(prefix + " — finding on BeatSaver…");
			string temporary = Path.Combine(Path.GetTempPath(), "qms-beatsaver-" + Guid.NewGuid().ToString("N") + ".zip");
			try
			{
				BeatSaverMap map = (await api.Resolve(entry.Hash, entry.Code, ct)) ?? throw new IOException("The requested map version was not found on BeatSaver.");
				if (!state.KnownHashes.Contains(map.Hash))
				{
					Report(prefix + " — downloading…");
					await api.Download(map, temporary, ct);
					downloaded++;
					prefix = $"{playlist.Title} · {downloaded}/{missing.Count} downloaded · {entry.Name}";
					await InstallQueuedMap(temporary, map.Code, map.Hash, state, installer, (string message) =>
					{
						Report(prefix + " — " + message);
					}, ct);
				}
				else
				{
					state.AlreadyThere.TryAdd(map.Hash, entry.Name);
				}
				foreach (JsonObject item2 in entry.Songs)
				{
					SetSongReference(item2, map.Hash);
				}
			}
			catch (Exception ex) when (!(ex is OperationCanceledException) || !ct.IsCancellationRequested)
			{
				errors.Add(entry.Name + " — " + ((ex is OperationCanceledException) ? "BeatSaver timed out." : ex.Message));
				await Client(ct).CheckConnection();
			}
			finally
			{
				File.Delete(temporary);
			}
		}
		if (errors.Count > 0)
		{
			throw new IOException($"{errors.Count} of {missing.Count} missing map(s) could not be loaded. {errors[0]}");
		}
		PlaylistDocument existing = playlists.FirstOrDefault((PlaylistDocument p) => p.LocalId == playlist.LocalId);
		if (existing?.RemotePath == null)
		{
			await store.Save(playlist, ct);
			data.SaveDraft(profile, playlist);
			if (existing != null)
			{
				playlists.Remove(existing);
			}
			playlists.Add(playlist);
		}
		Report($"{playlist.Title} · {downloaded}/{missing.Count} downloaded · playlist installed.");
	}

	private static void SetSongReference(JsonObject song, string hash)
	{
		string text = "custom_level_" + hash.ToUpperInvariant();
		song["hash"] = hash.ToUpperInvariant();
		song["levelid"] = text;
		if (song.ContainsKey("levelId"))
		{
			song["levelId"] = text;
		}
	}

	public MainWindow(LocalData? storage = null, HttpClient? beatSaverClient = null)
	{
		data = storage ?? new LocalData();
		this.beatSaverClient = beatSaverClient ?? beatSaverHttp;
		Title = "Quest Map Studio • Beat Saber 1.40.8";
		using (var icon = AssetLoader.Open(new Uri("avares://QuestMapStudio/Assets/QuestMapStudio.png")))
			Icon = new WindowIcon(icon);
		Width = 1500.0;
		Height = 900.0;
		MinWidth = 920.0;
		MinHeight = 625.0;
		WindowStartupLocation = WindowStartupLocation.CenterScreen;
		Background = Ink;
		try
		{
			data.LoadSettings();
		}
		catch (Exception ex)
		{
			Report(ex.Message);
		}
		if (!string.IsNullOrWhiteSpace(data.Settings.LastSerial))
		{
			profile = data.Settings.LastSerial;
		}
		Place(toolbar, Label("Quest Map Studio", 24.0, bold: true), 0);
		Place(toolbar, Button("Find Quest", () => Run(FindDevices)), 1, HorizontalAlignment.Right);
		StackPanel child = Row(devices, Button("Connect", () => Run(SelectDevice)), Button("Refresh", () => Run(Refresh)));
		Place(toolbar, child, 2);
		Place(toolbar, connection, 3);
		Button button = Button("BeatSaver", () =>
		{
			using (Process.Start(new ProcessStartInfo("https://beatsaver.com/")
			{
				UseShellExecute = true
			}))
			{
				return Task.CompletedTask;
			}
		});
		button.Content = new TextBlock
		{
			Inlines = new InlineCollection
			{
				new Run("Beat")
				{
					Foreground = Brushes.White
				},
				new Run("Saver")
				{
					Foreground = Accent
				}
			},
			FontSize = 19.0,
			FontWeight = FontWeight.Black,
			FontStyle = FontStyle.Italic
		};
		button.Cursor = new Cursor(StandardCursorType.Hand);
		ToolTip.SetTip(button, "Open BeatSaver in your browser");
		Place(toolbar, button, 4);
		toolbar.Margin = new Thickness(20.0, 15.0);
		workspace = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("1*,218,1*"),
			ColumnSpacing = 12.0,
			Margin = new Thickness(20.0, 0.0)
		};
		Place(workspace, MapsPane(), 0);
		Place(workspace, ImportPane(), 1);
		Place(workspace, PlaylistsPane(), 2);
		Grid grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("*,Auto"),
			Margin = new Thickness(20.0, 12.0),
			ColumnSpacing = 16.0
		};
		grid.Children.Add(status);
		undoButton = HistoryButton("↶", "Undo last edit", forward: false);
        redoButton = HistoryButton("↷", "Redo last edit", forward: true);
        Place(grid, Row(pendingText, cancelButton, undoButton, redoButton, applyButton), 1);
		status.VerticalAlignment = VerticalAlignment.Center;
		applyButton.Background = Accent;
		applyButton.Foreground = Ink;
		applyButton.FontWeight = FontWeight.Bold;
		applyButton.Click += async (object? _, RoutedEventArgs _) =>
		{
			await Run(Apply);
		};
		cancelButton.Click += (object? _, RoutedEventArgs _) =>
		{
			operation?.Cancel();
		};
		Grid grid2 = new Grid
		{
			RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto")
		};
		grid2.Children.Add(toolbar);
		Grid.SetRow(progress, 1);
		grid2.Children.Add(progress);
		Grid.SetRow(workspace, 2);
		grid2.Children.Add(workspace);
		Grid.SetRow(grid, 3);
		grid2.Children.Add(grid);
		Grid.SetRowSpan(dragLayer, 4);
		grid2.Children.Add(dragLayer);
		Content = grid2;
		WireCardDrag();
		KeyDown += (object? _, KeyEventArgs e) =>
		{
			if (e.Key == Key.Escape && cardDrag != null)
			{
				EndCardDrag();
				e.Handled = true;
			}
			else if (e.Key == Key.F11)
			{
				WindowState = ((WindowState != WindowState.FullScreen) ? WindowState.FullScreen : WindowState.Normal);
				e.Handled = true;
			}
			else if (e.Key == Key.Escape && WindowState == WindowState.FullScreen)
			{
				WindowState = WindowState.Normal;
			}
		};
		Closing += (object? _, WindowClosingEventArgs e) =>
		{
			if (Busy)
			{
				e.Cancel = true;
				Report("Wait for the current operation or click Cancel.");
			}
		};
		Closed += (object? _, EventArgs _) =>
		{
			EndCardDrag();
			windowClosed = true;
			if (durationCacheChanged)
			{
				try
				{
					data.SaveMaps(profile, maps);
				}
				catch (IOException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
			}
			foreach (Bitmap value in thumbnails.Values)
			{
				value.Dispose();
			}
		};
		try
		{
			importQueue.AddRange(data.LoadQueue().Distinct(StringComparer.OrdinalIgnoreCase));
			LoadProfile();
		}
		catch (Exception ex2)
		{
			Report("Could not load saved data: " + ex2.Message);
		}
		UpdateQueue();
	}

	private Button Button(string caption, Func<Task> action)
	{
		Button button = new Button();
		button.Content = caption;
		button.VerticalAlignment = VerticalAlignment.Center;
		button.Click += async (object? _, RoutedEventArgs _) =>
		{
			if (Busy)
			{
				return;
			}
			try
			{
				await action();
			}
			catch (Exception ex)
			{
				Report(ex.Message);
			}
		};
		return button;
	}

	private static StackPanel Row(params Control[] controls)
	{
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6.0
		};
		foreach (Control item in controls)
		{
			stackPanel.Children.Add(item);
		}
		return stackPanel;
	}

	private static void Place(Grid grid, Control child, int column, HorizontalAlignment? alignment = null)
	{
		Grid.SetColumn(child, column);
		if (alignment.HasValue)
		{
			HorizontalAlignment valueOrDefault = alignment.GetValueOrDefault();
			child.HorizontalAlignment = valueOrDefault;
		}
		grid.Children.Add(child);
	}

	private async Task Run(Func<CancellationToken, Task> action)
	{
		if (Busy)
		{
			return;
		}
		EndCardDrag();
		using CancellationTokenSource token = new CancellationTokenSource();
		operation = token;
		Grid grid = workspace;
		bool isEnabled = (toolbar.IsEnabled = false);
		grid.IsEnabled = isEnabled;
		ProgressBar progressBar = progress;
		isEnabled = (cancelButton.IsVisible = true);
		progressBar.IsVisible = isEnabled;
		UpdatePending();
		try
		{
			await action(token.Token);
		}
		catch (OperationCanceledException)
		{
			Report("Canceled. Completed changes were kept; the remaining work can be retried.");
		}
		catch (Exception ex2)
		{
			Report(ex2.Message);
		}
		finally
		{
			operation = null;
			Grid grid2 = workspace;
			isEnabled = (toolbar.IsEnabled = true);
			grid2.IsEnabled = isEnabled;
			ProgressBar progressBar2 = progress;
			isEnabled = (cancelButton.IsVisible = false);
			progressBar2.IsVisible = isEnabled;
			UpdatePending();
		}
	}

	private void Report(string text)
	{
		status.Text = text;
		try
		{
			File.AppendAllText(Path.Combine(data.Root, "application.log"), $"{DateTimeOffset.Now:O} {text}{Environment.NewLine}");
		}
		catch (IOException)
		{
		}
		catch (UnauthorizedAccessException)
		{
		}
	}

	private AdbClient Client(CancellationToken ct)
	{
		if (activeSerial != null)
		{
			return new AdbClient(AdbPath, activeSerial, ct);
		}
		throw new IOException("Connect an authorized Quest first.");
	}

	private async Task FindDevices(CancellationToken ct)
	{
		List<DeviceInfo> list = await new AdbClient(AdbPath, null, ct).Devices();
		devices.ItemsSource = list;
		DeviceInfo[] array = list.Where((DeviceInfo d) => d.State == "device").ToArray();
		devices.SelectedItem = list.FirstOrDefault((DeviceInfo d) => d.Serial == activeSerial) ?? array.FirstOrDefault() ?? list.FirstOrDefault();
		if (array.Length == 1)
		{
			devices.SelectedItem = array[0];
			await SelectDevice(ct);
		}
		else
		{
			Report((list.Count == 0) ? "No Quest detected. Check the USB connection." : "Select a Quest and click Connect. If it says unauthorized, allow USB debugging in the headset.");
		}
	}

	private async Task SelectDevice(CancellationToken ct)
	{
		object selectedItem = devices.SelectedItem;
		if (!(selectedItem is DeviceInfo { State: "device" } selected))
		{
			throw new IOException("Select an authorized Quest.");
		}
		await new AdbClient(AdbPath, selected.Serial, ct).CheckConnection();
		if (profile != selected.Serial)
		{
			if (durationCacheChanged)
			{
				data.SaveMaps(profile, maps);
			}
			PlaylistDocument[] array = ((profile == "local") ? playlists.Where((PlaylistDocument p) => p.RemotePath == null).ToArray() : Array.Empty<PlaylistDocument>());
			profile = selected.Serial;
			LoadProfile();
			PlaylistDocument[] array2 = array;
			foreach (PlaylistDocument draft in array2)
			{
				if (playlists.All((PlaylistDocument p) => p.LocalId != draft.LocalId))
				{
					data.SaveDraft(profile, draft);
					playlists.Add(draft);
				}
				data.DeleteDraft("local", draft);
			}
		}
		activeSerial = selected.Serial;
		data.Settings.LastSerial = profile;
		data.SaveSettings();
		await Refresh(ct);
	}

	private void LoadProfile()
	{
		ResetEditorHistory();
		current = null;
		durationCacheChanged = false;
		checkedMaps.Clear();
		checkedDuration.Clear();
		maps.Clear();
		maps.AddRange(data.LoadMaps(profile));
		playlists.Clear();
		playlists.AddRange(data.LoadDrafts(profile));
		nativeOrder = Array.Empty<string>();
		orderLoaded = false;
		pendingOrder = data.LoadOrderDraft(profile);
		if (pendingOrder != null)
		{
			SortPlaylists(pendingOrder.LocalIds, (PlaylistDocument p) => p.LocalId);
		}
		imports.Clear();
		imports.AddRange(data.LoadImports(profile));
		FilterMaps();
		RenderPlaylists();
	}

	private async Task Refresh(CancellationToken ct)
	{
		AdbClient client = Client(ct);
		await client.CheckConnection();
		connection.Text = "Connected";
		QuestRepository repo = new QuestRepository(client);
		List<PlaylistDocument> remote = await repo.ReadPlaylists(Report);
		orderLoaded = false;
		try
		{
			nativeOrder = await new PlaylistOrder(client).Read();
			orderLoaded = true;
		}
		catch (Exception ex) when (!(ex is OperationCanceledException))
		{
			Report(ex.Message);
		}
		foreach (PlaylistDocument remotePlaylist in remote)
		{
			PlaylistDocument playlistDocument = playlists.FirstOrDefault((PlaylistDocument p) => (p.RemotePath ?? PlaylistStore.DefaultPath(p)) == remotePlaylist.RemotePath);
			if (playlistDocument == null)
			{
				data.SaveDraft(profile, remotePlaylist);
				playlists.Add(remotePlaylist);
			}
			else if (!playlistDocument.Pending && !playlistDocument.PendingDelete && playlistDocument.RemoteDigest != remotePlaylist.RemoteDigest)
			{
				remotePlaylist.LocalId = playlistDocument.LocalId;
				data.SaveDraft(profile, remotePlaylist);
				playlists[playlists.IndexOf(playlistDocument)] = remotePlaylist;
				if (current == playlistDocument)
				{
					current = remotePlaylist;
				}
			}
		}
		HashSet<string> foundPaths = remote.Select((PlaylistDocument p) => p.RemotePath).ToHashSet(StringComparer.Ordinal);
		PlaylistDocument[] array = playlists.Where((PlaylistDocument p) => p.RemotePath != null && !p.Pending && !p.PendingDelete && !foundPaths.Contains(p.RemotePath)).ToArray();
		foreach (PlaylistDocument playlistDocument2 in array)
		{
			playlists.Remove(playlistDocument2);
			if (current == playlistDocument2)
			{
				current = null;
			}
		}
		if (pendingOrder != null)
		{
			SortPlaylists(pendingOrder.LocalIds, (PlaylistDocument p) => p.LocalId);
		}
		else
		{
			SortPlaylists(nativeOrder.Concat(remote.Select((PlaylistDocument p) => p.RemotePath)), (PlaylistDocument p) => p.RemotePath);
		}
		ResetEditorHistory();
		RenderPlaylists();
		await RefreshMaps(repo, ct);
		Report($"{maps.Count} maps · {playlists.Count} playlists");
	}

	private async Task RefreshMaps(QuestRepository repo, CancellationToken ct)
	{
		List<MapEntry> fresh = await repo.Scan(QuestRepository.DefaultRoots.Concat(data.Settings.ExtraRoots), Report, ct);
		Dictionary<string, MapEntry> dictionary = maps.ToDictionary((MapEntry m) => m.Folder, StringComparer.Ordinal);
		foreach (MapEntry item in fresh)
		{
			if (!dictionary.TryGetValue(item.Folder, out var value) || !(value.RawInfo == item.RawInfo))
			{
				continue;
			}
			item.GameHash = value.GameHash;
			MapEntry mapEntry2 = item;
			if (!mapEntry2.ModifiedUnix.HasValue)
			{
				mapEntry2.ModifiedUnix = value.ModifiedUnix;
			}
			mapEntry2 = item;
			if (mapEntry2.MapCode == null)
			{
				mapEntry2.MapCode = value.MapCode;
			}
			if (item.ModifiedUnix == value.ModifiedUnix)
			{
				mapEntry2 = item;
				if (!mapEntry2.Duration.HasValue)
				{
					mapEntry2.Duration = value.Duration;
				}
			}
		}
		await repo.ResolveHashes(fresh, Report, ct);
		checkedDuration.Clear();
		maps.Clear();
		maps.AddRange(fresh);
		checkedMaps.IntersectWith(maps.Select((MapEntry m) => m.Folder));
		data.SaveMaps(profile, maps);
		FilterMaps();
		RefreshTracks();
	}

	private async Task Apply(CancellationToken ct)
	{
		AdbClient client = Client(ct);
		PlaylistDevice device = new PlaylistDevice(client);
		await device.Check();
		ResetEditorHistory();
		int saved = 0;
		int failed = 0;
		ImportProgress state = new ImportProgress();
		QuestRepository repo = new QuestRepository(client);
		PlaylistStore store = new PlaylistStore(device);
		MapInstaller installer = new MapInstaller(client);
		if (importQueue.Count > 0)
		{
			await RefreshMaps(repo, ct);
			foreach (MapEntry map in maps)
			{
				string gameHash = map.GameHash;
				if (gameHash != null)
				{
					state.KnownHashes.Add(gameHash);
					string mapCode = map.MapCode;
					if (mapCode != null)
					{
						state.KnownCodes.TryAdd(mapCode, gameHash);
					}
				}
			}
		}
		try
		{
			string[] array = importQueue.OrderBy((string text2) => !text2.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToArray();
			foreach (string path in array)
			{
				ct.ThrowIfCancellationRequested();
				try
				{
					if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
					{
						Report("Checking " + Path.GetFileName(path) + "…");
						await InstallQueuedMap(path, MapEntry.MapCodeFromName(Path.GetFileName(path)), null, state, installer, Report, ct);
					}
					else
					{
						await ImportQueuedPlaylist(path, state, installer, store, ct);
						saved++;
					}
					importQueue.Remove(path);
					archiveErrors.Remove(path);
					data.SaveQueue(importQueue);
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					failed++;
					archiveErrors[path] = ex.Message;
					Report(ex.Message);
					await client.CheckConnection();
				}
				finally
				{
					UpdateQueue();
				}
			}
		}
		finally
		{
			if (state.InstalledFolders.Count > 0)
			{
				imports.Insert(0, new ImportBatch(DateTimeOffset.Now, state.InstalledFolders.Keys.ToArray()));
				data.SaveImports(profile, imports);
			}
			RenderPlaylists();
		}
		PlaylistDocument[] array2 = playlists.Where((PlaylistDocument p) => p.Pending || p.PendingDelete).ToArray();
		foreach (PlaylistDocument playlist in array2)
		{
			ct.ThrowIfCancellationRequested();
			try
			{
				if (playlist.PendingDelete)
				{
					await store.Delete(playlist);
					data.DeleteDraft(profile, playlist);
					playlists.Remove(playlist);
					if (current == playlist)
					{
						current = null;
					}
				}
				else
				{
					await store.Save(playlist, ct);
					playlist.Pending = false;
					data.SaveDraft(profile, playlist);
				}
				saved++;
			}
			catch (Exception ex2) when (!(ex2 is OperationCanceledException))
			{
				failed++;
				Report(playlist.Title + " — " + ex2.Message);
				await client.CheckConnection();
			}
			finally
			{
				RenderPlaylists();
			}
		}
		bool orderSaved = false;
		string orderError = null;
		PlaylistOrderDraft playlistOrderDraft = pendingOrder;
		if ((object)playlistOrderDraft != null)
		{
			try
			{
				if (playlists.Any((PlaylistDocument p) => p.RemotePath == null || p.PendingDelete))
				{
					throw new IOException("Finish the pending playlist operations before saving their order.");
				}
				nativeOrder = await new PlaylistOrder(client).Save(playlistOrderDraft.BasePaths, playlists.Select((PlaylistDocument p) => p.RemotePath).ToArray(), ct);
				data.SaveOrderDraft(profile, null);
				pendingOrder = null;
				orderSaved = true;
			}
			catch (Exception ex3) when (!(ex3 is OperationCanceledException))
			{
				failed++;
				orderError = ex3.Message;
				Report(ex3.Message);
				await client.CheckConnection();
			}
		}
		if (state.InstalledFolders.Count > 0)
		{
			await RefreshMaps(repo, ct);
			foreach (MapEntry map2 in maps)
			{
				if (map2.MapCode == null && state.InstalledFolders.TryGetValue(map2.Folder, out string value))
				{
					map2.MapCode = value;
				}
			}
			data.SaveMaps(profile, maps);
			FilterMaps();
		}
		string[] array3 = state.AlreadyThere.Values.ToArray();
		string text = ((array3.Length == 0) ? "" : ($" · Already on Quest: {array3.Length} ({string.Join(", ", array3.Take(3))}" + ((array3.Length > 3) ? $", +{array3.Length - 3} more)" : ")")));
		Report($"Installed: {state.Installed} · Playlists updated: {saved}" + text + (orderSaved ? " · Playlist order saved" : "") + ((orderError != null) ? (" · " + orderError) : "") + ((failed == 0) ? "" : $" · Failed: {failed}. Unapplied items are still pending.") + (((state.Installed + saved > 0) | orderSaved) ? " Restart Beat Saber to see changes." : ""));
	}

	private Control PlaylistsPane()
	{
		Grid grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("*,Auto")
		};
		grid.Children.Add(Label("Playlists", 21.0, bold: true));
		undoOrder = Button("Undo order", UndoPlaylistOrder);
		undoOrder.IsVisible = false;
		Place(grid, Row(undoOrder, Button("New", NewPlaylist), Button("Import…", ImportPlaylist)), 1);
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 5.0
		};
		stackPanel.Children.Add(addSelected);
		stackPanel.Children.Add(entries);
		playlistTarget = Surface(stackPanel, new Thickness(5.0));
        playlistTarget.Margin = new Thickness(14, 0, 0, 3);
		playlistTarget.Background = Ink;
		playlistTarget.MinHeight = 68.0;
		expanded.Content = playlistTarget;
		CompactItems(entries);
		ConfigureScrollBar(entries);
		AutomationProperties.SetName(entries, "Playlist tracks");
		entries.ItemTemplate = new FuncDataTemplate<EntryRow>((EntryRow entry, INameScope _) => (!(entry == null)) ? PlaylistTrack(entry) : new Border());
		addSelected.Click += async (object? _, RoutedEventArgs _) =>
		{
			await Run((CancellationToken ct) => AddMaps(CheckedInListOrder(), ct));
		};
		RegisterCardDrag(entries);
		ConfigureScrollBar(playlistScroll);
        playlistCards.Margin = new Thickness(0, 0, 9, 0);
        playlistScroll.Content = playlistCards;
		playlistScroll.SizeChanged += (object? _, SizeChangedEventArgs e) =>
		{
			entries.MaxHeight = Math.Max(172.0, e.NewSize.Height - 170.0);
		};
		return Surface(Dock(grid, playlistScroll), new Thickness(10.0));
	}

    private void RenderPlaylists(bool resetPosition = false)
    {
        Vector? savedTrackOffset = TrackScroller()?.Offset;
        double savedPlaylistOffset = playlistScroll.Offset.Y;
        Control? openedHeader = null;
        playlistCards.Children.Clear();
        if (playlists.Count == 0)
            playlistCards.Children.Add(Meta("Connect Quest to load playlists, or create a new one."));

        var opened = current;
        foreach (var playlist in playlists)
        {
            var header = PlaylistHeader(playlist);
            playlistCards.Children.Add(header);
            if (opened == playlist && !playlist.PendingDelete)
            {
                openedHeader = header;
                playlistCards.Children.Add(expanded);
            }
        }
        if (openedHeader != null) playlistCards.Children.Add(playlistEndSpace);
        RefreshTracks();
        UpdateMapCount();
        UpdatePending();
        if (openedHeader == null)
        {
            playlistEndSpace.IsVisible = false;
            return;
        }

        var headerToAlign = openedHeader;
        Dispatcher.UIThread.Post(() =>
        {
            if (opened != current || headerToAlign.Parent != playlistCards) return;
            if (entries.ItemCount > 0)
            {
                if (resetPosition) entries.ScrollIntoView(0);
                if (TrackScroller() is { } tracks)
                    tracks.Offset = resetPosition ? default : savedTrackOffset.GetValueOrDefault();
            }
            PositionPlaylistHeader(headerToAlign, resetPosition, savedPlaylistOffset);
        }, DispatcherPriority.Loaded);
    }

	private Control PlaylistHeader(PlaylistDocument playlist, bool interactive = true)
	{
		Grid grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("40,*,Auto"),
			ColumnSpacing = 7.0,
			Children = { PlaylistThumbnail(playlist) }
		};
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 2.0
		};
		stackPanel.Children.Add(Label(playlist.Title, 13.0, bold: true));
		stackPanel.Children.Add(Meta(playlist.PendingDelete ? "Removal pending" : ($"{playlist.Songs.Count} maps" + (playlist.Pending ? " · Pending" : ""))));
		Place(grid, stackPanel, 1);
		Place(grid, Label((current == playlist) ? "⌄" : "›", 16.0), 2);
		Border border = Surface(grid, new Thickness(8.0, 5.0));
		border.Background = ((current == playlist) ? Marked : Card);
		if (!interactive)
		{
			return border;
		}
		Grid grid2 = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("*,Auto"),
			ColumnSpacing = 5.0
		};
		Button button = Button(playlist.Title, () =>
		{
			TogglePlaylist(playlist);
			return Task.CompletedTask;
		});
		button.Content = border;
		button.Padding = new Thickness(0.0);
		button.HorizontalAlignment = HorizontalAlignment.Stretch;
		button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
		ToolTip.SetTip(button, playlist.Title);
		RegisterPlaylistDrag(button, border, playlist);
		grid2.Children.Add(button);
		Place(grid2, PlaylistMenu(playlist), 1);
		grid2.Tag = playlist;
		return grid2;
	}

	private void TogglePlaylist(PlaylistDocument playlist)
	{
		current = ((current == playlist) ? null : playlist);
		RenderPlaylists(resetPosition: true);
	}

	private void SortPlaylists(IEnumerable<string> order, Func<PlaylistDocument, string?> key)
	{
		Dictionary<string, int> ranks = order.Distinct(StringComparer.Ordinal).Select((string value, int index) => (value: value, index: index)).ToDictionary(((string value, int index) pair) => pair.value, ((string value, int index) pair) => pair.index, StringComparer.Ordinal);
		PlaylistDocument[] collection = playlists.OrderBy((PlaylistDocument playlist) =>
		{
			string text = key(playlist);
			return (text == null) ? int.MaxValue : ranks.GetValueOrDefault(text, int.MaxValue);
		}).ToArray();
		playlists.Clear();
		playlists.AddRange(collection);
	}

	private void MovePlaylist(PlaylistDocument playlist, int slot)
	{
		int num = playlists.IndexOf(playlist);
		int num2 = ((slot > num) ? (slot - 1) : slot);
		if (num >= 0 && num != num2)
		{
			RememberEdit();
			playlists.RemoveAt(num);
			playlists.Insert(num2, playlist);
			pendingOrder = new PlaylistOrderDraft(pendingOrder?.BasePaths ?? nativeOrder, playlists.Select((PlaylistDocument p) => p.LocalId).ToArray());
			data.SaveOrderDraft(profile, pendingOrder);
			RenderPlaylists();
			Report("Playlist order staged. Click Apply to save it on Quest.");
		}
	}

	private async Task UndoPlaylistOrder()
	{
		pendingOrder = null;
		data.SaveOrderDraft(profile, null);
		await Run(Refresh);
	}

	private MapEntry[] CheckedInListOrder()
	{
		IEnumerable? itemsSource = mapList.ItemsSource;
		return (from m in (((itemsSource != null) ? itemsSource.OfType<MapEntry>() : null) ?? Array.Empty<MapEntry>()).Concat(maps)
			where checkedMaps.Contains(m.Folder) && m.Error == null
			select m).DistinctBy((MapEntry m) => m.Folder).ToArray();
	}

	private async Task AddMaps(MapEntry[] selections, CancellationToken ct, int? index = null)
	{
		PlaylistDocument document = current;
		if (document == null || document.PendingDelete || selections.Length == 0)
		{
			return;
		}
		QuestRepository repo = ((activeSerial == null) ? null : new QuestRepository(Client(ct)));
		var before = CaptureEditor();
		int added = 0;
		int skipped = 0;
		HashSet<string> hashes = document.Songs.Select((JsonNode s) => SongHash(s)).ToHashSet(StringComparer.OrdinalIgnoreCase);
		try
		{
			foreach (MapEntry map in selections)
			{
				ct.ThrowIfCancellationRequested();
				string hash = map.GameHash;
				if (hash == null && repo != null)
				{
					try
					{
						hash = await repo.ResolveHash(map);
					}
					catch (IOException ex)
					{
						Report(ex.Message);
						await Client(ct).CheckConnection();
					}
				}
				if (hash == null || !hashes.Add(hash) || !document.Add(map, hash))
				{
					skipped++;
					continue;
				}
				if (index.HasValue)
				{
					int valueOrDefault = index.GetValueOrDefault();
					document.Move(document.Songs.Count - 1, valueOrDefault + added);
				}
				added++;
			}
		}
		finally
		{
			if (added > 0)
			{
				RememberEdit(before);
				StagePlaylist(document);
				data.SaveMaps(profile, maps);
				RenderPlaylists();
			}
		}
		Report($"Added {added} map(s) to {document.Title}" + ((skipped == 0) ? "." : $" · Skipped {skipped} duplicate or unavailable map(s).") + ((added > 0) ? " Click Apply to save." : ""));
	}

	private static string SongHash(JsonNode? node)
	{
		string text = MapEntry.S(node, "hash");
		if (text.Length == 0)
		{
			string text2 = MapEntry.S(node, "levelid", MapEntry.S(node, "levelId"));
			if (text2.StartsWith("custom_level_", StringComparison.OrdinalIgnoreCase))
			{
				text = text2.Substring("custom_level_".Length);
			}
		}
		if (!BeatSaverMaps.IsHash(text))
		{
			return "";
		}
		return text;
	}

	private Control PlaylistTrack(EntryRow entry)
	{
		Grid grid = new Grid
		{
			ColumnDefinitions = new ColumnDefinitions("30,*,Auto"),
			ColumnSpacing = 7.0,
			MinHeight = 43.0
		};
		grid.Children.Add(MapCover(entry.Map, (int)TrackCoverSize));
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 2.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		TextBlock textBlock = Label(entry.Name, 12.0, bold: true);
		if (entry.Missing)
		{
			textBlock.Foreground = Muted;
		}
		stackPanel.Children.Add(textBlock);
		MapEntry map = entry.Map;
		if (map != null)
		{
			string duration = map.Duration is > 0 and < 86400 ? FormatDuration(map.Duration.Value) : "";
            string text = string.Join(" · ", new[] { map.Artist, map.Mapper, duration }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
			if (text.Length > 0)
			{
				stackPanel.Children.Add(Meta(text));
			}
		}
		Place(grid, stackPanel, 1);
		StackPanel stackPanel2 = new StackPanel
		{
			Spacing = 1.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		Button button = Button("×", () =>
		{
			PlaylistDocument owner = entry.Owner;
			if (current == owner && !owner.PendingDelete)
			{
				int num = owner.Songs.IndexOf(entry.Node);
				if (num >= 0)
				{
					RememberEdit();
					owner.Songs.RemoveAt(num);
					StagePlaylist(owner);
					RenderPlaylists();
				}
			}
			return Task.CompletedTask;
		});
		button.Foreground = Brush.Parse("#FF8C9B");
		ToolTip.SetTip(button, "Remove from playlist");
		AutomationProperties.SetName(button, "Remove from playlist");
		button.Width = 24.0;
		button.Height = 21.0;
		button.Padding = new Thickness(0.0);
		button.HorizontalAlignment = HorizontalAlignment.Right;
		button.HorizontalContentAlignment = HorizontalAlignment.Center;
		stackPanel2.Children.Add(button);
		StackPanel stackPanel3 = Row(Arrow("↑", "Move up", -1), Arrow("↓", "Move down", 1));
		stackPanel3.Spacing = 2.0;
		stackPanel2.Children.Add(stackPanel3);
		Place(grid, stackPanel2, 2);
		Border border = Surface(grid, new Thickness(6.0, 2.0));
		border.Background = PlaylistTrackBackground;
		return border;
		Button Arrow(string label, string tip, int delta)
		{
			Button button2 = Button(label, () =>
			{
				int num = entry.Owner.Songs.IndexOf(entry.Node);
				MoveTrack(entry.Owner, num, num + delta);
				return Task.CompletedTask;
			});
			button2.Width = 19.0;
			button2.Height = 20.0;
			button2.Padding = new Thickness(0.0);
			button2.HorizontalContentAlignment = HorizontalAlignment.Center;
			button2.IsEnabled = entry.Index + delta >= 0 && entry.Index + delta < entry.Owner.Songs.Count;
			ToolTip.SetTip(button2, tip);
			AutomationProperties.SetName(button2, tip);
			return button2;
		}
	}

	private void RefreshTracks()
	{
		PlaylistDocument document = current;
		if (document == null || document.PendingDelete)
		{
			entries.ItemsSource = null;
			return;
		}
		ILookup<string, MapEntry> known = maps.Where((MapEntry m) => m.GameHash != null).ToLookup((MapEntry m) => m.GameHash, StringComparer.OrdinalIgnoreCase);
		entries.ItemsSource = document.Songs.Select((JsonNode song, int index) =>
		{
			string text = SongHash(song);
			MapEntry mapEntry = known[text].FirstOrDefault();
			string name = mapEntry?.Title ?? MapEntry.S(song, "songName", MapEntry.S(song, "levelid", MapEntry.S(song, "levelId", "Untitled map")));
			return new EntryRow(document, index, song, name, mapEntry, text.Length > 0 && mapEntry == null);
		}).ToArray();
	}

	private void MoveTrack(PlaylistDocument? playlist, int from, int to)
	{
		if (playlist == null || current != playlist || playlist.PendingDelete || from == to || from < 0 || to < 0 || from >= playlist.Songs.Count || to >= playlist.Songs.Count)
		{
			return;
		}
		Vector position = entries.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.Offset ?? default(Vector);
		RememberEdit();
		playlist.Move(from, to);
		StagePlaylist(playlist);
		RefreshTracks();
		entries.SelectedIndex = to;
		Dispatcher.UIThread.Post(() =>
		{
			if (current == playlist)
			{
				ScrollViewer scrollViewer = entries.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
				if (scrollViewer != null)
				{
					scrollViewer.Offset = position;
				}
				entries.ScrollIntoView(to);
			}
		}, DispatcherPriority.Loaded);
	}

	private void StagePlaylist(PlaylistDocument playlist)
	{
		cachedDrafts.Remove(playlist);
		playlist.Pending = true;
		data.SaveDraft(profile, playlist);
		UpdatePending();
	}

	private async Task NewPlaylist()
	{
		PlaylistDocument draft = PlaylistDocument.New();
		if (await EditPlaylist(draft, creating: true))
		{
			playlists.Add(draft);
			current = draft;
			RenderPlaylists(resetPosition: true);
		}
	}

	private Control PlaylistMenu(PlaylistDocument playlist)
	{
		Button button = new Button
		{
			Content = "⋯",
			MinWidth = 32.0
		};
		ToolTip.SetTip(button, "Playlist actions");
		MenuFlyout menu = new MenuFlyout();
		if (playlist.PendingDelete)
		{
			Add("Undo removal", () =>
			{
				RememberEdit();
				playlist.PendingDelete = false;
				cachedDrafts.Remove(playlist);
				data.SaveDraft(profile, playlist);
				RenderPlaylists();
				return Task.CompletedTask;
			});
		}
		else
		{
			Add("Edit details…", async () =>
			{
				if (await EditPlaylist(playlist, creating: false))
				{
					RenderPlaylists();
				}
			});
			Add("Change cover…", () => ChangeCover(playlist));
			Add("Export .bplist…", () => ExportPlaylist(playlist));
			if (playlist.RemotePath != null)
			{
				Add("Reload from Quest…", async () =>
				{
					if (await Confirm("Replace this draft with the playlist on Quest?"))
					{
						await Run((CancellationToken ct) => ReloadPlaylist(playlist, ct));
					}
				});
			}
			Add("Remove playlist…", async () =>
			{
				if (await Confirm("Queue removal of “" + playlist.Title + "”? Its maps stay in your library."))
				{
					RememberEdit();
					playlist.PendingDelete = true;
					cachedDrafts.Remove(playlist);
					data.SaveDraft(profile, playlist);
					RenderPlaylists();
				}
			});
		}
		button.Flyout = menu;
		return button;
		void Add(string name, Func<Task> action)
		{
			MenuItem menuItem = new MenuItem
			{
				Header = name
			};
			menuItem.Click += async (object? _, RoutedEventArgs _) =>
			{
				if (Busy)
				{
					return;
				}
				try
				{
					await action();
				}
				catch (Exception ex)
				{
					Report(ex.Message);
				}
			};
			menu.Items.Add(menuItem);
		}
	}

	private async Task<bool> EditPlaylist(PlaylistDocument playlist, bool creating)
	{
		TextBox name = new TextBox
		{
			Text = (creating ? "" : playlist.Title),
			Watermark = "Playlist name"
		};
		TextBox author = new TextBox
		{
			Text = playlist.Author,
			Watermark = "Author"
		};
		TextBox description = new TextBox
		{
			Text = playlist.Description,
			Watermark = "Description",
			AcceptsReturn = true,
			MinHeight = 70.0,
			TextWrapping = TextWrapping.Wrap
		};
		Window dialog = Dialog(creating ? "New playlist" : "Edit playlist");
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(18.0),
			Spacing = 8.0
		};
		stackPanel.Children.Add(name);
		stackPanel.Children.Add(author);
		stackPanel.Children.Add(description);
		Button button = Button("Save draft", () =>
		{
			if (!string.IsNullOrWhiteSpace(name.Text))
			{
				dialog.Close(true);
			}
			return Task.CompletedTask;
		});
		stackPanel.Children.Add(Row(button, Button("Cancel", () =>
		{
			dialog.Close(false);
			return Task.CompletedTask;
		})));
		dialog.Content = stackPanel;
		dialog.Opened += (object? _, EventArgs _) =>
		{
			name.Focus();
		};
		if (!(await dialog.ShowDialog<bool>(this)))
		{
			return false;
		}
		RememberEdit();
		playlist.Title = name.Text.Trim();
		playlist.Author = author.Text ?? "";
		playlist.Description = description.Text ?? "";
		StagePlaylist(playlist);
		return true;
	}

	private async Task ChangeCover(PlaylistDocument playlist)
	{
		string text = (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Choose playlist cover",
			AllowMultiple = false,
			FileTypeFilter = new _003C_003Ez__ReadOnlySingleElementList<FilePickerFileType>(new FilePickerFileType("Images")
			{
				Patterns = new _003C_003Ez__ReadOnlyArray<string>(new string[3] { "*.png", "*.jpg", "*.jpeg" })
			})
		})).FirstOrDefault()?.TryGetLocalPath();
		if (text != null)
		{
			if (new FileInfo(text).Length > 2097152)
			{
				throw new IOException("Cover exceeds 2 MiB.");
			}
			var before = CaptureEditor();
			playlist.SetCover(await File.ReadAllBytesAsync(text), "");
			RememberEdit(before);
			StagePlaylist(playlist);
			RenderPlaylists();
		}
	}

	private async Task ImportPlaylist()
	{
		foreach (IStorageFile item in await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
		{
			Title = "Import playlists",
			AllowMultiple = true,
			FileTypeFilter = new _003C_003Ez__ReadOnlySingleElementList<FilePickerFileType>(new FilePickerFileType("Playlists")
			{
				Patterns = new _003C_003Ez__ReadOnlyArray<string>(new string[2] { "*.bplist", "*.json" })
			})
		}))
		{
			string path = item.TryGetLocalPath();
			if (path == null)
			{
				continue;
			}
			try
			{
				if (new FileInfo(path).Length > 8388608)
				{
					throw new IOException("Playlist exceeds 8 MiB.");
				}
				PlaylistDocument playlistDocument = PlaylistDocument.Parse(await File.ReadAllTextAsync(path));
				RememberEdit();
				StagePlaylist(playlistDocument);
				playlists.Add(playlistDocument);
				current = playlistDocument;
			}
			catch (Exception ex)
			{
				Report(Path.GetFileName(path) + " — " + ex.Message);
			}
		}
		RenderPlaylists(resetPosition: true);
	}

	private async Task ExportPlaylist(PlaylistDocument playlist)
	{
		string text = (await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
		{
			Title = "Export playlist",
			DefaultExtension = "bplist",
			SuggestedFileName = "playlist.bplist"
		}))?.TryGetLocalPath();
		if (text != null)
		{
			await File.WriteAllBytesAsync(text, playlist.Bytes());
			Report("Playlist exported.");
		}
	}

	private async Task ReloadPlaylist(PlaylistDocument playlist, CancellationToken ct)
	{
		string path = playlist.RemotePath ?? throw new IOException("This draft has no file on Quest.");
		byte[] array = await Client(ct).Read(path, 8388608);
		PlaylistDocument playlistDocument = PlaylistDocument.Parse(Encoding.UTF8.GetString(array));
		playlistDocument.LocalId = playlist.LocalId;
		playlistDocument.RemotePath = path;
		playlistDocument.RemoteDigest = QuestRepository.Digest(array);
		RememberEdit();
		cachedDrafts.Remove(playlist);
		data.DeleteDraft(profile, playlist);
		data.SaveDraft(profile, playlistDocument);
		playlists[playlists.IndexOf(playlist)] = playlistDocument;
		if (current == playlist)
		{
			current = playlistDocument;
		}
		RenderPlaylists(resetPosition: true);
	}

	private static Window Dialog(string title)
	{
		return new Window
		{
			Title = title,
			Width = 500.0,
			Height = 260.0,
			WindowStartupLocation = WindowStartupLocation.CenterOwner
		};
	}

	private async Task<bool> Confirm(string text)
	{
		Window dialog = Dialog("Quest Map Studio");
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(18.0),
			Spacing = 14.0
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = text,
			TextWrapping = TextWrapping.Wrap
		});
		stackPanel.Children.Add(Row(Button("Continue", () =>
		{
			dialog.Close(true);
			return Task.CompletedTask;
		}), Button("Cancel", () =>
		{
			dialog.Close(false);
			return Task.CompletedTask;
		})));
		dialog.Content = stackPanel;
		return await dialog.ShowDialog<bool>(this);
	}
}
