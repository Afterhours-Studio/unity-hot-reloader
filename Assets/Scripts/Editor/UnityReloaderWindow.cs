using System;
using System.Collections.Generic;
using ImmersiveVRTools.Editor.Common.WelcomeScreen;
using ImmersiveVRTools.Editor.Common.WelcomeScreen.PreferenceDefinition;
using UnityEditor;
using UnityEngine;

namespace UnityReloader.Editor
{
    public sealed class UnityReloaderWindow : EditorWindow
    {
        private static readonly string[] Tabs = { "Reload", "Settings", "Exclusions", "Activity", "About" };
        private int _tabIndex;
        private Vector2 _scrollPos;

        private static readonly Color Accent = new(0.24f, 0.44f, 0.74f);
        private static readonly Color AccentDim = new(0.22f, 0.42f, 0.72f);
        private static readonly Color AccentGlow = new(0.40f, 0.65f, 1f, 0.15f);
        private static bool Pro => EditorGUIUtility.isProSkin;
        private static Color C(float v) => new(v, v, v);
        private static Color C(float v, float a) => new(v, v, v, a);
        private static Color HeaderBg => Pro ? C(0.13f) : C(0.80f);
        private static Color TabBarBg => Pro ? C(0.16f) : C(0.84f);
        private static Color ActiveTabBg => Pro ? C(0.21f) : C(0.94f);
        private static Color SepColor => Pro ? C(0.28f) : C(0.76f);
        private static Color MutedText => Pro ? C(0.50f) : C(0.42f);
        private static Color SubText => Pro ? C(0.60f) : C(0.35f);
        private static Color SuccessText => new(0.28f, 0.78f, 0.45f);
        private static Color WarningText => new(0.95f, 0.75f, 0.15f);

        private static readonly Color AccentHover = new(0.28f, 0.50f, 0.80f);
        private static readonly Color BtnGray = new(0.35f, 0.35f, 0.35f);
        private static readonly Color BtnGrayHover = new(0.45f, 0.45f, 0.45f);

        // ── Cached Styles ────────────────────────────────
        private static bool s_StylesBuiltForPro;
        private static bool s_StylesInitialized;
        private static GUIStyle s_HeaderTitle;
        private static GUIStyle s_VersionBadge;
        private static GUIStyle s_CardTitle;
        private static GUIStyle s_BtnLabel;
        private static GUIStyle s_MiniHint;
        private static GUIStyle s_TabLabel;
        private static GUIStyle s_ToggleTitle;
        private static GUIStyle s_ToggleDesc;
        private static GUIStyle s_ToggleState;
        private static GUIStyle s_KvLabel;
        private static GUIStyle s_KvValue;
        private static GUIStyle s_LogTime;
        private static GUIStyle s_LogFile;
        private static GUIStyle s_LogOk;
        private static GUIStyle s_LogFail;

        private static void EnsureStyles()
        {
            if (s_StylesInitialized && s_StylesBuiltForPro == Pro) return;
            s_StylesInitialized = true;
            s_StylesBuiltForPro = Pro;

            s_HeaderTitle = new GUIStyle
            {
                fontSize = 14, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Pro ? Color.white : C(0.12f) },
            };
            s_VersionBadge = new GUIStyle
            {
                fontSize = 9, alignment = TextAnchor.MiddleRight,
                padding = new RectOffset(0, 12, 0, 0),
                normal = { textColor = MutedText },
            };
            s_CardTitle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = Pro ? C(0.85f) : C(0.18f) },
            };
            s_BtnLabel = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };
            s_MiniHint = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = MutedText }, wordWrap = true, fontSize = 10,
            };
            s_TabLabel = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11, alignment = TextAnchor.MiddleCenter,
            };
            s_ToggleTitle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12, fontStyle = FontStyle.Bold,
                normal = { textColor = Pro ? C(0.88f) : C(0.15f) },
            };
            s_ToggleDesc = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = MutedText }, wordWrap = true, fontSize = 10,
            };
            s_ToggleState = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };
            s_KvLabel = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = MutedText }, fontSize = 11,
            };
            s_KvValue = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11, alignment = TextAnchor.MiddleRight,
                normal = { textColor = Pro ? C(0.85f) : C(0.16f) },
            };
            s_LogTime = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = MutedText }, fontSize = 10,
            };
            s_LogFile = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11, normal = { textColor = Pro ? C(0.82f) : C(0.16f) },
            };
            s_LogOk = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10, alignment = TextAnchor.MiddleRight,
                normal = { textColor = SuccessText },
            };
            s_LogFail = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10, alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.92f, 0.34f, 0.34f) },
            };
        }

        // ── Tab animation ────────────────────────────────
        private float _tabLineX;
        private float _tabLineFromX;
        private bool _tabLineInitialized;
        private double _tabAnimStart;
        private const float TabAnimDuration = 0.2f;

        // ── Inspect error state ──────────────────────────
        private DynamicFileHotReloadState _inspectError;

        // ── Activity log ─────────────────────────────────
        // Captured globally (even when window closed) so the user can open the
        // dashboard and review what hot-reloaded. Events fire on a background
        // thread, so the buffer is lock-guarded and Repaint is marshalled to the
        // main thread via EditorApplication.update.
        private readonly struct LogEntry
        {
            public readonly DateTime TimeUtc;
            public readonly string FileName;
            public readonly bool Success;
            public readonly string Error;
            public LogEntry(DateTime timeUtc, string fileName, bool success, string error)
            {
                TimeUtc = timeUtc; FileName = fileName; Success = success; Error = error;
            }
        }

        private const int MaxLogEntries = 200;
        private static readonly List<LogEntry> s_Log = new List<LogEntry>();
        private static readonly object s_LogLock = new object();
        private static volatile bool s_LogDirty;
        private static UnityReloaderWindow s_OpenInstance;

        [InitializeOnLoadMethod]
        private static void HookHotReloadLog()
        {
            var mgr = UnityReloaderManager.Instance;
            mgr.HotReloadSucceeded += states => AppendLog(states, true);
            mgr.HotReloadFailed += states => AppendLog(states, false);
            EditorApplication.update += PumpLogRepaint;
        }

        private static void AppendLog(List<DynamicFileHotReloadState> states, bool success)
        {
            if (states == null) return;
            lock (s_LogLock)
            {
                foreach (var s in states)
                {
                    var ok = success && !s.IsFailed;
                    s_Log.Add(new LogEntry(
                        s.HotSwappedOn ?? s.ErrorOn ?? DateTime.UtcNow,
                        System.IO.Path.GetFileName(s.FullFileName),
                        ok,
                        ok ? null : s.ErrorText));
                }
                if (s_Log.Count > MaxLogEntries)
                    s_Log.RemoveRange(0, s_Log.Count - MaxLogEntries);
            }
            s_LogDirty = true;
        }

        // Runs on the main thread; repaints the open window when new entries arrived.
        private static void PumpLogRepaint()
        {
            if (!s_LogDirty) return;
            s_LogDirty = false;
            if (s_OpenInstance != null) s_OpenInstance.Repaint();
        }

        // ── Lifecycle ────────────────────────────────────

        [MenuItem("Tools/Unity Reloader/Dashboard", false, 1999)]
        public static UnityReloaderWindow Open()
        {
            var w = GetWindow<UnityReloaderWindow>("Unity Reloader");
            w.minSize = new Vector2(480, 440);
            w.Show();
            return w;
        }

        private void OnEnable() => s_OpenInstance = this;
        private void OnDisable()
        {
            if (s_OpenInstance == this) s_OpenInstance = null;
        }

        public void OpenExclusionsTab()
        {
            _tabIndex = 2;
            Repaint();
        }

        public void OpenInspectError(DynamicFileHotReloadState state)
        {
            _inspectError = state;
            _tabIndex = 2;
            Repaint();
        }

        // ── Main Layout ─────────────────────────────────

        private void OnGUI()
        {
            EnsureStyles();
            DrawHeader();
            DrawTabBar();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            GUILayout.Space(4);

            switch (_tabIndex)
            {
                case 0: DrawReloadTab(); break;
                case 1: DrawSettingsTab(); break;
                case 2: DrawExclusionsTab(); break;
                case 3: DrawActivityTab(); break;
                case 4: DrawAboutTab(); break;
            }

            GUILayout.Space(4);
            EditorGUILayout.EndScrollView();
        }

        // ── Header ───────────────────────────────────────

        private void DrawHeader()
        {
            var rect = GUILayoutUtility.GetRect(0, 38, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rect, HeaderBg);

            var isPlaying = EditorApplication.isPlaying;
            var statusColor = isPlaying ? SuccessText : MutedText;
            if (Event.current.type == EventType.Repaint)
            {
                var dcX = rect.x + 16;
                var dcY = rect.y + rect.height / 2;
                EditorGUI.DrawRect(new Rect(dcX - 3, dcY - 4, 6, 8), statusColor);
                EditorGUI.DrawRect(new Rect(dcX - 4, dcY - 3, 8, 6), statusColor);
            }

            UnityEngine.GUI.Label(rect, "Unity Reloader", s_HeaderTitle);
            UnityEngine.GUI.Label(rect, "v1.3.0", s_VersionBadge);

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Accent);
        }

        // ── Tab Bar ──────────────────────────────────────

        private void DrawTabBar()
        {
            var barRect = GUILayoutUtility.GetRect(0, 32, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(barRect, TabBarBg);

            var tabW = barRect.width / Tabs.Length;
            var targetX = barRect.x + _tabIndex * tabW;

            if (!_tabLineInitialized)
            {
                _tabLineX = targetX;
                _tabLineFromX = targetX;
                _tabLineInitialized = true;
            }

            var animating = false;
            if (Mathf.Abs(_tabLineX - targetX) > 0.5f)
            {
                var t = (float)((EditorApplication.timeSinceStartup - _tabAnimStart) / TabAnimDuration);
                t = Mathf.Clamp01(t);
                t = 1f - (1f - t) * (1f - t);
                _tabLineX = Mathf.Lerp(_tabLineFromX, targetX, t);
                animating = t < 1f;
            }
            else
            {
                _tabLineX = targetX;
            }

            for (int i = 0; i < Tabs.Length; i++)
            {
                var tabRect = new Rect(barRect.x + i * tabW, barRect.y, tabW, barRect.height);
                bool active = _tabIndex == i;

                if (Event.current.type == EventType.Repaint && active)
                    EditorGUI.DrawRect(tabRect, ActiveTabBg);

                s_TabLabel.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
                s_TabLabel.normal.textColor = active ? (Pro ? Color.white : C(0.1f)) : MutedText;

                if (UnityEngine.GUI.Button(tabRect, Tabs[i], s_TabLabel))
                {
                    _tabLineFromX = _tabLineX;
                    _tabAnimStart = EditorApplication.timeSinceStartup;
                    _tabIndex = i;
                }
                EditorGUIUtility.AddCursorRect(tabRect, MouseCursor.Link);
            }

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(_tabLineX + 8, barRect.yMax - 3, tabW - 16, 3), Accent);
                EditorGUI.DrawRect(new Rect(_tabLineX + 4, barRect.yMax - 1, tabW - 8, 1), AccentGlow);
            }

            if (animating) Repaint();
        }

        // ── Tab: Reload ──────────────────────────────────

        // Mirrors UnityEditor's internal AssetPipelineAutoRefreshMode (kAutoRefreshMode).
        private const int AutoRefreshDisabled = 0;
        private const int AutoRefreshEnabled = 1;
        private const int AutoRefreshEnabledOutsidePlaymode = 2;

        private static void DrawStatusCard()
        {
            BeginCard("Status");

            var isPlaying = EditorApplication.isPlaying;
            var editorReload = (bool)UnityReloaderPreference.EnableExperimentalEditorHotReloadSupport.GetEditorPersistedValueOrDefault();
            var autoRefreshMode = EditorPrefs.GetInt("kAutoRefreshMode", EditorPrefs.GetBool("kAutoRefresh") ? AutoRefreshEnabled : AutoRefreshDisabled);

            DrawKV("Mode", isPlaying ? "Play Mode" : "Edit Mode");
            DrawKV("Editor Hot-Reload", editorReload ? "On" : "Off");
            DrawKV("Unity Auto-Refresh",
                autoRefreshMode == AutoRefreshDisabled ? "Disabled"
                : autoRefreshMode == AutoRefreshEnabledOutsidePlaymode ? "Outside Play Mode"
                : "Enabled");

            GUILayout.Space(4);
            if (isPlaying)
            {
                EditorGUILayout.HelpBox("In Play Mode: script changes hot-reload automatically (if Auto Hot-Reload is on).", MessageType.Info);
            }
            else if (!editorReload && autoRefreshMode == AutoRefreshDisabled)
            {
                EditorGUILayout.HelpBox(
                    "In Edit Mode with Unity Auto-Refresh disabled, changing a script does nothing until you recompile manually (Ctrl+R).\n\n" +
                    "To apply edit-mode changes automatically, enable 'Editor Hot-Reload' in Settings, or re-enable Unity Auto-Refresh via Edit -> Preferences -> Asset Pipeline.",
                    MessageType.Warning);
            }
            else if (!editorReload)
            {
                EditorGUILayout.HelpBox("In Edit Mode, changes apply on Unity's normal recompile. Enable 'Editor Hot-Reload' in Settings for instant edit-mode reloads.", MessageType.Info);
            }
            EndCard();
        }

        private void DrawReloadTab()
        {
            DrawStatusCard();

            BeginCard("Hot Reload");
            var watchOnlySpecified = (bool)UnityReloaderPreference.WatchOnlySpecified.GetEditorPersistedValueOrDefault();
            DrawToggleField(
                "Auto Hot-Reload",
                watchOnlySpecified
                    ? "Managed automatically in 'watch specified' mode (on when any watcher is set)."
                    : "Reload changed scripts automatically (Play Mode; also Edit Mode if Editor Hot-Reload is on). Required for any auto reload.",
                UnityReloaderPreference.EnableAutoReloadForChangedFiles,
                disabled: watchOnlySpecified);

            DrawToggleField(
                "Editor Hot-Reload",
                "Reload outside Play Mode. With Unity auto-refresh off, this applies edit-mode changes instantly instead of waiting for a manual recompile.",
                UnityReloaderPreference.EnableExperimentalEditorHotReloadSupport);

            DrawToggleField(
                "Watch Only Specified",
                "Watch files/folders you pick manually instead of the whole project.",
                UnityReloaderPreference.WatchOnlySpecified);

            DrawToggleField(
                "On-Demand Reload",
                "Skip auto file-watching. Apply changes only when you press Force Reload.",
                UnityReloaderPreference.EnableOnDemandReload);

            GUILayout.Space(4);
            using (LabelWidth(300))
            {
                ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.BatchScriptChangesAndReloadEveryNSeconds);
            }
            EndCard();

            BeginCard("Force Reload");
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to use Force Reload.", MessageType.Info);
            }
            else if (!(bool)UnityReloaderPreference.EnableOnDemandReload.GetEditorPersistedValueOrDefault())
            {
                EditorGUILayout.HelpBox(
                    "Force Reload (menu + hotkey) requires On-Demand Reload to be enabled.",
                    MessageType.Info);
                GUILayout.Space(4);
                DrawColorButton("Enable On-Demand Reload", Accent, AccentHover, () =>
                {
                    UnityReloaderPreference.EnableOnDemandReload.SetEditorPersistedValue(true);
                    Repaint();
                });
            }
            else
            {
                DrawColorButton("Force Reload Now", Accent, AccentHover, () =>
                {
                    UnityReloaderManager.Instance.TriggerReloadForChangedFiles();
                });
            }
            GUILayout.Space(2);
            GUILayout.Label("You can also use Tools -> Unity Reloader -> Force Reload or bind a hotkey via Edit -> Shortcuts.", s_MiniHint);
            EndCard();

            BeginCard("Fallback");
            DrawToggleField(
                "Auto Recompile On Failure",
                "When a hot reload fails (e.g. structural change like a new field), trigger a full Unity recompile so the change still applies. Recommended while Unity auto-refresh is off.",
                UnityReloaderPreference.AutoRecompileOnHotReloadFailure);
            EndCard();

            BeginCard("Assembly");
            DrawToggleField(
                "Force Lock Assemblies",
                "Lock assembly reload via code. Use if Unity still reloads in Play Mode with Auto-Refresh off.",
                UnityReloaderPreference.IsForceLockAssembliesViaCode);
            DrawToggleField(
                "Disable Fields-Changed Check",
                "Allow redirection when fields are added/removed. Needed for IL-weaving assets (e.g. Mirror).",
                UnityReloaderPreference.IsDidFieldsOrPropertyCountChangedCheckDisabled);
            DrawToggleField(
                "Project Window Indicator",
                "Show red / green bar in Project window for each file's hot-reload state.",
                UnityReloaderPreference.IsVisualHotReloadIndicationShownInProjectWindow);
            EndCard();
        }

        // ── Tab: Settings ────────────────────────────────

        private void DrawSettingsTab()
        {
            BeginCard("File Watchers");
            using (LabelWidth(240))
            {
                ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.FileWatcherImplementationInUse);
            }
            EditorGUILayout.HelpBox(
                "Default Unity - standard, may be slow on some versions\n" +
                "Direct Windows API - (experimental) faster, no symlinks\n" +
                "Custom Polling - (experimental) manual polling, slowest", MessageType.Info);
            GUILayout.Space(4);
            DrawFileWatcherList(UnityReloaderPreference.FileWatcherSetupEntries);
            EndCard();

            BeginCard("Experimental");
            DrawToggleField(
                "This-Argument Fix",
                "Rewrite methods that pass 'this' as an argument so they keep the correct type after reload. Disable if you see issues.",
                UnityReloaderPreference.EnableExperimentalThisCallLimitationFix);
            DrawToggleField(
                "Runtime Added Fields",
                "Render newly added fields in the Editor. Minor overhead from dynamic dictionary lookups.",
                UnityReloaderPreference.EnableExperimentalAddedFieldsSupport, warn: true);
            DrawToggleField(
                "Partial Class Support",
                "Support partial class definitions. Can be file-read heavy.",
                UnityReloaderPreference.IsPartialClassSupportEnabled, warn: true);
            GUILayout.Space(2);
            using (LabelWidth(420))
            {
                ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.TriggerDomainReloadIfOverNDynamicallyLoadedAssembles);
            }
            EndCard();

            BeginCard("Debugging");
            DrawToggleField(
                "Auto-Open Generated Source",
                "Open the generated/combined source file for debugging whenever it changes.",
                UnityReloaderPreference.IsAutoOpenGeneratedSourceFileOnChangeEnabled);
            DrawToggleField(
                "Write Rewrite Reason",
                "Annotate the changed file with comments explaining each code rewrite.",
                UnityReloaderPreference.DebugWriteRewriteReasonAsComment);
            EndCard();

            BeginCard("Logging");
            DrawToggleField(
                "Detailed Debug Logging",
                "Enable verbose logging with debug symbols.",
                UnityReloaderPreference.EnableDetailedDebugLogging);
            DrawToggleField(
                "How-To-Fix Messages",
                "Show helpful fix tips on compilation errors.",
                UnityReloaderPreference.LogHowToFixMessageOnCompilationError);
            DrawToggleField(
                "Suppress Auto-Reload Dialog",
                "Stop showing the auto-reload enabled warning dialog.",
                UnityReloaderPreference.StopShowingAutoReloadEnabledDialogBox);
            EndCard();
        }

        // ── Tab: Exclusions ──────────────────────────────

        private void DrawExclusionsTab()
        {
            if (_inspectError != null)
            {
                BeginCard("Last Error");
                EditorGUILayout.HelpBox(
                    $"File: {_inspectError.FullFileName}\n\n" +
                    $"Error: {_inspectError.ErrorText}",
                    MessageType.Error);
                GUILayout.Space(4);
                if (GUILayout.Button("Clear"))
                {
                    _inspectError = null;
                }
                EndCard();
            }

            BeginCard("File Exclusions");
            GUILayout.Label(
                "Manage exclusions via right-click context menu:\n" +
                "  Unity Reloader -> Add Hot-Reload Exclusion\n" +
                "  Unity Reloader -> Remove Hot-Reload Exclusion", s_MiniHint);
            GUILayout.Space(4);
            DrawStringListPref(UnityReloaderPreference.FilesExcludedFromHotReload, readOnly: true);
            EndCard();

            BeginCard("Reference Exclusions");
            EditorGUILayout.HelpBox(
                "Asset removes ExCSS.Unity by default (collides with Tuple type). " +
                "Remove from list if you need that library.", MessageType.Warning);
            GUILayout.Space(4);
            DrawStringListPref(UnityReloaderPreference.ReferencesExcludedFromHotReload, readOnly: false);
            EndCard();

            BeginCard("Script Overrides");
            GUILayout.Label(
                "User Script Rewrite Overrides allow you to fix compilation issues on a per-file basis.\n\n" +
                "Manage via right-click context menu:\n" +
                "  Unity Reloader -> Add / Open User Script Rewrite Override\n" +
                "  Unity Reloader -> Remove User Script Rewrite Override\n" +
                "  Unity Reloader -> Show User Script Rewrite Overrides", s_MiniHint);
            EndCard();
        }

        // Compact custom renderer for the file-watcher object list (replaces the DLL's
        // ReorderableList which leaves an empty footer/drag strip). Persists as JSON via
        // the pref's list API. Does not flag FileWatcherSetupEntriesChanged on text edits
        // (matches DLL behaviour - changes apply on next watcher re-init / restart - so the
        // watcher does not thrash on partial paths while typing).
        private static void DrawFileWatcherList(JsonObjectListProjectEditorPreferenceDefinition<FileWatcherSetupEntry> pref)
        {
            var items = pref.GetElementsTyped();
            var changed = false;
            var removeAt = -1;

            for (int i = 0; i < items.Count; i++)
            {
                var e = items[i];

                EditorGUILayout.BeginVertical(EditorStyles.helpBox); // container box per watcher

                // Row 1: Path (label aligned via fixed-width LabelField on the same line as the field)
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Path", GUILayout.Width(38));
                var path = EditorGUILayout.TextField(e.path);
                EditorGUILayout.EndHorizontal();

                // Row 2: Filter + Subdirs toggle + remove
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Filter", GUILayout.Width(38));
                var filter = EditorGUILayout.TextField(e.filter, GUILayout.Width(90));
                GUILayout.FlexibleSpace();
                var sub = EditorGUILayout.ToggleLeft("Subdirs", e.includeSubdirectories, GUILayout.Width(70));
                if (GUILayout.Button("Remove", GUILayout.Width(64)))
                    removeAt = i;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                GUILayout.Space(3);

                if (path != e.path || filter != e.filter || sub != e.includeSubdirectories)
                {
                    e.path = path;
                    e.filter = filter;
                    e.includeSubdirectories = sub;
                    changed = true;
                }
            }

            if (removeAt >= 0) { items.RemoveAt(removeAt); changed = true; }

            GUILayout.Space(2);
            if (GUILayout.Button("Add Watcher", GUILayout.Width(110)))
            {
                items.Add(new FileWatcherSetupEntry(
                    UnityReloaderManager.FileWatcherReplacementTokenForApplicationDataPath, "*.cs", true));
                changed = true;
            }

            if (changed)
            {
                var json = new List<string>();
                foreach (var it in items) json.Add(it.Serialize());
                pref.SetEditorPersistedValue(json);
            }
        }

        // Compact replacement for the DLL's ReorderableList renderer (which leaves an
        // empty footer/drag strip). Reads/writes via the pref's public list API.
        private static void DrawStringListPref(StringListProjectEditorPreferenceDefinition pref, bool readOnly)
        {
            var items = new List<string>(pref.GetElements());
            var changed = false;
            var removeAt = -1;

            for (int i = 0; i < items.Count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox); // container box per entry
                EditorGUILayout.BeginHorizontal();
                if (readOnly)
                {
                    GUILayout.Label("•", s_MiniHint, GUILayout.Width(12));
                    EditorGUILayout.SelectableLabel(items[i], s_LogFile, GUILayout.Height(16));
                }
                else
                {
                    var v = EditorGUILayout.TextField(items[i]);
                    if (v != items[i]) { items[i] = v; changed = true; }
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Remove", GUILayout.Width(64)))
                        removeAt = i;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                GUILayout.Space(3);
            }

            if (removeAt >= 0) { items.RemoveAt(removeAt); changed = true; }

            if (readOnly)
            {
                if (items.Count == 0)
                    GUILayout.Label("(none)", s_MiniHint);
            }
            else
            {
                GUILayout.Space(2);
                if (GUILayout.Button("Add Reference", GUILayout.Width(120)))
                {
                    items.Add("NewReference.dll");
                    changed = true;
                }
            }

            if (changed)
                pref.SetEditorPersistedValue(items);
        }

        // ── Tab: Activity ────────────────────────────────

        private void DrawActivityTab()
        {
            BeginCard("Hot Reload Activity");

            int count;
            LogEntry[] entries;
            lock (s_LogLock)
            {
                count = s_Log.Count;
                entries = s_Log.ToArray();
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"{count} event(s) this session", s_MiniHint);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Recompile", GUILayout.Width(90)))
            {
                lock (s_LogLock) s_Log.Clear();
                UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
            }
            using (new EditorGUI.DisabledScope(count == 0))
            {
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                {
                    lock (s_LogLock) s_Log.Clear();
                }
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);

            if (count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No hot-reloads yet. Enter Play Mode (or enable Editor Hot-Reload in Settings) and edit a script to see activity here.",
                    MessageType.Info);
                EndCard();
                return;
            }

            for (int i = entries.Length - 1; i >= 0; i--) // newest first
                DrawLogRow(entries[i]);

            EndCard();
        }

        private void DrawLogRow(LogEntry e)
        {
            EditorGUILayout.BeginHorizontal();

            var dotRect = GUILayoutUtility.GetRect(8, 16, GUILayout.Width(8));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(dotRect.x, dotRect.y + 5, 6, 6),
                    e.Success ? SuccessText : new Color(0.92f, 0.34f, 0.34f));

            GUILayout.Label(e.TimeUtc.ToLocalTime().ToString("HH:mm:ss"), s_LogTime, GUILayout.Width(58));
            GUILayout.Label(e.FileName, s_LogFile);
            GUILayout.FlexibleSpace();
            GUILayout.Label(e.Success ? "Reloaded" : "Failed", e.Success ? s_LogOk : s_LogFail, GUILayout.Width(70));

            EditorGUILayout.EndHorizontal();

            if (!e.Success && !string.IsNullOrEmpty(e.Error))
                EditorGUILayout.HelpBox(Truncate(e.Error, 400), MessageType.Error);

            GUILayout.Space(2);
        }

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max) + " ...";

        // ── Tab: About ───────────────────────────────────

        private void DrawAboutTab()
        {
            BeginCard("Unity Reloader");
            GUILayout.Label("Hot Reload implementation for Unity.", EditorStyles.label);
            GUILayout.Space(8);

            DrawKV("Version", "1.3.0");
            DrawKV("Unity", "2022.3+");
            DrawKV("Author", "h1dr0n");
            DrawKV("License", "MIT");

            GUILayout.Space(8);
            GUILayout.Label(
                "1. Enter Play Mode\n" +
                "2. Make a change to any .cs file\n" +
                "3. See results instantly without restarting", EditorStyles.label);
            EndCard();

            BeginCard("Callbacks");
            GUILayout.Label(
                "Execute custom code on hot reload by adding methods to your scripts:", s_MiniHint);
            GUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "void OnScriptHotReload()\n" +
                "{\n" +
                "    // access instance via 'this'\n" +
                "}\n\n" +
                "static void OnScriptHotReloadNoInstance()\n" +
                "{\n" +
                "    // no instance needed\n" +
                "}", MessageType.None);
            EndCard();
        }

        // ── UI Primitives ────────────────────────────────

        private static void BeginCard(string title)
        {
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            GUILayout.Space(14);
            GUILayout.BeginVertical();

            GUILayout.Label(title, s_CardTitle);

            var lineRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(lineRect, SepColor);

            GUILayout.Space(6);
        }

        private static void EndCard()
        {
            GUILayout.Space(6);
            GUILayout.EndVertical();
            GUILayout.Space(14);
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        private static void DrawKV(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, s_KvLabel, GUILayout.Width(120), GUILayout.Height(18));
            EditorGUILayout.LabelField(value, s_KvValue, GUILayout.Height(18));
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawToggleField(
            string title,
            string description,
            ToggleProjectEditorPreferenceDefinition pref,
            bool disabled = false,
            bool warn = false)
        {
            const float SwitchW = 54f;
            const float SwitchH = 22f;
            var value = (bool)pref.GetEditorPersistedValueOrDefault();

            var row = EditorGUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label(title, s_ToggleTitle, GUILayout.ExpandWidth(false));
            GUILayout.Label(description, s_ToggleDesc, GUILayout.MaxWidth(360));
            GUILayout.EndVertical();

            GUILayout.FlexibleSpace();
            var swRect = GUILayoutUtility.GetRect(SwitchW, SwitchH, GUILayout.Width(SwitchW), GUILayout.Height(SwitchH));
            swRect.y = row.y + (row.height > 0 ? (row.height - SwitchH) / 2f : 0);

            var on = value && !disabled;
            var onColor = warn ? new Color(0.85f, 0.6f, 0.12f) : Accent;
            var trackColor = disabled ? (Pro ? C(0.22f) : C(0.70f))
                : on ? onColor
                : (Pro ? C(0.30f) : C(0.62f));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(swRect, trackColor);
                var knobX = on ? swRect.xMax - SwitchH + 3 : swRect.x + 3;
                EditorGUI.DrawRect(new Rect(knobX, swRect.y + 3, SwitchH - 6, SwitchH - 6),
                    disabled ? C(0.5f) : Color.white);

                // Label sits opposite the knob so they never overlap.
                var labelRect = on
                    ? new Rect(swRect.x, swRect.y, SwitchW - SwitchH, SwitchH)
                    : new Rect(swRect.x + SwitchH, swRect.y, SwitchW - SwitchH, SwitchH);
                UnityEngine.GUI.Label(labelRect, on ? "ON" : "OFF", s_ToggleState);
            }

            if (!disabled)
            {
                EditorGUIUtility.AddCursorRect(swRect, MouseCursor.Link);
                if (Event.current.type == EventType.MouseDown && swRect.Contains(Event.current.mousePosition))
                {
                    pref.SetEditorPersistedValue(!value);
                    Event.current.Use();
                }
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

        private static void DrawColorButton(string text, Color color, Color hoverColor, Action onClick)
        {
            var btnRect = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            var hover = btnRect.Contains(Event.current.mousePosition);

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(btnRect, hover ? hoverColor : color);

            UnityEngine.GUI.Label(btnRect, text, s_BtnLabel);

            if (Event.current.type == EventType.MouseDown && btnRect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                onClick?.Invoke();
            }
            EditorGUIUtility.AddCursorRect(btnRect, MouseCursor.Link);
        }

        private static LabelWidthScope LabelWidth(float width) => new(width);

        private readonly struct LabelWidthScope : IDisposable
        {
            private readonly float _prev;
            public LabelWidthScope(float width)
            {
                _prev = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = width;
            }
            public void Dispose() => EditorGUIUtility.labelWidth = _prev;
        }
    }
}
