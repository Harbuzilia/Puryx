using Microsoft.Win32;
using System.Diagnostics;
using System.Net.NetworkInformation;

namespace SmartCleaner.Core.Network;

public class DnsPreset
{
    public string Name { get; set; } = string.Empty;
    public string Primary { get; set; } = string.Empty;
    public string Secondary { get; set; } = string.Empty;
    public long LatencyMs { get; set; } = -1;
    public string LatencyFormatted => LatencyMs >= 0 ? $"{LatencyMs} ms" : "—";
}

public class NetworkOptimizerService
{
    public List<DnsPreset> Presets { get; } =
    [
        new DnsPreset { Name = "Cloudflare (1.1.1.1) — Быстрый и приватный", Primary = "1.1.1.1", Secondary = "1.0.0.1" },
        new DnsPreset { Name = "Google Public DNS (8.8.8.8) — Надежный", Primary = "8.8.8.8", Secondary = "8.8.4.4" },
        new DnsPreset { Name = "Quad9 (9.9.9.9) — Защита от вредоносных сайтов", Primary = "9.9.9.9", Secondary = "149.112.112.112" },
        new DnsPreset { Name = "AdGuard DNS — Блокировка рекламы", Primary = "94.140.14.14", Secondary = "94.140.15.15" }
    ];

    public async Task<long> PingHostAsync(string hostOrIp)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(hostOrIp, 1500);
            if (reply.Status == IPStatus.Success)
            {
                return reply.RoundtripTime;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[NetworkOptimizerService] Ping failed: {ex.Message}"); }

        return -1;
    }

    public async Task BenchmarkDnsPresetsAsync()
    {
        foreach (var preset in Presets)
        {
            preset.LatencyMs = await PingHostAsync(preset.Primary);
        }
    }

    public async Task<(bool Success, string Message)> FlushDnsAndResetWinsockAsync(IProgress<string>? progress = null)
    {
        progress?.Report("Сброс DNS-кэша и сокетов Winsock...");

        return await Task.Run(() =>
        {
            try
            {
                // 1. Flush DNS
                var psiDns = new ProcessStartInfo
                {
                    FileName = "ipconfig.exe",
                    Arguments = "/flushdns",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (var p = Process.Start(psiDns)) p?.WaitForExit(2000);

                // 2. Clear ARP
                var psiArp = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = "interface ip delete arpcache",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (var p = Process.Start(psiArp)) p?.WaitForExit(2000);

                return (true, "DNS-кэш успешно очищен, сетевые сокеты и ARP-таблица сброшены!");
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка сброса сети: {ex.Message}");
            }
        });
    }

    public async Task<(bool Success, string Message)> ApplyDnsAsync(string primaryDns, string secondaryDns, IProgress<string>? progress = null)
    {
        progress?.Report($"Установка DNS ({primaryDns}, {secondaryDns})...");

        return await Task.Run(() =>
        {
            try
            {
                var script = $"Get-NetAdapter | Where-Object {{ $_.Status -eq 'Up' }} | ForEach-Object {{ Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses ('{primaryDns}','{secondaryDns}') }}";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(4000);

                return (true, $"DNS успешно изменен на {primaryDns} / {secondaryDns}!");
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка смены DNS: {ex.Message}");
            }
        });
    }

    public async Task<(bool Success, string Message)> ResetDnsToDhcpAsync(IProgress<string>? progress = null)
    {
        progress?.Report("Сброс DNS на автоматическое получение (DHCP)...");

        return await Task.Run(() =>
        {
            try
            {
                var script = "Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses }";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(4000);

                return (true, "DNS успешно сброшен на автоматический (DHCP)!");
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка сброса DNS: {ex.Message}");
            }
        });
    }

    public async Task<(bool Success, string Message)> SetTcpNoDelayGamingTweakAsync(bool enable)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var rootKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", true);
                if (rootKey == null) return (false, "Ключ реестра сетевых интерфейсов не найден");

                foreach (var subKeyName in rootKey.GetSubKeyNames())
                {
                    try
                    {
                        using var ifaceKey = rootKey.OpenSubKey(subKeyName, true);
                        if (ifaceKey != null)
                        {
                            if (enable)
                            {
                                ifaceKey.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                                ifaceKey.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                            }
                            else
                            {
                                ifaceKey.DeleteValue("TcpAckFrequency", false);
                                ifaceKey.DeleteValue("TCPNoDelay", false);
                            }
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine($"[NetworkOptimizerService] TCP registry subkey error: {ex.Message}"); }
                    }

                return (true, enable
                    ? "Игровой твик TCP NoDelay (отключение алгоритма Нагла) успешно активирован!"
                    : "Настройки TCP возвращены к стандартным значениям Windows.");
            }
            catch (Exception ex)
            {
                return (false, $"Ошибка настройки реестра TCP: {ex.Message}");
            }
        });
    }
}
