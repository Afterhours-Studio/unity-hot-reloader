# Changelog

## [1.1.0] - 2026-06-17

### Fixed
- Race condition on file change processing. Background watcher threads (FileSystemWatcher, custom polling timer) previously wrote directly to `_dynamicFileHotReloadStateEntries` (a non-thread-safe `List<T>`), which could cause `InvalidOperationException` or corrupt state under concurrent access. Watchers now enqueue paths to a `ConcurrentQueue<string>` and the main thread drains it each `Update` tick, applying debounce and exclusion checks before committing entries to the processing list.
- `_isEditorModeHotReloadEnabled` marked `volatile` to ensure background threads see fresh values when doing fast-path ignore checks.
- Crash log path corruption on Windows: `DetourCrashHandler` was applying `Path.GetInvalidFileNameChars()` (which includes `:` and `\`) to the full path string, corrupting the drive letter and directory separators. Fix: sanitize only `Application.productName`, then compose the path with `Path.Combine`.
- `CustomFileWatcher` polling timer was eligible for GC collection after the thread that created it exited. The `Timer` is now held in a `static` field, keeping it alive for the session.
- `DotnetExeCompilator` static caches (`_typeNameAssemblyCache`, `_assemblyNameToFriendAssemblyCache`) switched to `ConcurrentDictionary` and friend-assembly list is built before being stored, preventing a partial-population race when two compiles run concurrently. Cleanup list (`_createdFilesToCleanUp`) access locked with a dedicated lock object.

## [1.0.4] - 2026-06-17

### Fixed
- Stopped console spam for compiler-generated types (lambda closures `<>c`, display classes, async/iterator state machines). These have no stable name to match an existing type and are applied through their containing type, so they no longer log a warning.
- The `InternalsVisibleTo` assembly rewrite now supplies Cecil a resolver seeded with every loaded assembly's directory. This fixes `AssemblyResolutionException` when a project references a third-party SDK (e.g. one with no metadata version) that Cecil's default resolver could not locate.

### Known issues
- Burst may log `Failed to find entry-points ... BadImageFormatException: Read out of bounds` while hashing `0Harmony.dll`. This is a bug in Burst's bundled metadata reader (present through Burst 1.8.24) parsing Harmony's assembly, not a fault in this tool - hot reload works regardless. Swapping the Harmony DLL does not help (all target-framework builds of a given Harmony version share the same metadata).

## [1.0.3] - 2026-06-17

### Changed
- Promoted editor-mode hot reload out of "experimental". The toggle now lives in the main Hot Reload settings (renamed "Editor Hot-Reload") instead of the Experimental section, and its warning copy has been replaced with informational guidance. Behaviour and the persisted preference key are unchanged (still on by default).

## [1.0.2] - 2026-06-17

### Added
- Hot reload for newly added methods. Private methods are applied instantly via detour (they are only reachable from their declaring type). Public/protected/internal methods auto-trigger a full recompile so they become available to scripts that were not recompiled.

### Changed
- Replaced the blanket "adding new methods is not supported" warning with accurate per-method handling and messaging.

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

