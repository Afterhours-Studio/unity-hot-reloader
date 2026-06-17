# Changelog

## [1.0.1] - 2026-06-17

### Added
- Hot reload for methods on generic classes. Constructed instantiations (e.g. `Container<int>`) reachable through base types, interfaces, or fields are discovered and detoured individually, covering both reference-type and value-type instantiations.

### Changed
- Generic methods (e.g. `void Foo<T>()`) no longer fail silently - they now log a precise message explaining a full recompile is required, since their runtime instantiations cannot be discovered via reflection.

## [1.0.0] - 2026-06-17

### Added
- Hot reload for method bodies in Play Mode via Harmony method detour
- Editor hot reload support (experimental) - reload without exiting Play Mode
- Structural change detection: auto-triggers full recompile when fields, serialization attributes, or field types change
- Auto-recompile on hot reload failure (gated by preference)
- Activity tab: real-time log of hot reload events with success/failure status
- Dashboard editor window (Tools / Unity Reloader / Dashboard) with tab-based UI
- Prominent ON/OFF toggle pill switches for all boolean preferences
- File watcher configuration per-path with filter and subdirectory options
- Assembly reference exclusion list
- Force Reload command (Tools / Unity Reloader / Force Reload)
- `OnScriptHotReload` / `OnScriptHotReloadNoInstance` callbacks for reacting to reloads
- Custom polling and Direct Windows API file watcher modes
- ZLogger-based scoped logging (ZLogger dependency)

