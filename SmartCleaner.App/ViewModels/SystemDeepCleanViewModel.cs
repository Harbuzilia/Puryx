using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCleaner.Core.Localization;
using SmartCleaner.Core.WinSxS;
using System.Collections.ObjectModel;
using System.Windows;

namespace SmartCleaner.App.ViewModels;

public partial class SystemDeepCleanViewModel : ObservableObject
{
    private readonly WinSxSEngine _winsxs;
    private readonly DriverStoreCleaner _drivers;

    /// <summary>Последний результат анализа DISM — для честного текста подтверждения очистки.</summary>
    private WinSxSAnalysisResult? _lastWinSxSAnalysis;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = Localizer.GetString("SystemDeepClean_Status_PressAudit");

    [ObservableProperty]
    private string _winSxSActualSize = "-";

    [ObservableProperty]
    private string _winSxSReclaimable = "-";

    [ObservableProperty]
    private bool _winSxSCleanupRecommended;

    [ObservableProperty]
    private int _oldDriversCount;

    public ObservableCollection<DriverStoreItem> OldDrivers { get; } = new();

    public SystemDeepCleanViewModel(WinSxSEngine winsxs, DriverStoreCleaner drivers)
    {
        _winsxs = winsxs;
        _drivers = drivers;
    }

    [RelayCommand]
    public async Task RunAuditAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusText = Localizer.GetString("SystemDeepClean_Status_Analyzing");

        try
        {
            var sxsProgress = new Progress<string>(s => StatusText = s);
            var sxsResult = await _winsxs.AnalyzeComponentStoreAsync(sxsProgress);
            _lastWinSxSAnalysis = sxsResult;

            WinSxSActualSize = sxsResult.ActualSizeFormatted;
            WinSxSReclaimable = sxsResult.ReclaimablePackagesFormatted;
            WinSxSCleanupRecommended = sxsResult.CleanupRecommended;

            StatusText = Localizer.GetString("SystemDeepClean_Status_SearchingDrivers");
            var drvProgress = new Progress<string>(s => StatusText = s);
            var driversList = await _drivers.ScanDriversAsync(drvProgress);

            OldDrivers.Clear();
            foreach (var d in driversList.Where(x => x.IsOldDuplicate))
            {
                OldDrivers.Add(d);
            }
            OldDriversCount = OldDrivers.Count;

            var winSxSSummary = sxsResult.AnalysisAvailable
                ? Localizer.GetString("SystemDeepClean_Status_WinSxSSummaryOk", WinSxSReclaimable)
                : Localizer.GetString("SystemDeepClean_Status_WinSxSSummaryUnavailable", sxsResult.AnalysisUnavailableReason);
            StatusText = Localizer.GetString("SystemDeepClean_Status_AuditDone", winSxSSummary, OldDriversCount);
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("SystemDeepClean_Status_AuditError", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task CleanWinSxSAsync()
    {
        if (IsBusy) return;

        // Честная оценка объёма: из последнего анализа DISM; без анализа — «неизвестно»,
        // а не выдуманный диапазон «от 2 до 10+ ГБ» (аудит H4).
        var reclaimLine = _lastWinSxSAnalysis is { AnalysisAvailable: true, ReclaimablePackagesBytes: > 0 }
            ? Localizer.GetString("SystemDeepClean_Confirm_ReclaimKnown", _lastWinSxSAnalysis.ReclaimablePackagesFormatted)
            : Localizer.GetString("SystemDeepClean_Confirm_ReclaimUnknown");

        // День 21, L5: /ResetBase (необратимо) — только явный opt-in. Базовое
        // подтверждение описывает обратимую очистку без /ResetBase
        var result = MessageBox.Show(
            Localizer.GetString("SystemDeepClean_Confirm_Body", reclaimLine),
            Localizer.GetString("SystemDeepClean_Caption_CleanWinSxS"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        // Отдельный явный вопрос про необратимый режим — не часть базового потока
        var useResetBase = MessageBox.Show(
            Localizer.GetString("SystemDeepClean_Confirm_ResetBaseBody"),
            Localizer.GetString("SystemDeepClean_Caption_ResetBase"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        IsBusy = true;
        StatusText = useResetBase == MessageBoxResult.Yes
            ? Localizer.GetString("SystemDeepClean_Status_CleaningDeep")
            : Localizer.GetString("SystemDeepClean_Status_Cleaning");

        try
        {
            var (success, msg) = await _winsxs.RunComponentCleanupAsync(useResetBase == MessageBoxResult.Yes);
            StatusText = msg;
            MessageBox.Show(msg, Localizer.GetString("SystemDeepClean_Caption_CleanWinSxDone"), MessageBoxButton.OK, MessageBoxImage.Information);
            WinSxSReclaimable = Localizer.GetString("SystemDeepClean_WinSxSCleanedValue");
            WinSxSCleanupRecommended = false;
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("Common_Status_Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task CleanSelectedDriversAsync()
    {
        var selected = OldDrivers.Where(d => d.IsSelected).ToList();
        if (selected.Count == 0) return;

        // День 15 — M8: по умолчанию удаление БЕЗ /force — драйверы, используемые
        // системой, pnputil откажется удалять (честный отказ), а не вырывает принудительно.
        var result = MessageBox.Show(
            Localizer.GetString("SystemDeepClean_Confirm_DriversBody", selected.Count),
            Localizer.GetString("SystemDeepClean_Caption_CleanDriverStore"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        StatusText = Localizer.GetString("SystemDeepClean_Status_RemovingDrivers");

        try
        {
            var (removed, errors, removedItems) = await _drivers.RemoveDriversAsync(selected, new Progress<string>(s => StatusText = s));

            // Список покидают только реально удалённые записи; неудалённые остаются видимыми.
            foreach (var item in removedItems)
            {
                OldDrivers.Remove(item);
            }
            OldDriversCount = OldDrivers.Count;

            var totalRemoved = removed;

            if (errors.Count > 0)
            {
                var failed = selected.Except(removedItems).ToList();
                // Осознанное решение о /force: только для записей, от которых отказался pnputil
                // (возможно, используются системой), и только по явному подтверждению пользователя.
                var force = MessageBox.Show(
                    Localizer.GetString("SystemDeepClean_Confirm_ForceBody", errors.Count, errors[0]),
                    Localizer.GetString("SystemDeepClean_Caption_CleanDriverStore"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (force == MessageBoxResult.Yes)
                {
                    var (forceRemoved, forceErrors, forceRemovedItems) = await _drivers.RemoveDriversAsync(
                        failed, new Progress<string>(s => StatusText = s), forceConfirmed: true);

                    totalRemoved += forceRemoved;
                    foreach (var item in forceRemovedItems)
                    {
                        OldDrivers.Remove(item);
                    }
                    OldDriversCount = OldDrivers.Count;
                    errors = forceErrors;
                }
            }

            StatusText = errors.Count == 0
                ? Localizer.GetString("SystemDeepClean_Status_DriversRemovedOk", totalRemoved)
                : Localizer.GetString("SystemDeepClean_Status_DriversRemovedPartial", totalRemoved, errors.Count);
            MessageBox.Show(
                Localizer.GetString("SystemDeepClean_Msg_DriversRemovedSummary", totalRemoved, errors.Count),
                Localizer.GetString("Common_Caption_CleanDone"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = Localizer.GetString("Common_Status_Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
