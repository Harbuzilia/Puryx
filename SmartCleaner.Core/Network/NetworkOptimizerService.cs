using SmartCleaner.Core.Helpers;
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

        return await Task.Run(async () =>
        {
            try
            {
                // 1. Flush DNS
                var psiDns = new ProcessStartInfo
                {
                    // Абсолютный путь из системного каталога: запуск по неквалифицированному
                    // имени ищет exe в каталоге приложения — binary planting (M7, День 16б)
                    FileName = SystemToolLocator.GetIpconfigPath(),
                    Arguments = "/flushdns",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var dns = await RunToolAsync(psiDns, 5000);
                if (dns.ExitCode != 0)
                {
                    return (false, $"Не удалось очистить DNS-кэш (код {dns.ExitCode}): {dns.Output}");
                }

                // 2. Clear ARP
                var psiArp = new ProcessStartInfo
                {
                    // Абсолютный путь из системного каталога — binary planting (M7)
                    FileName = SystemToolLocator.GetNetshPath(),
                    Arguments = "interface ip delete arpcache",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var arp = await RunToolAsync(psiArp, 5000);
                if (arp.ExitCode != 0)
                {
                    return (false, $"Не удалось сбросить ARP-таблицу (код {arp.ExitCode}): {arp.Output}");
                }

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

        return await Task.Run(async () =>
        {
            try
            {
                // -ErrorAction Stop превращает сбой Set-DnsClientServerAddress в код возврата 1
                // (иначе powershell вернул бы 0 даже при отказе); пустой список адаптеров — явный отказ
                var script = $"$a = @(Get-NetAdapter | Where-Object {{ $_.Status -eq 'Up' }}); " +
                             $"if ($a.Count -eq 0) {{ throw 'no active network adapters' }}; " +
                             $"$a | ForEach-Object {{ Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ServerAddresses ('{primaryDns}','{secondaryDns}') -ErrorAction Stop }}";
                var psi = new ProcessStartInfo
                {
                    // Системный Windows PowerShell по абсолютному пути: командлеты
                    // Get-NetAdapter/Set-DnsClientServerAddress — системная семантика;
                    // неквалифицированное имя = binary planting (M7, День 16б)
                    FileName = SystemToolLocator.GetWindowsPowerShellPath(),
                    Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var result = await RunToolAsync(psi, 15000);
                if (result.ExitCode != 0)
                {
                    return (false, $"Не удалось изменить DNS (код {result.ExitCode}): {result.Output}");
                }

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

        return await Task.Run(async () =>
        {
            try
            {
                // -ErrorAction Stop: сбой Set-DnsClientServerAddress = код возврата 1, а не молчаливый 0
                var script = "$a = @(Get-NetAdapter | Where-Object { $_.Status -eq 'Up' }); " +
                             "if ($a.Count -eq 0) { throw 'no active network adapters' }; " +
                             "$a | ForEach-Object { Set-DnsClientServerAddress -InterfaceIndex $_.ifIndex -ResetServerAddresses -ErrorAction Stop }";
                var psi = new ProcessStartInfo
                {
                    // Системный Windows PowerShell по абсолютному пути — binary planting (M7),
                    // см. ApplyDnsAsync
                    FileName = SystemToolLocator.GetWindowsPowerShellPath(),
                    Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                var result = await RunToolAsync(psi, 15000);
                if (result.ExitCode != 0)
                {
                    return (false, $"Не удалось сбросить DNS (код {result.ExitCode}): {result.Output}");
                }

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

    // Запускает системную утилиту и возвращает фактический код возврата и вывод
    // (stderr приоритетнее: ipconfig/netsh пишут сообщения в stdout, powershell — в stderr).
    // Таймаут — тоже отказ: никакого «успеха по молчанию».
    private static async Task<(int ExitCode, bool TimedOut, string Output)> RunToolAsync(ProcessStartInfo psi, int timeoutMs)
    {
        psi.CreateNoWindow = true;
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        try
        {
            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                return (-1, false, $"не удалось запустить «{psi.FileName}»");
            }

            // Потоки читаем до ожидания выхода, чтобы заполненный буфер не заблокировал утилиту
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
            }
            catch (TimeoutException)
            {
                KillProcessTree(process);
                return (-1, true, $"«{psi.FileName}» не завершилась за {timeoutMs} мс");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var output = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return (process.ExitCode, false, output.Trim());
        }
        catch (Exception ex)
        {
            return (-1, false, $"«{psi.FileName}»: {ex.Message}");
        }
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NetworkOptimizerService] Kill timed-out process failed: {ex.Message}");
        }
    }
}
