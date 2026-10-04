using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SmartCleaner.Core.Helpers;

namespace SmartCleaner.Core.CliInspector;

public class CliInspectorEngine
{
    private static readonly string UserHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string RoamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    private readonly PathEnvironmentService _pathService;

    public CliInspectorEngine(PathEnvironmentService pathService)
    {
        _pathService = pathService;
    }

    private static readonly Dictionary<string, (string Name, string Desc, string Cat, string Uninstall)> KnownSignatures = new(StringComparer.OrdinalIgnoreCase)
    {
        [".opencode"] = ("OpenCode", "OpenCode AI Assistant CLI", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.opencode\""),
        [".orca"] = ("Orca", "Orca CLI / Agent Environment", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.orca\""),
        [".hermes"] = ("Hermes Agent", "Hermes AI Assistant CLI / Agent", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.hermes\""),
        [".omp"] = ("Oh My Posh (Config)", "Oh My Posh Prompt Engine Themes/Config", "CLI Tools", "winget uninstall JanDeDobbeleer.OhMyPosh"),
        [".claude"] = ("Claude CLI / Code", "Anthropic Claude CLI config & data", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.claude\""),
        [".copilot"] = ("GitHub Copilot CLI", "Copilot CLI cache & tools", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.copilot\""),
        [".gemini"] = ("Gemini / Antigravity CLI", "Google Gemini / Antigravity assistant data", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.gemini\""),
        [".kimi-code"] = ("Kimi Code CLI", "Kimi Code AI CLI tool", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.kimi-code\""),
        [".openclaude"] = ("OpenClaude CLI", "OpenClaude tool", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.openclaude\""),
        [".agent-browser"] = ("Agent Browser", "Headless agent browser tool", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.agent-browser\""),
        [".agents"] = ("Agents Suite", "AI Agents directory", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.agents\""),
        [".audiopolish"] = ("AudioPolish CLI", "Audio processing CLI", "CLI Tools", "rmdir /s /q \"%USERPROFILE%\\.audiopolish\""),
        [".bun"] = ("Bun JS Runtime", "Bun JavaScript toolkit & binary cache", "Runtimes", "rmdir /s /q \"%USERPROFILE%\\.bun\""),
        [".codex"] = ("Codex CLI", "Codex AI assistant files", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.codex\""),
        [".commandcode"] = ("CommandCode", "CLI tool", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.commandcode\""),
        [".dsh"] = ("DSH Shell CLI", "DSH developer shell utility", "CLI Tools", "rmdir /s /q \"%USERPROFILE%\\.dsh\""),
        [".factory"] = ("Factory CLI", "Factory developer agent / tool", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.factory\""),
        [".grok"] = ("Grok CLI", "Grok AI CLI configuration", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.grok\""),
        [".paseo"] = ("Paseo CLI", "Paseo developer tool", "CLI Tools", "rmdir /s /q \"%USERPROFILE%\\.paseo\""),
        [".pi"] = ("Pi CLI", "Inflection Pi CLI / agent helper", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.pi\""),
        [".zcode"] = ("ZCode CLI", "ZCode assistant utility", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.zcode\""),
        [".antigravity_tools"] = ("Antigravity Tools", "Antigravity developer extensions & tools", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.antigravity_tools\""),
        [".antigravity_cockpit"] = ("Antigravity Cockpit", "Antigravity mission cockpit data", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.antigravity_cockpit\""),
        [".torexxx-ide"] = ("Torexxx IDE", "IDE data / configuration", "Developer Tools", "rmdir /s /q \"%USERPROFILE%\\.torexxx-ide\""),
        [".cli-proxy-api"] = ("CLI Proxy API", "Proxy API utility for CLI tools", "CLI Tools", "rmdir /s /q \"%USERPROFILE%\\.cli-proxy-api\""),
        [".cline"] = ("Cline CLI", "Autonomous coding agent CLI", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.cline\""),
        [".roo-cline"] = ("Roo Cline", "Roo Cline coding assistant", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.roo-cline\""),
        [".continue"] = ("Continue Dev", "Continue open-source AI assistant data", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.continue\""),
        [".aider"] = ("Aider Chat", "Aider AI pair programming data", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.aider\""),
        [".v0"] = ("V0 Dev", "V0 CLI dev data", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.v0\""),
        [".boltai"] = ("Bolt AI", "Bolt agent workspace", "AI & Agents", "rmdir /s /q \"%USERPROFILE%\\.boltai\"")
    };

    private static readonly HashSet<string> StandardIgnore = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cache", ".config", ".docker", ".nuget", ".vscode", ".ssh", ".git", ".gradle", ".m2", ".idea"
    };

    public async Task<CliInspectorScanResult> ScanAsync(CancellationToken ct = default)
    {
        var allTools = new List<CliToolItem>();
        var pathHealth = new List<PathHealthItem>();
        var psProfiles = new List<PowerShellProfileItem>();

        var taskDots = Task.Run(() => ScanDotFoldersAndUserTools(ct), ct);
        var taskNpm = Task.Run(() => ScanNpmGlobals(ct), ct);
        var taskPip = Task.Run(() => ScanPipPackages(ct), ct);
        var taskPkg = Task.Run(() => ScanPackageManagers(ct), ct);
        var taskPath = Task.Run(() => ScanPathEnvironmentAndBinaries(ct), ct);
        var taskPs = Task.Run(() => ScanPowerShellProfilesAndModules(ct), ct);

        await Task.WhenAll(taskDots, taskNpm, taskPip, taskPkg, taskPath, taskPs);

        // Все задачи уже завершены (WhenAll выше) — await читает результаты
        // без блокировки; прямой .Result запрещён стилем проекта (L1).
        allTools.AddRange(await taskDots);
        allTools.AddRange(await taskNpm);
        allTools.AddRange(await taskPip);
        allTools.AddRange(await taskPkg);

        var (pathBins, health) = await taskPath;
        allTools.AddRange(pathBins);
        pathHealth.AddRange(health);
        psProfiles.AddRange(await taskPs);

        // Deduplicate tools by path
        var uniqueTools = new List<CliToolItem>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in allTools)
        {
            if (string.IsNullOrWhiteSpace(tool.Path)) continue;
            var p = Path.GetFullPath(tool.Path);
            if (seenPaths.Add(p))
            {
                uniqueTools.Add(tool);
            }
        }

        uniqueTools.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));

        var totalBytes = uniqueTools.Sum(t => t.SizeBytes);
        var categories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in uniqueTools)
        {
            var cat = string.IsNullOrWhiteSpace(tool.Category) ? "Other" : tool.Category;
            categories[cat] = categories.GetValueOrDefault(cat, 0) + 1;
        }

        var deadCount = pathHealth.Count(p => p.IsDead);

        return new CliInspectorScanResult
        {
            Timestamp = DateTime.Now,
            TotalToolsCount = uniqueTools.Count,
            TotalSizeBytes = totalBytes,
            TotalSizeFormatted = SizeFormatter.Format(totalBytes),
            DeadPathsCount = deadCount,
            TotalPathsScanned = pathHealth.Count,
            PsProfilesCount = psProfiles.Count,
            CategoriesCount = categories,
            Tools = uniqueTools,
            PathHealth = pathHealth,
            PowerShellProfiles = psProfiles
        };
    }

    private List<CliToolItem> ScanDotFoldersAndUserTools(CancellationToken ct)
    {
        var tools = new List<CliToolItem>();

        // 1. Scan User Home Dot-directories
        try
        {
            if (Directory.Exists(UserHome))
            {
                foreach (var dir in Directory.EnumerateDirectories(UserHome))
                {
                    ct.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(dir);
                    if (!name.StartsWith('.')) continue;

                    var isMatched = KnownSignatures.TryGetValue(name, out var sig);
                    if (isMatched || !StandardIgnore.Contains(name))
                    {
                        var (size, mtime, count) = GetDirSizeAndMTime(dir, maxDepth: 3);
                        var toolName = isMatched ? sig.Name : name[1..] + " Config/Tool";
                        var toolDesc = isMatched ? sig.Desc : $"Пользовательская директория: {name}";
                        var toolCat = isMatched ? sig.Cat : "User Dotfolder / Tool";
                        var uninstall = isMatched ? sig.Uninstall : $"rmdir /s /q \"{dir}\"";

                        tools.Add(new CliToolItem
                        {
                            Id = $"dotfolder_{name}",
                            Name = toolName,
                            FolderName = name,
                            Category = toolCat,
                            Description = toolDesc,
                            Path = dir,
                            SizeBytes = size,
                            SizeFormatted = SizeFormatter.Format(size),
                            FileCount = count,
                            LastModified = mtime?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown",
                            MTime = mtime,
                            UninstallCmd = uninstall,
                            Source = "User Home (~/.)",
                            ToolType = "folder"
                        });
                    }
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[CliInspectorEngine] UserHome scan error: {ex.Message}"); }

        // 2. Scan Custom Program Directories
        var programFolders = new[]
        {
            Path.Combine(LocalAppData, "Programs"),
            Path.Combine(LocalAppData, "hermes"),
            Path.Combine(LocalAppData, "omp"),
            Path.Combine(UserHome, "bin"),
            Path.Combine(UserHome, ".local", "bin"),
            Path.Combine(UserHome, "orca"),
            Path.Combine(UserHome, ".dotnet", "tools"),
            Path.Combine(UserHome, "go", "bin"),
            Path.Combine(UserHome, ".cargo", "bin"),
            Path.Combine(UserHome, ".bun", "bin")
        };

        foreach (var pdir in programFolders)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(pdir)) continue;

            var dirName = Path.GetFileName(pdir);
            var parentName = Path.GetFileName(Path.GetDirectoryName(pdir)) ?? "";

            if (dirName is "bin" or "tools")
            {
                var (size, mtime, count) = GetDirSizeAndMTime(pdir, maxDepth: 2);
                var exes = GetExecutablesInDir(pdir);
                var exeListStr = exes.Count > 0 ? string.Join(", ", exes.Take(6)) + (exes.Count > 6 ? "..." : "") : "Пользовательская директория утилит";

                tools.Add(new CliToolItem
                {
                    Id = $"bin_dir_{parentName}_{dirName}",
                    Name = $"User Binaries ({parentName}/{dirName})",
                    FolderName = pdir,
                    Category = "CLI Binaries",
                    Description = $"Исполняемые файлы: {exeListStr}",
                    Path = pdir,
                    SizeBytes = size,
                    SizeFormatted = SizeFormatter.Format(size),
                    FileCount = count,
                    LastModified = mtime?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown",
                    MTime = mtime,
                    UninstallCmd = $"Remove-Item -Recurse -Force \"{pdir}\"",
                    Source = "User PATH / Bin",
                    ToolType = "folder",
                    Executables = exes
                });
            }
            else if (dirName == "Programs")
            {
                try
                {
                    foreach (var sub in Directory.EnumerateDirectories(pdir))
                    {
                        var subName = Path.GetFileName(sub);
                        var (size, mtime, count) = GetDirSizeAndMTime(sub, maxDepth: 3);
                        tools.Add(new CliToolItem
                        {
                            Id = $"appdata_prog_{subName}",
                            Name = subName,
                            FolderName = subName,
                            Category = "User Program / AppData",
                            Description = $"Приложение в AppData/Local/Programs/{subName}",
                            Path = sub,
                            SizeBytes = size,
                            SizeFormatted = SizeFormatter.Format(size),
                            FileCount = count,
                            LastModified = mtime?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown",
                            MTime = mtime,
                            UninstallCmd = $"rmdir /s /q \"{sub}\"",
                            Source = "AppData Programs",
                            ToolType = "folder"
                        });
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[CliInspectorEngine] Programs subdirs enumeration error: {ex.Message}"); }
            }
            else
            {
                var (size, mtime, count) = GetDirSizeAndMTime(pdir, maxDepth: 3);
                tools.Add(new CliToolItem
                {
                    Id = $"appdata_custom_{dirName}",
                    Name = dirName,
                    FolderName = dirName,
                    Category = "CLI Tools & Agents",
                    Description = $"Инструмент в {pdir}",
                    Path = pdir,
                    SizeBytes = size,
                    SizeFormatted = SizeFormatter.Format(size),
                    FileCount = count,
                    LastModified = mtime?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown",
                    MTime = mtime,
                    UninstallCmd = $"rmdir /s /q \"{pdir}\"",
                    Source = "AppData / Local",
                    ToolType = "folder"
                });
            }
        }

        return tools;
    }

    private List<CliToolItem> ScanNpmGlobals(CancellationToken ct)
    {
        var tools = new List<CliToolItem>();
        var npmNodeModules = Path.Combine(RoamingAppData, "npm", "node_modules");

        if (Directory.Exists(npmNodeModules))
        {
            try
            {
                foreach (var item in Directory.EnumerateDirectories(npmNodeModules))
                {
                    ct.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(item);
                    if (name.StartsWith('.')) continue;

                    var pkgJson = Path.Combine(item, "package.json");
                    var version = "";
                    var desc = "Глобальный NPM пакет";
                    var homepage = "";
                    var binInfo = "";

                    if (File.Exists(pkgJson))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(File.ReadAllText(pkgJson));
                            var root = doc.RootElement;
                            if (root.TryGetProperty("version", out var vProp)) version = vProp.GetString() ?? "";
                            if (root.TryGetProperty("description", out var dProp)) desc = dProp.GetString() ?? desc;
                            if (root.TryGetProperty("homepage", out var hProp)) homepage = hProp.GetString() ?? "";
                            if (root.TryGetProperty("bin", out var bProp))
                            {
                                if (bProp.ValueKind == JsonValueKind.Object)
                                {
                                    binInfo = string.Join(", ", bProp.EnumerateObject().Select(o => o.Name));
                                }
                                else if (bProp.ValueKind == JsonValueKind.String)
                                {
                                    binInfo = name;
                                }
                            }
                        }
                        catch (Exception ex) { Debug.WriteLine($"[CliInspectorEngine] NPM package.json read error: {ex.Message}"); }
                    }

                    var (size, mtime, count) = GetDirSizeAndMTime(item, maxDepth: 3);

                    tools.Add(new CliToolItem
                    {
                        Id = $"npm_{name}",
                        Name = name,
                        FolderName = name,
                        Version = version,
                        Category = "NPM Global",
                        Description = $"{desc}" + (!string.IsNullOrEmpty(binInfo) ? $" (Команда: {binInfo})" : ""),
                        Path = item,
                        SizeBytes = size,
                        SizeFormatted = SizeFormatter.Format(size),
                        FileCount = count,
                        LastModified = mtime?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown",
                        MTime = mtime,
                        UninstallCmd = $"npm uninstall -g {name}",
                        Source = "npm (Global)",
                        ToolType = "package",
                        Homepage = homepage
                    });
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[CliInspectorEngine] NPM node_modules enumeration error: {ex.Message}"); }
        }

        return tools;
    }

    private List<CliToolItem> ScanPipPackages(CancellationToken ct)
    {
        var tools = new List<CliToolItem>();

        var pipxVenvs = Path.Combine(LocalAppData, "pipx", "venvs");
        if (Directory.Exists(pipxVenvs))
        {
            try
            {
                foreach (var venv in Directory.EnumerateDirectories(pipxVenvs))
                {
                    ct.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(venv);
                    var (size, mtime, count) = GetDirSizeAndMTime(venv, maxDepth: 3);

                    tools.Add(new CliToolItem
                    {
                        Id = $"pipx_{name}",
                        Name = name,
                        FolderName = name,
                        Category = "Pipx CLI App",
                        Description = $"Изолированное Python-приложение pipx: {name}",
                        Path = venv,
                        SizeBytes = size,
                        SizeFormatted = SizeFormatter.Format(size),
                        FileCount = count,
                        LastModified = mtime?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown",
                        MTime = mtime,
                        UninstallCmd = $"pipx uninstall {name}",
                        Source = "pipx",
                        ToolType = "folder"
                    });
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[CliInspectorEngine] Pipx venvs enumeration error: {ex.Message}"); }
        }

        return tools;
    }

    private List<CliToolItem> ScanPackageManagers(CancellationToken ct)
    {
        var tools = new List<CliToolItem>();

        // 1. Scoop Apps
        var scoopApps = Path.Combine(UserHome, "scoop", "apps");
        if (Directory.Exists(scoopApps))
        {
            try
            {
                foreach (var appDir in Directory.EnumerateDirectories(scoopApps))
                {
                    ct.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(appDir);
                    if (name.Equals("scoop", StringComparison.OrdinalIgnoreCase)) continue;

                    var (size, mtime, count) = GetDirSizeAndMTime(appDir, maxDepth: 3);
                    tools.Add(new CliToolItem
                    {
                        Id = $"scoop_{name}",
                        Name = name,
                        FolderName = name,
                        Category = "Scoop App",
                        Description = $"Пакет Scoop: {name}",
                        Path = appDir,
                        SizeBytes = size,
                        SizeFormatted = SizeFormatter.Format(size),
                        FileCount = count,
                        LastModified = mtime?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown",
                        MTime = mtime,
                        UninstallCmd = $"scoop uninstall {name}",
                        Source = "Scoop",
                        ToolType = "folder"
                    });
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[CliInspectorEngine] Scoop apps enumeration error: {ex.Message}"); }
        }

        // 2. Chocolatey Packages
        var chocoLib = Path.Combine(ProgramData, "chocolatey", "lib");
        if (Directory.Exists(chocoLib))
        {
            try
            {
                foreach (var pkg in Directory.EnumerateDirectories(chocoLib))
                {
                    ct.ThrowIfCancellationRequested();
                    var name = Path.GetFileName(pkg);
                    if (name.Equals("chocolatey", StringComparison.OrdinalIgnoreCase)) continue;

                    var (size, mtime, count) = GetDirSizeAndMTime(pkg, maxDepth: 2);
                    tools.Add(new CliToolItem
                    {
                        Id = $"choco_{name}",
                        Name = name,
                        FolderName = name,
                        Category = "Chocolatey",
                        Description = $"Пакет Chocolatey: {name}",
                        Path = pkg,
                        SizeBytes = size,
                        SizeFormatted = SizeFormatter.Format(size),
                        FileCount = count,
                        LastModified = mtime?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown",
                        MTime = mtime,
                        UninstallCmd = $"choco uninstall {name}",
                        Source = "Chocolatey",
                        ToolType = "folder"
                    });
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[CliInspectorEngine] Chocolatey packages enumeration error: {ex.Message}"); }
        }

        // 3. Cargo Binaries
        var cargoBin = Path.Combine(UserHome, ".cargo", "bin");
        if (Directory.Exists(cargoBin))
        {
            try
            {
                foreach (var exe in Directory.EnumerateFiles(cargoBin, "*.exe"))
                {
                    ct.ThrowIfCancellationRequested();
                    var fi = new FileInfo(exe);
                    var stem = Path.GetFileNameWithoutExtension(exe);

                    tools.Add(new CliToolItem
                    {
                        Id = $"cargo_{stem}",
                        Name = stem,
                        FolderName = fi.Name,
                        Category = "Cargo (Rust)",
                        Description = $"Rust cargo бинарник: {fi.Name}",
                        Path = exe,
                        SizeBytes = fi.Length,
                        SizeFormatted = SizeFormatter.Format(fi.Length),
                        FileCount = 1,
                        LastModified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        MTime = fi.LastWriteTime,
                        UninstallCmd = $"cargo uninstall {stem}",
                        Source = "Cargo",
                        ToolType = "file"
                    });
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[CliInspectorEngine] Cargo binaries enumeration error: {ex.Message}"); }
        }

        // 4. Winget Links
        var wingetLinks = Path.Combine(LocalAppData, "Microsoft", "WinGet", "Links");
        if (Directory.Exists(wingetLinks))
        {
            try
            {
                foreach (var link in Directory.EnumerateFiles(wingetLinks))
                {
                    ct.ThrowIfCancellationRequested();
                    var fi = new FileInfo(link);
                    var stem = Path.GetFileNameWithoutExtension(link);

                    tools.Add(new CliToolItem
                    {
                        Id = $"winget_link_{stem}",
                        Name = stem,
                        FolderName = fi.Name,
                        Category = "WinGet CLI Link",
                        Description = $"WinGet алиас команды: {fi.Name}",
                        Path = link,
                        SizeBytes = fi.Length,
                        SizeFormatted = SizeFormatter.Format(fi.Length),
                        FileCount = 1,
                        LastModified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        MTime = fi.LastWriteTime,
                        UninstallCmd = $"winget uninstall {stem}",
                        Source = "WinGet",
                        ToolType = "link"
                    });
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[CliInspectorEngine] Winget links enumeration error: {ex.Message}"); }
        }

        return tools;
    }

    private (List<CliToolItem> Bins, List<PathHealthItem> Health) ScanPathEnvironmentAndBinaries(CancellationToken ct)
    {
        var pathBins = new List<CliToolItem>();
        var pathHealth = new List<PathHealthItem>();

        var userDirs = _pathService.GetUserPathEntries();
        var sysDirs = _pathService.GetSystemPathEntries();

        var allUniqueDirs = new List<(string Dir, string Scope)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in userDirs)
        {
            if (seen.Add(d)) allUniqueDirs.Add((d, "User"));
        }
        foreach (var d in sysDirs)
        {
            if (seen.Add(d)) allUniqueDirs.Add((d, "System"));
        }

        foreach (var (pathDir, scope) in allUniqueDirs)
        {
            ct.ThrowIfCancellationRequested();
            var exists = Directory.Exists(pathDir);
            var exes = new List<string>();
            long dirSize = 0;

            if (exists)
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(pathDir))
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext is ".exe" or ".cmd" or ".bat" or ".ps1")
                        {
                            var fi = new FileInfo(file);
                            dirSize += fi.Length;
                            exes.Add(fi.Name);

                            var isSystem = pathDir.Contains("system32", StringComparison.OrdinalIgnoreCase) ||
                                           pathDir.Contains("syswow64", StringComparison.OrdinalIgnoreCase);

                            if (!isSystem)
                            {
                                pathBins.Add(new CliToolItem
                                {
                                    Id = $"path_bin_{fi.Name}_{Math.Abs(file.GetHashCode())}",
                                    Name = fi.Name,
                                    FolderName = Path.GetFileName(pathDir),
                                    Category = "PATH Executable",
                                    Description = $"Команда '{Path.GetFileNameWithoutExtension(fi.Name)}' из {scope} PATH: {pathDir}",
                                    Path = file,
                                    SizeBytes = fi.Length,
                                    SizeFormatted = SizeFormatter.Format(fi.Length),
                                    FileCount = 1,
                                    LastModified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                                    MTime = fi.LastWriteTime,
                                    UninstallCmd = $"Remove-Item \"{file}\"",
                                    Source = $"{scope} PATH",
                                    ToolType = "executable"
                                });
                            }
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[CliInspectorEngine] PATH dir enumeration error: {ex.Message}"); }
            }

            var status = !exists ? "Мертвый путь (Папка не найдена)" : (exes.Count > 0 ? "Активен" : "Пустая директория");

            pathHealth.Add(new PathHealthItem
            {
                Path = pathDir,
                Scope = scope,
                Exists = exists,
                IsDead = !exists,
                Status = status,
                BinCount = exes.Count,
                SizeFormatted = SizeFormatter.Format(dirSize),
                Executables = exes
            });
        }

        return (pathBins, pathHealth);
    }

    private List<PowerShellProfileItem> ScanPowerShellProfilesAndModules(CancellationToken ct)
    {
        var profiles = new List<PowerShellProfileItem>();

        var potentialProfiles = new[]
        {
            Path.Combine(UserHome, "Documents", "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1"),
            Path.Combine(UserHome, "Documents", "WindowsPowerShell", "profile.ps1"),
            Path.Combine(UserHome, "Documents", "PowerShell", "Microsoft.PowerShell_profile.ps1"),
            Path.Combine(UserHome, "Documents", "PowerShell", "profile.ps1"),
            Path.Combine(UserHome, ".config", "powershell", "Microsoft.PowerShell_profile.ps1")
        };

        foreach (var prof in potentialProfiles)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(prof))
            {
                try
                {
                    var content = File.ReadAllText(prof);
                    var fi = new FileInfo(prof);
                    profiles.Add(new PowerShellProfileItem
                    {
                        Path = prof,
                        Name = fi.Name,
                        LinesCount = content.Split('\n').Length,
                        SizeFormatted = SizeFormatter.Format(fi.Length),
                        Content = content,
                        LastModified = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        IsModule = false
                    });
                }
                catch (Exception ex) { Debug.WriteLine($"[CliInspectorEngine] PowerShell profile read error: {ex.Message}"); }
            }
        }

        var psModDirs = new[]
        {
            Path.Combine(UserHome, "Documents", "PowerShell", "Modules"),
            Path.Combine(UserHome, "Documents", "WindowsPowerShell", "Modules")
        };

        foreach (var modDir in psModDirs)
        {
            if (Directory.Exists(modDir))
            {
                try
                {
                    foreach (var mod in Directory.EnumerateDirectories(modDir))
                    {
                        ct.ThrowIfCancellationRequested();
                        var name = Path.GetFileName(mod);
                        var (size, mtime, _) = GetDirSizeAndMTime(mod, maxDepth: 2);

                        profiles.Add(new PowerShellProfileItem
                        {
                            Path = mod,
                            Name = $"Модуль: {name}",
                            SizeFormatted = SizeFormatter.Format(size),
                            LastModified = mtime?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown",
                            IsModule = true,
                            Content = $"PowerShell модуль, расположенный в: {mod}"
                        });
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[CliInspectorEngine] PowerShell module enumeration error: {ex.Message}"); }
            }
        }

        return profiles;
    }

    private static (long Size, DateTime? MTime, int FileCount) GetDirSizeAndMTime(string path, int maxDepth = 3)
    {
        long size = 0;
        DateTime? latestMTime = null;
        int count = 0;

        try
        {
            if (!Directory.Exists(path)) return (0, null, 0);

            latestMTime = Directory.GetLastWriteTime(path);

            void Walk(string current, int depth)
            {
                if (depth > maxDepth) return;
                try
                {
                    foreach (var file in Directory.EnumerateFiles(current))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            size += fi.Length;
                            count++;
                            if (latestMTime == null || fi.LastWriteTime > latestMTime)
                            {
                                latestMTime = fi.LastWriteTime;
                            }
                        }
                        catch (Exception ex) { Debug.WriteLine($"[CliInspectorEngine] File size read error: {ex.Message}"); }
                    }

                    foreach (var dir in Directory.EnumerateDirectories(current))
                    {
                        Walk(dir, depth + 1);
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[CliInspectorEngine] Dir enumeration error in Walk: {ex.Message}"); }
            }

            Walk(path, 0);
        }
        catch (Exception ex) { Debug.WriteLine($"[CliInspectorEngine] GetDirSizeAndMTime error: {ex.Message}"); }

        return (size, latestMTime, count);
    }

    private static List<string> GetExecutablesInDir(string dir)
    {
        var list = new List<string>();
        try
        {
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext is ".exe" or ".cmd" or ".bat" or ".ps1")
                    {
                        list.Add(Path.GetFileName(file));
                    }
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[CliInspectorEngine] GetExecutablesInDir error: {ex.Message}"); }
        return list;
    }
}
