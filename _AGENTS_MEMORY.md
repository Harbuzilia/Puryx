# SmartCleaner — Agent Memory

## Current Goal
All 10 features implemented. Audit completed and all issues fixed (0 errors, 0 warnings).

## Current Tech Stack
- .NET 8.0 LTS (net8.0-windows), WPF, C# 12
- CommunityToolkit.Mvvm, DotNet.Glob
- MVVM, DI (Microsoft.Extensions.DependencyInjection 8.0.1)

## Recent Changes Log

### Session: .NET 8 Retargeting, Directory Cleanup & Modern UI Redesign (21.08.2026)

**Critical & Architecture:**
1. **.NET 8 LTS Retargeting**: Adapted all projects (`SmartCleaner.App`, `SmartCleaner.Core`, `SmartCleaner.Core.Tests`) from .NET 9 to .NET 8 LTS (supported by installed SDK 8.0.421).
2. **Hash API Fix**: Fixed `DuplicateEngine.cs` hash conversion for .NET 8 (`Convert.ToHexString(hash).ToLowerInvariant()`).
3. **Docker Cleanup Routing**: Added `ExecuteDockerDeleteAsync` in `CleaningService.cs` for virtual Docker targets (`docker:image:...`, `docker:container:...`, `docker:build-cache`).
4. **Workspace Cleanup**: Deleted `CHISTilka.rar` (124 MB), `Duplicater/` (old python venv with 29,800+ files), `publish/`, `tmp/`, `.opencode/`.
5. **Theme Manager & Switcher**: Created `SmartCleaner.App.Services.ThemeManager` with dynamic runtime switching (Dark / Light / System), persistence in `ui_settings.json`, integration into `SettingsView` and sidebar quick toggle (`🌓 Тема`).
6. **Sidebar Navigation Redesign**: Completely redesigned `MainWindow.xaml` sidebar into clean grouped sections (**ОЧИСТКА**, **ИНСТРУМЕНТЫ**, **СИСТЕМА**), dynamic category badges, distinct Segoe MDL2 glyphs.
7. **Shortcuts**: Added `Ctrl+A` (Select all safe), `Ctrl+F` (Search), `Ctrl+S` (Scan), `Delete` (Clean), `Escape` (Cancel).
8. **About Dialog & Build Script**: Updated `AboutWindow.xaml` and `build.bat` with modern design and correct .NET 8 paths.

### Session: Deliter Integration (CLI, AI Agents, PATH Health, PowerShell Profiles) (23.08.2026)

**Critical & Architecture:**
1. **Native C# CLI & AI Engine**: Implemented `SmartCleaner.Core.CliInspector.CliInspectorEngine` scanning dotfolders, NPM globals, Pipx, Scoop, Choco, Cargo, Winget, and PATH executables.
2. **Path Environment Service**: Implemented `PathEnvironmentService` with Win32 registry read/write and `WM_SETTINGCHANGE` broadcast for zero-reboot PATH environment updating and dead path purge.
3. **WPF UI & ViewModel**: Created `CliInspectorPage.xaml` with 3 tab views, metric badges, search, filter, explorer opening, shell launching, recycle bin deletion, and report export.
4. **Deliter Standalone Upgraded**: Added dead PATH cleanup endpoint in `Deliter/app.py` and new agent signatures in `Deliter/scanner.py`.
5. **Changelogs & Documentation**: Updated `CHANGELOG.md` in both projects.

### Session: Full 9 Tier Features (S, SSS, SSSS) Implementation (23.08.2026)

**Critical & Architecture:**
1. **S-Tier MFT Instant Scan**: `SmartCleaner.Core.Mft.MftReader` and `MftScanner` with direct Win32 `FSCTL_ENUM_USN_DATA` MFT volume parsing for 1-2 sec instant drive index.
2. **S-Tier Deep Uninstaller & Leftover Hunter**: `UninstallerEngine` and `LeftoverHunter` scanning registry, AppData, ProgramData, with interactive `UninstallerPage.xaml`.
3. **S-Tier Dev Super-Cleaner**: `DevSuperScanner` (WSL2 VHDX, Rust cargo/target, Gradle, Maven, Go, Android, Unity, Unreal DDC) + `WslShrinkService`.
4. **SSS-Tier CompactOS / LZX Compression**: `CompactEngine` with `compact.exe /c /exe:lzx` auto-discovering games (Steam/Epic/GOG) and heavy projects (+30-50% space savings) with `CompactPage.xaml`.
5. **SSS-Tier Hardlink Deduplication**: `DuplicateEngine.ReplaceDuplicatesWithHardlinksAsync` creating NTFS hardlinks (`CreateHardLinkW`) to eliminate duplicate space without breaking paths.
6. **SSS-Tier WinSxS & DriverStore Purge**: `WinSxSEngine` (DISM /StartComponentCleanup /ResetBase) and `DriverStoreCleaner` (PnPUtil) with `SystemDeepCleanPage.xaml`.
7. **SSSS-Tier AI Natural Language Cleanup**: `NaturalLanguageQueryEngine` with semantic intent classification and conversational action cards in `AiAssistantPage.xaml`.
8. **SSSS-Tier Zero-Risk Sandbox Quarantine**: `QuarantineService` with manifest tracking and 1-click restore in `QuarantinePage.xaml`.
9. **SSSS-Tier Open Plugin Scripting Engine**: `PluginEngine` with JSON manifest rules (`*.plugin.json`) and `PluginsPage.xaml`.

### Session: God-Tier (SSSSS) Features Implementation (23.08.2026)

**Critical & Architecture:**
1. **Interactive Squarified Treemap**: `SmartCleaner.Core.DiskMap.TreemapLayout` and `SmartCleaner.App.Controls.TreeMapControl` with categorized file extension colors, double-click drilldown, and breadcrumbs.
2. **SQLite Database Vacuum & Compactor**: `SmartCleaner.Core.Optimization.SqliteCompactorService` for Cursor, VS Code, Chrome, Edge, Telegram.
3. **GPU Shader Cache Cleaner**: `SmartCleaner.Core.Scanning.Scanners.ShaderCacheScanner` (DirectX D3DSCache, NVIDIA DXCache/GLCache, AMD, Intel).
4. **RAM & Working Set Optimizer**: `SmartCleaner.Core.SystemOpt.RamOptimizerService` with Win32 `EmptyWorkingSet`.
5. **File Shredder (DoD 5220.22-M)**: `SmartCleaner.Core.Safety.FileShredderService` with 3-pass cryptographically secure wipe.
6. **Tray Sentinel Service**: `SmartCleaner.App.Services.TraySentinelService` disk health warning monitor.
7. **Localization Manager**: `SmartCleaner.Core.Localization.LocalizationManager` supporting dynamic RU / EN runtime switching.

### Session: Master-Tier Features Implementation (23.08.2026)

**Critical & Architecture:**
1. **Game Turbo Boost Engine**: `SmartCleaner.Core.SystemOpt.GameBoostService` with 1-click RAM purge, telemetry/SysMain background pause, and high performance power scheme activation.
2. **S.M.A.R.T. SSD Health & Wear Telemetry**: `SmartCleaner.Core.DiskHealth.DiskHealthService` reading NVMe/SSD physical health, wear percentage, and temperature in real time.
3. **Single-File Portable Release Script**: `publish_portable.bat` compiling standalone zero-dependency executable `SmartCleaner.App.exe`.

### Session: Ultimate Edition (v2.5.0) Implementation (23.08.2026)

**Critical & Architecture:**
1. **Gaming Network & Latency Optimizer**: `SmartCleaner.Core.Network.NetworkOptimizerService` with DNS flush, Winsock reset, multi-provider DNS switcher (Cloudflare/Google/Quad9/AdGuard) with live Ping benchmark, and TCP NoDelay registry tweak.
2. **Explorer Context Menu Manager**: `SmartCleaner.Core.Shell.ExplorerContextMenuManager` with registered right-click actions for scanning, LZX compaction, and DoD shredding.
3. **Executive HTML System Passport & Report**: `SmartCleaner.Core.Reporting.SystemReportGenerator` generating styled responsive dark-mode HTML reports.
4. **Audio Feedback Engine**: `SmartCleaner.App.Services.AudioFeedbackService` with configurable audio cues.
### Session: Modern Fluent / Obsidian Design System & UI/UX Polish Overhaul (v2.6.0) (24.08.2026)

**Critical & Architecture:**
1. **Modern Fluent / Obsidian Design System**: Redesigned `Themes/DarkTheme.xaml` and `Themes/LightTheme.xaml` with deep Obsidian palette (`#0D1117`), vibrant gradient buttons, custom minimalist 7px scrollbars with smooth rounded thumbs, and custom Fluent checkboxes.
2. **Navigation & Sidebar Overhaul**: Added active vertical indicator pill (3.5px accent bar) to `NavButton`, redesigned brand header with radiant icon and `v2.5 ULTIMATE` pill badge in `MainWindow.xaml`.
3. **Stat Widgets & Hero Cards**: Redesigned 4 summary cards across `MainWindow.xaml` and `DashboardPage.xaml` into executive stat widgets with colored icon badges and clean typography.
4. **Interactive Controls & Status Pills**: Upgraded search and filter controls, status pills (`SafeToDelete`, `PerformanceCache`, `UserData`, `Locked`), and primary action buttons.
### Session: Full Suite — Windows Privacy Debloater & Services Optimizer (v2.7.0) (24.08.2026)

**Critical & Architecture:**
1. **Windows Privacy & Anti-Spy Telemetry Debloater**: Implemented `SmartCleaner.Core.Privacy.PrivacyDebloatService` managing 14 privacy tweaks across 4 categories (Telemetry, Advertising, Feedback & Errors, Tracking & Sensors), with automatic snapshot `privacy_backup.json` and 1-click restore.
2. **Windows Services Optimizer**: Implemented `SmartCleaner.Core.ServicesOpt.WindowsServicesOptimizer` with preset profiles (Gaming / Max FPS, Balanced, Workstation, Restore Defaults) and per-service toggle with risk level categorization.
3. **UI Integration**: Added `PrivacyDebloatPage.xaml` and `ServicesOptimizerPage.xaml` with Deep Obsidian theme, wired into `MainWindow.xaml` under `СИСТЕМА`, and registered in DI.
4. **Unit Tests**: Added 4 unit tests in `TierFeaturesTests.cs`, bringing total to 51/51 passing tests (100% green).
5. **Pixel-Perfect UI/UX Polish Overhaul**: Standardized all 17 page headers and outer margins (`Margin="24,20"`), added pill badges across all modules, fixed layout overlap on summary cards in `MainWindow.xaml`, upgraded `ScanOverlay`/`CleanOverlay` to centered elevated frosted cards with smooth progress bars, modernized `ToastNotification`, `AboutWindow`, `PreviewDialog`, eliminated hardcoded colors (`OrangeRed`, `LimeGreen`, `DodgerBlue`), and implemented universal `ToolTip` and `ProgressBar` control templates.

## Important Rules for Agents
- **Changelog**: Every change MUST be recorded in [`CHANGELOG.md`](./CHANGELOG.md) with details on **what** was changed, **which files**, and **why** (rationale/reasoning).
- **Target Framework**: Must remain `.NET 8.0 LTS` (`net8.0-windows`). Do not retarget to .NET 9.

## Build Status
✅ 0 errors, 0 warnings (51/51 tests passing, 100% green)





