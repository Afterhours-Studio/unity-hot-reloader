using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using ImmersiveVrToolsCommon.Runtime.Logging;
using UnityEditor;

namespace UnityReloader.Editor
{
    public class FileChangeSource
    {
        private readonly ConcurrentQueue<string> _queue;
        private readonly Func<bool> _shouldIgnoreChange;
        private readonly Dictionary<string, Func<string>> _tokenResolvers;
        private readonly string _dataPath;
        private readonly List<IDisposable> _watchers = new List<IDisposable>();
        private bool _hotReloadDisabledWarningShown;

        public int WatcherCount => _watchers.Count;

        public FileChangeSource(
            ConcurrentQueue<string> queue,
            Func<bool> shouldIgnoreChange,
            Dictionary<string, Func<string>> tokenResolvers,
            string dataPath)
        {
            _queue = queue;
            _shouldIgnoreChange = shouldIgnoreChange;
            _tokenResolvers = tokenResolvers;
            _dataPath = dataPath;
        }

        public void AddFileChangeToProcess(string filePath)
        {
            if (!File.Exists(filePath))
            {
                LoggerScoped.LogWarning($"Specified file: '{filePath}' does not exist. Hot-Reload will not be performed.");
                return;
            }

            if (_shouldIgnoreChange()) return;

            _queue.Enqueue(filePath);
        }

        public void EnsureInitialized()
        {
            if (!(bool)UnityReloaderPreference.EnableAutoReloadForChangedFiles.GetEditorPersistedValueOrDefault()
                && !(bool)UnityReloaderPreference.EnableOnDemandReload.GetEditorPersistedValueOrDefault()
                && !(bool)UnityReloaderPreference.WatchOnlySpecified.GetEditorPersistedValueOrDefault())
            {
                if (!_hotReloadDisabledWarningShown)
                {
                    LoggerScoped.LogWarning($"Neither auto hot reload / on-demand reload / or watch specific is specified, file watchers will not be initialized. Please adjust settings and restart if you want hot reload to work.");
                    _hotReloadDisabledWarningShown = true;
                }
                return;
            }

            var isUsingCustomFileWatchers = (FileWatcherImplementation)UnityReloaderPreference.FileWatcherImplementationInUse.GetEditorPersistedValueOrDefault()
                                            == FileWatcherImplementation.CustomPolling;
            if (!isUsingCustomFileWatchers)
            {
                if (_watchers.Count == 0 || UnityReloaderPreference.FileWatcherSetupEntriesChanged)
                {
                    UnityReloaderPreference.FileWatcherSetupEntriesChanged = false;
                    Clear();
                    InitializeFromSetupEntries();
                }
            }
            else if (!CustomFileWatcher.InitSignaled)
            {
                CustomFileWatcher.TryEnableLivewatching();
                InitializeFromSetupEntries();
                CustomFileWatcher.InitSignaled = true;
            }
        }

        public void Clear()
        {
            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }
            _watchers.Clear();
        }

        public void StartWatching(string directoryPath, string filter, bool includeSubdirectories)
        {
            foreach (var kv in _tokenResolvers)
            {
                directoryPath = directoryPath.Replace(kv.Key, kv.Value());
            }

            var directoryInfo = new DirectoryInfo(directoryPath);
            if (!directoryInfo.Exists)
            {
                LoggerScoped.LogWarning($"UnityReloader: Directory: '{directoryPath}' does not exist, make sure file-watcher setup is correct. You can access via: Tools -> Unity Reloader -> File Watcher (Advanced Setup)");
            }

            switch ((FileWatcherImplementation)UnityReloaderPreference.FileWatcherImplementationInUse.GetEditorPersistedValueOrDefault())
            {
                case FileWatcherImplementation.UnityDefault:
                    var fileWatcher = new FileSystemWatcher();
                    fileWatcher.Path = directoryInfo.FullName;
                    fileWatcher.IncludeSubdirectories = includeSubdirectories;
                    fileWatcher.Filter = filter;
                    fileWatcher.NotifyFilter = NotifyFilters.LastWrite;
                    fileWatcher.Changed += OnWatchedFileChange;
                    fileWatcher.EnableRaisingEvents = true;
                    _watchers.Add(fileWatcher);
                    break;

#if UNITY_2021_1_OR_NEWER && UNITY_EDITOR_WIN
                case FileWatcherImplementation.DirectWindowsApi:
                    var windowsFileSystemWatcher = new WindowsFileSystemWatcher()
                    {
                        Path = directoryInfo.FullName,
                        IncludeSubdirectories = includeSubdirectories,
                        Filter = filter,
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
                    };
                    windowsFileSystemWatcher.Changed += OnWatchedFileChange;
                    windowsFileSystemWatcher.Renamed += (source, e) =>
                    {
                        if (e.Name.EndsWith(".cs"))
                            OnWatchedFileChange(source, e);
                    };
                    windowsFileSystemWatcher.EnableRaisingEvents = true;
                    _watchers.Add(windowsFileSystemWatcher);
                    break;
#endif

                case FileWatcherImplementation.CustomPolling:
                    CustomFileWatcher.InitializeSingularFilewatcher(directoryPath, filter, includeSubdirectories);
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void InitializeFromSetupEntries()
        {
            var entries = UnityReloaderPreference.FileWatcherSetupEntries.GetElementsTyped();
            if (entries.Count == 0)
            {
                LoggerScoped.LogWarning($"There are no file watcher setup entries. Tool will not be able to pick changes automatically");
            }

            foreach (var entry in entries)
            {
                StartWatching(entry.path, entry.filter, entry.includeSubdirectories);
            }
        }

        private void OnWatchedFileChange(object source, FileSystemEventArgs e)
        {
            if (_shouldIgnoreChange()) return;

            var filePathToUse = e.FullPath;
            if (!File.Exists(filePathToUse))
            {
                if (!TryWorkaroundForUnityFileWatcherBug(e, ref filePathToUse))
                    return;
            }

            AddFileChangeToProcess(filePathToUse);
        }

        private bool TryWorkaroundForUnityFileWatcherBug(FileSystemEventArgs e, ref string filePathToUse)
        {
            LoggerScoped.LogWarning(@"Unity Reloader - Unity File Path Bug - Warning!
Path for changed file passed by Unity does not exist. This is a known editor bug, more info: https://issuetracker.unity3d.com/issues/filesystemwatcher-returns-bad-file-path

Best course of action is to update editor as issue is already fixed in newer (minor and major) versions.

As a workaround asset will try to resolve paths via directory search.

Workaround will search in all folders (under project root) and will use first found file. This means it's possible it'll pick up wrong file as there's no directory information available.");

            var changedFileName = new FileInfo(filePathToUse).Name;
            var fileFoundInAssets = Directory.GetFiles(_dataPath, changedFileName, SearchOption.AllDirectories);
            if (fileFoundInAssets.Length == 0)
            {
                LoggerScoped.LogError($"FileWatcherBugWorkaround: Unable to find file '{changedFileName}', changes will not be reloaded. Please update unity editor.");
                return false;
            }
            else if (fileFoundInAssets.Length == 1)
            {
                LoggerScoped.Log($"FileWatcherBugWorkaround: Original Unity passed file path: '{e.FullPath}' adjusted to found: '{fileFoundInAssets[0]}'");
                filePathToUse = fileFoundInAssets[0];
                return true;
            }
            else
            {
                LoggerScoped.LogWarning($"FileWatcherBugWorkaround: Multiple files found. Original Unity passed file path: '{e.FullPath}' adjusted to found: '{fileFoundInAssets[0]}'");
                filePathToUse = fileFoundInAssets[0];
                return true;
            }
        }
    }
}
