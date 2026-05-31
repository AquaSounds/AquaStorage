using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
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
using AquaStorage.Models;
using AquaStorage.Services;
using Projektanker.Icons.Avalonia;

namespace AquaStorage.Views;

public partial class FileManager : UserControl
{
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

    private readonly ProgressBar? _loadingBar;
    private readonly TextBlock? _tipTextBlock;
    private bool _isLoading;
    private int _totalLoaded;
    private int _totalItems;

    private readonly AudioPlayerService _audioPlayer = new();
    private bool _suppressSelectionChanged;
    private double _treeFontSize = CacheService.DefaultFontSize;

    private string _searchText = "";
    private bool _showFavoritesOnly;
    private HashSet<string> _favoritePaths = new();
    private HashSet<string> _preSearchExpanded = new();
    private bool _searchWasActive;

    public FileManager()
    {
        InitializeComponent();
        _loadingBar = this.FindControl<ProgressBar>("LoadingBar");
        _tipTextBlock = this.FindControl<TextBlock>("TipTextBlock");
        WaveForm.Player = _audioPlayer;
        VolumeKnob.Value = _audioPlayer.VolumeDb;
        VolumeKnob.ValueChanged += db => _audioPlayer.VolumeDb = db;

        if (_loadingBar != null)
        {
            _loadingBar.IsVisible = false;
            _loadingBar.Minimum = 0;
            _loadingBar.Maximum = 100;
        }

        TreeFiles.AddHandler(PointerWheelChangedEvent, TreeFiles_OnPointerWheelChanged, RoutingStrategies.Bubble, handledEventsToo: true);

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
        {
            ShowTip(Localizer.Instance["TipNoSavedPaths"], false);
            return;
        }

        _isLoading = true;
        UpdateLoadingUI(true);
        ShowTip(string.Format(Localizer.Instance["TipLoadingPaths"], AddedPaths.Count), false);

        try
        {
            // Try loading from cache first
            var cachedRoots = await Task.Run(() => CacheHelper.LoadFolderTree(AddedPaths));
            if (cachedRoots != null)
            {
                foreach (var node in cachedRoots)
                {
                    var rootItem = BuildTreeItemFromCache(node);
                    await Dispatcher.UIThread.InvokeAsync(() => TreeFiles.Items.Add(rootItem));
                }
                ShowTip(string.Format(Localizer.Instance["TipLoadedFromCache"], AddedPaths.Count), false);
                _isLoading = false;
                UpdateLoadingUI(false);
                return;
            }

            // Cache miss — scan filesystem
            var treeDataRoots = new List<TreeNodeData>();

            foreach (var path in AddedPaths)
            {
                if (!Directory.Exists(path))
                {
                    ShowTip(string.Format(Localizer.Instance["TipNotFound"], path), true);
                    continue;
                }

                var dirInfo = new DirectoryInfo(path);
                _totalItems = 0;
                _totalLoaded = 0;
                await Task.Run(() => CountItems(dirInfo));

                var rootItem = await CreateTreeItemAsync(dirInfo, false);
                rootItem.Tag = path;

                var nodeData = await Task.Run(() => BuildTreeNodeData(dirInfo));
                treeDataRoots.Add(nodeData);

                await Dispatcher.UIThread.InvokeAsync(() => TreeFiles.Items.Add(rootItem));
            }

            // Save to cache
            if (treeDataRoots.Count > 0)
                await Task.Run(() => CacheHelper.SaveFolderTree(treeDataRoots, AddedPaths));

            ShowTip(string.Format(Localizer.Instance["TipLoadedPaths"], AddedPaths.Count), false);
        }
        catch (Exception ex)
        {
            ShowTip(string.Format(Localizer.Instance["TipLoadFailed"], ex.Message), true);
        }
        finally
        {
            _isLoading = false;
            UpdateLoadingUI(false);
        }
    }

    #endregion

    #region Buttons

    private async void BtnAdd_Click(object? sender, RoutedEventArgs e)
    {
        if (_isLoading) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        try
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = Localizer.Instance["SelectFolder"], AllowMultiple = true });

            if (folders.Count == 0) return;

            _isLoading = true;
            UpdateLoadingUI(true);
            int added = 0;

            foreach (var folder in folders)
            {
                var inputPath = folder.Path.LocalPath;
                if (string.IsNullOrEmpty(inputPath)) continue;

                if (!Directory.Exists(inputPath))
                {
                    ShowTip(string.Format(Localizer.Instance["TipNotFound"], inputPath), true);
                    continue;
                }
                if (AddedPaths.Contains(inputPath))
                {
                    ShowTip(string.Format(Localizer.Instance["TipAlreadyAdded"], inputPath), true);
                    continue;
                }

                try
                {
                    _totalLoaded = 0;
                    _totalItems = 0;

                    var dirInfo = new DirectoryInfo(inputPath);
                    await Task.Run(() => CountItems(dirInfo));
                    var rootItem = await CreateTreeItemAsync(dirInfo, false);
                    rootItem.Tag = inputPath;

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        AddedPaths.Add(inputPath);
                        SaveConfig();
                        TreeFiles.Items.Add(rootItem);
                        rootItem.IsExpanded = false;
                        TreeFiles.SelectedItem = rootItem;
                        TreeFiles.Focus();
                        added++;
                    });

                    CacheHelper.ClearAll(); // invalidate cache
                }
                catch (Exception ex)
                {
                    ShowTip(string.Format(Localizer.Instance["TipFailedToLoad"], inputPath, ex.Message), true);
                }
            }

            ShowTip(added > 0 ? string.Format(Localizer.Instance["TipAddedFolders"], added) : Localizer.Instance["TipNoNewFolders"], false);
        }
        catch (Exception ex)
        {
            ShowTip(string.Format(Localizer.Instance["TipFolderSelectionFailed"], ex.Message), true);
        }
        finally
        {
            _isLoading = false;
            UpdateLoadingUI(false);
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
                CacheHelper.ClearAll(); // invalidate cache
                ShowTip(string.Format(Localizer.Instance["TipRemoved"], deletedPath), false);
            }
        };

        settingsWin.Show();
    }

    #endregion

    #region TreeView Construction

    private async Task<TreeViewItem> CreateTreeItemAsync(FileSystemInfo fsInfo, bool isExpand)
    {
        var treeItem = new TreeViewItem { IsExpanded = isExpand };

        treeItem.PointerWheelChanged += OnItemWheelChanged;

        // Drag support — only trigger after significant pointer movement
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

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            VerticalAlignment = VerticalAlignment.Center
        };

        if (fsInfo is DirectoryInfo dir)
        {
            var starBtn = CreateStarButton(dir.FullName);
            Grid.SetColumn(starBtn, 0);
            headerGrid.Children.Add(starBtn);

            var accentColor = Application.Current?.FindResource("AccentPrimary") is Color c ? c : Colors.DodgerBlue;
            var icon = new Icon { Value = "fa-solid fa-folder", FontSize = _treeFontSize, Foreground = new SolidColorBrush(accentColor), Margin = new Thickness(2, 0, 4, 0) };
            Grid.SetColumn(icon, 1);
            headerGrid.Children.Add(icon);

            var text = new TextBlock { Text = dir.Name, VerticalAlignment = VerticalAlignment.Center, FontSize = _treeFontSize,  };
            Grid.SetColumn(text, 2);
            headerGrid.Children.Add(text);

            treeItem.Tag = dir.FullName;
            await LoadChildrenAsync(dir, treeItem, isExpand);
        }
        else if (fsInfo is FileInfo file)
        {
            if ((file.Attributes & FileAttributes.Hidden) != 0 || (file.Attributes & FileAttributes.System) != 0)
            {
                _totalLoaded++;
                UpdateProgress();
                treeItem.Header = new TextBlock();
                return treeItem;
            }

            var starBtn = CreateStarButton(file.FullName);
            Grid.SetColumn(starBtn, 0);
            headerGrid.Children.Add(starBtn);

            var icon = new Icon
            {
                Value = AudioFormats.IsAudioFile(file.FullName) ? "fa-solid fa-file-waveform" : "fa-regular fa-file",
                FontSize = _treeFontSize,
                Margin = new Thickness(2, 0, 4, 0)
            };
            Grid.SetColumn(icon, 1);
            headerGrid.Children.Add(icon);

            var text = new TextBlock { Text = file.Name, VerticalAlignment = VerticalAlignment.Center, FontSize = _treeFontSize,  };
            Grid.SetColumn(text, 2);
            headerGrid.Children.Add(text);

            treeItem.Tag = file.FullName;

            _totalLoaded++;
            UpdateProgress();
        }

        treeItem.Header = headerGrid;
        return treeItem;
    }

    private async Task LoadChildrenAsync(DirectoryInfo dir, TreeViewItem parent, bool isExpand)
    {
        try
        {
            var (subDirs, files) = await Task.Run(() =>
            {
                var dirs = new List<DirectoryInfo>();
                var fils = new List<FileInfo>();

                foreach (var d in dir.GetDirectories())
                {
                    if ((d.Attributes & FileAttributes.Hidden) != 0 || (d.Attributes & FileAttributes.System) != 0)
                        continue;
                    dirs.Add(d);
                }
                foreach (var f in dir.GetFiles())
                {
                    if ((f.Attributes & FileAttributes.Hidden) != 0 || (f.Attributes & FileAttributes.System) != 0)
                        continue;
                    fils.Add(f);
                }
                return (dirs, fils);
            });

            var children = new List<TreeViewItem>();

            var tasks = new List<Task<TreeViewItem>>();
            foreach (var sub in subDirs)
                tasks.Add(CreateTreeItemAsync(sub, isExpand));
            children.AddRange(await Task.WhenAll(tasks));

            foreach (var file in files)
                children.Add(await CreateTreeItemAsync(file, isExpand));

            Dispatcher.UIThread.Post(() =>
            {
                foreach (var child in children)
                    if (child.Header != null)
                        parent.Items.Add(child);
            });

            _totalLoaded += subDirs.Count;
            UpdateProgress();
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                parent.Items.Add(new TreeViewItem
                {
                    Header = new TextBlock
                    {
                        Text = $"[Error: {ex.Message}]",
                        Foreground = Brushes.DarkRed,
                        Margin = new Thickness(18, 0, 0, 0)
                    }
                });
            });
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

    // ── Cache helpers ──────────────────────────────────────────────────

    private static TreeNodeData BuildTreeNodeData(DirectoryInfo dir)
    {
        var node = new TreeNodeData
        {
            Name = dir.Name,
            Path = dir.FullName,
            IsDirectory = true
        };

        try
        {
            foreach (var d in dir.GetDirectories())
            {
                if ((d.Attributes & FileAttributes.Hidden) != 0 || (d.Attributes & FileAttributes.System) != 0)
                    continue;
                node.Children.Add(BuildTreeNodeData(d));
            }
            foreach (var f in dir.GetFiles())
            {
                if ((f.Attributes & FileAttributes.Hidden) != 0 || (f.Attributes & FileAttributes.System) != 0)
                    continue;
                node.Children.Add(new TreeNodeData
                {
                    Name = f.Name,
                    Path = f.FullName,
                    IsDirectory = false
                });
            }
        }
        catch { }

        return node;
    }

    private TreeViewItem BuildTreeItemFromCache(TreeNodeData node)
    {
        var treeItem = new TreeViewItem { IsExpanded = false };
        treeItem.PointerWheelChanged += OnItemWheelChanged;

        // Drag support — only trigger after significant pointer movement
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

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"),
            VerticalAlignment = VerticalAlignment.Center
        };

        if (node.IsDirectory)
        {
            var starBtn = CreateStarButton(node.Path);
            Grid.SetColumn(starBtn, 0);
            headerGrid.Children.Add(starBtn);

            var accentColor = Application.Current?.FindResource("AccentPrimary") is Color c ? c : Colors.DodgerBlue;
            var icon = new Icon { Value = "fa-solid fa-folder", FontSize = _treeFontSize, Foreground = new SolidColorBrush(accentColor), Margin = new Thickness(2, 0, 4, 0) };
            Grid.SetColumn(icon, 1);
            headerGrid.Children.Add(icon);

            var text = new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center, FontSize = _treeFontSize,  };
            Grid.SetColumn(text, 2);
            headerGrid.Children.Add(text);

            treeItem.Tag = node.Path;

            foreach (var child in node.Children)
            {
                var childItem = BuildTreeItemFromCache(child);
                if (childItem.Header != null)
                    treeItem.Items.Add(childItem);
            }
        }
        else
        {
            var starBtn = CreateStarButton(node.Path);
            Grid.SetColumn(starBtn, 0);
            headerGrid.Children.Add(starBtn);

            var icon = new Icon
            {
                Value = AudioFormats.IsAudioFile(node.Path) ? "fa-solid fa-file-waveform" : "fa-regular fa-file",
                FontSize = _treeFontSize,
                Margin = new Thickness(2, 0, 4, 0)
            };
            Grid.SetColumn(icon, 1);
            headerGrid.Children.Add(icon);

            var text = new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center, FontSize = _treeFontSize,  };
            Grid.SetColumn(text, 2);
            headerGrid.Children.Add(text);

            treeItem.Tag = node.Path;
        }

        treeItem.Header = headerGrid;
        return treeItem;
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

    private void SearchBox_TextChanged(object? sender, TextChangedEventArgs e)
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

        ApplyTreeFilter();

        if (string.IsNullOrEmpty(_searchText) && _searchWasActive)
        {
            RestoreExpandedState(TreeFiles.Items);
            _searchWasActive = false;
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

    private void CountItems(DirectoryInfo dir)
    {
        try
        {
            foreach (var d in dir.GetDirectories())
            {
                if ((d.Attributes & FileAttributes.Hidden) != 0 || (d.Attributes & FileAttributes.System) != 0)
                    continue;
                _totalItems++;
                CountItems(d);
            }
            foreach (var f in dir.GetFiles())
            {
                if ((f.Attributes & FileAttributes.Hidden) != 0 || (f.Attributes & FileAttributes.System) != 0)
                    continue;
                _totalItems++;
            }
        }
        catch { }
    }

    private void UpdateProgress()
    {
        if (_totalItems == 0 || _loadingBar == null) return;
        var pct = (double)_totalLoaded / _totalItems * 100;
        Dispatcher.UIThread.Post(() => _loadingBar.Value = Math.Min(pct, 100));
    }

    private void UpdateLoadingUI(bool isLoading)
    {
        if (_loadingBar == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            _loadingBar.IsVisible = isLoading;
            _loadingBar.Value = isLoading ? 0 : 100;
        });
    }

    private void ShowTip(string message, bool isError)
    {
        if (_tipTextBlock == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            _tipTextBlock.Text = message;
            _tipTextBlock.Foreground = isError ? Brushes.Red : Brushes.White;
            _tipTextBlock.IsVisible = true;

            Task.Delay(3000).ContinueWith(_ =>
            {
                Dispatcher.UIThread.Post(() =>
                { 
                    _tipTextBlock.IsVisible = false;
                    _tipTextBlock.Text = string.Empty;
                });
            });
        });
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
            if (item is not TreeViewItem tvItem) continue;

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
            if (item is not TreeViewItem tvItem) continue;

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
