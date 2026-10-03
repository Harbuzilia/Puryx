using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartCleaner.Core.Services;

namespace SmartCleaner.Core.Safety;

/// <summary>
/// Хранилище per-install ключа подписи манифеста карантина:
/// 32 байта в Base64, файл <see cref="QuarantineManifestSigner.KeyFileName"/>
/// в ConfigDirectory (IConfigService, portable/installed режимы).
/// </summary>
public sealed class QuarantineSigningKey
{
    public string KeyBase64 { get; set; } = string.Empty;
}

/// <summary>
/// Подпись манифеста карантина (HMAC-SHA256). Образец реализации —
/// <see cref="SmartCleaner.Core.Cleaning.ElevatedCleanRequestFile"/>.
///
/// Схема подписи (v2, зафиксирована этим кодом):
/// 1. manifest.json = { "Items": [...], "Signature": "base64" }.
/// 2. Каноническая форма: QuarantineManifest с Signature = "" сериализуется
///    System.Text.Json с <see cref="CanonicalJsonOptions"/> (без индентации,
///    имена свойств как объявлены в C#).
/// 3. Signature = Base64(HMAC-SHA256(key, canonical_utf8)). Подпись покрывает
///    все поля всех элементов (OriginalPath, StoredPath, ...): любое изменение
///    манифеста после записи инвалидирует подпись.
/// 4. Ключ — 32 случайных байта, per-install: генерируется один раз при первом
///    использовании и хранится в ConfigDirectory через IConfigService
///    (portable/installed режимы). ACL на файл ключа не требуется: модель угроз
///    (findings M6) — манифест изменён БЕЗ доступа к ключу (manifest.json лежит
///    в user-writable %LOCALAPPDATA%); подпись не защищает от атакующего,
///    который может читать и ключ, и манифест.
/// 5. Верификация: десериализовать манифест, вычислить подпись канонической
///    формы (Signature в подпись не входит) и сравнить через
///    <see cref="CryptographicOperations.FixedTimeEquals"/>.
/// </summary>
public static class QuarantineManifestSigner
{
    /// <summary>Имя файла ключа подписи в ConfigDirectory (IConfigService).</summary>
    public const string KeyFileName = "quarantine_signing_key.json";

    private const int KeyBytes = 32;

    /// <summary>Опции канонической сериализации: без индентации, имена как объявлены.</summary>
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

    /// <summary>
    /// Загружает per-install ключ подписи; при отсутствии (или повреждении)
    /// генерирует новый и сохраняет через IConfigService. Манифесты, подписанные
    /// утерянным ключом, станут невалидными — fail-closed в Restore/Purge.
    /// </summary>
    public static byte[] GetOrCreateSigningKey(IConfigService configService)
    {
        var store = configService.Load<QuarantineSigningKey>(KeyFileName);
        if (!string.IsNullOrWhiteSpace(store.KeyBase64))
        {
            try
            {
                var existing = Convert.FromBase64String(store.KeyBase64);
                if (existing.Length == KeyBytes)
                {
                    return existing;
                }
            }
            catch (FormatException)
            {
                // Повреждённый ключ — перегенерируем (см. XML-doc выше).
            }
        }

        var key = RandomNumberGenerator.GetBytes(KeyBytes);
        configService.Save(KeyFileName, new QuarantineSigningKey { KeyBase64 = Convert.ToBase64String(key) });
        return key;
    }

    /// <summary>
    /// Вычисляет Base64(HMAC-SHA256(key, canonical(манифест))).
    /// Поле Signature само в подпись не входит — можно вызывать и на подписанном манифесте.
    /// </summary>
    public static string ComputeSignature(QuarantineManifest manifest, byte[] key)
    {
        var unsigned = new QuarantineManifest { Items = manifest.Items, Signature = string.Empty };
        var canonical = JsonSerializer.SerializeToUtf8Bytes(unsigned, CanonicalJsonOptions);
        using var hmac = new HMACSHA256(key);
        return Convert.ToBase64String(hmac.ComputeHash(canonical));
    }

    /// <summary>Сверяет Signature манифеста с ожидаемой (сравнение в константном времени).</summary>
    public static bool ValidateSignature(QuarantineManifest manifest, byte[] key)
    {
        try
        {
            var expected = ComputeSignature(manifest, key);
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            var actualBytes = Encoding.UTF8.GetBytes(manifest.Signature ?? string.Empty);
            return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
