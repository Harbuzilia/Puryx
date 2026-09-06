using Microsoft.Win32;
using SmartCleaner.Core.Models;
using SmartCleaner.Core.Services;
using System.IO;
using System.Windows;

namespace SmartCleaner.App.Services;

public enum AppTheme
{
    System,
    Dark,
    Light
}

public class ThemeManager
{
    private const string SettingsFilename = "ui_settings.json";
    private readonly IConfigService _configService;
    private AppTheme _currentTheme = AppTheme.System;

    public AppTheme CurrentTheme
    {
        get => _currentTheme;
        set
        {
            if (_currentTheme != value)
            {
                _currentTheme = value;
                ApplyTheme(value);
                SaveTheme(value);
                ThemeChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<AppTheme>? ThemeChanged;

    public ThemeManager(IConfigService configService)
    {
        _configService = configService;
    }

    public void Initialize()
    {
        var settings = _configService.Load<UiSettingsConfig>(SettingsFilename);
        if (Enum.TryParse<AppTheme>(settings.Theme, true, out var theme))
        {
            _currentTheme = theme;
        }
        else
        {
            _currentTheme = AppTheme.System;
        }

        ApplyTheme(_currentTheme);
    }

    public void ApplyTheme(AppTheme theme)
    {
        bool isDark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => IsSystemDarkTheme()
        };

        var themeUri = new Uri(isDark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml", UriKind.Relative);

        var app = Application.Current;
        if (app == null) return;

        var existingThemeDict = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source != null && d.Source.OriginalString.Contains("Theme.xaml"));

        var newDict = new ResourceDictionary { Source = themeUri };

        if (existingThemeDict != null)
        {
            var index = app.Resources.MergedDictionaries.IndexOf(existingThemeDict);
            app.Resources.MergedDictionaries[index] = newDict;
        }
        else
        {
            app.Resources.MergedDictionaries.Add(newDict);
        }
    }

    public void ToggleTheme()
    {
        bool isDark = _currentTheme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => IsSystemDarkTheme()
        };

        CurrentTheme = isDark ? AppTheme.Light : AppTheme.Dark;
    }

    private void SaveTheme(AppTheme theme)
    {
        _configService.Save(SettingsFilename, new UiSettingsConfig
        {
            Theme = theme.ToString()
        });
    }

    public static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            if (key != null)
            {
                var value = key.GetValue("AppsUseLightTheme");
                if (value is int intValue)
                {
                    return intValue == 0;
                }
            }
        }
        catch
        {
        }

        return true;
    }
}
