using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SmartCleaner.Core.Privacy;
using System.Text.Json;
using Xunit;

namespace SmartCleaner.Core.Tests;

/// <summary>
/// Roundtrip бэкап/восстановление значения реестра PrivacyDebloatService
/// (M4: применить → откатить → исходное значение). Детерминированные unit:
/// шов IRegistryValueStore вместо реестра, файл бэкапа — во временном каталоге.
/// </summary>
public class PrivacyDebloatTests : IDisposable
{
    // HKCU-твик без ServiceName: Apply/Revert трогают только реестр (без sc.exe)
    private const string TweakId = "advertising_id";
    private const string Root = "HKCU";
    private const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo";
    private const string ValueName = "Enabled";

    private readonly FakeRegistryValueStore _registry = new();
    private readonly string _backupPath = Path.Combine(
        Path.GetTempPath(), $"privacy_backup_test_{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_backupPath))
        {
            File.Delete(_backupPath);
        }
    }

    private PrivacyDebloatService CreateService(ILogger<PrivacyDebloatService>? logger = null) =>
        new(_registry, _backupPath, logger);

    [Fact]
    public async Task ApplyThenRevert_RestoresOriginalRegistryValue()
    {
        // Исходное 5 отличается и от DisabledValue (0), и от DefaultValue из кода (1):
        // откат обязан вернуть именно сохранённое исходное значение
        _registry.SetValue(Root, SubKey, ValueName, 5, RegistryValueKind.DWord);
        var service = CreateService();

        Assert.True(await service.ApplyTweakAsync(TweakId));
        var applied = _registry.GetValue(Root, SubKey, ValueName);
        Assert.NotNull(applied);
        Assert.IsType<int>(applied.Value);
        Assert.Equal(0, (int)applied.Value); // применено: DisabledValue

        Assert.True(await service.RevertTweakAsync(TweakId));
        var restored = _registry.GetValue(Root, SubKey, ValueName);
        Assert.NotNull(restored);
        Assert.IsType<int>(restored.Value);
        Assert.Equal(5, (int)restored.Value); // откат: исходное, а не DefaultValue из кода
        Assert.Equal(RegistryValueKind.DWord, restored.Kind);
    }

    [Fact]
    public async Task Revert_RestoresAbsentValue_ByDeletingIt()
    {
        // Значения до изменения не было: откат обязан вернуть «отсутствовало»
        // (DeleteValue), а не ставить DefaultValue из кода
        var service = CreateService();
        Assert.Null(_registry.GetValue(Root, SubKey, ValueName));

        Assert.True(await service.ApplyTweakAsync(TweakId));
        Assert.NotNull(_registry.GetValue(Root, SubKey, ValueName)); // apply создал значение

        Assert.True(await service.RevertTweakAsync(TweakId));
        Assert.Null(_registry.GetValue(Root, SubKey, ValueName)); // «отсутствовало» восстановлено
    }

    [Fact]
    public async Task Backup_SavesActualValueBeforeChange_NotCodeDefault()
    {
        // Бэкап обязан хранить фактическое значение до изменения (5),
        // а не константу DefaultValue из кода (1)
        _registry.SetValue(Root, SubKey, ValueName, 5, RegistryValueKind.DWord);
        var service = CreateService();

        Assert.True(await service.ApplyTweakAsync(TweakId));

        Assert.True(File.Exists(_backupPath));
        using var doc = JsonDocument.Parse(File.ReadAllText(_backupPath));
        var entry = doc.RootElement.GetProperty(TweakId);
        Assert.Equal(JsonValueKind.Object, entry.ValueKind); // новая схема, не legacy-строка
        Assert.True(entry.GetProperty("Existed").GetBoolean());
        Assert.Equal("5", entry.GetProperty("Value").GetString());
        Assert.Equal("DWord", entry.GetProperty("Kind").GetString());
    }

    [Fact]
    public async Task Revert_IgnoresLegacyBackupConstant_WithHonestMessage()
    {
        // Бэкап старой схемы (до M4) хранит константу, а не фактическое
        // значение. Политика: записи не доверять как исходной — откат ставит
        // DefaultValue из кода и честно сообщает об этом в лог
        _registry.SetValue(Root, SubKey, ValueName, 0, RegistryValueKind.DWord); // применённое состояние
        File.WriteAllText(_backupPath, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            [TweakId] = "9" // legacy-константа: не DefaultValue (1) и не DisabledValue (0)
        }));
        var logger = new RecordingLogger();
        var service = CreateService(logger);

        Assert.True(await service.RevertTweakAsync(TweakId));

        var restored = _registry.GetValue(Root, SubKey, ValueName);
        Assert.NotNull(restored);
        Assert.IsType<int>(restored.Value);
        Assert.Equal(1, (int)restored.Value); // DefaultValue из кода, а не legacy-константа «9»
        Assert.Contains(logger.Messages, m => m.Contains("старой схемы", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Revert_WithoutBackupEntry_UsesCodeDefault()
    {
        // Файла бэкапа нет — прежнее поведение отката: DefaultValue из кода
        _registry.SetValue(Root, SubKey, ValueName, 0, RegistryValueKind.DWord);
        var service = CreateService();

        Assert.True(await service.RevertTweakAsync(TweakId));

        var restored = _registry.GetValue(Root, SubKey, ValueName);
        Assert.NotNull(restored);
        Assert.IsType<int>(restored.Value);
        Assert.Equal(1, (int)restored.Value);
    }

    [Fact]
    public async Task Backup_IsNotOverwrittenBySubsequentApplies()
    {
        // Первый бэкап побеждает: повторный apply не должен затирать сохранённое
        // исходное значение бэкапом DisabledValue (иначе откат вернёт применённое)
        _registry.SetValue(Root, SubKey, ValueName, 7, RegistryValueKind.DWord);
        var service = CreateService();

        Assert.True(await service.ApplyTweakAsync(TweakId));
        Assert.True(await service.ApplyTweakAsync(TweakId));

        Assert.True(await service.RevertTweakAsync(TweakId));
        var restored = _registry.GetValue(Root, SubKey, ValueName);
        Assert.NotNull(restored);
        Assert.IsType<int>(restored.Value);
        Assert.Equal(7, (int)restored.Value);
    }

    /// <summary>
    /// Детерминированное хранилище значений реестра в памяти
    /// (ключ = root|subKey|valueName).
    /// </summary>
    private sealed class FakeRegistryValueStore : IRegistryValueStore
    {
        private readonly Dictionary<string, RegistryValueSnapshot> _values = new(StringComparer.Ordinal);

        private static string Key(string rootKeyName, string subKeyPath, string valueName) =>
            $"{rootKeyName}|{subKeyPath}|{valueName}";

        public RegistryValueSnapshot? GetValue(string rootKeyName, string subKeyPath, string valueName) =>
            _values.TryGetValue(Key(rootKeyName, subKeyPath, valueName), out var snapshot) ? snapshot : null;

        public void SetValue(string rootKeyName, string subKeyPath, string valueName, object value, RegistryValueKind kind) =>
            _values[Key(rootKeyName, subKeyPath, valueName)] = new RegistryValueSnapshot(value, kind);

        public void DeleteValue(string rootKeyName, string subKeyPath, string valueName) =>
            _values.Remove(Key(rootKeyName, subKeyPath, valueName));
    }

    /// <summary>Логгер, запоминающий отформатированные сообщения.</summary>
    private sealed class RecordingLogger : ILogger<PrivacyDebloatService>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
