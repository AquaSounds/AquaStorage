using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AquaStorage.Helpers;
using AquaStorage.Services;
using Projektanker.Icons.Avalonia;

namespace AquaStorage.Views;

public partial class FileManager : UserControl
{
    private readonly FsTreeService _fsTree = new();
    private const string ConfigPath = "Config/FileManagerConfig";
    private const string FavoritesConfigPath = "Config/FavoritesConfig";

    private List<string>? _addedPaths;
    private List<string> AddedPaths
    {
        get
        {
            _addedPaths ??= ConfigHelper.LoadConfig<List<string>>(ConfigPath) ?? new List<string>();
            return _addedPaths;
        }
    }

    private void SaveConfig() => ConfigHelper.SaveConfig(ConfigPath, AddedPaths);

    private readonly AudioPlayerService _audioPlayer = new();
    private bool _suppressSelectionChanged;
    private double _treeFontSize = CacheService.DefaultFontSize;

    private string _searchText = "";
    private bool _showFavoritesOnly;
    private HashSet<string> _favoritePaths = new();
    private HashSet<string> _preSearchExpanded = new();
    private bool _searchWasActive;
    private CancellationTokenSource? _searchDebounce;
    private CancellationTokenSource? _walkCts;
    // Set to true when background walk completes; used by search
    private bool _fsTreeReady { get; set; }

    public FileManager()
    {
        InitializeComponent();
        WaveForm.Player = _audioPlayer;
        VolumeKnob.Value = _audioPlayer.VolumeDb;
        VolumeKnob.ValueChanged += db => _audioPlayer.VolumeDb = db;

        TreeFiles.AddHandler(PointerWheelChangedEvent, TreeFiles_OnPointerWheelChanged, RoutingStrategies.Bubble, handledEventsToo: true);
        TreeFiles.AddHandler(TreeViewItem.ExpandedEvent, OnTreeItemExpanded, RoutingStrategies.Bubble, handledEventsToo: true);

        App.AccentColorChanged += color => UpdateFolderIconColors(TreeFiles.Items, color);

        LoadFavorites();

        FilterCombo.Items.Add(new ComboBoxItem { Content = Localizer.Instance["AllFiles"] });
        FilterCombo.Items.Add(new ComboBoxItem { Content = Localizer.Instance["Favorites"] });
        FilterCombo.SelectedIndex = 0;

        _ = LoadSavedPathsOnStartup();
    }

    #region Favorites

    private void LoadFavorites()
    {
        var favs = ConfigHelper.LoadConfig<List<string>>(FavoritesConfigPath);
        if (favs != null)
            _favoritePaths = new HashSet<string>(favs, StringComparer.OrdinalIgnoreCase);
    }

    private void SaveFavorites()
    {
        ConfigHelper.SaveConfig(FavoritesConfigPath, new List<string>(_favoritePaths));
    }

    private bool IsFavorite(string path) => _favoritePaths.Contains(path);

    private ToggleButton CreateStarButton(string path)
    {
        var starIcon = new Icon
        {
            Value = "fa-regular fa-star",
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.Gold)
        };

        var btn = new ToggleButton
        {
            Content = starIcon,
            Width = 22, Height = 22,
            Padding = new Thickness(0),
            Margin = new Thickness(4, 0, 10, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            IsChecked = IsFavorite(path)
        };

        btn.IsCheckedChanged += (_, _) =>
        {
            bool isChecked = btn.IsChecked == true;
            starIcon.Value = isChecked ? "fa-solid fa-star" : "fa-regular fa-star";
            if (isChecked)
                _favoritePaths.Add(path);
            else
                _favoritePaths.Remove(path);
            SaveFavorites();
            if (_showFavoritesOnly && !isChecked)
                ApplyTreeFilter();
        };

        return btn;
    }

    #endregion

    #region Startup Loading

    private async Task LoadSavedPathsOnStartup()
    {
        if (AddedPaths.Count == 0)
            return;

        ShowRootsFromDisk(AddedPaths);

        // Background: walk full tree for search index
        _ = BackgroundIndexAsync();
    }

    private async Task BackgroundIndexAsync()
    {
        _walkCts?.Cancel();
        _walkCts = new CancellationTokenSource();
        _fsTreeReady = false;
        try
        {
            await _fsTree.WalkAsync(AddedPaths, _walkCts.Token);
            _fsTreeReady = true;
        }
        catch
        {
            // Walk failed silently — search just won't be available
        }
    }

    #endregion

    #region Buttons

    private async void BtnAdd_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        try
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = Localizer.Instance["SelectFolder"], AllowMultiple = true });

            if (folders.Count == 0) return;

            var newPaths = new List<string>();
            foreach (var folder in folders)
            {
                var inputPath = folder.Path.LocalPath;
                if (string.IsNullOrEmpty(inputPath)) continue;

                if (!Directory.Exists(inputPath))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        ShowTip(string.Format(Localizer.Instance["TipNotFound"], inputPath), true));
                    continue;
                }
                if (AddedPaths.Contains(inputPath))
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        ShowTip(string.Format(Localizer.Instance["TipAlreadyAdded"], inputPath), true));
                    continue;
                }

                newPaths.Add(inputPath);
                AddedPaths.Add(inputPath);
                SaveConfig();

                var rootItem = CreateDirectoryNode(GetDirName(inputPath), inputPath);
                await Dispatcher.UIThread.InvokeAsync(() => TreeFiles.Items.Add(rootItem));
            }

            if (newPaths.Count > 0)
            {
                ShowTip(string.Format(Localizer.Instance["TipAddedFolders"], newPaths.Count), false);
                _ = BackgroundIndexAsync();
            }
        }
        catch (Exception ex)
        {
            ShowTip(string.Format(Localizer.Instance["TipFolderSelectionFailed"], ex.Message), true);
        }
    }

    private void BtnDetail_Click(object? sender, RoutedEventArgs e)
    {
        var settingsWin = new SettingsWindow(AddedPaths);

        settingsWin.OnPathDeleted += (deletedPath) =>
        {
            if (AddedPaths.Contains(deletedPath))
            {
                AddedPaths.Remove(deletedPath);
                SaveConfig();
                RemoveTreeItemByPath(deletedPath);
                ShowTip(string.Format(Localizer.Instance["TipRemoved"], deletedPath), false);
            }
        };

        settingsWin.Show();
    }

    #endregion

    #region TreeView Construction

    /// <summary>
    /// Placeholder item used to make the expand arrow appear for non-empty directories.
    /// </summary>
    private sealed class PlaceholderItem : TreeViewItem
    {
        public PlaceholderItem() => Header = new TextBlock();
    }

    private static bool DirHasEntries(string path)
    {
        try { return Directory.EnumerateFileSystemEntries(path).Any(); }
        catch { return false; }
    }

    /// <summary>
    /// Extract the last component from a path. Handles both / and \ separators.
    /// </summary>
    private static string GetDirName(string path)
    {
        var span = path.AsSpan().TrimEnd('\\').TrimEnd('/');
        int idx = span.LastIndexOfAny('\\', '/');
        return idx >= 0 ? span.Slice(idx + 1).ToString() : span.ToString();
    }

    /// <summary>
    /// Read root directories and build top-level TreeViewItems instantly.
    /// Uses Dispatcher.InvokeAsync instead of async/await because this may be called
    /// from the constructor before Avalonia's SynchronizationContext is active.
    /// </summary>
    private void ShowRootsFromDisk(List<string> paths)
    {
        Task.Run(() =>
        {
            var list = new List<(string Name, string Path)>();
            foreach (var rootPath in paths)
            {
                if (!Directory.Exists(rootPath)) continue;
                list.Add((GetDirName(rootPath), rootPath));
            }

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var (name, path) in list)
                {
                    var item = CreateDirectoryNode(name, path);
                    TreeFiles.Items.Add(item);
                }
            });
        });
    }

    /// <summary>
    /// Build a file TreeViewItem with icon, star button, and drag support.
    /// </summary>
    private TreeViewItem CreateFileNode(string name, string path)
    {
        var isAudio = AudioFormats.IsAudioFile(path);
        var item = new TreeViewItem { Tag = path };
        item.PointerWheelChanged += OnItemWheelChanged;
        SetupDragSupport(item);

        var iconValue = isAudio ? "fa-solid fa-file-waveform" : "fa-regular fa-file";
        item.Header = BuildHeaderGrid(path, name, iconValue, isAudio ? null : Colors.White);
        return item;
    }

    /// <summary>
    /// Build a directory TreeViewItem. Adds placeholder if non-empty.
    /// </summary>
    private TreeViewItem CreateDirectoryNode(string name, string path)
    {
        var item = new TreeViewItem { Tag = path };
        item.PointerWheelChanged += OnItemWheelChanged;
        SetupDragSupport(item);

        var accentColor = Application.Current?.FindResource("AccentPrimary") is Color c ? c : Colors.DodgerBlue;
        item.Header = BuildHeaderGrid(path, name, "fa-solid fa-folder", accentColor);

        // Only show expand arrow if directory has entries
        if (DirHasEntries(path))
            item.Items.Add(new PlaceholderItem());

        return item;
    }

    /// <summary>
    /// Populate children of a directory TreeViewItem on first expand.
    /// </summary>
    private async void ExpandDirectory(TreeViewItem item)
    {
        var path = item.Tag as string;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

        // Clear placeholder
        item.Items.Clear();

        try
        {
            // Read filesystem on background thread
            var entries = await Task.Run(() =>
            {
                var list = new List<(string Name, string Path, bool IsDir)>();
                foreach (var d in Directory.GetDirectories(path))
                {
                    if (IsHiddenOrSystem(d)) continue;
                    list.Add((GetDirName(d), d, true));
                }
                foreach (var f in Directory.GetFiles(path))
                {
                    if (IsHiddenOrSystem(f)) continue;
                    list.Add((GetDirName(f), f, false));
                }
                return list;
            });

            // Create UI elements on UI thread
            foreach (var (name, entryPath, isDir) in entries)
            {
                TreeViewItem child = isDir
                    ? CreateDirectoryNode(name, entryPath)
                    : CreateFileNode(name, entryPath);
                item.Items.Add(child);
            }
        }
        catch (Exception ex)
        {
            item.Items.Add(new TreeViewItem
            {
                Header = new TextBlock
                {
                    Text = $"[Error: {ex.Message}]",
                    Foreground = Brushes.DarkRed,
                    Margin = new Thickness(18, 0, 0, 0)
                }
            });
        }
    }

    private void OnTreeItemExpanded(object? sender, RoutedEventArgs e)
    {
        if (e.Source is TreeViewItem item && item.Items.Count == 1 && item.Items[0] is PlaceholderItem)
        {
            ExpandDirectory(item);
        }
    }

    private void RemoveTreeItemByPath(string fullPath)
    {
        Dispatcher.UIThread.Post(() =>
        {
            TreeViewItem? toRemove = null;
            foreach (var item in TreeFiles.Items)
            {
                if (item is TreeViewItem ti && ti.Tag is string tag &&
                    tag.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    toRemove = ti;
                    break;
                }
            }
            if (toRemove != null)
            {
                TreeFiles.Items.Remove(toRemove);
                if (TreeFiles.SelectedItem == toRemove)
                    TreeFiles.SelectedItem = null;
            }
        });
    }

    #endregion

    #region Selection & Audio

    private void TreeFiles_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged) return;

        if (TreeFiles.SelectedItem is not TreeViewItem selected)
            return;

        string path = GetFullPath(selected);
        if (string.IsNullOrEmpty(path))
            return;

        if (AudioFormats.IsAudioFile(path))
        {
            PlayAudio(path);
        }
        else
        {
            _suppressSelectionChanged = true;
            selected.IsExpanded = !selected.IsExpanded;
            Dispatcher.UIThread.Post(() => _suppressSelectionChanged = false, DispatcherPriority.Loaded);
        }
    }

    private void PlayAudio(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            _audioPlayer.Play(filePath);

            WaveForm.Clear();
            WaveForm.RenderWaveform(filePath);
        }
        catch
        {
        }
    }

    #endregion

    #region Search & Favorites Filter

    private async void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var newText = SearchBox?.Text?.Trim() ?? "";
        bool wasEmpty = string.IsNullOrEmpty(_searchText);
        _searchText = newText;

        if (wasEmpty && !string.IsNullOrEmpty(_searchText))
        {
            _preSearchExpanded.Clear();
            SaveExpandedState(TreeFiles.Items);
            _searchWasActive = true;
        }

        _searchDebounce?.Cancel();
        _searchDebounce = new CancellationTokenSource();
        var token = _searchDebounce.Token;
        try
        {
            await Task.Delay(200, token);
            if (token.IsCancellationRequested) return;

            if (!string.IsNullOrEmpty(_searchText))
                await ApplySearchFilterAsync(token);
            else
                await Dispatcher.UIThread.InvokeAsync(() => ApplyTreeFilter());

            if (string.IsNullOrEmpty(_searchText) && _searchWasActive)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    RestoreExpandedState(TreeFiles.Items);
                    _searchWasActive = false;
                });
            }
        }
        catch (TaskCanceledException) { }
    }

    private async Task ApplySearchFilterAsync(CancellationToken token)
    {
        // Collect tree snapshot on UI thread
        var flat = new List<(TreeViewItem item, string path, string name, string? parentPath)>();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            CollectFlatItems(TreeFiles.Items, null, flat);
        });

        // Compute visibility on background thread
        var search = _searchText;
        var showFavOnly = _showFavoritesOnly;
        var favPaths = _favoritePaths;
        var (visible, expand) = await Task.Run(() =>
        {
            var vis = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var exp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pathToParent = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, path, _, parentPath) in flat)
                pathToParent[path] = parentPath;

            foreach (var (_, path, name, _) in flat)
            {
                if (token.IsCancellationRequested) break;
                bool match = name.Contains(search, StringComparison.OrdinalIgnoreCase);
                bool isFav = favPaths.Contains(path);

                bool shouldShow = showFavOnly ? (match && isFav) : match;
                if (shouldShow)
                {
                    vis.Add(path);
                    // Mark ancestors visible + expand them
                    var p = pathToParent.GetValueOrDefault(path);
                    while (p != null)
                    {
                        vis.Add(p);
                        exp.Add(p);
                        p = pathToParent.GetValueOrDefault(p);
                    }
                }
            }
            return (vis, exp);
        }, token);

        if (token.IsCancellationRequested) return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var (item, path, _, _) in flat)
            {
                item.IsVisible = visible.Contains(path);
                if (expand.Contains(path))
                    item.IsExpanded = true;
            }
        });
    }

    private void CollectFlatItems(ItemCollection items, string? parentPath, List<(TreeViewItem, string, string, string?)> result)
    {
        foreach (var item in items)
        {
            if (item is not TreeViewItem tvItem) continue;
            var path = GetFullPath(tvItem);
            if (string.IsNullOrEmpty(path)) continue;
            var name = System.IO.Path.GetFileName(path);
            result.Add((tvItem, path, name, parentPath));
            CollectFlatItems(tvItem.Items, path, result);
        }
    }

    private void SearchBox_KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchBox.Text = "";
            e.Handled = true;
        }
    }

    private void SaveExpandedState(ItemCollection items)
    {
        foreach (var item in items)
        {
            if (item is TreeViewItem tvItem)
            {
                if (tvItem.IsExpanded)
                    _preSearchExpanded.Add(GetFullPath(tvItem));
                SaveExpandedState(tvItem.Items);
            }
        }
    }

    private void RestoreExpandedState(ItemCollection items)
    {
        foreach (var item in items)
        {
            if (item is TreeViewItem tvItem)
            {
                tvItem.IsExpanded = _preSearchExpanded.Contains(GetFullPath(tvItem));
                RestoreExpandedState(tvItem.Items);
            }
        }
    }

    private void FilterCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FilterCombo.SelectedIndex < 0) return;
        _showFavoritesOnly = FilterCombo.SelectedIndex == 1;
        ApplyTreeFilter();
    }

    private void ApplyTreeFilter()
    {
        bool hasSearch = !string.IsNullOrEmpty(_searchText);
        foreach (var rootItem in TreeFiles.Items)
        {
            if (rootItem is TreeViewItem tvItem)
                FilterTreeItem(tvItem, hasSearch);
        }
    }

    private bool FilterTreeItem(TreeViewItem item, bool hasSearch)
    {
        string path = GetFullPath(item);
        string name = System.IO.Path.GetFileName(path);

        if (hasSearch)
        {
            bool childMatches = false;
            foreach (var child in item.Items)
            {
                if (child is TreeViewItem childItem && FilterTreeItem(childItem, true))
                    childMatches = true;
            }

            bool selfMatches = name.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
            if (!selfMatches && !childMatches)
            {
                item.IsVisible = false;
                return false;
            }

            item.IsVisible = true;
            if (childMatches)
                item.IsExpanded = true;
            return true;
        }

        if (_showFavoritesOnly)
        {
            bool childHasFav = false;
            foreach (var child in item.Items)
            {
                if (child is TreeViewItem childItem && FilterTreeItem(childItem, false))
                    childHasFav = true;
            }

            bool anyFavInSubtree = IsFavorite(path) || childHasFav;
            item.IsVisible = anyFavInSubtree;
            if (anyFavInSubtree && IsFavorite(path))
                item.IsExpanded = true;
            return anyFavInSubtree;
        }

        foreach (var child in item.Items)
        {
            if (child is TreeViewItem childItem)
                FilterTreeItem(childItem, false);
        }
        item.IsVisible = true;
        return true;
    }

    #endregion

    #region Helpers

    private string GetFullPath(TreeViewItem item)
    {
        try { return item.Tag as string ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static bool IsHiddenOrSystem(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            return (attr & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
        catch { return false; }
    }

    private void SetupDragSupport(TreeViewItem treeItem)
    {
        var itemPressed = false;
        var pressPoint = new Point();
        const double DragThreshold = 10.0;

        treeItem.PointerPressed += (_, e) =>
        {
            itemPressed = true;
            pressPoint = e.GetPosition(treeItem);
        };
        treeItem.PointerReleased += (_, _) => itemPressed = false;
        treeItem.PointerMoved += async (sender, args) =>
        {
            if (!itemPressed || sender is not TreeViewItem item) return;

            var pos = args.GetPosition(item);
            if (Math.Abs(pos.X - pressPoint.X) < DragThreshold &&
                Math.Abs(pos.Y - pressPoint.Y) < DragThreshold)
                return;

            itemPressed = false;

            string path = GetFullPath(item);
            if (!File.Exists(path) || !AudioFormats.IsAudioFile(path))
                return;

            var dataTransfer = new DataTransfer();
            var top = TopLevel.GetTopLevel(this);
            if (top != null)
            {
                var storageFile = await top.StorageProvider.TryGetFileFromPathAsync(path);
                if (storageFile == null) return;

                var dataItem = new DataTransferItem();
                dataItem.Set(DataFormat.File, storageFile);
                dataTransfer.Add(dataItem);
            }

            _audioPlayer.Stop();
            await DragDrop.DoDragDropAsync(args, dataTransfer, DragDropEffects.Copy);
        };
    }

    private Grid BuildHeaderGrid(string path, string name, string iconValue, Color? iconColor)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            VerticalAlignment = VerticalAlignment.Center
        };

        var starBtn = CreateStarButton(path);
        Grid.SetColumn(starBtn, 0);
        grid.Children.Add(starBtn);

        var icon = new Icon
        {
            Value = iconValue,
            FontSize = _treeFontSize,
            Margin = new Thickness(2, 0, 4, 0)
        };
        if (iconColor.HasValue)
            icon.Foreground = new SolidColorBrush(iconColor.Value);
        Grid.SetColumn(icon, 1);
        grid.Children.Add(icon);

        var text = new TextBlock
        {
            Text = name,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = _treeFontSize
        };
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        return grid;
    }

    private async void ShowTip(string message, bool isError)
    {
        try
        {
            var tipBlock = this.FindControl<TextBlock>("TipTextBlock");
            if (tipBlock == null) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                tipBlock.Text = message;
                tipBlock.Foreground = isError ? Brushes.Red : Brushes.White;
                tipBlock.IsVisible = true;
            });

            await Task.Delay(3000);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                tipBlock.IsVisible = false;
                tipBlock.Text = string.Empty;
            });
        }
        catch { }
    }

    private void OnItemWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

        _treeFontSize = Math.Clamp(_treeFontSize + (e.Delta.Y > 0 ? 1 : -1), 8, 32);
        UpdateTreeFontSizes(TreeFiles.Items);
        ShowTip(string.Format(Localizer.Instance["TipFontSize"], _treeFontSize.ToString("F0"), CacheService.DefaultFontSize.ToString("F0")), false);
        e.Handled = true;
    }

    private void TreeFiles_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

        _treeFontSize = Math.Clamp(_treeFontSize + (e.Delta.Y > 0 ? 1 : -1), 8, 32);
        UpdateTreeFontSizes(TreeFiles.Items);
        ShowTip(string.Format(Localizer.Instance["TipFontSize"], _treeFontSize.ToString("F0"), CacheService.DefaultFontSize.ToString("F0")), false);
        e.Handled = true;
    }

    private void UpdateTreeFontSizes(ItemCollection items)
    {
        foreach (var item in items)
        {
            if (item is not TreeViewItem tvItem || tvItem is PlaceholderItem) continue;

            if (tvItem.Header is Grid grid)
            {
                foreach (var child in grid.Children)
                {
                    if (child is Icon icon)
                        icon.FontSize = _treeFontSize;
                    else if (child is TextBlock tb)
                        tb.FontSize = _treeFontSize;
                    else if (child is Button btn && btn.Content is Icon btnIcon)
                        btnIcon.FontSize = _treeFontSize > 0 ? Math.Max(10, _treeFontSize - 2) : 10;
                }
            }

            if (tvItem.Items.Count > 0)
                UpdateTreeFontSizes(tvItem.Items);
        }
    }

    private void UpdateFolderIconColors(ItemCollection items, Color color)
    {
        var brush = new SolidColorBrush(color);
        foreach (var item in items)
        {
            if (item is not TreeViewItem tvItem || tvItem is PlaceholderItem) continue;

            if (tvItem.Header is Grid grid)
            {
                foreach (var child in grid.Children)
                {
                    if (child is Icon icon && icon.Value == "fa-solid fa-folder")
                        icon.Foreground = brush;
                }
            }

            if (tvItem.Items.Count > 0)
                UpdateFolderIconColors(tvItem.Items, color);
        }
    }

    #endregion
}
