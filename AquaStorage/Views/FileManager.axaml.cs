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
using Projektanker.Icons.Avalonia;

namespace AquaStorage.Views;

public partial class FileManager : UserControl
{
    private const string ConfigPath = "Config/FileManagerConfig";

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

    public FileManager()
    {
        InitializeComponent();
        _loadingBar = this.FindControl<ProgressBar>("LoadingBar");
        _tipTextBlock = this.FindControl<TextBlock>("TipTextBlock");

        if (_loadingBar != null)
        {
            _loadingBar.IsVisible = false;
            _loadingBar.Minimum = 0;
            _loadingBar.Maximum = 100;
        }

        _ = LoadSavedPathsOnStartup();
    }

    #region Startup Loading

    private async Task LoadSavedPathsOnStartup()
    {
        if (AddedPaths.Count == 0)
        {
            ShowTip("No saved paths. Click 'Add Folder' to get started.", false);
            return;
        }

        _isLoading = true;
        UpdateLoadingUI(true);
        ShowTip($"Loading {AddedPaths.Count} saved path(s)...", false);

        try
        {
            foreach (var path in AddedPaths)
            {
                if (!Directory.Exists(path))
                {
                    ShowTip($"Not found: {path} (skipped)", true);
                    continue;
                }

                var dirInfo = new DirectoryInfo(path);
                _totalItems = 0;
                _totalLoaded = 0;
                await Task.Run(() => CountItems(dirInfo));

                var rootItem = await CreateTreeItemAsync(dirInfo, false);
                rootItem.Tag = path;

                await Dispatcher.UIThread.InvokeAsync(() => TreeFiles.Items.Add(rootItem));
            }

            ShowTip($"Loaded {AddedPaths.Count} path(s)", false);
        }
        catch (Exception ex)
        {
            ShowTip($"Load failed: {ex.Message}", true);
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
                new FolderPickerOpenOptions { Title = "Select folder", AllowMultiple = true });

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
                    ShowTip($"Not found: {inputPath} (skipped)", true);
                    continue;
                }
                if (AddedPaths.Contains(inputPath))
                {
                    ShowTip($"Already added: {inputPath}", true);
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
                }
                catch (Exception ex)
                {
                    ShowTip($"Failed to load {inputPath}: {ex.Message}", true);
                }
            }

            ShowTip(added > 0 ? $"Added {added} folder(s)" : "No new folders added", false);
        }
        catch (Exception ex)
        {
            ShowTip($"Folder selection failed: {ex.Message}", true);
        }
        finally
        {
            _isLoading = false;
            UpdateLoadingUI(false);
        }
    }

    private void BtnDetail_Click(object? sender, RoutedEventArgs e)
    {
        var detailWin = new PathDetailWindow(AddedPaths);

        detailWin.OnPathAdded += async (addedPath) =>
        {
            if (AddedPaths.Contains(addedPath)) return;
            try
            {
                var dirInfo = new DirectoryInfo(addedPath);
                var rootItem = await CreateTreeItemAsync(dirInfo, false);
                rootItem.Tag = addedPath;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AddedPaths.Add(addedPath);
                    SaveConfig();
                    TreeFiles.Items.Add(rootItem);
                    rootItem.IsExpanded = false;
                    TreeFiles.SelectedItem = rootItem;
                    TreeFiles.Focus();
                    ShowTip($"Added: {addedPath}", false);
                });
            }
            catch (Exception ex)
            {
                ShowTip($"Failed to load {addedPath}: {ex.Message}", true);
            }
        };

        detailWin.OnPathDeleted += (deletedPath) =>
        {
            if (AddedPaths.Contains(deletedPath))
            {
                AddedPaths.Remove(deletedPath);
                SaveConfig();
                RemoveTreeItemByPath(deletedPath);
                ShowTip($"Removed: {deletedPath}", false);
            }
        };

        detailWin.Show();
    }

    #endregion

    #region TreeView Construction

    private async Task<TreeViewItem> CreateTreeItemAsync(FileSystemInfo fsInfo, bool isExpand)
    {
        var treeItem = new TreeViewItem { IsExpanded = isExpand };

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
            if (!File.Exists(path) || !path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
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

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (fsInfo is DirectoryInfo dir)
        {
            content.Children.Add(new Icon { Value = "fa-solid fa-folder", FontSize = 12 });
            content.Children.Add(new TextBlock { Text = dir.Name, VerticalAlignment = VerticalAlignment.Center });
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

            if (file.Extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
                content.Children.Add(new Icon { Value = "fa-solid fa-file-waveform", FontSize = 12 });
            else
                content.Children.Add(new Icon { Value = "fa-regular fa-file", FontSize = 12 });

            content.Children.Add(new TextBlock { Text = file.Name, VerticalAlignment = VerticalAlignment.Center });
            treeItem.Tag = file.FullName;

            _totalLoaded++;
            UpdateProgress();
        }

        treeItem.Header = content;
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

        if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            PlayWav(path);
        }
        else
        {
            _suppressSelectionChanged = true;
            selected.IsExpanded = !selected.IsExpanded;
            Dispatcher.UIThread.Post(() => _suppressSelectionChanged = false, DispatcherPriority.Loaded);
        }
    }

    private void PlayWav(string filePath)
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
            _tipTextBlock.Foreground = isError ? Brushes.DarkRed : Brushes.Gray;
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

    #endregion
}
