using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using SmartCleaner.Core.CliInspector;
using SmartCleaner.Core.Helpers;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Safety;

namespace SmartCleaner.App.ViewModels;

public partial class CliInspectorViewModel : ObservableObject
{
    private readonly CliInspectorEngine _engine;
    private readonly PathEnvironmentService _pathService;
    private readonly ISafetyService _safety;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusText = "Нажмите «Сканировать» для инспекции CLI, AI-агентов и переменных PATH";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = "Все";

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private int _totalToolsCount;

    [ObservableProperty]
    private long _totalSizeBytes;

    [ObservableProperty]
    private string _totalSizeFormatted = "0 B";

    [ObservableProperty]
    private int _deadPathsCount;

    [ObservableProperty]
    private int _totalPathsCount;

    [ObservableProperty]
    private int _psProfilesCount;

    public ObservableCollection<string> Categories { get; } = ["Все"];
    public ObservableCollection<CliToolItem> AllTools { get; } = new();
    public ObservableCollection<CliToolItem> FilteredTools { get; } = new();
    public ObservableCollection<PathHealthItem> PathEntries { get; } = new();
    public ObservableCollection<PowerShellProfileItem> PsProfiles { get; } = new();

    public CliInspectorViewModel(CliInspectorEngine engine, PathEnvironmentService pathService, ISafetyService safety)
    {
        _engine = engine;
        _pathService = pathService;
        _safety = safety;
    }

    [RelayCommand]
    public async Task ScanAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        StatusText = "Сканирование CLI-инструментов, AI-агентов и системных переменных PATH...";

        try
        {
            var result = await _engine.ScanAsync();

            AllTools.Clear();
            foreach (var tool in result.Tools)
            {
                AllTools.Add(tool);
            }

            PathEntries.Clear();
            foreach (var p in result.PathHealth)
            {
                PathEntries.Add(p);
            }

            PsProfiles.Clear();
            foreach (var prof in result.PowerShellProfiles)
            {
                PsProfiles.Add(prof);
            }

            TotalToolsCount = result.TotalToolsCount;
            TotalSizeBytes = result.TotalSizeBytes;
            TotalSizeFormatted = result.TotalSizeFormatted;
            DeadPathsCount = result.DeadPathsCount;
            TotalPathsCount = result.TotalPathsScanned;
            PsProfilesCount = result.PsProfilesCount;

            Categories.Clear();
            Categories.Add("Все");
            foreach (var cat in result.CategoriesCount.Keys.OrderBy(c => c))
            {
                Categories.Add(cat);
            }

            ApplyFilter();

            StatusText = $"Найдено {TotalToolsCount} утилит ({TotalSizeFormatted}), {DeadPathsCount} битых путей в PATH, {PsProfilesCount} профилей PowerShell";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка при сканировании: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedCategoryChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var filtered = AllTools.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SelectedCategory) && SelectedCategory != "Все")
        {
            filtered = filtered.Where(t => string.Equals(t.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            filtered = filtered.Where(t =>
                t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.Path.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.Category.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        FilteredTools.Clear();
        foreach (var item in filtered)
        {
            FilteredTools.Add(item);
        }
    }

    [RelayCommand]
    private void OpenInExplorer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!Directory.Exists(path) && !File.Exists(path)))
        {
            MessageBox.Show($"Путь не найден: {path}", "Smart Cleaner", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else
            {
                Process.Start("explorer.exe", $"\"{path}\"");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось открыть: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void OpenTerminal(string? path)
    {
        var workDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (Directory.Exists(path)) workDir = path;
            else if (File.Exists(path)) workDir = Path.GetDirectoryName(path) ?? workDir;
        }

        try
        {
            // Рабочая директория задаётся через ProcessStartInfo — никакой
            // инъекции пути в командную строку PowerShell (апостроф в имени папки)
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoExit",
                WorkingDirectory = workDir,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось запустить терминал: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CopyUninstall(string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return;

        try
        {
            Clipboard.SetText(cmd);
            StatusText = $"Команда скопирована в буфер: {cmd}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось скопировать: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void DeleteTool(CliToolItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Path)) return;

        var result = MessageBox.Show(
            $"Удалить инструмент '{item.Name}'?\nПуть: {item.Path}\nРазмер: {item.SizeFormatted}\n\nФайлы будут перемещены в Корзину.",
            "Подтверждение удаления",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        var validation = _safety.ValidateForDeletion(new ScannedItem
        {
            Path = item.Path,
            IsDirectory = Directory.Exists(item.Path) && !File.Exists(item.Path),
            Size = item.SizeBytes,
            Risk = RiskCategory.PerformanceCache,
            Description = $"cli-tool:{item.Name}"
        });

        if (!validation.CanDelete)
        {
            MessageBox.Show($"Удаление заблокировано: {validation.BlockReason}", "Защита", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (validation.RequiresElevation)
        {
            MessageBox.Show("Инструмент находится в защищённом каталоге и требует прав администратора.", "Защита", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            if (Directory.Exists(item.Path))
            {
                FileSystem.DeleteDirectory(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            else if (File.Exists(item.Path))
            {
                FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }

            AllTools.Remove(item);
            FilteredTools.Remove(item);
            TotalSizeBytes = Math.Max(0, TotalSizeBytes - item.SizeBytes);
            TotalSizeFormatted = SizeFormatter.Format(TotalSizeBytes);
            TotalToolsCount = AllTools.Count;

            StatusText = $"Удалено в Корзину: {item.Name} ({item.SizeFormatted})";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при удалении: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CleanDeadPaths()
    {
        var deadEntries = PathEntries.Where(p => p.IsDead && p.Scope == "User").ToList();
        if (deadEntries.Count == 0)
        {
            MessageBox.Show("Мертвых путей в переменной PATH пользователя не обнаружено.", "Здоровье PATH", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var msg = $"Найдено {deadEntries.Count} несуществующих записей в User PATH:\n\n" +
                  string.Join("\n", deadEntries.Select(d => $"• {d.Path}")) +
                  "\n\nУдалить эти записи из системного реестра?";

        var answer = MessageBox.Show(msg, "Очистка битых путей PATH", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        var (success, error, removedCount) = _pathService.CleanDeadUserPaths();
        if (success)
        {
            foreach (var d in deadEntries)
            {
                PathEntries.Remove(d);
            }
            DeadPathsCount = PathEntries.Count(p => p.IsDead);
            StatusText = $"Успешно вычищено {removedCount} битых путей из переменной PATH пользователя!";
            MessageBox.Show($"Удалено {removedCount} битых путей. Системные переменные обновлены без перезагрузки.", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show($"Ошибка при очистке: {error}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void RemoveSinglePath(PathHealthItem? item)
    {
        if (item == null || item.Scope != "User")
        {
            MessageBox.Show("Удаление возможно только для пользовательских записей PATH (User).", "Smart Cleaner", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var (success, error) = _pathService.RemoveUserPathEntry(item.Path);
        if (success)
        {
            PathEntries.Remove(item);
            DeadPathsCount = PathEntries.Count(p => p.IsDead);
            StatusText = $"Путь удален из User PATH: {item.Path}";
        }
        else
        {
            MessageBox.Show($"Ошибка при удалении: {error}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ExportReport()
    {
        var sfd = new SaveFileDialog
        {
            Filter = "JSON Report (*.json)|*.json|CSV File (*.csv)|*.csv",
            FileName = $"deliter_report_{DateTime.Now:yyyyMMdd_HHmm}.json"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                if (sfd.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    var lines = new List<string> { "Name,Category,Path,Size,LastModified,UninstallCommand" };
                    lines.AddRange(AllTools.Select(t => $"\"{t.Name}\",\"{t.Category}\",\"{t.Path}\",\"{t.SizeFormatted}\",\"{t.LastModified}\",\"{t.UninstallCmd}\""));
                    File.WriteAllLines(sfd.FileName, lines);
                }
                else
                {
                    var report = new
                    {
                        Timestamp = DateTime.Now,
                        TotalTools = TotalToolsCount,
                        TotalSize = TotalSizeFormatted,
                        DeadPathsCount = DeadPathsCount,
                        Tools = AllTools,
                        PathHealth = PathEntries,
                        PowerShellProfiles = PsProfiles
                    };
                    var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(sfd.FileName, json);
                }

                StatusText = $"Отчет успешно сохранен: {sfd.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

