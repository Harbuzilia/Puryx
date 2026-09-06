# SCRATCHPAD — Smart System Cleaner

## 📝 Research Notes

### AI Agents Data Locations (Windows)
| Agent | Root Path | Cache | Protected |
|-------|-----------|-------|-----------|
| Antigravity | `%USERPROFILE%\.gemini` | - | **ALL** |
| Cursor | `%APPDATA%\Cursor` | Cache, CachedData, Code Cache, GPUCache, logs | User/*, workspaceStorage |
| Kiro | `%USERPROFILE%\.kiro` | Cache, logs | settings/** |
| Windsurf | `%USERPROFILE%\.codeium` | cache | windsurf/cascade (chat history!) |
| Claude | `%APPDATA%\Claude` | logs | claude_desktop_config.json, *.db |

### node_modules Criteria (npkill approach)
- Last access > 14 days → Safe
- Has package-lock.json/yarn.lock → Restorable
- Is nested node_modules → Skip
- Active process using it → Skip

### Standard Windows Paths
- `%LOCALAPPDATA%` — User-specific cache
- `%APPDATA%` — Roaming data
- `%TEMP%` — Temporary files
- `%PROGRAMDATA%` — Shared app data

## 🔧 Implementation Notes

### Build Modes (User Decision)
```
build.bat --mode installer  → Installs to Program Files, config in %APPDATA%
build.bat --mode portable   → Single exe, config in ./config/ next to exe
```

### UAC Strategy
- System folders (Prefetch, Windows Temp) → spawn elevated child process
- User folders → no elevation needed

---

## 📋 TODO (Scratch)
- [x] Добавить .venv сканер (Python virtual environments) → PythonVenvScanner.cs
- [x] Добавить bin/obj сканер (C# build artifacts) → DotnetArtifactsScanner.cs
- [ ] Подумать про Docker cleanup (dangling images)
