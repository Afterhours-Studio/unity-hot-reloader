using System.Collections.Generic;
using System.Linq;
using ImmersiveVRTools.Editor.Common.Utilities;
using ImmersiveVRTools.Runtime.Common;
using UnityEditor;
using UnityEngine;

namespace UnityReloader.Editor.GUI
{
    [InitializeOnLoad]
    public class ProjectWindowReloadStatusIndicatorDrawer
    {
        private static Texture HelpTexture;
        
        private static bool IsEnabled;
        
        
        static ProjectWindowReloadStatusIndicatorDrawer()
        {
            EditorApplication.projectWindowItemOnGUI += ProjectWindowItemOnGUI;
            UnityReloaderManager.Instance.HotReloadFailed += OnHotReloadFailed;
            EditorApplication.update += () =>
            {
                IsEnabled = (bool)UnityReloaderPreference.IsVisualHotReloadIndicationShownInProjectWindow.GetEditorPersistedValueOrDefault();
            };
        }

        private static void OnHotReloadFailed(List<DynamicFileHotReloadState> failedForFileStates)
        {
            if (!IsEnabled)
            {
                return;
            }
            
            UnityMainThreadDispatcher.Instance.Enqueue(() =>
            {
                var lastFailed = failedForFileStates.LastOrDefault();
                if (lastFailed != null)
                {
                    var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabaseHelper.AbsolutePathToAssetPath(lastFailed.FullFileName));
                    EditorGUIUtility.PingObject(script);
                }
            });
        }

        private static void ProjectWindowItemOnGUI(string guid, Rect rect)
        {
            if (!IsEnabled)
            {
                return;
            }
            
            if (HelpTexture == null)
            {
                HelpTexture = EditorGUIUtility.TrIconContent("_Help").image;
            }

            if (UnityReloaderManager.Instance.LastProcessedDynamicFileHotReloadStatesInSession.TryGetValue(guid, out var fileHotReloadState))
            {
                var sideLineRect = new Rect(1, rect.y, 2, rect.height);
                if (fileHotReloadState.IsFailed)
                {
                    EditorGUI.DrawRect(sideLineRect, Color.red);
                    
                    var sideInfoRect = new Rect(1, rect.y - 3, 20, 20);
                    if (UnityEngine.GUI.Button(sideInfoRect,
                            new GUIContent(new GUIContent(HelpTexture, 
@"Unity Reloader - Error

Last hot reload failed.

Please click for more details.")),
                            EditorStyles.linkLabel))
                    {
                        UnityReloaderWindow.Open().OpenInspectError(fileHotReloadState);
                    }; 
                }    
                else if (fileHotReloadState.IsChangeHotSwapped)
                {
                    EditorGUI.DrawRect(sideLineRect, Color.green);
                }
            }
        }
    }
}