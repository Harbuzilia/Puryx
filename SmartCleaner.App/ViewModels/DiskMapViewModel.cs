using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SmartCleaner.Core.DiskMap;
using SmartCleaner.Core.Helpers;

namespace SmartCleaner.App.ViewModels;

/// <summary>
/// ViewModel для страницы карты диска (TreeMap).
/// Визуализирует использование дискового пространства.
/// </summary>
public partial class DiskMapViewModel : ObservableObject
{
    private readonly DiskMapEngine _engine;
    private CancellationTokenSource? _cts;

    public DiskMapViewModel(DiskMapEngine engine)
    {
        _engine = engine;
    }

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusText = "Выберите папку или диск для анализа";
    [ObservableProperty] private string _scanPath = "";
    [ObservableProperty] private int _maxDepth = 5;
    [ObservableProperty] private DiskNode? _rootNode;
    [ObservableProperty] private DiskNode? _selectedNode;
    [ObservableProperty] private DiskNode? _originalRoot; // для кнопки «Назад»
    [ObservableProperty] private double _canvasWidth = 700;
    [ObservableProperty] private double _canvasHeight = 450;

    public System.Collections.ObjectModel.ObservableCollection<TreemapRect> TreemapTiles { get; } = new();
    public System.Collections.Generic.Stack<DiskNode> BreadcrumbHistory { get; } = new();

    /// <summary>
    /// Пересчитывает раскладку Squarified Treemap для текущего узла.
    /// </summary>
    public void RefreshTreemap(double width = 0, double height = 0)
    {
        if (width > 0) CanvasWidth = width;
        if (height > 0) CanvasHeight = height;

        TreemapTiles.Clear();
        if (RootNode == null || CanvasWidth <= 10 || CanvasHeight <= 10) return;

        var tiles = TreemapLayout.CalculateSquarified(RootNode, CanvasWidth, CanvasHeight);
        foreach (var t in tiles)
        {
            TreemapTiles.Add(t);
        }
    }

    [RelayCommand]
    public void DrillDown(TreemapRect? tile)
    {
        if (tile == null || tile.Node.IsFile || tile.Node.Children.Count == 0) return;

        if (RootNode != null)
        {
            BreadcrumbHistory.Push(RootNode);
        }

        RootNode = tile.Node;
        RefreshTreemap();
        StatusText = $"Переход в папку: {tile.DisplayName} ({SizeFormatter.Format(tile.Node.Size)})";
    }

    [RelayCommand]
    public void DrillUp()
    {
        if (BreadcrumbHistory.Count > 0)
        {
            RootNode = BreadcrumbHistory.Pop();
            RefreshTreemap();
            StatusText = $"Возврат на уровень: {RootNode.Name}";
        }
        else if (OriginalRoot != null && RootNode != OriginalRoot)
        {
            RootNode = OriginalRoot;
            RefreshTreemap();
            StatusText = $"Корень: {OriginalRoot.FullPath}";
        }
    }

    /// <summary>
    /// Информация о выбранном узле.
    /// </summary>
    [ObservableProperty] private string _selectedNodeInfo = "";

    partial void OnSelectedNodeChanged(DiskNode? value)
    {
        if (value == null)
        {
            SelectedNodeInfo = "";
            return;
        }

        SelectedNodeInfo = $"📁 {value.Name}\n" +
                          $"Размер: {SizeFormatter.Format(value.Size)}\n" +
                          $"Доля: {value.Percentage:F1}%\n" +
                          (value.IsFile ? "Файл" : $"Папка ({value.Children.Count} элементов)") +
                          $"\n{value.FullPath}";
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Выберите папку для анализа" };
        if (dlg.ShowDialog() == true)
            ScanPath = dlg.FolderName;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(ScanPath) || !System.IO.Directory.Exists(ScanPath))
        {
            StatusText = "⚠️ Укажите существующую папку";
            return;
        }

        IsScanning = true;
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<string>(msg => StatusText = msg);
            var root = await _engine.ScanAsync(ScanPath, MaxDepth, progress, _cts.Token);

            RootNode = root;
            OriginalRoot = root;
            BreadcrumbHistory.Clear();
            RefreshTreemap();
            StatusText = $"Готово • {SizeFormatter.Format(root.Size)} • {root.Children.Count} элементов";
        }
        catch (OperationCanceledException) { StatusText = "Сканирование отменено"; }
        catch (Exception ex) { StatusText = $"Ошибка: {ex.Message}"; }
        finally { IsScanning = false; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _cts?.Cancel();
        StatusText = "Отмена...";
    }

    /// <summary>
    /// Вернуться к оригинальному корню (после зума).
    /// </summary>
    [RelayCommand]
    private void GoToRoot()
    {
        if (OriginalRoot != null)
        {
            RootNode = OriginalRoot;
            StatusText = $"Корень: {OriginalRoot.FullPath}";
        }
    }

    /// <summary>
    /// Открыть папку в проводнике.
    /// </summary>
    [RelayCommand]
    private void OpenInExplorer()
    {
        if (SelectedNode == null) return;

        try
        {
            var path = SelectedNode.IsFile
                ? System.IO.Path.GetDirectoryName(SelectedNode.FullPath) ?? SelectedNode.FullPath
                : SelectedNode.FullPath;

            // Обоснованное исключение (День 19, срез C): explorer.exe —
            // естественный shell-запуск (показать элемент в Проводнике),
            // не исполнение команды: UseShellExecute=true и видимое окно —
            // сам смысл операции; контракт исполнителя не применим
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = SelectedNode.IsFile
                    ? $"/select,\"{SelectedNode.FullPath}\""
                    : $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex) { /* best-effort */ System.Diagnostics.Debug.WriteLine($"[DiskMapViewModel] OpenInExplorer error: {ex.Message}"); }
    }
}
