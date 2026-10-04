using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private string _statusText = "Нажмите «Выполнить аудит системы» для проверки WinSxS и старых драйверов";

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
        StatusText = "Анализ хранилища компонентов WinSxS и DriverStore...";

        try
        {
            var sxsProgress = new Progress<string>(s => StatusText = s);
            var sxsResult = await _winsxs.AnalyzeComponentStoreAsync(sxsProgress);
            _lastWinSxSAnalysis = sxsResult;

            WinSxSActualSize = sxsResult.ActualSizeFormatted;
            WinSxSReclaimable = sxsResult.ReclaimablePackagesFormatted;
            WinSxSCleanupRecommended = sxsResult.CleanupRecommended;

            StatusText = "Поиск устаревших дубликатов драйверов...";
            var drvProgress = new Progress<string>(s => StatusText = s);
            var driversList = await _drivers.ScanDriversAsync(drvProgress);

            OldDrivers.Clear();
            foreach (var d in driversList.Where(x => x.IsOldDuplicate))
            {
                OldDrivers.Add(d);
            }
            OldDriversCount = OldDrivers.Count;

            var winSxSSummary = sxsResult.AnalysisAvailable
                ? $"В WinSxS можно освободить {WinSxSReclaimable}"
                : $"анализ WinSxS недоступен: {sxsResult.AnalysisUnavailableReason}";
            StatusText = $"Аудит завершен! {winSxSSummary}. Найдено {OldDriversCount} старых драйверов.";
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка аудита: {ex.Message}";
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
            ? $"• По последнему анализу DISM можно освободить до {_lastWinSxSAnalysis.ReclaimablePackagesFormatted}."
            : "• Объем освобождения заранее неизвестен (анализ DISM не выполнен или недоступен); фактический результат покажет только очистка.";

        // День 21, L5: /ResetBase (необратимо) — только явный opt-in. Базовое
        // подтверждение описывает обратимую очистку без /ResetBase
        var result = MessageBox.Show(
            "Запустить консолидацию и очистку хранилища компонентов Windows (WinSxS /StartComponentCleanup)?\n\n" +
            "• Будут удалены устаревшие версии компонентов; установленные обновления останутся удаляемыми.\n" +
            reclaimLine + "\n" +
            "• Процесс может занять 3-10 минут.",
            "Очистка WinSxS",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        // Отдельный явный вопрос про необратимый режим — не часть базового потока
        var useResetBase = MessageBox.Show(
            "Дополнительно применить /ResetBase (НЕОБРАТИМО)?\n\n" +
            "• Все superseded-компоненты обновлений будут удалены окончательно.\n" +
            "• После /ResetBase установленные обновления Windows НЕЛЬЗЯ будет удалить — только полная переустановка Windows.\n" +
            "• «Нет» — обычная обратимая очистка (рекомендуется).",
            "Необратимая операция /ResetBase",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        IsBusy = true;
        StatusText = useResetBase == MessageBoxResult.Yes
            ? "Выполняется глубокая очистка WinSxS (DISM + /ResetBase)... Пожалуйста, подождите."
            : "Выполняется очистка WinSxS (DISM)... Пожалуйста, подождите.";

        try
        {
            var (success, msg) = await _winsxs.RunComponentCleanupAsync(useResetBase == MessageBoxResult.Yes);
            StatusText = msg;
            MessageBox.Show(msg, "Очистка WinSxS завершена", MessageBoxButton.OK, MessageBoxImage.Information);
            WinSxSReclaimable = "0 B (Очищено)";
            WinSxSCleanupRecommended = false;
        }
        catch (Exception ex)
        {
            StatusText = $"Исключение: {ex.Message}";
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
            $"Удалить {selected.Count} устаревших дубликатов драйверов из DriverStore через PnPUtil?\n\n" +
            "• Удаление выполняется без ключа /force: драйверы, которые используются системой, удалены НЕ будут.\n" +
            "• Принудительное удаление (/force) — только по отдельному подтверждению, если что-то не удалится.",
            "Очистка DriverStore",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsBusy = true;
        StatusText = "Удаление устаревших драйверов...";

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
                    $"Не удалено пакетов: {errors.Count}.\n{errors[0]}\n\n" +
                    "Windows может использовать эти драйверы прямо сейчас.\n" +
                    "Принудительно удалить их с ключом /force? Это может нарушить работу устройств.",
                    "Очистка DriverStore",
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
                ? $"Успешно удалено {totalRemoved} устаревших пакетов драйверов!"
                : $"Удалено {totalRemoved}; не удалено: {errors.Count} (драйверы могут использоваться системой).";
            MessageBox.Show(
                $"Удалено драйверов: {totalRemoved}. Не удалено: {errors.Count}.",
                "Очистка завершена",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
