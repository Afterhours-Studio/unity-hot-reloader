using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ImmersiveVrToolsCommon.Runtime.Logging;

namespace UnityReloader.Editor
{
    public class ChangeBatcher
    {
        private const int MsThresholdForDuplicateDetection = 500;

        private readonly ConcurrentQueue<string> _queue;
        private readonly Func<bool> _shouldIgnoreChange;
        private readonly List<DynamicFileHotReloadState> _entries = new List<DynamicFileHotReloadState>();

        public IReadOnlyList<DynamicFileHotReloadState> Entries => _entries;

        public ChangeBatcher(ConcurrentQueue<string> queue, Func<bool> shouldIgnoreChange)
        {
            _queue = queue;
            _shouldIgnoreChange = shouldIgnoreChange;
        }

        public void Drain(IEnumerable<string> currentExclusions)
        {
            while (_queue.TryDequeue(out var filePath))
            {
                if (_shouldIgnoreChange()) continue;

                if (currentExclusions != null && currentExclusions.Any(fp => filePath.Replace("\\", "/").EndsWith(fp)))
                {
                    LoggerScoped.LogWarning($"UnityReloader: File: '{filePath}' changed, but marked as exclusion. Hot-Reload will not be performed. You can manage exclusions via" +
                                            $"\r\nRight click context menu (Unity Reloader > Add / Remove Hot-Reload exclusion)" +
                                            $"\r\nor via Tools -> Unity Reloader -> Start Screen -> Exclusion menu");
                    continue;
                }

                var isDuplicate = _entries
                    .Any(f => f.FullFileName == filePath
                              && (DateTime.UtcNow - f.FileChangedOn).TotalMilliseconds < MsThresholdForDuplicateDetection);
                if (isDuplicate)
                {
                    LoggerScoped.LogWarning($"UnityReloader: Looks like change to: {filePath} have already been added for processing. This can happen if you have multiple file watchers set in a way that they overlap.");
                    continue;
                }

                _entries.Add(new DynamicFileHotReloadState(filePath, DateTime.UtcNow));
            }
        }
    }
}
