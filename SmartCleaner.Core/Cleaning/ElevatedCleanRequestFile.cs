using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmartCleaner.Core.Models;

namespace SmartCleaner.Core.Cleaning;

public sealed record ElevatedCleanRequest
{
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; init; } = DateTime.UtcNow.AddMinutes(10);
    public string Nonce { get; init; } = Guid.NewGuid().ToString("N");
    public ElevatedCleanPolicyContract Policy { get; init; } = new();
    public List<ElevatedCleanRequestItem> Items { get; init; } = [];
    public string Signature { get; init; } = string.Empty;
}

public sealed record ElevatedCleanRequestItem
{
    public required string Path { get; init; }
    public required bool IsDirectory { get; init; }
    public long Size { get; init; }
}

public sealed record ElevatedCleanPolicyContract
{
    public string ContractId { get; init; } = string.Empty;
    public List<string> AllowedRoots { get; init; } = [];
}

public sealed record ElevatedCleanLaunchData
{
    public required string RequestFile { get; init; }
    public required string AuthToken { get; init; }
}

public sealed record ElevatedCleanExecutionResult
{
    public int DeletedCount { get; init; }
    public int FailedCount { get; init; }
    public int SkippedCount { get; init; }
    public List<CleaningError> Errors { get; init; } = [];
}

public static class ElevatedCleanRequestFile
{
    private const int SignatureKeyBytes = 32;
    private static readonly string NonceStorePath = Path.Combine(Path.GetTempPath(), "SmartCleaner", "ElevatedNonceStore");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static async Task<ElevatedCleanLaunchData> WriteAsync(
        IEnumerable<ScannedItem> items,
        ElevatedCleanPolicyContract policy,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        var createdAt = DateTime.UtcNow;
        var expiresAt = createdAt.Add(ttl);
        var authToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(SignatureKeyBytes));

        var request = new ElevatedCleanRequest
        {
            CreatedAtUtc = createdAt,
            ExpiresAtUtc = expiresAt,
            Nonce = Guid.NewGuid().ToString("N"),
            Policy = new ElevatedCleanPolicyContract
            {
                ContractId = policy.ContractId,
                AllowedRoots = policy.AllowedRoots.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            },
            Items = items.Select(i => new ElevatedCleanRequestItem
            {
                Path = Path.GetFullPath(i.Path),
                IsDirectory = i.IsDirectory,
                Size = i.Size
            }).ToList()
        };

        var signedRequest = request with { Signature = ComputeSignature(request, authToken) };

        var requestFile = Path.GetTempFileName();
        var payload = JsonSerializer.Serialize(signedRequest, JsonOptions);
        await File.WriteAllTextAsync(requestFile, payload, ct);

        return new ElevatedCleanLaunchData
        {
            RequestFile = requestFile,
            AuthToken = authToken
        };
    }

    public static async Task<ElevatedCleanRequest?> ReadValidatedAsync(
        string requestFile,
        string authToken,
        TimeSpan maxAge,
        CancellationToken ct = default)
    {
        if (!IsSafeRequestPath(requestFile) || !File.Exists(requestFile))
        {
            return null;
        }

        try
        {
            var payload = await File.ReadAllTextAsync(requestFile, ct);
            var request = JsonSerializer.Deserialize<ElevatedCleanRequest>(payload, JsonOptions);
            if (request is null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(request.Signature) ||
                string.IsNullOrWhiteSpace(request.Nonce) ||
                request.Policy is null ||
                request.Items.Count == 0)
            {
                return null;
            }

            var now = DateTime.UtcNow;
            if (now < request.CreatedAtUtc ||
                now - request.CreatedAtUtc > maxAge ||
                now > request.ExpiresAtUtc)
            {
                return null;
            }

            if (!Guid.TryParseExact(request.Nonce, "N", out _))
            {
                return null;
            }

            if (request.Items.Any(i => string.IsNullOrWhiteSpace(i.Path)))
            {
                return null;
            }

            if (!ValidateSignature(request, authToken))
            {
                return null;
            }

            if (!TryConsumeNonce(request.Nonce, request.ExpiresAtUtc))
            {
                return null;
            }

            return request;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsSafeRequestPath(string requestFile)
    {
        if (string.IsNullOrWhiteSpace(requestFile))
        {
            return false;
        }

        try
        {
            var tempRoot = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(requestFile);

            return fullPath.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(Path.GetExtension(fullPath), ".tmp", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void TryDelete(string requestFile)
    {
        try
        {
            if (IsSafeRequestPath(requestFile) && File.Exists(requestFile))
            {
                File.Delete(requestFile);
            }
        }
        catch
        {
        }
    }

    public static string GetResultFilePath(string requestFile)
    {
        return requestFile + ".result.json";
    }

    public static async Task WriteResultAsync(string requestFile, ElevatedCleanExecutionResult result, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(result, JsonOptions);
        await File.WriteAllTextAsync(GetResultFilePath(requestFile), payload, ct);
    }

    public static async Task<ElevatedCleanExecutionResult?> ReadResultAsync(string requestFile, CancellationToken ct = default)
    {
        var resultFile = GetResultFilePath(requestFile);
        if (!File.Exists(resultFile))
        {
            return null;
        }

        try
        {
            var payload = await File.ReadAllTextAsync(resultFile, ct);
            return JsonSerializer.Deserialize<ElevatedCleanExecutionResult>(payload, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void TryDeleteResult(string requestFile)
    {
        try
        {
            var resultFile = GetResultFilePath(requestFile);
            if (IsSafeRequestPath(requestFile) && File.Exists(resultFile))
            {
                File.Delete(resultFile);
            }
        }
        catch
        {
        }
    }

    private static string ComputeSignature(ElevatedCleanRequest request, string authToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(request with { Signature = string.Empty }, JsonOptions);
        var key = Convert.FromBase64String(authToken);

        using var hmac = new HMACSHA256(key);
        var signature = hmac.ComputeHash(payload);
        return Convert.ToBase64String(signature);
    }

    private static bool ValidateSignature(ElevatedCleanRequest request, string authToken)
    {
        try
        {
            var expected = ComputeSignature(request, authToken);
            var actualBytes = Encoding.UTF8.GetBytes(request.Signature);
            var expectedBytes = Encoding.UTF8.GetBytes(expected);
            return CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryConsumeNonce(string nonce, DateTime expiresAtUtc)
    {
        try
        {
            Directory.CreateDirectory(NonceStorePath);
            CleanupExpiredNonceMarkers();

            var nonceFile = Path.Combine(NonceStorePath, nonce + ".nonce");
            using var stream = new FileStream(nonceFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write(expiresAtUtc.ToString("O"));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void CleanupExpiredNonceMarkers()
    {
        var now = DateTime.UtcNow;
        foreach (var file in Directory.EnumerateFiles(NonceStorePath, "*.nonce"))
        {
            try
            {
                var content = File.ReadAllText(file);
                if (DateTime.TryParse(content, out var expiresAt) && expiresAt < now)
                {
                    File.Delete(file);
                }
            }
            catch
            {
            }
        }
    }
}
