#if UNITY_EDITOR
using DigitalTwin;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EditorRobotPoseTool))]
public sealed class EditorRobotPoseToolEditor : Editor
{
    private bool _showSavedHomes = true;
    private bool _showTargetPoints = true;

    public override void OnInspectorGUI()
    {
        EditorRobotPoseTool tool = (EditorRobotPoseTool)target;

        DrawTopFields(tool);
        EditorGUILayout.Space(8f);
        DrawRuntimeManual(tool);
        EditorGUILayout.Space(8f);
        DrawBinding(tool);
        EditorGUILayout.Space(8f);
        DrawJointSliders(tool);
        EditorGUILayout.Space(8f);
        DrawZero(tool);
        EditorGUILayout.Space(8f);
        DrawHome0(tool);
        EditorGUILayout.Space(8f);
        DrawSavedHomes(tool);
        EditorGUILayout.Space(8f);
        DrawTargetPoints(tool);
        EditorGUILayout.Space(8f);
        DrawIkLink(tool);
    }

    private void DrawTopFields(EditorRobotPoseTool tool)
    {
        serializedObject.Update();
        EditorGUILayout.LabelField("Binding / 绑定", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("robotModel"), new GUIContent("Robot Model", "机器人模型控制器。通常与本工具挂在同一个 base_link 上。"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("enableInEditMode"), new GUIContent("Enable In Edit Mode", "是否允许非 Play 模式下通过 Inspector 控制模型。"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("applyContinuously"), new GUIContent("Apply Continuously", "拖动关节滑条时是否立即应用到 ArticulationBody。"));
        if (serializedObject.ApplyModifiedProperties())
        {
            tool.SyncHome0FromRuntimeModel();
            MarkDirty(tool);
        }
    }


    private void DrawRuntimeManual(EditorRobotPoseTool tool)
    {
        EditorGUILayout.LabelField("Play Mode Manual Control / Play 模式手动控制", EditorStyles.boldLabel);

        if (tool.RobotModel != null)
        {
            EditorGUILayout.LabelField("Current Authority", tool.RobotModel.CurrentControlAuthority.ToString());
            EditorGUILayout.LabelField("Last Source", tool.RobotModel.LastCommandSource.ToString());
        }

        serializedObject.Update();
        SerializedProperty manualProp = serializedObject.FindProperty("enableRuntimeManualJointControl");
        SerializedProperty releaseProp = serializedObject.FindProperty("releaseToLiveWhenManualOff");

        EditorGUI.BeginChangeCheck();
        bool nextManual = EditorGUILayout.Toggle(new GUIContent("Enable Runtime Manual Joint Control", "Play 模式下允许检查器关节滑条直接控制虚拟模型。开启后 RuntimeLive 实时反馈不会覆盖模型。"), manualProp != null && manualProp.boolValue);
        if (manualProp != null) manualProp.boolValue = nextManual;
        if (releaseProp != null)
        {
            EditorGUILayout.PropertyField(releaseProp, new GUIContent("Release To Live When Off", "关闭手动控制时自动恢复 RuntimeLive 实时反馈控制。"));
        }

        bool changed = EditorGUI.EndChangeCheck();
        if (serializedObject.ApplyModifiedProperties() || changed)
        {
            tool.SetRuntimeManualJointControl(nextManual);
            MarkDirty(tool);
        }

        if (Application.isPlaying)
        {
            if (tool.EnableRuntimeManualJointControl)
            {
                EditorGUILayout.HelpBox("手动控制已开启：下面关节滑条可在 Play 模式下驱动虚拟模型；DigitalTwinRuntime 实时反馈会暂时停止覆盖模型。", MessageType.Warning);
                if (GUILayout.Button("Release Back To Live Feedback / 释放回实时反馈"))
                {
                    Undo.RecordObject(tool, "Release To Live Feedback");
                    if (tool.RobotModel != null) Undo.RecordObject(tool.RobotModel, "Release To Live Feedback");
                    tool.ReleaseBackToLiveFeedback();
                    MarkDirty(tool);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("手动控制关闭：Play 模式下关节滑条只显示实时角度，不会抢 DigitalTwinRuntime 的实时同步。", MessageType.Info);
            }
        }
        else
        {
            EditorGUILayout.HelpBox("非 Play 模式使用 Edit Mode Joint Control；Play 模式手动控制只在运行时生效。", MessageType.None);
        }
    }

    private void DrawBinding(EditorRobotPoseTool tool)
    {
        EditorGUILayout.HelpBox(tool.GetBindingReport(), MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("Auto Bind Joints", "按 joint_1/link_1... 自动绑定 URDF Importer 生成的 ArticulationBody。")))
            {
                Undo.RecordObject(tool, "Auto Bind Joints");
                if (tool.RobotModel != null) Undo.RecordObject(tool.RobotModel, "Auto Bind Robot Model");
                tool.AutoBind();
                MarkDirty(tool);
            }

        }
    }

    private void DrawJointSliders(EditorRobotPoseTool tool)
    {
        EditorGUILayout.LabelField("Joint Sliders / 关节滑条", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(Application.isPlaying
            ? "Play 模式下显示实时 Current 与 Target。只有开启 Runtime Manual Joint Control 后，滑条才会驱动虚拟模型。"
            : "当前编辑态调试目标角。所有角度都是相对于 Zero 的绝对小数角度，不是增量。", MessageType.None);

        float[] targets = tool.TargetDeg;
        float[] current = tool.RobotModel == null ? null : tool.RobotModel.ReadCurrentDisplayDegrees();
        int count = Mathf.Max(0, tool.JointCount);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Joint", GUILayout.Width(38f));
            EditorGUILayout.LabelField("Target Deg", GUILayout.Width(250f));
            EditorGUILayout.LabelField("Value", GUILayout.Width(74f));
            EditorGUILayout.LabelField("Current", GUILayout.Width(74f));
        }

        for (int i = 0; i < count; i++)
        {
            Vector2 limits = tool.GetJointLimitsDeg(i);
            float targetValue = i < targets.Length ? targets[i] : 0f;
            float currentValue = current != null && i < current.Length ? current[i] : 0f;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"J{i + 1}", GUILayout.Width(38f));
                using (new EditorGUI.DisabledScope(Application.isPlaying && !tool.EnableRuntimeManualJointControl))
                {
                    EditorGUI.BeginChangeCheck();
                    float nextSlider = EditorGUILayout.Slider(targetValue, limits.x, limits.y, GUILayout.Width(250f));
                    float nextValue = EditorGUILayout.FloatField(nextSlider, GUILayout.Width(74f));
                    EditorGUILayout.LabelField(currentValue.ToString("0.###"), GUILayout.Width(74f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(tool, $"Set J{i + 1} Target");
                        if (tool.RobotModel != null) Undo.RecordObject(tool.RobotModel, $"Apply J{i + 1} Target");
                        tool.SetTargetDeg(i, nextValue);
                        if (tool.ApplyContinuously) tool.ApplyAll();
                        MarkDirty(tool);
                    }
                }
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(Application.isPlaying && !tool.EnableRuntimeManualJointControl))
            {
                if (GUILayout.Button(new GUIContent("Apply All", "把当前关节滑条角度应用到模型。Play 模式下需要先开启 Runtime Manual Joint Control。")))
                {
                    Undo.RecordObject(tool, "Apply Joint Targets");
                    if (tool.RobotModel != null) Undo.RecordObject(tool.RobotModel, "Apply Joint Targets");
                    tool.ApplyAll();
                    MarkDirty(tool);
                }
            }
            if (GUILayout.Button(new GUIContent("Set Target From Current", "把当前模型显示角读取到关节滑条。")))
            {
                Undo.RecordObject(tool, "Set Target From Current");
                tool.SetTargetFromCurrentModel();
                MarkDirty(tool);
            }
            using (new EditorGUI.DisabledScope(Application.isPlaying && !tool.EnableRuntimeManualJointControl))
            {
                if (GUILayout.Button(new GUIContent("Reset Target To Zero", "只把关节滑条目标清零并应用；不会修改 Zero 校准。")))
                {
                    Undo.RecordObject(tool, "Reset Target To Zero");
                    if (tool.RobotModel != null) Undo.RecordObject(tool.RobotModel, "Reset Target To Zero");
                    tool.ResetTargetToZero(true);
                    MarkDirty(tool);
                }
            }
        }
    }

    private void DrawZero(EditorRobotPoseTool tool)
    {
        EditorGUILayout.LabelField("Zero Calibration / 零点校准", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(tool.GetZeroReport(), MessageType.Info);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(new GUIContent("Capture Zero From Current", "只有模型处于 URDF 导入的机械 0 点姿态时才点击。它只记录 Zero 基准，不会被 Home/Target/IK 自动改。")))
            {
                Undo.RecordObject(tool, "Capture Zero");
                if (tool.RobotModel != null) Undo.RecordObject(tool.RobotModel, "Capture Zero");
                tool.CaptureCurrentAsZero();
                MarkDirty(tool);
            }
            if (GUILayout.Button(new GUIContent("Clear Zero", "清除 Zero 捕获标记。不会移动模型。")))
            {
                Undo.RecordObject(tool, "Clear Zero");
                if (tool.RobotModel != null) Undo.RecordObject(tool.RobotModel, "Clear Zero");
                tool.ClearZero();
                MarkDirty(tool);
            }
        }
    }

    private void DrawHome0(EditorRobotPoseTool tool)
    {
        EditorGUILayout.LabelField("Runtime Initial Home0 / 运行初始 Home0", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Home0 是 Play 开始可应用的初始显示姿态。这里只显示/输入关节小数角度，不使用滑条；它不会改变 Zero。", MessageType.None);
        DrawPoseFields(tool.Home0Deg, i => $"Home0 J{i + 1}", (i, v) =>
        {
            Undo.RecordObject(tool, "Edit Home0");
            if (tool.RobotModel != null) Undo.RecordObject(tool.RobotModel, "Edit Runtime Home0");
            tool.SetHome0JointDeg(i, v);
            MarkDirty(tool);
        });

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Set Home0 From Current"))
            {
                Undo.RecordObject(tool, "Set Home0 From Current");
                if (tool.RobotModel != null) Undo.RecordObject(tool.RobotModel, "Set Runtime Home0 From Current");
                tool.SetHome0FromCurrent();
                MarkDirty(tool);
            }
            if (GUILayout.Button("Set Home0 From Target"))
            {
                Undo.RecordObject(tool, "Set Home0 From Target");
                if (tool.RobotModel != null) Undo.RecordObject(tool.RobotModel, "Set Runtime Home0 From Target");
                tool.SetHome0FromTarget();
                MarkDirty(tool);
            }
            if (GUILayout.Button("Apply Home0"))
            {
                Undo.RecordObject(tool, "Apply Home0");
                if (tool.RobotModel != null) Undo.RecordObject(tool.RobotModel, "Apply Runtime Home0");
                tool.ApplyHome0();
                MarkDirty(tool);
            }
        }
    }

    private void DrawSavedHomes(EditorRobotPoseTool tool)
    {
        _showSavedHomes = EditorGUILayout.Foldout(_showSavedHomes, "Saved Homes / 其他 Home 姿态", true);
        if (!_showSavedHomes) return;
        EditorGUILayout.HelpBox("其他 Home 只用于编辑态测试或后续控制；不会作为 Runtime 初始姿态。Runtime 初始姿态只使用 Home0。", MessageType.None);

        EditorRobotPoseTool.SavedHomePose[] homes = tool.SavedHomes;
        for (int h = 0; h < homes.Length; h++)
        {
            EditorRobotPoseTool.SavedHomePose home = homes[h];
            if (home == null) continue;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                home.enabled = EditorGUILayout.Toggle(home.enabled, GUILayout.Width(20f));
                home.name = EditorGUILayout.TextField(home.name);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(tool, "Edit Home Header");
                    MarkDirty(tool);
                }
                if (GUILayout.Button("X", GUILayout.Width(24f)))
                {
                    Undo.RecordObject(tool, "Remove Saved Home");
                    tool.RemoveSavedHome(h);
                    MarkDirty(tool);
                    EditorGUILayout.EndVertical();
                    break;
                }
            }

            DrawPoseFields(home.jointDeg, i => $"J{i + 1}", (i, v) =>
            {
                Undo.RecordObject(tool, "Edit Saved Home");
                home.jointDeg[i] = v;
                MarkDirty(tool);
            });

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Set From Current")) { Undo.RecordObject(tool, "Set Home From Current"); tool.SetSavedHomeFromCurrent(h); MarkDirty(tool); }
                if (GUILayout.Button("Set From Target")) { Undo.RecordObject(tool, "Set Home From Target"); tool.SetSavedHomeFromTarget(h); MarkDirty(tool); }
                if (GUILayout.Button("Apply")) { Undo.RecordObject(tool, "Apply Saved Home"); tool.ApplySavedHome(h); MarkDirty(tool); }
            }
            EditorGUILayout.EndVertical();
        }

        if (GUILayout.Button("Add Saved Home"))
        {
            Undo.RecordObject(tool, "Add Saved Home");
            tool.AddSavedHome();
            MarkDirty(tool);
        }
    }

    private void DrawTargetPoints(EditorRobotPoseTool tool)
    {
        _showTargetPoints = EditorGUILayout.Foldout(_showTargetPoints, "Target Points / 目标点位", true);
        if (!_showTargetPoints) return;
        EditorGUILayout.HelpBox("Target 点只保存关节小数角。可来自当前模型、滑条或 IK 最后解；Apply 不会改变 Zero。", MessageType.None);

        EditorRobotPoseTool.TargetPoint[] points = tool.TargetPoints;
        for (int t = 0; t < points.Length; t++)
        {
            EditorRobotPoseTool.TargetPoint point = points[t];
            if (point == null) continue;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                point.enabled = EditorGUILayout.Toggle(point.enabled, GUILayout.Width(20f));
                point.name = EditorGUILayout.TextField(point.name);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(tool, "Edit Target Header");
                    MarkDirty(tool);
                }
                if (GUILayout.Button("X", GUILayout.Width(24f)))
                {
                    Undo.RecordObject(tool, "Remove Target Point");
                    tool.RemoveTargetPoint(t);
                    MarkDirty(tool);
                    EditorGUILayout.EndVertical();
                    break;
                }
            }

            DrawPoseFields(point.jointDeg, i => $"J{i + 1}", (i, v) =>
            {
                Undo.RecordObject(tool, "Edit Target Joint");
                tool.SetTargetPointJointDeg(t, i, v);
                MarkDirty(tool);
            });

            EditorGUI.BeginChangeCheck();
            string notes = EditorGUILayout.TextField("Notes", point.notes);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(tool, "Edit Target Notes");
                point.notes = notes;
                MarkDirty(tool);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Set From Current")) { Undo.RecordObject(tool, "Set Target From Current"); tool.SetTargetPointFromCurrent(t); MarkDirty(tool); }
                if (GUILayout.Button("Set From Sliders")) { Undo.RecordObject(tool, "Set Target From Sliders"); tool.SetTargetPointFromSliders(t); MarkDirty(tool); }
                if (GUILayout.Button("Set From IK")) { Undo.RecordObject(tool, "Set Target From IK"); tool.SetTargetPointFromIkSolution(t); MarkDirty(tool); }
                if (GUILayout.Button("Apply")) { Undo.RecordObject(tool, "Apply Target"); tool.ApplyTargetPoint(t); MarkDirty(tool); }
            }
            EditorGUILayout.EndVertical();
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Target From Current"))
            {
                Undo.RecordObject(tool, "Add Target From Current");
                tool.AddTargetPointFromCurrent();
                MarkDirty(tool);
            }
            if (GUILayout.Button("Add Target From IK"))
            {
                Undo.RecordObject(tool, "Add Target From IK");
                tool.AddTargetPointFromIkSolution();
                MarkDirty(tool);
            }
            if (GUILayout.Button("Add Empty Target"))
            {
                Undo.RecordObject(tool, "Add Target");
                tool.AddTargetPoint();
                MarkDirty(tool);
            }
        }
    }

    private void DrawIkLink(EditorRobotPoseTool tool)
    {
        RobotIkController ik = tool.IkController;
        EditorGUILayout.LabelField("IK Link / IK 联动", EditorStyles.boldLabel);
        if (ik == null)
        {
            EditorGUILayout.HelpBox("当前对象未挂 RobotIkController。需要 IK 拖动时，把 RobotIkController 加到同一个 base_link。", MessageType.Warning);
            if (GUILayout.Button("Add RobotIkController"))
            {
                Undo.AddComponent<RobotIkController>(((EditorRobotPoseTool)target).gameObject);
            }
            return;
        }

        EditorGUILayout.HelpBox("IK 已拆分为独立 RobotIkController。IK 求解后会通过 RobotModelController 更新模型，并自动同步这里的关节滑条。", MessageType.Info);
        if (ik.HasLastSolution)
        {
            if (GUILayout.Button("Add Target From Last IK Solution"))
            {
                Undo.RecordObject(tool, "Add Target From IK Solution");
                tool.AddTargetPointFromIkSolution();
                MarkDirty(tool);
            }
        }
    }

    private static void DrawPoseFields(float[] pose, System.Func<int, string> label, System.Action<int, float> onChange)
    {
        if (pose == null) return;
        for (int i = 0; i < pose.Length; i++)
        {
            EditorGUI.BeginChangeCheck();
            float next = EditorGUILayout.FloatField(new GUIContent(label(i), "基于 Zero 的绝对关节角，单位 degree，支持小数。"), pose[i]);
            if (EditorGUI.EndChangeCheck())
            {
                onChange(i, next);
            }
        }
    }

    private static void MarkDirty(EditorRobotPoseTool tool)
    {
        EditorUtility.SetDirty(tool);
        if (tool.RobotModel != null) EditorUtility.SetDirty(tool.RobotModel);
        if (tool.IkController != null) EditorUtility.SetDirty(tool.IkController);
        SceneView.RepaintAll();
    }
}
#endif
