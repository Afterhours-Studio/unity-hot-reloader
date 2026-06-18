using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityReloader.Editor.Compilation;
using UnityReloader.Editor.Compilation.ScriptGenerationOverrides;
using UnityReloader.Runtime;
using ImmersiveVRTools.Editor.Common.Utilities;
using ImmersiveVRTools.Runtime.Common;
using ImmersiveVrToolsCommon.Runtime.Logging;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UnityReloader.Editor
{
    [InitializeOnLoad]
    [PreventHotReload]
    public class UnityReloaderManager
    {
        private static UnityReloaderManager _instance;
        public static UnityReloaderManager Instance
        {
            get {
                if (_instance == null)
                {
                    _instance = new UnityReloaderManager();
                    LoggerScoped.LogDebug("Created Manager");
                }

                return _instance;
            }
        }

        private static string DataPath = Application.dataPath;

        public const string FileWatcherReplacementTokenForApplicationDataPath = "<Application.dataPath>";
        private const int BaseMenuItemPriority_ManualScriptOverride = 100;
        private const int BaseMenuItemPriority_Exclusions = 200;
        private const int BaseMenuItemPriority_FileWatcher = 300;

        public Dictionary<string, Func<string>> FileWatcherTokensToResolvePathFn = new Dictionary<string, Func<string>>
        {
            [FileWatcherReplacementTokenForApplicationDataPath] = () => DataPath
        };

        private Dictionary<string, DynamicFileHotReloadState> _lastProcessedDynamicFileHotReloadStatesInSession = new Dictionary<string, DynamicFileHotReloadState>();
        public IReadOnlyDictionary<string, DynamicFileHotReloadState> LastProcessedDynamicFileHotReloadStatesInSession => _lastProcessedDynamicFileHotReloadStatesInSession;
        public event Action<List<DynamicFileHotReloadState>> HotReloadFailed;
        public event Action<List<DynamicFileHotReloadState>> HotReloadSucceeded;

        private bool _wasLockReloadAssembliesCalled;
        private PlayModeStateChange _lastPlayModeStateChange;
        private IEnumerable<string> _currentFileExclusions;
        private int _triggerDomainReloadIfOverNDynamicallyLoadedAssembles = 100;
        public bool EnableExperimentalThisCallLimitationFix { get; private set; }
        public bool IsPartialClassSupportEnabled { get; private set; }
#pragma warning disable 0618
        public AssemblyChangesLoaderEditorOptionsNeededInBuild AssemblyChangesLoaderEditorOptionsNeededInBuild { get; private set; } = new AssemblyChangesLoaderEditorOptionsNeededInBuild();
#pragma warning restore 0618

        private DateTime _lastTimeChangeBatchRun = default(DateTime);
        private bool _assemblyChangesLoaderResolverResolutionAlreadyCalled;
        private volatile bool _isEditorModeHotReloadEnabled;
        private int _hotReloadPerformedCount = 0;
        private bool _isOnDemandHotReloadEnabled;

        private readonly FileChangeSource _fileChangeSource;
        private readonly ChangeBatcher _changeBatcher;
        private readonly CompilationService _compilationService;
        private readonly PatchApplicator _patchApplicator;
        private readonly FallbackRecompileService _fallbackRecompileService;

        private UnityReloaderManager()
        {
            var pendingFileChangePaths = new ConcurrentQueue<string>();
            _fileChangeSource = new FileChangeSource(
                pendingFileChangePaths,
                ShouldIgnoreFileChange,
                FileWatcherTokensToResolvePathFn,
                DataPath);
            _changeBatcher = new ChangeBatcher(pendingFileChangePaths, ShouldIgnoreFileChange);
            _compilationService = new CompilationService();
            _patchApplicator = new PatchApplicator();
            _fallbackRecompileService = new FallbackRecompileService();
        }

        public void AddFileChangeToProcess(string filePath)
        {
            _fileChangeSource.AddFileChangeToProcess(filePath);
        }

        public bool ShouldIgnoreFileChange()
        {
            if (!_isEditorModeHotReloadEnabled && _lastPlayModeStateChange != PlayModeStateChange.EnteredPlayMode)
            {
#if ImmersiveVrTools_DebugEnabled
                LoggerScoped.Log($"Application not playing, change won't be compiled and hot reloaded");
#endif
                return true;
            }

            return false;
        }

        static UnityReloaderManager()
        {
            //do not add init code in here as with domain reload turned off it won't be properly set on play-mode enter, use Init method instead
            ApplyDefaultHotReloadSettingsOnce();

            EditorApplication.update += Instance.Update;
            EditorApplication.playModeStateChanged += Instance.OnEditorApplicationOnplayModeStateChanged;

            ///if <see cref="UnityReloaderPreference.WatchOnlySpecified"/> is enabled, disable auto reload automatically when launching editor. Will be enabled automatically when adding file watcher manually
            if ((bool)UnityReloaderPreference.WatchOnlySpecified.GetEditorPersistedValueOrDefault() && SessionState.GetBool("NEED_EDITOR_SESSION_INIT", true))
            {
                SessionState.SetBool("NEED_EDITOR_SESSION_INIT", false);
                ClearFileWatchersEntries();
            }
        }

        // One-time defaults: enable hot reload for BOTH play mode and editor mode out of the box.
        // Runs once per machine (guarded by EditorPrefs flag) so the user can still turn things off afterwards.
        private static void ApplyDefaultHotReloadSettingsOnce()
        {
            const string defaultsAppliedKey = "UnityReloader_HotReloadDefaultsApplied_v1";
            if (!EditorPrefs.GetBool(defaultsAppliedKey, false))
            {
                EditorPrefs.SetBool(defaultsAppliedKey, true);
                UnityReloaderPreference.WatchOnlySpecified.SetEditorPersistedValue(false);
                UnityReloaderPreference.EnableAutoReloadForChangedFiles.SetEditorPersistedValue(true);
                UnityReloaderPreference.EnableExperimentalEditorHotReloadSupport.SetEditorPersistedValue(true);
                LoggerScoped.LogDebug("Unity Reloader: applied default hot-reload settings (auto reload + editor mode enabled).");
            }

            // Independent one-time migration: the experimental added-fields feature only fakes fields via a
            // side-table (never updates the Inspector) and conflicts with reliable structural-change detection,
            // so default it off. Users can re-enable it in Settings if they want the experimental behaviour.
            const string addedFieldsOffKey = "UnityReloader_AddedFieldsDefaultOff_v1";
            if (!EditorPrefs.GetBool(addedFieldsOffKey, false))
            {
                EditorPrefs.SetBool(addedFieldsOffKey, true);
                UnityReloaderPreference.EnableExperimentalAddedFieldsSupport.SetEditorPersistedValue(false);
            }
        }

        ~UnityReloaderManager()
        {
            LoggerScoped.LogDebug("Destroying FSR Manager ");
            if (_instance != null)
            {
                if (_lastPlayModeStateChange == PlayModeStateChange.EnteredPlayMode)
                {
                    LoggerScoped.LogError("Manager is being destroyed in play session, this indicates some sort of issue where static variables were reset, hot reload will not function properly please reset. " +
                                          "This is usually caused by Unity triggering that reset for some reason that's outside of asset control - other static variables will also be affected and recovering just hot reload would hide wider issue.");
                }
                _fileChangeSource.Clear();
            }
        }

        private const string WatchSpecificFileOrFolderMenuItemName = "Assets/Unity Reloader/Watch File\\Folder";
        [MenuItem(WatchSpecificFileOrFolderMenuItemName, true, BaseMenuItemPriority_FileWatcher + 1)]
        public static bool ToggleSelectionFileWatchersSetupValidation()
        {
            if (!(bool)UnityReloaderPreference.WatchOnlySpecified.GetEditorPersistedValueOrDefault())
            {
                return false;
            }

            Menu.SetChecked(WatchSpecificFileOrFolderMenuItemName, false);

            var isSelectionContaininingFolderOrScript = false;
            for (var i = 0; i < Selection.objects.Length; i++)
            {
                if (Selection.objects[i] is MonoScript selectedMonoScript)
                {
                    isSelectionContaininingFolderOrScript = true;

                    if (IsFileWatcherSetupEntryAlreadyPresent(selectedMonoScript))
                    {
                        Menu.SetChecked(WatchSpecificFileOrFolderMenuItemName, true);
                        break;
                    }
                }
                else if (Selection.objects[i] is DefaultAsset selectedAsset)
                {
                    isSelectionContaininingFolderOrScript = true;

                    if (IsFileWatcherSetupEntryAlreadyPresent(selectedAsset))
                    {
                        Menu.SetChecked(WatchSpecificFileOrFolderMenuItemName, true);
                        break;
                    }
                }
            }

            return isSelectionContaininingFolderOrScript;
        }

        /// <summary>Used to add/remove scripts/folders to the <see cref="UnityReloaderPreference.FileWatcherSetupEntries"/></summary>
        [MenuItem(WatchSpecificFileOrFolderMenuItemName, false, BaseMenuItemPriority_FileWatcher + 1)]
        public static void ToggleSelectionFileWatchersSetup()
        {
            var isFileWatchersChange = false;
            for (var i = 0; i < Selection.objects.Length; i++)
            {
                if (Selection.objects[i] is MonoScript selectedMonoScript)
                {
                    if (IsFileWatcherSetupEntryAlreadyPresent(selectedMonoScript, out var foundFileWatcherSetupEntry))
                    {
                        UnityReloaderPreference.FileWatcherSetupEntries.RemoveElement(JsonUtility.ToJson(foundFileWatcherSetupEntry));
                    }
                    else
                    {
                        UnityReloaderPreference.FileWatcherSetupEntries.AddElement(JsonUtility.ToJson(foundFileWatcherSetupEntry));
                    }

                    isFileWatchersChange = true;
                }
                else if (Selection.objects[i] is DefaultAsset selectedAsset)
                {
                    if (IsFileWatcherSetupEntryAlreadyPresent(selectedAsset, out var foundFileWatcherSetupEntry))
                    {
                        UnityReloaderPreference.FileWatcherSetupEntries.RemoveElement(JsonUtility.ToJson(foundFileWatcherSetupEntry));
                    }
                    else
                    {
                        UnityReloaderPreference.FileWatcherSetupEntries.AddElement(JsonUtility.ToJson(foundFileWatcherSetupEntry));
                    }

                    isFileWatchersChange = true;
                }
            }

            if (isFileWatchersChange)
            {
                UnityReloaderPreference.FileWatcherSetupEntriesChanged = true; // Ensures file watcher are updated in play mode

                /// When in <see cref="UnityReloaderPreference.WatchOnlySpecified"/> mode, <see cref="UnityReloaderPreference.EnableAutoReloadForChangedFiles"/> state is managed automatically (disabled when no file watcher)
                if ((bool)UnityReloaderPreference.WatchOnlySpecified.GetEditorPersistedValueOrDefault())
                {
                    var isAnyFileWatcherSet = UnityReloaderPreference.FileWatcherSetupEntries.GetElements().Any();
                    UnityReloaderPreference.EnableAutoReloadForChangedFiles.SetEditorPersistedValue(isAnyFileWatcherSet);
                }
            }
        }

        [MenuItem("Assets/Unity Reloader/Clear Watched Files", true, BaseMenuItemPriority_FileWatcher + 2)]
        public static bool ClearUnityReloaderValidation()
        {
            if (!(bool)UnityReloaderPreference.WatchOnlySpecified.GetEditorPersistedValueOrDefault())
            {
                return false;
            }

            return UnityReloaderPreference.FileWatcherSetupEntries.GetElements().Any();
        }
        [MenuItem("Assets/Unity Reloader/Clear Watched Files", false, BaseMenuItemPriority_FileWatcher + 2)]
        public static void ClearFileWatchersEntries()
        {
            foreach (var item in UnityReloaderPreference.FileWatcherSetupEntries.GetElements())
            {
                UnityReloaderPreference.FileWatcherSetupEntries.RemoveElement(item);
            }
            Debug.LogWarning("File Watcher Setup has been cleared - make sure to add some.");

            UnityReloaderPreference.EnableAutoReloadForChangedFiles.SetEditorPersistedValue(false);

            ClearFileWatchers();
        }


        [MenuItem("Assets/Unity Reloader/Add \\ Open User Script Rewrite Override", false, BaseMenuItemPriority_ManualScriptOverride + 1)]
        public static void AddHotReloadManualScriptOverride()
        {
            if (Selection.activeObject is MonoScript script)
            {
                ScriptGenerationOverridesManager.AddScriptOverride(script);
            }
        }

        [MenuItem("Assets/Unity Reloader/Add \\ Open User Script Rewrite Override", true)]
        public static bool AddHotReloadManualScriptOverrideValidateFn()
        {
            return Selection.activeObject is MonoScript;
        }

        [MenuItem("Assets/Unity Reloader/Remove User Script Rewrite Override", false, BaseMenuItemPriority_ManualScriptOverride + 2)]
        public static void RemoveHotReloadManualScriptOverride()
        {
            if (Selection.activeObject is MonoScript script)
            {
                ScriptGenerationOverridesManager.TryRemoveScriptOverride(script);
            }
        }

        [MenuItem("Assets/Unity Reloader/Remove User Script Rewrite Override", true)]
        public static bool RemoveHotReloadManualScriptOverrideValidateFn()
        {
            if (Selection.activeObject is MonoScript script)
            {
                return ScriptGenerationOverridesManager.TryGetScriptOverride(
                    new FileInfo(Path.Combine(Path.Combine(Application.dataPath + "//..", AssetDatabase.GetAssetPath(script)))),
                    out var _
                );
            }

            return false;
        }

        [MenuItem("Assets/Unity Reloader/Show User Script Rewrite Overrides", false, BaseMenuItemPriority_ManualScriptOverride + 3)]
        public static void ShowManualScriptRewriteOverridesInUi()
        {
            var window = UnityReloaderWindow.Open();
            window.OpenExclusionsTab();
        }

        [MenuItem("Assets/Unity Reloader/Add Hot-Reload Exclusion", false, BaseMenuItemPriority_Exclusions + 1)]
        public static void AddFileAsExcluded()
        {
            UnityReloaderPreference.FilesExcludedFromHotReload.AddElement(ResolveRelativeToAssetDirectoryFilePath(Selection.activeObject));
        }

        [MenuItem("Assets/Unity Reloader/Add Hot-Reload Exclusion", true)]
        public static bool AddFileAsExcludedValidateFn()
        {
            return Selection.activeObject is MonoScript
                   && !((UnityReloaderPreference.FilesExcludedFromHotReload.GetEditorPersistedValueOrDefault() as IEnumerable<string>) ?? Array.Empty<string>())
                       .Contains(ResolveRelativeToAssetDirectoryFilePath(Selection.activeObject));
        }

        [MenuItem("Assets/Unity Reloader/Remove Hot-Reload Exclusion", false, BaseMenuItemPriority_Exclusions + 2)]
        public static void RemoveFileAsExcluded()
        {
            UnityReloaderPreference.FilesExcludedFromHotReload.RemoveElement(ResolveRelativeToAssetDirectoryFilePath(Selection.activeObject));
        }

        [MenuItem("Assets/Unity Reloader/Remove Hot-Reload Exclusion", true)]
        public static bool RemoveFileAsExcludedValidateFn()
        {
            return Selection.activeObject is MonoScript
                   && ((UnityReloaderPreference.FilesExcludedFromHotReload.GetEditorPersistedValueOrDefault() as IEnumerable<string>) ?? Array.Empty<string>())
                   .Contains(ResolveRelativeToAssetDirectoryFilePath(Selection.activeObject));
        }

        [MenuItem("Assets/Unity Reloader/Show Exclusions", false, BaseMenuItemPriority_Exclusions + 3)]
        public static void ShowExcludedFilesInUi()
        {
            var window = UnityReloaderWindow.Open();
            window.OpenExclusionsTab();
        }

        private static string ResolveRelativeToAssetDirectoryFilePath(UnityEngine.Object obj)
        {
            // Object overload works on all Unity versions and avoids the obsolete int/EntityId overloads.
            return AssetDatabase.GetAssetPath(obj);
        }

        public void Update()
        {
            _fallbackRecompileService.ProcessIfPending();

            _isEditorModeHotReloadEnabled = (bool)UnityReloaderPreference.EnableExperimentalEditorHotReloadSupport.GetEditorPersistedValueOrDefault();
            if (_lastPlayModeStateChange == PlayModeStateChange.ExitingPlayMode && _fileChangeSource.WatcherCount > 0)
            {
                ClearFileWatchers();
            }

            if (!_isEditorModeHotReloadEnabled && !EditorApplication.isPlaying)
            {
                return;
            }

            if (_isEditorModeHotReloadEnabled)
            {
                _fileChangeSource.EnsureInitialized();
            }
            else if (_lastPlayModeStateChange == PlayModeStateChange.EnteredPlayMode)
            {
                _fileChangeSource.EnsureInitialized();
            }

            AssignConfigValuesThatCanNotBeAccessedOutsideOfMainThread();
            _changeBatcher.Drain(_currentFileExclusions);

            if (!_assemblyChangesLoaderResolverResolutionAlreadyCalled)
            {
                AssemblyChangesLoaderResolver.Instance.Resolve(); //WARN: need to resolve initially in case monobehaviour singleton is not created
                _assemblyChangesLoaderResolverResolutionAlreadyCalled = true;
            }

            if ((bool)UnityReloaderPreference.EnableAutoReloadForChangedFiles.GetEditorPersistedValueOrDefault() &&
                (DateTime.UtcNow - _lastTimeChangeBatchRun).TotalSeconds > (int)UnityReloaderPreference.BatchScriptChangesAndReloadEveryNSeconds.GetEditorPersistedValueOrDefault())
            {
                TriggerReloadForChangedFiles();
            }
        }

        private static void ClearFileWatchers()
        {
            Instance._fileChangeSource.Clear();
        }

        private void AssignConfigValuesThatCanNotBeAccessedOutsideOfMainThread()
        {
            //TODO: PERF: needed in file watcher but when run on non-main thread causes exception.
            _currentFileExclusions = UnityReloaderPreference.FilesExcludedFromHotReload.GetElements();
            _triggerDomainReloadIfOverNDynamicallyLoadedAssembles = (int)UnityReloaderPreference.TriggerDomainReloadIfOverNDynamicallyLoadedAssembles.GetEditorPersistedValueOrDefault();
            _isOnDemandHotReloadEnabled = (bool)UnityReloaderPreference.EnableOnDemandReload.GetEditorPersistedValueOrDefault();
            EnableExperimentalThisCallLimitationFix = (bool)UnityReloaderPreference.EnableExperimentalThisCallLimitationFix.GetEditorPersistedValueOrDefault();
            AssemblyChangesLoaderEditorOptionsNeededInBuild.UpdateValues(
                (bool)UnityReloaderPreference.IsDidFieldsOrPropertyCountChangedCheckDisabled.GetEditorPersistedValueOrDefault(),
                (bool)UnityReloaderPreference.EnableExperimentalAddedFieldsSupport.GetEditorPersistedValueOrDefault()
            );
            IsPartialClassSupportEnabled = (bool)UnityReloaderPreference.IsPartialClassSupportEnabled.GetEditorPersistedValueOrDefault();
        }

        public void TriggerReloadForChangedFiles()
        {
            if (!Application.isPlaying && _hotReloadPerformedCount > _triggerDomainReloadIfOverNDynamicallyLoadedAssembles)
            {
                _hotReloadPerformedCount = 0;
                LoggerScoped.LogWarning($"Dynamically created assembles reached over: {_triggerDomainReloadIfOverNDynamicallyLoadedAssembles} - triggering full domain reload to clean up. You can adjust that value in settings.");
#if UNITY_2019_3_OR_NEWER
                CompilationPipeline.RequestScriptCompilation(); //TODO: add some timer to ensure this does not go into some kind of loop
#elif UNITY_2017_1_OR_NEWER
                 var editorAssembly = Assembly.GetAssembly(typeof(Editor));
                 var editorCompilationInterfaceType = editorAssembly.GetType("UnityEditor.Scripting.ScriptCompilation.EditorCompilationInterface");
                 var dirtyAllScriptsMethod = editorCompilationInterfaceType.GetMethod("DirtyAllScripts", BindingFlags.Static | BindingFlags.Public);
                 dirtyAllScriptsMethod.Invoke(editorCompilationInterfaceType, null);
#endif
                ClearLastProcessedDynamicFileHotReloadStates();
            }

            var assemblyChangesLoader = AssemblyChangesLoaderResolver.Instance.Resolve();
            var changesAwaitingHotReload = _changeBatcher.Entries
                .Where(e => e.IsAwaitingCompilation)
                .ToList();

            if (changesAwaitingHotReload.Any())
            {
                UpdateLastProcessedDynamicFileHotReloadStates(changesAwaitingHotReload);
                foreach (var c in changesAwaitingHotReload)
                {
                    c.IsBeingProcessed = true;
                }

                var unityMainThreadDispatcher = UnityMainThreadDispatcher.Instance.EnsureInitialized(); //need to pass that in, resolving on other than main thread will cause exception
                Task.Run(() =>
                {
                    List<string> sourceCodeFilesWithUniqueChangesAwaitingHotReload = null;
                    try
                    {
                        sourceCodeFilesWithUniqueChangesAwaitingHotReload = changesAwaitingHotReload
                            .GroupBy(e => e.FullFileName)
                            .Select(e => e.First().FullFileName).ToList();

                        var compilationResult = _compilationService.Compile(sourceCodeFilesWithUniqueChangesAwaitingHotReload, unityMainThreadDispatcher);
                        LoggerScoped.Log(compilationResult.PlanLog);

                        if (compilationResult.RequiresFullRecompile)
                        {
                            unityMainThreadDispatcher.Enqueue(() =>
                            {
#if UNITY_2019_3_OR_NEWER
                                LoggerScoped.Log("Hot reload pre-analysis: structural changes detected - skipping hot compile, requesting full recompile.");
                                CompilationPipeline.RequestScriptCompilation();
#endif
                            });
                            changesAwaitingHotReload.ForEach(c => { c.IsBeingProcessed = false; });
                            return;
                        }

                        var dynamicallyLoadedAssemblyCompilerResult = compilationResult.CompilerResult;
                        if (!dynamicallyLoadedAssemblyCompilerResult.IsError)
                        {
                            changesAwaitingHotReload.ForEach(c =>
                            {
                                c.FileCompiledOn = DateTime.UtcNow;
                                c.AssemblyNameCompiledIn = dynamicallyLoadedAssemblyCompilerResult.CompiledAssemblyPath;
                            });

                            var patchResult = _patchApplicator.Apply(dynamicallyLoadedAssemblyCompilerResult.CompiledAssembly, assemblyChangesLoader, AssemblyChangesLoaderEditorOptionsNeededInBuild);

                            if (patchResult.FallenBackToRecompile.Length > 0)
                            {
                                _fallbackRecompileService.Request();
                                LoggerScoped.Log($"Hot reload: {patchResult.Applied.Length} change(s) applied; {patchResult.FallenBackToRecompile.Length} change(s) require full recompile [{string.Join(", ", patchResult.FallenBackToRecompile)}].");
                            }

                            changesAwaitingHotReload.ForEach(c =>
                            {
                                c.HotSwappedOn = DateTime.UtcNow;
                                c.IsBeingProcessed = false;
                            }); //TODO: technically not all were hot swapped at same time

                            _hotReloadPerformedCount++;

                            SafeInvoke(HotReloadSucceeded, changesAwaitingHotReload);
                        }
                        else
                        {
                            if (dynamicallyLoadedAssemblyCompilerResult.MessagesFromCompilerProcess.Count > 0)
                            {
                                var msg = new StringBuilder();
                                foreach (string message in dynamicallyLoadedAssemblyCompilerResult.MessagesFromCompilerProcess)
                                {
                                    msg.AppendLine($"Error  when compiling, it's best to check code and make sure it's compilable \r\n {message}\n");
                                }

                                var errorMessage = msg.ToString();

                                changesAwaitingHotReload.ForEach(c =>
                                {
                                    c.ErrorOn = DateTime.UtcNow;
                                    c.ErrorText = errorMessage;
                                });

                                throw new Exception(errorMessage);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex is SourceCodeHasErrorsException e)
                            LoggerScoped.LogError(e.Message + Environment.NewLine);
                        else
                            LoggerScoped.LogError($"Error when updating files: '{(sourceCodeFilesWithUniqueChangesAwaitingHotReload != null ? string.Join(",", sourceCodeFilesWithUniqueChangesAwaitingHotReload.Select(fn => new FileInfo(fn).Name)) : "unknown")}', {ex}");

                        changesAwaitingHotReload.ForEach(c =>
                        {
                            c.ErrorOn = DateTime.UtcNow;
                            c.ErrorText = ex.Message;
                            c.SourceCodeCombinedFilePath = (ex as HotReloadCompilationException)?.SourceCodeCombinedFileCreated;
                        });

                        _fallbackRecompileService.Request();
                        SafeInvoke(HotReloadFailed, changesAwaitingHotReload);
                    }
                });
            }

            _lastTimeChangeBatchRun = DateTime.UtcNow;
        }

        private void SafeInvoke(Action<List<DynamicFileHotReloadState>> ev, List<DynamicFileHotReloadState> changesAwaitingHotReload)
        {
            try
            {
                ev?.Invoke(changesAwaitingHotReload);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error when executing event, {e}");
            }
        }

        private void AddToLastProcessedDynamicFileHotReloadStates(DynamicFileHotReloadState c)
        {
            var assetGuid = AssetDatabaseHelper.AbsolutePathToGUID(c.FullFileName);
            if (!string.IsNullOrEmpty(assetGuid))
            {
                _lastProcessedDynamicFileHotReloadStatesInSession[assetGuid] = c;
            }
        }

        private void ClearLastProcessedDynamicFileHotReloadStates()
        {
            _lastProcessedDynamicFileHotReloadStatesInSession.Clear();
        }

        //Success entries will always be cleared - errors will remain till another change fixes them
        private void UpdateLastProcessedDynamicFileHotReloadStates(List<DynamicFileHotReloadState> changesToHotReload)
        {
            var succeededReloads = _lastProcessedDynamicFileHotReloadStatesInSession
                .Where(s => s.Value.IsChangeHotSwapped).ToList();
            foreach (var kv in succeededReloads)
            {
                _lastProcessedDynamicFileHotReloadStatesInSession.Remove(kv.Key);
            }

            foreach (var changeToHotReload in changesToHotReload)
            {
                AddToLastProcessedDynamicFileHotReloadStates(changeToHotReload);
            }
        }

        private void OnEditorApplicationOnplayModeStateChanged(PlayModeStateChange obj)
        {
            Instance._lastPlayModeStateChange = obj;

            if ((bool)UnityReloaderPreference.IsForceLockAssembliesViaCode.GetEditorPersistedValueOrDefault())
            {
                if (obj == PlayModeStateChange.EnteredPlayMode)
                {
                    EditorApplication.LockReloadAssemblies();
                    _wasLockReloadAssembliesCalled = true;
                }
            }

            if(obj == PlayModeStateChange.EnteredEditMode && _wasLockReloadAssembliesCalled)
            {
                EditorApplication.UnlockReloadAssemblies();
                _wasLockReloadAssembliesCalled = false;
            }
        }

        private static bool IsFileWatcherSetupEntryAlreadyPresent(FileWatcherSetupEntry fileWatcherSetupEntry)
        {
            //TODO: could be a bit of a per hit, GetElementsTypes will parse json every time
            return UnityReloaderPreference.FileWatcherSetupEntries.GetElementsTyped()
                .Any(e => e.path == fileWatcherSetupEntry.path
                          && e.filter == fileWatcherSetupEntry.filter
                          && e.includeSubdirectories == fileWatcherSetupEntry.includeSubdirectories);
        }

        private static bool IsFileWatcherSetupEntryAlreadyPresent(DefaultAsset selectedAsset)
        {
            FileWatcherSetupEntry fileWatcherSetupEntry;
            return IsFileWatcherSetupEntryAlreadyPresent(selectedAsset, out fileWatcherSetupEntry);
        }

        private static bool IsFileWatcherSetupEntryAlreadyPresent(DefaultAsset selectedAsset, out FileWatcherSetupEntry fileWatcherSetupEntry)
        {
            var path = FileWatcherReplacementTokenForApplicationDataPath + AssetDatabase.GetAssetPath(selectedAsset).Remove(0, "Assets".Length);
            fileWatcherSetupEntry = new FileWatcherSetupEntry(path, "*.cs", true);

            var isFileWatcherSetupEntryAlreadyPresent = IsFileWatcherSetupEntryAlreadyPresent(fileWatcherSetupEntry);
            return isFileWatcherSetupEntryAlreadyPresent;
        }

        private static bool IsFileWatcherSetupEntryAlreadyPresent(MonoScript selectedMonoScript)
        {
            FileWatcherSetupEntry fileWatcherSetupEntry;
            return IsFileWatcherSetupEntryAlreadyPresent(selectedMonoScript, out fileWatcherSetupEntry);
        }

        private static bool IsFileWatcherSetupEntryAlreadyPresent(MonoScript selectedMonoScript, out FileWatcherSetupEntry fileWatcherSetupEntry)
        {
            var path = FileWatcherReplacementTokenForApplicationDataPath + AssetDatabase.GetAssetPath(selectedMonoScript).Remove(0, "Assets".Length);
            var fileSeperatorIndex = path.LastIndexOf('/');
            var fileName = path.Substring(fileSeperatorIndex + 1);
            path = path.Substring(0, fileSeperatorIndex);

            fileWatcherSetupEntry = new FileWatcherSetupEntry(path, fileName, false);
            var isFileWatcherSetupEntryAlreadyPresent = IsFileWatcherSetupEntryAlreadyPresent(fileWatcherSetupEntry);
            return isFileWatcherSetupEntryAlreadyPresent;
        }
    }

    public class DynamicFileHotReloadState
    {
        public string FullFileName { get; set; }
        public DateTime FileChangedOn { get; set; }
        public bool IsAwaitingCompilation => !IsFileCompiled && !ErrorOn.HasValue && !IsBeingProcessed;
        public bool IsFileCompiled => FileCompiledOn.HasValue;
        public DateTime? FileCompiledOn { get; set; }

        public string AssemblyNameCompiledIn { get; set; }

        public bool IsAwaitingHotSwap => IsFileCompiled && !HotSwappedOn.HasValue;
        public DateTime? HotSwappedOn { get; set; }
        public bool IsChangeHotSwapped => HotSwappedOn.HasValue;

        public string ErrorText { get; set; }
        public DateTime? ErrorOn { get; set; }
        public bool IsFailed => ErrorOn.HasValue;
        public bool IsBeingProcessed { get; set; }
        public string SourceCodeCombinedFilePath { get; set; }

        public DynamicFileHotReloadState(string fullFileName, DateTime fileChangedOn)
        {
            FullFileName = fullFileName;
            FileChangedOn = fileChangedOn;
        }
    }

    public enum FileWatcherImplementation
    {
        UnityDefault = 0,
#if UNITY_EDITOR_WIN && UNITY_2021_1_OR_NEWER
        DirectWindowsApi = 1,
#endif
        CustomPolling = 2
    }
}
