using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityReloader.Editor.Compilation;
using UnityReloader.Editor.Compilation.ScriptGenerationOverrides;
using UnityReloader.Runtime;
using ImmersiveVRTools.Editor.Common.Utilities;
using ImmersiveVRTools.Editor.Common.WelcomeScreen;
using ImmersiveVRTools.Editor.Common.WelcomeScreen.GuiElements;
using ImmersiveVRTools.Editor.Common.WelcomeScreen.PreferenceDefinition;
using ImmersiveVRTools.Editor.Common.WelcomeScreen.Utilities;
using ImmersiveVrToolsCommon.Runtime.Logging;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityReloader.Editor
{
    public class UnityReloaderWelcomeScreen : ProductWelcomeScreenBase 
    {
        public static string BaseUrl = "https://immersivevrtools.com";
        public static string GenerateGetUpdatesUrl(string userId, string versionId)
        {
            //WARN: the URL can sometimes be adjusted, make sure updated correctly
            return $"{BaseUrl}/updates/fast-script-reload/{userId}?CurrentVersion={versionId}";
        }
        public static string VersionId = "1.0.3";
        private static readonly string ProjectIconName = "ProductIcon64";
        public static readonly string ProjectName = "fast-script-reload";

        private static Vector2 _WindowSizePx = new Vector2(650, 500);
        private static string _WindowTitle = "Unity Reloader";

        public static ChangeMainViewButton ExclusionsSection { get; private set; }
        public static ChangeMainViewButton EditorHotReloadSection { get; private set; }
        public static ChangeMainViewButton NewFieldsSection { get; private set; }
        public static ChangeMainViewButton UserScriptRewriteOverrides { get; private set; }
        public static ChangeMainViewButton InspectError { get; private set; }

        public static DynamicFileHotReloadState LastInspectFileHotReloadStateError;

        public void OpenInspectError(DynamicFileHotReloadState fileHotReloadState)
        {
            LastInspectFileHotReloadStateError = fileHotReloadState;
            InspectError.OnClick(this);
        }
        
        public void OpenExclusionsSection()
        {
            ExclusionsSection.OnClick(this);
        }
        
        public void OpenUserScriptRewriteOverridesSection()
        {
            UserScriptRewriteOverrides.OnClick(this);
        }
        
        public void OpenEditorHotReloadSection()
        {
            EditorHotReloadSection.OnClick(this);
        }

        public void OpenNewFieldsSection()
        {
            NewFieldsSection.OnClick(this);
        }
        
        private static readonly ScrollViewGuiSection MainScrollViewSection = new ScrollViewGuiSection(
            "", (screen) =>
            {
                GUILayout.Label(
@"Unity Reloader - Hot Reload for Unity.

There are some options that you can customise, those are visible in sections on the left.

You can always get back to this screen via:
1) Tools -> Unity Reloader -> Start Screen
2) Edit -> Preferences... -> Unity Reloader", screen.TextStyle, GUILayout.ExpandHeight(true));

                GUILayout.Label("Enabled Features:", screen.LabelStyle);
                using (LayoutHelper.LabelWidth(350))
                {
                    /// When <see cref="UnityReloaderPreference.WatchOnlySpecified"/> is enabled, <see cref="UnityReloaderPreference.EnableAutoReloadForChangedFiles"/> state is handled automatically (disabled when empty file watcher)
                    using (new EditorGUI.DisabledGroupScope((bool)UnityReloaderPreference.WatchOnlySpecified.GetEditorPersistedValueOrDefault()))
                    {
                        ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.EnableAutoReloadForChangedFiles);
                    }
                    
                    RenderSettingsWithCheckLimitationsButton(UnityReloaderPreference.EnableExperimentalAddedFieldsSupport, true, () => ((UnityReloaderWelcomeScreen)screen).OpenNewFieldsSection());
                    RenderSettingsWithCheckLimitationsButton(UnityReloaderPreference.EnableExperimentalEditorHotReloadSupport, false, () => ((UnityReloaderWelcomeScreen)screen).OpenEditorHotReloadSection());
                }
            }
        );

        private static void RenderSettingsWithCheckLimitationsButton(ToggleProjectEditorPreferenceDefinition preferenceDefinition, bool allowChange, Action onCheckLimitationsClick)
        {
            EditorGUILayout.BeginHorizontal();
            if (!allowChange)
            {
                using (LayoutHelper.LabelWidth(313))
                {
                    EditorGUILayout.LabelField(preferenceDefinition.Label);
                }
            }
            else
            {
                ProductPreferenceBase.RenderGuiAndPersistInput(preferenceDefinition);
            }

            if (GUILayout.Button("Check limitations"))
            {
                onCheckLimitationsClick();
            }

            EditorGUILayout.EndHorizontal();
        }

        private static readonly List<GuiSection> LeftSections = CreateLeftSections(new List<ChangeMainViewButton>
            {
                new ChangeMainViewButton("On-Device\r\nHot-Reload",  
                    (screen) =>
                    {
                        EditorGUILayout.LabelField("Live Script Reload", screen.BoldTextStyle); 
                        
                        GUILayout.Space(10);
                        EditorGUILayout.LabelField(@"There's an extension to this asset that'll allow you to include Hot-Reload capability in builds (standalone / Android), please click the button below to learn more.", screen.TextStyle);

                        GUILayout.Space(20);
                        if (GUILayout.Button("View Live Script Reload on Asset Store"))
                        {
                            Application.OpenURL($"{RedirectBaseUrl}/live-script-reload-extension");
                        }
                    }
                )
            }, 
            new LaunchSceneButton("Basic Example", (s) =>
            {
                var path = GetScenePath("ExampleScene");
                if (path == null)
                {
                    var userChoice = EditorUtility.DisplayDialogComplex("Example not found",
                        "Example scene was not found. If you got FSR via package manager, please make sure to import samples.", 
                        "Ok", "Close", "Open Package Manager");
                    if (userChoice == 2)
                    {
                        UnityEditor.PackageManager.UI.Window.Open("com.fastscriptreload");
                    }
                }

                return path;
            }, (screen) =>
            {
                GUILayout.Label(
                    $@"Asset is very simple to use:

1) Hit play to start.
2) Go to 'FunctionLibrary.cs' ({@"Assets/UnityReloader/Examples/Scripts/"})", screen.TextStyle);
                
                CreateOpenFunctionLibraryOnRippleMethodButton();

                
                GUILayout.Label(
                    $@"3) Change 'Ripple' method (eg change line before return statement to 'p.z = v * 10'
4) Save file
5) See change immediately",
                    screen.TextStyle
                );
                
                GUILayout.Space(10);
                EditorGUILayout.HelpBox("There are some limitations to what can be Hot-Reloaded, documentation lists them under 'limitations' section.", MessageType.Warning);
            }), MainScrollViewSection);

        static void OnScriptHotReloadNoInstance() 
        { 
            Debug.Log("Reloaded - start");
            LastInspectFileHotReloadStateError = (DynamicFileHotReloadState) HarmonyLib.AccessTools
                .Field("UnityReloader.Editor.UnityReloaderWelcomeScreen:LastInspectFileHotReloadStateError")
                .GetValue(null);
            Debug.Log("Reloaded - end");
        }
        
        protected static List<GuiSection> CreateLeftSections(List<ChangeMainViewButton> additionalSections, LaunchSceneButton launchSceneButton, ScrollViewGuiSection mainScrollViewSection)
        {
            return new List<GuiSection>() {
                new GuiSection("", new List<ClickableElement>
                {
                    (InspectError = new ChangeMainViewButton("Error - Inspect", (screen) =>
                    {
            if (UnityReloaderWelcomeScreen.LastInspectFileHotReloadStateError == null)
            {
                GUILayout.Label(
                    @"No error selected. Possibly it's been cleared by domain reload.

Choose other tab on the left.", screen.TextStyle);
                return;
            }


            EditorGUILayout.HelpBox(
                @"Errors are usually down to compilation / rewrite issue. There are ways you can mitigate those.",
                MessageType.Warning);
            GUILayout.Space(10);

            GUILayout.Label("1) Review compilation error, especially looking for specific lines that caused error:");
            EditorGUILayout.HelpBox(
                @"For example following error below shows line 940 as causing compilation issue due to missing #endif directive.

System.Exception: Compiler failed to produce the assembly. 
Output: '<filepath>.SourceCodeCombined.cs(940,1): error CS1027: #endif directive expected'",
                MessageType.Info);

            GUILayout.Space(10);
            GUILayout.Label("Error:");
            GUILayout.TextArea(LastInspectFileHotReloadStateError.ErrorText);

            GUILayout.Space(10);
            if (GUILayout.Button("2) Click here to open generated file that failed to compile"))
            {
                InternalEditorUtility.OpenFileAtLineExternal(LastInspectFileHotReloadStateError.SourceCodeCombinedFilePath, 1);
            }

            GUILayout.Label(
                @"Error could be caused by a normal compilation issue that you created in source file 
(eg typo), in that case please fix and it'll recompile.

It's possible compilation fails due to existing limitation, while I work continuously 
on mitigating limitations it's best that you're aware where they are.

Please see documentation (link above) to understand them better 
They also contain workarounds if needed.");

            GUILayout.Space(10);
            GUILayout.Label(
                @"You can also create one-off override file that'll allow to specify
custom rewrites for methods.", screen.BoldTextStyle);
            if (GUILayout.Button("3) Create User Defined Script Override"))
            {
                ScriptGenerationOverridesManager.AddScriptOverride(new FileInfo(UnityReloaderWelcomeScreen.LastInspectFileHotReloadStateError.FullFileName));
            }

            GUILayout.Space(10);
            GUILayout.Label(@"You can help make FSR better!", screen.BoldTextStyle);
            EditorGUILayout.HelpBox(@"Could you please assist in improving the tool by providing me with the details of the error? 
I can use those to recreate the issue and fix the limitation.

Simply click the button below - it'll create a support pack automatically.

Support pack contains:
1) Original script file that caused error
2) Patched script file that was generated
3) Error message", MessageType.Warning);

            if (GUILayout.Button("4) Click here to create support-pack"))
            {
                try
                {
                    var folder = EditorUtility.OpenFolderPanel("Select Folder", "", "");
                    var sourceCodeCombinedFile = new FileInfo(LastInspectFileHotReloadStateError.SourceCodeCombinedFilePath);
                    var originalFile = new FileInfo(LastInspectFileHotReloadStateError.FullFileName);
                    File.Copy(LastInspectFileHotReloadStateError.SourceCodeCombinedFilePath, Path.Combine(folder, sourceCodeCombinedFile.Name));
                    File.Copy(LastInspectFileHotReloadStateError.FullFileName, Path.Combine(folder, originalFile.Name));
                    File.WriteAllText(Path.Combine(folder, "error-message.txt"), LastInspectFileHotReloadStateError.ErrorText);
                    
                    EditorUtility.DisplayDialog("Support Pack Created", $"Thanks!\r\n\r\nPlease send files from folder:\r\n'{folder}'\r\n\r\nto:\r\n\r\nsupport@immersivevrtools.com", "Ok, copy email to clipboard");
                    EditorGUIUtility.systemCopyBuffer = "support@immersivevrtools.com";
                }
                catch (Exception e)
                {
                    Debug.LogError($"Unable to create support pack., {e}");
                }
            }
                    })).WithShouldRender(() => LastInspectFileHotReloadStateError != null), 
                    new LastUpdateButton("New Update!", (screen) => LastUpdateUpdateScrollViewSection.RenderMainScrollViewSection(screen)),
                    new ChangeMainViewButton("Welcome", (screen) => mainScrollViewSection.RenderMainScrollViewSection(screen)),
                }),
                new GuiSection("Options", new List<ClickableElement>
                {
                    new ChangeMainViewButton("Reload", (screen) =>
                    {
                        const int sectionBreakHeight = 15;
                        GUILayout.Label(
                            @"Asset watches all script files and automatically hot-reloads on change, you can disable that behaviour and reload on demand.",
                            screen.TextStyle
                        );
                
                        using (new EditorGUI.DisabledGroupScope((bool)UnityReloaderPreference.WatchOnlySpecified.GetEditorPersistedValueOrDefault()))
                        using (LayoutHelper.LabelWidth(320))
                        {
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.EnableAutoReloadForChangedFiles);
                        }
                        GUILayout.Space(sectionBreakHeight);

                        using (LayoutHelper.LabelWidth(320))
                        {
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.WatchOnlySpecified);
                        }

                        if ((bool)UnityReloaderPreference.WatchOnlySpecified.GetEditorPersistedValueOrDefault())
                        {
                            EditorGUILayout.HelpBox(@"With manual watching you need to right click on file/folder in project window and select 'Watch File'", MessageType.Info);
                        }
                        GUILayout.Space(sectionBreakHeight);
                
                        EditorGUILayout.HelpBox("On demand reload :\r\n(only works if you opted in below, this is to avoid unnecessary file watching)\r\nvia Tools -> Unity Reloader -> Force Reload, \r\nor by calling 'FastScriptIterationManager.Instance.TriggerReloadForChangedFiles()'", MessageType.Warning);
                        
                        using (LayoutHelper.LabelWidth(320))
                        {
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.EnableOnDemandReload);
                        }
                        
                        GUILayout.Space(sectionBreakHeight);

                        GUILayout.Label(
                            @"For performance reasons script changes are batched are reloaded every N seconds",
                            screen.TextStyle
                        );

                        using (LayoutHelper.LabelWidth(300))
                        {
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.BatchScriptChangesAndReloadEveryNSeconds);
                        }

                        GUILayout.Space(sectionBreakHeight);
                    
                        using (LayoutHelper.LabelWidth(350))
                        {
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.EnableExperimentalThisCallLimitationFix);
                        }
                        EditorGUILayout.HelpBox("Method calls utilizing 'this' will trigger compiler exception, if enabled tool will rewrite the calls to have proper type after adjustments." +
                                                "\r\n\r\nIn case you're seeing compile errors relating to 'this' keyword please let me know via support page. Also turning this setting off will prevent rewrite.", MessageType.Info);
                        
                        GUILayout.Space(sectionBreakHeight);
                        
                        using (LayoutHelper.LabelWidth(350))
                        {
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.IsForceLockAssembliesViaCode);
                        }
                        EditorGUILayout.HelpBox(
@"Sometimes Unity continues to reload assemblies on change in playmode even when Auto-Refresh is turned off.

Use this setting to force lock assemblies via code."
, MessageType.Info);
                        GUILayout.Space(sectionBreakHeight);
                        
                        using (LayoutHelper.LabelWidth(350))
                        {
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.IsDidFieldsOrPropertyCountChangedCheckDisabled);
                        }
                        EditorGUILayout.HelpBox("By default if you add / remove fields, tool will not redirect method calls for recompiled class." +
                                                "\r\nYou can also enable added-fields support (experimental)." +
                                                "\r\n\r\nSome assets however will use IL weaving to adjust your classes (eg Mirror) as a post compile step. In that case it's quite likely hot-reload will still work. " +
                                                "\r\n\r\nTick this box for tool to try and reload changes when that happens."
                            
                            , MessageType.Info);
                        GUILayout.Space(sectionBreakHeight);

                        using (LayoutHelper.LabelWidth(430))
                        {
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.IsVisualHotReloadIndicationShownInProjectWindow);
                        }
                        
                        GUILayout.Space(sectionBreakHeight);
                    }),
                    (UserScriptRewriteOverrides = new ChangeMainViewButton("User Script\r\nRewrite Overrides", (screen) =>
                    {
                        EditorGUILayout.HelpBox(
                            $@"For tool to work it'll need to slightly adjust your code to make it compilable. Sometimes due to existing limitations this can fail and you'll see an error.

You can specify custom script rewrite overrides, those are specified for specific parts of code that fail, eg method. 

It will help overcome limitations in the short run while I work on implementing proper solution."
                            , MessageType.Info);
                        
                        EditorGUILayout.HelpBox(
                            $@"To add:
1) right-click in project panel on the file that causes the issue. 
2) select Unity Reloader -> Add / Open User Script Rewrite Override

It'll open override file with template already in. You can read top comments that describe how to use it."
                            , MessageType.Warning);

                        EditorGUILayout.LabelField("Existing User Defined Script Overrides:", screen.BoldTextStyle);
                        Action executeAfterIteration = null;
                        foreach (var scriptOverride in ScriptGenerationOverridesManager.UserDefinedScriptOverrides)
                        {
                            EditorGUILayout.BeginHorizontal();
                            
                            EditorGUILayout.LabelField(scriptOverride.File.Name);
                            if (GUILayout.Button("Open"))
                            {
                                InternalEditorUtility.OpenFileAtLineExternal(scriptOverride.File.FullName, 0);
                            }
                            
                            if (GUILayout.Button("Delete"))
                            {
                                executeAfterIteration = () =>
                                {
                                    if (EditorUtility.DisplayDialog("Are you sure", "This will permanently remove override file.", "Delete", "Keep File"))
                                    {
                                        ScriptGenerationOverridesManager.TryRemoveScriptOverride(scriptOverride);
                                    }
                                };
                            }
                            
                            EditorGUILayout.EndHorizontal();
                        }
                        executeAfterIteration?.Invoke();
                    })),
                    (ExclusionsSection = new ChangeMainViewButton("Exclusions", (screen) => 
                    {
                        EditorGUILayout.HelpBox("Those are easiest to manage from Project window by right clicking on script file and selecting: " +
                                                "\r\nUnity Reloader -> Add Hot-Reload Exclusion " +
                                                "\r\nUnity Reloader -> Remove Hot-Reload Exclusion", MessageType.Info);
                        GUILayout.Space(10);
                
                        ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.FilesExcludedFromHotReload);
                    })),
                    new ChangeMainViewButton("Debugging", (screen) =>
                    {
                        EditorGUILayout.HelpBox(
                            @"To debug you'll need to set breakpoints in dynamically-compiled file. 

BREAKPOINTS IN ORIGINAL FILE WON'T BE HIT!", MessageType.Error);

                        EditorGUILayout.HelpBox(
@"You can do that via:
    - clicking link in console-window after change, eg
      'FSR: Files: FunctionLibrary.cs changed (click here to debug [in bottom details pane]) (...)'
      (it needs to be clicked in bottom details pane, double click will simply take you to log location)", MessageType.Warning);
                        GUILayout.Space(10);
                        
                        EditorGUILayout.HelpBox(@"Tool can also auto-open generated file on every change, to do so select below option", MessageType.Info);
                        using (LayoutHelper.LabelWidth(350))
                        {
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.IsAutoOpenGeneratedSourceFileOnChangeEnabled);
                            if ((bool)UnityReloaderPreference.IsAutoOpenGeneratedSourceFileOnChangeEnabled.GetEditorPersistedValueOrDefault())
                            {
                                EditorGUILayout.HelpBox(@"Do not edit debug file created. It'll be removed.", MessageType.Error);
                            }
                        }

                        GUILayout.Space(20);
                        using (LayoutHelper.LabelWidth(350))
                        {
                            EditorGUILayout.LabelField("Logging", screen.BoldTextStyle);
                            GUILayout.Space(5);
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.EnableDetailedDebugLogging);
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.LogHowToFixMessageOnCompilationError);
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.StopShowingAutoReloadEnabledDialogBox);
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.DebugWriteRewriteReasonAsComment);
                        }
                    })
                }.Concat(additionalSections).ToList()),
                new GuiSection("Experimental", new List<ClickableElement>
                {
                    (NewFieldsSection = new ChangeMainViewButton("New Fields", (screen) =>
                    {
#if LiveScriptReload_Enabled
                        EditorGUILayout.HelpBox(
                            @"On Device Reload (in running build) - Not Supported
If you enable - new fields WILL show in editor and work as expected but link with the device will be broken and changes won't be visible there!", MessageType.Error);
                        GUILayout.Space(10);
#endif
                        
                        EditorGUILayout.HelpBox(
                            @"Adding new fields is still in experimental mode, it will have issues. 

When you encounter them please get in touch (via any support links above) and I'll be sure to sort them out. Thanks!", MessageType.Error);
                        GUILayout.Space(10);
                        
                        EditorGUILayout.HelpBox(
                            @"Adding new fields will affect performance, behind the scenes your code is rewritten to access field via static dictionary.

Once you exit playmode and do a full recompile they'll turn to standard fields as you'd expect.

New fields will also show in editor - you can tweak them as normal variables.", MessageType.Warning);
                        GUILayout.Space(10);
                        
                        EditorGUILayout.HelpBox(
                            @"LIMITATIONS: (full list and more info in docs)
- outside classes can not call new fields added at runtime
- new fields will only show in editor if they were already used at least once", MessageType.Info);
                        GUILayout.Space(10);

                        using (LayoutHelper.LabelWidth(300))
                        {
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.EnableExperimentalAddedFieldsSupport);
                        }
                        GUILayout.Space(10);

                        if (Application.isPlaying)
                        {
                            EditorGUILayout.HelpBox(@"You're in playmode, for option to start working you need to restart playmode.", MessageType.Warning);
                        }

                        GUILayout.Space(10);
                    })),
                    (EditorHotReloadSection = new ChangeMainViewButton("Editor Hot-Reload", (screen) =>
                    {
                        EditorGUILayout.HelpBox(@"By default scripts hot-reload only in play-mode. Enable editor-mode hot reload here to apply changes without entering play mode.

Please make sure to read the limitations section as not all changes can be performed", MessageType.Info);
                        GUILayout.Space(10);
                        
                        using (LayoutHelper.LabelWidth(320))
                        {
                            var valueBefore = (bool)UnityReloaderPreference.EnableExperimentalEditorHotReloadSupport.GetEditorPersistedValueOrDefault();
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.EnableExperimentalEditorHotReloadSupport);
                            var valueAfter = (bool)UnityReloaderPreference.EnableExperimentalEditorHotReloadSupport.GetEditorPersistedValueOrDefault();
                            if (!valueBefore && valueAfter)
                            {
                                EditorUtility.DisplayDialog("Editor-mode Hot-Reload enabled",
                                    "Scripts will now hot-reload outside of play mode. " +
                                    "\r\n\r\nNot all changes can be applied this way - see the limitations section.",
                                    "Ok");
                                
#if UNITY_2019_3_OR_NEWER
                                CompilationPipeline.RequestScriptCompilation();
#elif UNITY_2017_1_OR_NEWER
                                 var editorAssembly = Assembly.GetAssembly(typeof(Editor));
                                 var editorCompilationInterfaceType = editorAssembly.GetType("UnityEditor.Scripting.ScriptCompilation.EditorCompilationInterface");
                                 var dirtyAllScriptsMethod = editorCompilationInterfaceType.GetMethod("DirtyAllScripts", BindingFlags.Static | BindingFlags.Public);
                                 dirtyAllScriptsMethod.Invoke(editorCompilationInterfaceType, null);
#endif
                            }
                        }
                        
                        GUILayout.Space(10);
                        
                        EditorGUILayout.HelpBox(@"Tool will automatically trigger full domain reload after number of hot-reloads specified below has been reached. 
This is to ensure dynamically created and loaded assembles are cleared out properly", MessageType.Info);
                        GUILayout.Space(10);
                        
                        using (LayoutHelper.LabelWidth(420))
                        {
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.TriggerDomainReloadIfOverNDynamicallyLoadedAssembles);
                        }
                        GUILayout.Space(10);
                    })),
                    new ChangeMainViewButton("Partial Class", (screen) =>
                    {
                        using (LayoutHelper.LabelWidth(350))
                        {
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.IsPartialClassSupportEnabled);
                        }
                        EditorGUILayout.HelpBox("Partial class support can be file-read heavy which could make FSR slower.", MessageType.Warning);
                    })
                }),
                new GuiSection("Advanced", new List<ClickableElement>
                {
                    new ChangeMainViewButton("File Watchers", (screen) => 
                    {
                        EditorGUILayout.HelpBox(
                            $@"Asset watches .cs files for changes. Unfortunately Unity's FileWatcher 
implementation has some performance issues.

By default all project directories can be watched, you can adjust that here.

path - which directory to watch
filter - narrow down files to match filter, eg all *.cs files (*.cs)
includeSubdirectories - whether child directories should be watched as well

{UnityReloaderManager.FileWatcherReplacementTokenForApplicationDataPath} - you can use that token and it'll be replaced with your /Assets folder"
                            , MessageType.Info);
                        
                        EditorGUILayout.HelpBox("Recompile after making changes for file watchers to re-load.", MessageType.Warning);
                        GUILayout.Space(10);

                        using (LayoutHelper.LabelWidth(240))
                        {
                            ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.FileWatcherImplementationInUse);
                        }
                        EditorGUILayout.HelpBox(
@"DefaultUnity - on some editor versions it could be slow or not trigger at all 
DirectWindowsApi - (experimental) uses Windows API directly, faster (symlinks not supported)
CustomPolling - (experimental) watches files by manual polling for changes, slowest. Make sure to narrow down watchers scope to script folders", MessageType.Info);

                        ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.FileWatcherSetupEntries);
                    }),
                    new ChangeMainViewButton("Exclude References", (screen) =>
                    {
                        EditorGUILayout.HelpBox(
                            $@"Asset pulls in all the references from changed assembly. If you're encountering some compilation errors relating to those - please use list below to exclude specific ones."
                            , MessageType.Info);
                        
                        EditorGUILayout.HelpBox($@"By default asset removes ExCSS.Unity as it collides with the Tuple type. If you need that library in changed code - please remove from the list", MessageType.Warning);
                         
                        ProductPreferenceBase.RenderGuiAndPersistInput(UnityReloaderPreference.ReferencesExcludedFromHotReload);
                    })
                }),
                new GuiSection("Launch Demo", new List<ClickableElement>
                {
                    launchSceneButton
                })
            };
        }

        private static readonly string RedirectBaseUrl = "https://immersivevrtools.com/redirect/fast-script-reload"; 
        private static readonly GuiSection TopSection = CreateTopSectionButtons(RedirectBaseUrl);

        protected static GuiSection CreateTopSectionButtons(string redirectBaseUrl)
        {
            return new GuiSection("Support", new List<ClickableElement>
                {
                    new OpenUrlButton("Documentation", $"{redirectBaseUrl}/documentation"),
                    new OpenUrlButton("Discord", $"{redirectBaseUrl}/discord"),
                    new OpenUrlButton("Github", $"{redirectBaseUrl}/github"),
                    new OpenUrlButton("Donate", $"{redirectBaseUrl}/donate", "sv_icon_name3")
                }
            );
        }

        private static readonly GuiSection BottomSection = new GuiSection(
            "I want to make this tool better. And I need your help!",
            $"Please spread the word and star github repo. Alternatively if you're in a position to make a donation I'd hugely appreciate that. It allows me to spend more time on the tool instead of paid client projects.",
            new List<ClickableElement>
            {
                new OpenUrlButton(" Star on Github", $"{RedirectBaseUrl}/github"),
                new OpenUrlButton(" Donate", $"{RedirectBaseUrl}/donate"),
            }
        );

        public override string WindowTitle { get; } = _WindowTitle;
        public override Vector2 WindowSizePx { get; } = _WindowSizePx;

        public static UnityReloaderWelcomeScreen Init()
        {
            return OpenWindow<UnityReloaderWelcomeScreen>(_WindowTitle, _WindowSizePx);
        }

#if !LiveScriptReload_Enabled
        [MenuItem("Tools/Unity Reloader/Force Reload", true, 1999)]
#endif
        public static bool ForceReloadValidate()
        {
            return EditorApplication.isPlaying && (bool)UnityReloaderPreference.EnableOnDemandReload.GetEditorPersistedValueOrDefault();
        }

#if !LiveScriptReload_Enabled
        [MenuItem("Tools/Unity Reloader/Force Reload", false, 1999)]
#endif
        public static void ForceReload()
        {
            if (!(bool)UnityReloaderPreference.EnableOnDemandReload.GetEditorPersistedValueOrDefault())
            {
                LoggerScoped.LogWarning("On demand hot reload is disabled, can't perform. You can enable it via 'Tools -> Unity Reloader -> Dashboard -> Reload -> Enable on demand reload'");
                return;
            }

            UnityReloaderManager.Instance.TriggerReloadForChangedFiles();
        }

        public void OnEnable()
        {
            OnEnableCommon(ProjectIconName);
        }

        public void OnGUI()
        {
            RenderGUI(LeftSections, TopSection, BottomSection, MainScrollViewSection);
        }
        
        protected static void CreateOpenFunctionLibraryOnRippleMethodButton()
        {
            if (GUILayout.Button("Open 'FunctionLibrary.cs'"))
            {
                var codeComponent = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets($"t:Script FunctionLibrary")[0]));
                CodeEditorManager.GotoScript(codeComponent, "Ripple");
            }
        }
    }

    public class UnityReloaderPreference : ProductPreferenceBase
    {
        public const string BuildSymbol_DetailedDebugLogging = "ImmersiveVrTools_DebugEnabled";
        
        public const string ProductName = "Unity Reloader";
        private static string[] ProductKeywords = new[] { "productivity", "tools" };
        
        /// <summary>Used to know when file watchers have changed from project window contextual menu (so when to update file watchers)</summary>
        public static bool FileWatcherSetupEntriesChanged = false;

        public static readonly IntProjectEditorPreferenceDefinition BatchScriptChangesAndReloadEveryNSeconds = new IntProjectEditorPreferenceDefinition(
            "Batch script changes and reload every N seconds", "BatchScriptChangesAndReloadEveryNSeconds", 1);

        public static readonly ToggleProjectEditorPreferenceDefinition EnableAutoReloadForChangedFiles = new ToggleProjectEditorPreferenceDefinition(
            "Enable auto Hot-Reload for changed files (in play mode)", "EnableAutoReloadForChangedFiles", true);
        
        public static readonly ToggleProjectEditorPreferenceDefinition EnableOnDemandReload = new ToggleProjectEditorPreferenceDefinition(
            "Enable on demand hot reload", "EnableOnDemandReload", false);
        
        public static readonly ToggleProjectEditorPreferenceDefinition EnableExperimentalThisCallLimitationFix = new ToggleProjectEditorPreferenceDefinition(
            "(Experimental) Enable method calls with 'this' as argument fix", "EnableExperimentalThisCallLimitationFix", true, (object newValue, object oldValue) =>
            {
                DynamicCompilationBase.EnableExperimentalThisCallLimitationFix = (bool)newValue;
            },
            (value) =>
            {
                DynamicCompilationBase.EnableExperimentalThisCallLimitationFix = (bool)value;
            });
    
        public static readonly StringListProjectEditorPreferenceDefinition FilesExcludedFromHotReload = new StringListProjectEditorPreferenceDefinition(
            "Files excluded from Hot-Reload", "FilesExcludedFromHotReload", new List<string> {}, isReadonly: true);
        
        public static readonly StringListProjectEditorPreferenceDefinition ReferencesExcludedFromHotReload = new StringListProjectEditorPreferenceDefinition(
            "References to exclude from Hot-Reload", "ReferencesExcludedFromHotReload", new List<string>
            {
                "ExCSS.Unity.dll"
            }, (newValue, oldValue) =>
            {
                DynamicCompilationBase.ReferencesExcludedFromHotReload = (List<string>)newValue;
            },
            (value) =>
            {
                DynamicCompilationBase.ReferencesExcludedFromHotReload = (List<string>)value;
            });
        
        public static readonly ToggleProjectEditorPreferenceDefinition LogHowToFixMessageOnCompilationError = new ToggleProjectEditorPreferenceDefinition(
            "Log how to fix message on compilation error", "LogHowToFixMessageOnCompilationError", true, (object newValue, object oldValue) =>
            {
                DynamicCompilationBase.LogHowToFixMessageOnCompilationError = (bool)newValue;
            },
            (value) =>
            {
                DynamicCompilationBase.LogHowToFixMessageOnCompilationError = (bool)value;
            }
        );
        
        public static readonly ToggleProjectEditorPreferenceDefinition DebugWriteRewriteReasonAsComment = new ToggleProjectEditorPreferenceDefinition(
            "Write rewrite reason as comment in changed file", "DebugWriteRewriteReasonAsComment", false, (object newValue, object oldValue) =>
            {
                DynamicCompilationBase.DebugWriteRewriteReasonAsComment = (bool)newValue;
            },
            (value) =>
            {
                DynamicCompilationBase.DebugWriteRewriteReasonAsComment = (bool)value;
            });
        
        public static readonly ToggleProjectEditorPreferenceDefinition IsAutoOpenGeneratedSourceFileOnChangeEnabled = new ToggleProjectEditorPreferenceDefinition(
            "Auto-open generated source file for debugging", "IsAutoOpenGeneratedSourceFileOnChangeEnabled", false);
        
        public static readonly ToggleProjectEditorPreferenceDefinition StopShowingAutoReloadEnabledDialogBox = new ToggleProjectEditorPreferenceDefinition(
            "Stop showing assets/script auto-reload enabled warning", "StopShowingAutoReloadEnabledDialogBox", false);
        public static readonly ToggleProjectEditorPreferenceDefinition EnableDetailedDebugLogging = new ToggleProjectEditorPreferenceDefinition(
            "Enable detailed debug logging", "EnableDetailedDebugLogging", false,
            (object newValue, object oldValue) =>
            {
                BuildDefineSymbolManager.SetBuildDefineSymbolState(BuildSymbol_DetailedDebugLogging, (bool)newValue);
            },
            (value) =>
            {
                BuildDefineSymbolManager.SetBuildDefineSymbolState(BuildSymbol_DetailedDebugLogging, (bool)value);
            }
        );
        
        public static readonly ToggleProjectEditorPreferenceDefinition IsDidFieldsOrPropertyCountChangedCheckDisabled = new ToggleProjectEditorPreferenceDefinition(
            "Disable added/removed fields check", "IsDidFieldsOrPropertyCountChangedCheckDisabled", false,
            (object newValue, object oldValue) =>
            {
                UnityReloaderManager.Instance.AssemblyChangesLoaderEditorOptionsNeededInBuild.IsDidFieldsOrPropertyCountChangedCheckDisabled = (bool)newValue;
            },
            (value) =>
            {
                UnityReloaderManager.Instance.AssemblyChangesLoaderEditorOptionsNeededInBuild.IsDidFieldsOrPropertyCountChangedCheckDisabled = (bool)value;
            }
        );
        
        public static readonly ToggleProjectEditorPreferenceDefinition IsVisualHotReloadIndicationShownInProjectWindow = new ToggleProjectEditorPreferenceDefinition(
            "Show red / green bar in project window to indicate hot reload state for file", "IsVisualHotReloadIndicationShownInProjectWindow", true);
        
        public static readonly ToggleProjectEditorPreferenceDefinition IsPartialClassSupportEnabled = new ToggleProjectEditorPreferenceDefinition(
            "(Experimental) Partial class support", "IsPartialClassSupportEnabled", true);
        
        public static readonly ToggleProjectEditorPreferenceDefinition IsForceLockAssembliesViaCode = new ToggleProjectEditorPreferenceDefinition(
            "Force prevent assembly reload during playmode", "IsForceLockAssembliesViaCode", false);
        
        public static readonly JsonObjectListProjectEditorPreferenceDefinition<FileWatcherSetupEntry> FileWatcherSetupEntries = new JsonObjectListProjectEditorPreferenceDefinition<FileWatcherSetupEntry>(
            "File Watchers Setup", "FileWatcherSetupEntries", new List<string>
            {
                JsonUtility.ToJson(new FileWatcherSetupEntry(UnityReloaderManager.FileWatcherReplacementTokenForApplicationDataPath, "*.cs", true))
            }, 
            () => new FileWatcherSetupEntry(UnityReloaderManager.FileWatcherReplacementTokenForApplicationDataPath, "*.cs", true)
        );
        
        public static readonly ToggleProjectEditorPreferenceDefinition EnableExperimentalAddedFieldsSupport = new ToggleProjectEditorPreferenceDefinition(
            "(Experimental) Enable runtime added field support", "EnableExperimentalAddedFieldsSupport", false,
            (object newValue, object oldValue) =>
            {
                UnityReloaderManager.Instance.AssemblyChangesLoaderEditorOptionsNeededInBuild.EnableExperimentalAddedFieldsSupport = (bool)newValue;
            },
            (value) =>
            {
                UnityReloaderManager.Instance.AssemblyChangesLoaderEditorOptionsNeededInBuild.EnableExperimentalAddedFieldsSupport = (bool)value;
            });
        
        public static readonly ToggleProjectEditorPreferenceDefinition EnableExperimentalEditorHotReloadSupport = new ToggleProjectEditorPreferenceDefinition(
            "Enable Hot-Reload outside of play mode", "EnableExperimentalEditorHotReloadSupport", true);
        
        [Obsolete("Use EnableExperimentalEditorHotReloadSupport instead")]
        public static readonly ToggleProjectEditorPreferenceDefinition EnableCustomFileWatcher = new ToggleProjectEditorPreferenceDefinition(
            "(Experimental) Use custom file watchers", "EnableCustomFileWatcher", false);
        
        public static readonly EnumProjectEditorPreferenceDefinition FileWatcherImplementationInUse = new EnumProjectEditorPreferenceDefinition(
            "File Watcher implementation", "FileWatcherImplementationInUse", FileWatcherImplementation.UnityDefault, typeof(FileWatcherImplementation));

        //TODO: potentially that's just a normal settings (also in playmode) - but in playmode user is unlikely to make this many changes
        public static readonly IntProjectEditorPreferenceDefinition TriggerDomainReloadIfOverNDynamicallyLoadedAssembles = new IntProjectEditorPreferenceDefinition(
            "Trigger full domain reload after N hot-reloads (when not in play mode)", "TriggerDomainReloadIfOverNDynamicallyLoadedAssembles", 50);

        public static readonly ToggleProjectEditorPreferenceDefinition WatchOnlySpecified = new ToggleProjectEditorPreferenceDefinition(
            "Specify watched files/folders manually", "WatchOnlySpecified", false);

        public static readonly ToggleProjectEditorPreferenceDefinition AutoRecompileOnHotReloadFailure = new ToggleProjectEditorPreferenceDefinition(
            "Auto full-recompile when a hot reload fails", "AutoRecompileOnHotReloadFailure", true);


        public static void SetCommonMaterialsShader(ShadersMode newShaderModeValue)
        {
            var rootToolFolder = AssetPathResolver.GetAssetFolderPathRelativeToScript(ScriptableObject.CreateInstance(typeof(UnityReloaderWelcomeScreen)), 1);
            if (rootToolFolder.Contains("/Scripts"))
            {
                rootToolFolder = rootToolFolder.Replace("/Scripts", ""); //if nested remove that and go higher level
            }
            var assets = AssetDatabase.FindAssets("t:Material Point", new[] { rootToolFolder });

            try
            {
                Shader shaderToUse = null;
                switch (newShaderModeValue)
                {
                    case ShadersMode.HDRP: shaderToUse = Shader.Find("Shader Graphs/Point URP"); break;
                    case ShadersMode.URP: shaderToUse = Shader.Find("Shader Graphs/Point URP"); break;
                    case ShadersMode.Surface: shaderToUse = Shader.Find("Graph/Point Surface"); break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                foreach (var guid in assets)
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (material.shader.name != shaderToUse.name)
                    {
                        material.shader = shaderToUse;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerScoped.LogWarning($"Shader does not exist: {ex.Message}");
            }
        }

        public static List<ProjectEditorPreferenceDefinitionBase> PreferenceDefinitions = new List<ProjectEditorPreferenceDefinitionBase>()
        {
            CreateDefaultShowOptionPreferenceDefinition(),
            BatchScriptChangesAndReloadEveryNSeconds,
            EnableAutoReloadForChangedFiles,
            EnableExperimentalThisCallLimitationFix,
            LogHowToFixMessageOnCompilationError,
            StopShowingAutoReloadEnabledDialogBox,
            IsDidFieldsOrPropertyCountChangedCheckDisabled,
            FileWatcherSetupEntries,
            IsAutoOpenGeneratedSourceFileOnChangeEnabled,
            EnableExperimentalAddedFieldsSupport,
            ReferencesExcludedFromHotReload,
            EnableExperimentalEditorHotReloadSupport,
            TriggerDomainReloadIfOverNDynamicallyLoadedAssembles,
            IsForceLockAssembliesViaCode,
            AutoRecompileOnHotReloadFailure
        };

        private static bool PrefsLoaded = false;


#if !LiveScriptReload_Enabled
    #if UNITY_2019_1_OR_NEWER
        [SettingsProvider]
        public static SettingsProvider ImpostorsSettings()
        {
            return GenerateProvider(ProductName, ProductKeywords, PreferencesGUI);
        }

    #else
	[PreferenceItem(ProductName)]
    #endif
#endif
        public static void PreferencesGUI()
        {
            if (!PrefsLoaded)
            {
                LoadDefaults(PreferenceDefinitions);
                PrefsLoaded = true;
            }

            RenderGuiCommon(PreferenceDefinitions);
        }

        public enum ShadersMode
        {
            HDRP,
            URP,
            Surface
        }
    }

#if !LiveScriptReload_Enabled
    [InitializeOnLoad]
#endif
    public class UnityReloaderWelcomeScreenInitializer : WelcomeScreenInitializerBase
    {
#if !LiveScriptReload_Enabled
        static UnityReloaderWelcomeScreenInitializer()
        {
            var userId = ProductPreferenceBase.CreateDefaultUserIdDefinition(UnityReloaderWelcomeScreen.ProjectName).GetEditorPersistedValueOrDefault().ToString();

            HandleUnityStartup(
                () => UnityReloaderWelcomeScreen.Init(),
                UnityReloaderWelcomeScreen.GenerateGetUpdatesUrl(userId, UnityReloaderWelcomeScreen.VersionId),
                new List<ProjectEditorPreferenceDefinitionBase>(),
                (isFirstRun) =>
                {
                    MigrateObsoleteEnableCustomFileWatcherPreference();
                }
            );
            
            InitCommon();
        }

        private static void MigrateObsoleteEnableCustomFileWatcherPreference()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if ((bool)UnityReloaderPreference.EnableCustomFileWatcher.GetEditorPersistedValueOrDefault())
            {
                UnityReloaderPreference.FileWatcherImplementationInUse.SetEditorPersistedValue(FileWatcherImplementation.CustomPolling);
                UnityReloaderPreference.EnableCustomFileWatcher.SetEditorPersistedValue(false);
            }
#pragma warning restore CS0618 // Type or member is obsolete
        }
#endif
        
        protected static void InitCommon()
        {
            DisplayMessageIfLastDetourPotentiallyCrashedEditor();
            EnsureUserAwareOfAutoRefresh();

            DynamicCompilationBase.LogHowToFixMessageOnCompilationError = (bool)UnityReloaderPreference.LogHowToFixMessageOnCompilationError.GetEditorPersistedValueOrDefault();
            DynamicCompilationBase.DebugWriteRewriteReasonAsComment = (bool)UnityReloaderPreference.DebugWriteRewriteReasonAsComment.GetEditorPersistedValueOrDefault();
            DynamicCompilationBase.ReferencesExcludedFromHotReload = (List<string>)UnityReloaderPreference.ReferencesExcludedFromHotReload.GetElements();
            UnityReloaderManager.Instance.AssemblyChangesLoaderEditorOptionsNeededInBuild.UpdateValues(
                (bool)UnityReloaderPreference.IsDidFieldsOrPropertyCountChangedCheckDisabled.GetEditorPersistedValueOrDefault(),
                (bool)UnityReloaderPreference.EnableExperimentalAddedFieldsSupport.GetEditorPersistedValueOrDefault()
            );
            
            BuildDefineSymbolManager.SetBuildDefineSymbolState(UnityReloaderPreference.BuildSymbol_DetailedDebugLogging,
                (bool)UnityReloaderPreference.EnableDetailedDebugLogging.GetEditorPersistedValueOrDefault()
            );
            
            AutoDetectAndSetShaderMode();
        }

        private static void EnsureUserAwareOfAutoRefresh()
        {
            var autoRefreshMode = (AssetPipelineAutoRefreshMode)EditorPrefs.GetInt("kAutoRefreshMode", EditorPrefs.GetBool("kAutoRefresh") ? 1 : 0);
            if (autoRefreshMode != AssetPipelineAutoRefreshMode.Enabled)
                return;
            
            if ((bool)UnityReloaderPreference.IsForceLockAssembliesViaCode.GetEditorPersistedValueOrDefault())
                return;
            
            LoggerScoped.LogWarning("Unity Reloader - asset auto refresh enabled - full reload will be triggered unless editor preference adjusted - see documentation for more details.");

            if ((bool)UnityReloaderPreference.StopShowingAutoReloadEnabledDialogBox.GetEditorPersistedValueOrDefault())
                return;

            var chosenOption = EditorUtility.DisplayDialogComplex("Unity Reloader - Warning",
                "Auto reload for assets/scripts is enabled." +
                $"\n\nThis means any change made in playmode will likely trigger full recompile." +
                $"\r\n\r\nIt's an editor setting and can be adjusted at any time via Edit -> Preferences -> Asset Pipeline -> Auto Refresh" +
                $"\r\n\r\nI can also adjust that for you now - that means you'll need to manually load changes (outside of playmode) via Assets -> Refresh (CTRL + R)." +
                $"\r\n\r\nIn some editor versions you can also set script compilation to happen outside of playmode and don't have to manually refresh. " +
                $"\r\n\r\nDepending on version you'll find it via: " +
                $"\r\n1) Edit -> Preferences -> General -> Script Changes While Playing -> Recompile After Finished Playing." +
                $"\r\n2) Edit -> Preferences -> Asset Pipeline -> Auto Refresh -> Enabled Outside Playmode",
                "Ok, disable asset auto refresh",
                "No, don't change (stop showing message)",
                "No, don't change"
            );

            switch (chosenOption)
            {
                // change.
                case 0:
                    EditorPrefs.SetInt("kAutoRefreshMode", (int)AssetPipelineAutoRefreshMode.Disabled);
                    EditorPrefs.SetInt("kAutoRefresh", 0); //older unity versions
                    break;

                // don't change and stop showing message.
                case 1:
                    UnityReloaderPreference.StopShowingAutoReloadEnabledDialogBox.SetEditorPersistedValue(true);

                    break;

                // don't change
                case 2:

                    break;

                default:
                    LoggerScoped.LogError("Unrecognized option.");
                    break;
            }
                
            
        }

        //copied from internal UnityEditor.AssetPipelineAutoRefreshMode
        internal enum AssetPipelineAutoRefreshMode
        {
            Disabled,
            Enabled,
            EnabledOutsidePlaymode,
        }

        private static void DisplayMessageIfLastDetourPotentiallyCrashedEditor()
        {
            const string firstInitSessionKey = "UnityReloaderWelcomeScreenInitializer_FirstInitDone";
            if (!SessionState.GetBool(firstInitSessionKey, false))
            {
                SessionState.SetBool(firstInitSessionKey, true);

                var lastDetour = DetourCrashHandler.RetrieveLastDetour();
                if (!string.IsNullOrEmpty(lastDetour))
                {
                    EditorUtility.DisplayDialog("Unity Reloader",
                        $@"That's embarrassing!

It seems like I've crashed your editor, sorry!

Last detoured method was: '{lastDetour}'

If this happens again, please reach out via support and we'll sort it out.

In the meantime, you can exclude any file from Hot-Reload by 
1) right-clicking on .cs file in Project menu
2) Unity Reloader
3) Add Hot-Reload Exclusion
", "Ok");
                    DetourCrashHandler.ClearDetourLog();
                }
            }
        }

        protected static void AutoDetectAndSetShaderMode()
        {
            var usedShaderMode = UnityReloaderPreference.ShadersMode.Surface;
            
#if UNITY_6000_0_OR_NEWER
            var renderPipelineAsset = GraphicsSettings.defaultRenderPipeline;
#else
            var renderPipelineAsset = GraphicsSettings.renderPipelineAsset;
#endif

            if (renderPipelineAsset == null)
            {
                usedShaderMode = UnityReloaderPreference.ShadersMode.Surface;
            }
            else if (renderPipelineAsset.GetType().Name.Contains("HDRenderPipelineAsset"))
            {
                usedShaderMode = UnityReloaderPreference.ShadersMode.HDRP;
            }
            else if (renderPipelineAsset.GetType().Name.Contains("UniversalRenderPipelineAsset"))
            {
                usedShaderMode = UnityReloaderPreference.ShadersMode.URP;
            }
        
            UnityReloaderPreference.SetCommonMaterialsShader(usedShaderMode);
        }
    }
}