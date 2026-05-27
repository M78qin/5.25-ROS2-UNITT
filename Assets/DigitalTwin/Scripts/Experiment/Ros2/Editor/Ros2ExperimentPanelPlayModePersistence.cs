#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace DigitalTwin.Editor
{
    [InitializeOnLoad]
    internal static class Ros2ExperimentPanelPlayModePersistence
    {
        static Ros2ExperimentPanelPlayModePersistence()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode || !Ros2CapturedPosePersistence.HasCapture)
            {
                return;
            }

            EditorApplication.delayCall += ApplyCaptureToAllPanels;
        }

        private static void ApplyCaptureToAllPanels()
        {
            Ros2ExperimentPanel[] panels = Object.FindObjectsByType<Ros2ExperimentPanel>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Ros2ExperimentPanel panel in panels)
            {
                if (panel == null)
                {
                    continue;
                }

                panel.ApplyPersistedCaptureToEditMode();
                EditorUtility.SetDirty(panel);
                if (PrefabUtility.IsPartOfPrefabInstance(panel))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(panel);
                }
            }
        }
    }
}
#endif
