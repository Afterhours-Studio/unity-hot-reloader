# Changelog

## [1.3.0] - 2026-06-18

### Changed
- Split `UnityReloaderManager` god class into five focused services injected via constructor:
  - `FileChangeSource` - owns `FileSystemWatcher` lifecycle, workaround for Unity file-path bug, path-token resolution, and the enqueue side of the pending-changes queue.
  - `ChangeBatcher` - drains the `ConcurrentQueue` on the main thread each `Update` tick, applies exclusion filtering and duplicate-detection debounce, and owns `_dynamicFileHotReloadStateEntries`.
  - `CompilationService` - runs `SourceChangeClassifier.Classify`, produces a `CompilationServiceResult` (plan log + early-exit flag + `CompileResult`), and delegates to `DynamicAssemblyCompiler.Compile`.
  - `PatchApplicator` - thin wrapper around `IAssemblyChangesLoader.DynamicallyUpdateMethodsForCreatedAssembly`, returns `PatchResult`.
  - `FallbackRecompileService` - holds the thread-safe `_fullRecompileRequestedAfterFailure` flag; `Request()` is called from the background compile thread, `ProcessIfPending()` is called on the main thread in `Update` and triggers `CompilationPipeline.RequestScriptCompilation` when appropriate.
- `UnityReloaderManager` is now a thin orchestrator: wires services together in `Update` and `TriggerReloadForChangedFiles`, manages play-mode state, and exposes public API (`HotReloadSucceeded`, `HotReloadFailed`, `AddFileChangeToProcess`, all `[MenuItem]` methods) unchanged.
- No services use static singletons internally - only `UnityReloaderManager` itself remains a singleton.
- Zero behavior change: all existing preferences, events, and menu items are preserved.

## [1.2.2] - 2026-06-17

### Added
- `ISourceRewriter` interface (`Rewrite(string sourceCode, RewriteContext context) : RewriteStepResult`) establishing a clear per-step contract for the source rewriting pipeline.
- `RewriteStepResult` with `Status` (`Changed`, `NoOp`, `Warning`, `Unsupported`), `TransformedSource`, `DiagnosticMessage`, and `StepName`. Factory helpers: `Changed`, `NoOp`, `Unsupported`.
- `RewriteContext` carries pipeline inputs (`PreprocessorSymbols`, `WriteRewriteReasonAsComment`) and collects out-of-band side effects (`StrippedUsingDirectives`) from rewriters that produce them.
- `SyntaxRewriterAdapter` wraps any `Func<SyntaxNode, RewriteContext, SyntaxNode>` as an `ISourceRewriter`. Each call re-parses the incoming string to a fresh `SyntaxTree`, applies the transform, and emits `Changed` or `NoOp` based on whether the output differs.
- All 10 rewriter calls in `DynamicCompilationBase.CreateSourceCodeCombinedContents` are now wrapped as `SyntaxRewriterAdapter` entries in an ordered `List<ISourceRewriter>`. Results are collected per step. If any step returns `Unsupported`, a warning is logged (`"File '...' failed at rewrite step '...':"`) and the pipeline halts for that file.
- `CompileResult` gains a `List<RewriteStepResult> RewriteStepResults` property, populated from the pipeline run, for logging and diagnostics.

## [1.2.1] - 2026-06-17

### Added
- Pre-compile capability layer (`SourceChangeClassifier`) that parses changed source files with Roslyn `SyntaxTree` (no semantic model) and classifies every change before compilation starts. Detects: `MethodBody`, `NewPrivateMethod`, `NewPublicMethod`, `FieldSignature`, `NewType`, `GenericMethod`, or `Unknown`. Classification is compared against the currently loaded assembly via reflection.
- Pre-compile log emitted before every hot reload batch. Example: "Hot reload pre-analysis: Foo.cs: Method body change 'Update' in 'Foo' - will hot reload; New public/protected/internal method 'Bar' in 'Foo' - requires full recompile. -> structural changes detected - will skip hot compile and request full recompile."
- Early-exit optimisation: when pre-compile analysis determines `RequiresFullRecompile`, `DynamicAssemblyCompiler.Compile` is skipped entirely. `CompilationPipeline.RequestScriptCompilation()` is dispatched to the main thread immediately, avoiding an unnecessary Roslyn compile followed by a second Unity recompile.

## [1.2.0] - 2026-06-17

### Changed
- Extracted all Roslyn source-rewriting code into a new `UnityReloader.Core` assembly (`Assets/Scripts/Editor/Compilation/CodeRewriting/`). The assembly covers every rewriter, walker, and partial-class combiner (21 files). It references `UnityReloader.Runtime` for shared constants and carries its own Roslyn precompiled references (`Microsoft.CodeAnalysis`, `CSharp`, `System.Collections.Immutable`). `UnityReloader.Editor` now lists `UnityReloader.Core` as a reference.
- No source-code changes: rewriter files retain their `UnityReloader.Editor.Compilation.CodeRewriting` namespace. Zero behavior change - the split is purely at the assembly boundary.
- The new assembly has no `#if UNITY_EDITOR` guards and no `UnityEditor` namespace usage. It can be referenced by an Editor test assembly without pulling in any Editor-API dependencies, making signature comparison, source rewriting, and partial-class merging independently testable.

## [1.1.1] - 2026-06-17

### Added
- `ChangeClassification` enum (`MethodBody`, `NewPrivateMethod`, `NewPublicMethod`, `FieldSignature`, `NewType`, `GenericMethod`, `Unknown`) in `UnityReloader.Runtime`.
- `HotReloadPlan` model (placeholder for the upcoming pre-compile capability pass).
- `PatchResult` model exposing `Applied` and `FallenBackToRecompile` classification arrays, with `Succeeded`/`Failed` factory helpers.
- `DynamicallyUpdateMethodsForCreatedAssembly` now returns `PatchResult` instead of `void`. Each decision point records the appropriate classification: `FieldSignature` on structural-change bail-out, `MethodBody` per successful detour, `GenericMethod` for undetourable generic methods, `NewPrivateMethod` / `NewPublicMethod` for newly added methods, and `NewType` for unmatched user types.
- `UnityReloaderManager` now logs a summary when any changes fall back to full recompile, showing applied vs. fallen-back counts and the classification list.
- Updated "FSR: Unable to find existing type" warning to use cleaner `UnityReloader:` prefix.

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

