using Microsoft.Extensions.Logging;
using SmartCleaner.App.Helpers;
using SmartCleaner.Core.Localization;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Services;
using SmartCleaner.Core.Update;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace SmartCleaner.App.Services;

/// <summary>
/// Трей-режим Puryx (ROADMAP v3.1, День 13, фаза A): NotifyIcon с иконкой
/// приложения, контекстное меню «Открыть»/«Выход», двойной клик — показать окно.
/// Поведение Minimize намеренно однозначное: настройка
/// <see cref="TraySettingsConfig.MinimizeToTray"/> управляет ТОЛЬКО кнопкой
/// «свернуть» (окно уходит в трей); закрытие окна (X) всегда завершает
/// приложение — стандартное поведение, минимум сюрпризов (docs/TRAY-MODE.md).
/// Фоновая авто-проверка обновлений (opt-in, <see cref="TraySettingsConfig.AutoCheckUpdates"/>)
/// идёт не чаще раза в сутки по расписанию <see cref="AutoUpdateSchedule"/>,
/// напоминает балуном и НЕ скачивает обновление само.
/// UI-часть покрывается смоук-запуском: юнит-тесты — только чистое расписание
/// авто-проверки (<see cref="AutoUpdateSchedule"/>).
/// </summary>
public sealed class TrayService : IDisposable
{
    /// <summary>Время показа балуна (Windows 10+ использует системную политику).</summary>
    private const int BalloonTimeoutMilliseconds = 10_000;

    private readonly IConfigService _configService;
    private readonly IUpdateChecker _updateChecker;
    private readonly ILogger<TrayService> _logger;
    private readonly CancellationTokenSource _disposeCts = new();

    private Window? _mainWindow;
    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ContextMenuStrip? _contextMenu;
    private DispatcherTimer? _autoCheckTimer;
    private Icon? _icon;
    private bool _minimizeToTray;
    private bool _autoCheckUpdates;
    private bool _disposed;

    public TrayService(
        IConfigService configService,
        IUpdateChecker updateChecker,
        ILogger<TrayService> logger)
    {
        _configService = configService;
        _updateChecker = updateChecker;
        _logger = logger;
    }

    /// <summary>Инициализирован ли трей (иконка создана).</summary>
    public bool IsInitialized => _notifyIcon is not null;

    /// <summary>
    /// Создать иконку в области уведомлений и подписаться на состояние главного
    /// окна. Идемпотентно: повторный вызов игнорируется.
    /// </summary>
    public void Initialize(Window mainWindow)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);

        if (_notifyIcon is not null)
        {
            return;
        }

        _mainWindow = mainWindow;
        var settings = _configService.Load<TraySettingsConfig>(TraySettingsConfig.Filename);
        _minimizeToTray = settings.MinimizeToTray;
        _autoCheckUpdates = settings.AutoCheckUpdates;

        _icon = LoadAppIcon();
        _contextMenu = BuildContextMenu();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = Localizer.GetString("Tray_Tooltip", AppInfo.Version),
            ContextMenuStrip = _contextMenu,
            Visible = true
        };
        _notifyIcon.DoubleClick += OnTrayDoubleClick;
        _notifyIcon.BalloonTipClicked += OnBalloonTipClicked;

        mainWindow.StateChanged += OnMainWindowStateChanged;

        ScheduleAutoCheck();
    }

    /// <summary>
    /// Применить опции из настроек без перезапуска: Minimize-поведение
    /// переключается на лету (чтение config при каждом событии не нужно),
    /// авто-проверка обновлений взводится/снимается сразу.
    /// </summary>
    public void ApplyOptions(bool minimizeToTray, bool autoCheckUpdates)
    {
        _minimizeToTray = minimizeToTray;

        if (_autoCheckUpdates == autoCheckUpdates)
        {
            return;
        }

        _autoCheckUpdates = autoCheckUpdates;
        ScheduleAutoCheck();
    }

    /// <summary>
    /// Показать балун-уведомление; клик по нему открывает страницу настроек
    /// (там кнопка установки обновления — авто-скачивания нет).
    /// </summary>
    public void ShowNotification(string title, string text)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(BalloonTimeoutMilliseconds);
    }

    /// <summary>Показать главное окно из трея (восстановить из Minimized, поднять наверх).</summary>
    public void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        _mainWindow.Activate();
    }

    /// <summary>Показать окно на странице настроек (клик по балуну обновления).</summary>
    public void ShowSettingsPage()
    {
        ShowMainWindow();

        if (_mainWindow is MainWindow mainWindow)
        {
            mainWindow.ShowSettingsPage();
        }
    }

    /// <summary>
    /// Взвести таймер следующей авто-проверки: due → 30 с после старта, иначе
    /// остаток суток (перезапуски не сбрасывают ритм). Таймер один: повторный
    /// вызов лишь пересчитывает интервал.
    /// </summary>
    private void ScheduleAutoCheck()
    {
        if (_disposed || _notifyIcon is null)
        {
            return;
        }

        if (_autoCheckTimer is null)
        {
            _autoCheckTimer = new DispatcherTimer();
            _autoCheckTimer.Tick += OnAutoCheckTick;
        }

        _autoCheckTimer.Stop();

        if (!_autoCheckUpdates)
        {
            return;
        }

        var state = _configService.Load<UpdateStateConfig>(UpdateStateConfig.Filename);
        _autoCheckTimer.Interval = AutoUpdateSchedule.InitialDelay(
            state.LastAutoCheckUtc,
            DateTimeOffset.UtcNow,
            AutoUpdateSchedule.DefaultInterval,
            AutoUpdateSchedule.FirstCheckDelay);
        _autoCheckTimer.Start();
    }

    private async void OnAutoCheckTick(object? sender, EventArgs e)
    {
        _autoCheckTimer?.Stop();

        try
        {
            await RunAutoCheckAsync();
        }
        catch (OperationCanceledException)
        {
            // Приложение закрывается — тихо выходим
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Фоновая проверка обновлений завершилась ошибкой.");
        }
        finally
        {
            ScheduleAutoCheck();
        }
    }

    /// <summary>
    /// Суточная проверка: отметка «последняя проверка» сохраняется ДО сетевого
    /// запроса — сбой сети не даёт шторма запросов при перезапусках. Успех с
    /// новой версией → балун; скачивание/установка — только вручную из Settings.
    /// </summary>
    private async Task RunAutoCheckAsync()
    {
        if (!_autoCheckUpdates)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var state = _configService.Load<UpdateStateConfig>(UpdateStateConfig.Filename);
        if (!AutoUpdateSchedule.ShouldCheck(state.LastAutoCheckUtc, now, AutoUpdateSchedule.DefaultInterval))
        {
            return;
        }

        state.LastAutoCheckUtc = now;
        _configService.Save(UpdateStateConfig.Filename, state);

        _logger.LogInformation("Фоновая проверка обновлений (не чаще раза в сутки).");

        var result = await _updateChecker.CheckAsync(_disposeCts.Token);
        if (!result.HasUpdate)
        {
            return;
        }

        _logger.LogInformation("Доступно обновление {Version}.", result.LatestVersion);
        ShowNotification(
            Localizer.GetString("Tray_UpdateAvailableTitle"),
            Localizer.GetString("Tray_UpdateAvailableText", result.LatestVersion));
    }

    private void OnMainWindowStateChanged(object? sender, EventArgs e)
    {
        if (!_minimizeToTray || _mainWindow is null)
        {
            return;
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.Hide();
        }
    }

    private void OnTrayDoubleClick(object? sender, EventArgs e) => ShowMainWindow();

    private void OnBalloonTipClicked(object? sender, EventArgs e) => ShowSettingsPage();

    private Forms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new Forms.ContextMenuStrip();

        var openItem = new Forms.ToolStripMenuItem(Localizer.GetString("Tray_Open"));
        openItem.Click += (_, _) => ShowMainWindow();

        var exitItem = new Forms.ToolStripMenuItem(Localizer.GetString("Tray_Exit"));
        exitItem.Click += (_, _) => ExitApplication();

        menu.Items.Add(openItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        return menu;
    }

    /// <summary>«Выход» из трея — полное завершение приложения (не сворачивание).</summary>
    private void ExitApplication()
    {
        _logger.LogInformation("Выход из приложения через меню трея.");
        Application.Current?.Shutdown();
    }

    /// <summary>
    /// Иконка приложения: app.ico рядом с exe (копируется сборкой) → иконка
    /// исполняемого файла → системная заглушка. Трей не должен ронять старт.
    /// </summary>
    private Icon LoadAppIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                return new Icon(iconPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось загрузить {IconPath}, используется иконка exe.", iconPath);
            }
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath))
        {
            try
            {
                var extracted = Icon.ExtractAssociatedIcon(processPath);
                if (extracted is not null)
                {
                    return extracted;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось извлечь иконку из {ProcessPath}.", processPath);
            }
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _disposeCts.Cancel();

        if (_autoCheckTimer is not null)
        {
            _autoCheckTimer.Stop();
            _autoCheckTimer.Tick -= OnAutoCheckTick;
            _autoCheckTimer = null;
        }

        if (_mainWindow is not null)
        {
            _mainWindow.StateChanged -= OnMainWindowStateChanged;
            _mainWindow = null;
        }

        if (_notifyIcon is not null)
        {
            _notifyIcon.DoubleClick -= OnTrayDoubleClick;
            _notifyIcon.BalloonTipClicked -= OnBalloonTipClicked;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _contextMenu?.Dispose();
        _contextMenu = null;

        _icon?.Dispose();
        _icon = null;

        _disposeCts.Dispose();
    }
}