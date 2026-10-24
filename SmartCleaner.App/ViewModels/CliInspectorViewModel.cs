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
using SmartCleaner.Core.Localization;
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
    private string _statusText = Localizer.GetString("CliInspector_Status_Ready");

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedCategory = Localizer.GetString("Common_CategoryAll");

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

    public ObservableCollection<string> Categories { get; } = [Localizer.GetString("Common_CategoryAll")];
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
        StatusText = Localizer.GetString("CliInspector_Status_Scanning");

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
            Categories.Add(Localizer.GetString("Common_CategoryAll"));
            foreach (var cat in result.CategoriesCount.Keys.OrderBy(c => c))
            {
                Categories.Add(cat);
            }

            ApplyFilter();

            StatusText = Localizer.GetString("CliInspector_Status_Found", TotalToolsCount, TotalSizeFormatted, DeadPathsCount, PsProfilesCount);
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("CliInspector_Status_ScanError", ex.Message);
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

        if (!string.IsNullOrWhiteSpace(SelectedCategory) && SelectedCategory != Localizer.GetString("Common_CategoryAll"))
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
            MessageBox.Show(Localizer.GetString("CliInspector_Msg_PathNotFound", path ?? ""), "Puryx", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // Обоснованное исключение (День 19, срез C): explorer.exe —
            // естественный shell-запуск для пользователя (показать файл в
            // Проводнике), не исполнение команды: UseShellExecute=true и
            // видимое окно — сам смысл операции; контракт исполнителя
            // (CreateNoWindow, редирект потоков, таймаут с kill-tree) здесь
            // не применим.
            // День 24 (M7): абсолютный путь из Windows-корня — binary planting
            if (File.Exists(path))
            {
                Process.Start(SystemToolLocator.GetExplorerPath(), $"/select,\"{path}\"");
            }
            else
            {
                Process.Start(SystemToolLocator.GetExplorerPath(), $"\"{path}\"");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(Localizer.GetString("CliInspector_Msg_OpenError", ex.Message), Localizer.GetString("Common_Caption_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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
            // Обоснованное исключение (День 19, срез C): интерактивный терминал
            // для пользователя — видимое окно (UseShellExecute=true) и есть
            // смысл операции; контракт исполнителя (CreateNoWindow, редирект
            // потоков, таймаут с kill-tree) превратил бы терминал в невидимый
            // подвешенный процесс. Рабочая директория задаётся через
            // ProcessStartInfo — никакой
            // инъекции пути в командную строку PowerShell (апостроф в имени папки).
            // День 23 (M7, перенос Дня 19): FileName — абсолютный путь из
            // SystemToolLocator; запуск "powershell.exe" по неквалифицированному
            // имени искал exe в каталоге приложения (binary planting в portable)
            var startInfo = new ProcessStartInfo
            {
                FileName = SystemToolLocator.GetWindowsPowerShellPath(),
                Arguments = "-NoExit",
                WorkingDirectory = workDir,
                UseShellExecute = true
            };
            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Localizer.GetString("CliInspector_Msg_TerminalError", ex.Message), Localizer.GetString("Common_Caption_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CopyUninstall(string? cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd)) return;

        try
        {
            Clipboard.SetText(cmd);
            StatusText = Localizer.GetString("CliInspector_Status_Copied", cmd);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Localizer.GetString("CliInspector_Msg_CopyError", ex.Message), Localizer.GetString("Common_Caption_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void DeleteTool(CliToolItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Path)) return;

        var result = MessageBox.Show(
            Localizer.GetString("CliInspector_Msg_DeleteConfirmBody", item.Name, item.Path, item.SizeFormatted),
            Localizer.GetString("CliInspector_Msg_DeleteConfirmTitle"),
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
            MessageBox.Show(Localizer.GetString("CliInspector_Msg_DeleteBlocked", validation.BlockReason ?? ""), Localizer.GetString("CliInspector_Caption_Protection"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (validation.RequiresElevation)
        {
            MessageBox.Show(Localizer.GetString("CliInspector_Msg_ElevationRequired"), Localizer.GetString("CliInspector_Caption_Protection"), MessageBoxButton.OK, MessageBoxImage.Warning);
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

            StatusText = Localizer.GetString("CliInspector_Status_Deleted", item.Name, item.SizeFormatted);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Localizer.GetString("CliInspector_Msg_DeleteError", ex.Message), Localizer.GetString("Common_Caption_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CleanDeadPaths()
    {
        var deadEntries = PathEntries.Where(p => p.IsDead && p.Scope == "User").ToList();
        if (deadEntries.Count == 0)
        {
            MessageBox.Show(Localizer.GetString("CliInspector_Msg_NoDeadPaths"), Localizer.GetString("CliInspector_Caption_PathHealth"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var msg = Localizer.GetString(
            "CliInspector_Msg_CleanDeadPathsConfirm",
            deadEntries.Count,
            string.Join("\n", deadEntries.Select(d => $"• {d.Path}")));

        var answer = MessageBox.Show(msg, Localizer.GetString("CliInspector_Caption_CleanDeadPaths"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        var (success, error, removedCount) = _pathService.CleanDeadUserPaths();
        if (success)
        {
            foreach (var d in deadEntries)
            {
                PathEntries.Remove(d);
            }
            DeadPathsCount = PathEntries.Count(p => p.IsDead);
            StatusText = Localizer.GetString("CliInspector_Status_DeadPathsCleaned", removedCount);
            MessageBox.Show(Localizer.GetString("CliInspector_Msg_DeadPathsRemoved", removedCount), Localizer.GetString("CliInspector_Caption_Success"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(Localizer.GetString("CliInspector_Msg_CleanError", error ?? ""), Localizer.GetString("Common_Caption_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void RemoveSinglePath(PathHealthItem? item)
    {
        if (item == null || item.Scope != "User")
        {
            MessageBox.Show(Localizer.GetString("CliInspector_Msg_UserScopeOnly"), "Puryx", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var (success, error) = _pathService.RemoveUserPathEntry(item.Path);
        if (success)
        {
            PathEntries.Remove(item);
            DeadPathsCount = PathEntries.Count(p => p.IsDead);
            StatusText = Localizer.GetString("CliInspector_Status_PathRemoved", item.Path);
        }
        else
        {
            MessageBox.Show(Localizer.GetString("CliInspector_Msg_DeleteError", error ?? ""), Localizer.GetString("Common_Caption_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
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

                StatusText = Localizer.GetString("CliInspector_Status_ReportSaved", sfd.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Localizer.GetString("CliInspector_Msg_SaveError", ex.Message), Localizer.GetString("Common_Caption_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

