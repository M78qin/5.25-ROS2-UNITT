#if UNITY_EDITOR
using DigitalTwin;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RobotIkController))]
public sealed class RobotIkControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        RobotIkController ik = (RobotIkController)target;
        serializedObject.Update();

        EditorGUILayout.LabelField("Robot IK Controller / 机器人 IK 控制器", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("IK 是独立可选模块。关闭后不求解、不绘制 Scene Handle、不影响 DigitalTwinRuntime / Recorder / UI。真实控制必须交给 TwinCommandController。", MessageType.Info);

        DrawProperty("enableIk", "Enable IK", "是否启用 IK 模块。关闭后不求解、不绘制 Scene Handle、不影响其他模块。");
        DrawProperty("enableRuntimeIk", "Enable Runtime IK", "是否允许 Play 模式下使用 IK。默认关闭，只有 UI 或调试明确开启时才工作。");
        DrawProperty("previewSolutionOnModel", "Preview Solution On Model", "求解成功后是否把结果预览到虚拟模型。");
        DrawProperty("applyBestEffortWhenUnsolved", "Apply Best Effort", "未完全达到容差但误差下降时，是否应用最佳努力解。适合拖动目标时让模型持续跟随。");

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("References / 引用", EditorStyles.boldLabel);
        DrawProperty("robotModel", "Robot Model", "机器人模型控制器。通常与本 IK 脚本挂在同一个 base_link 上。");
        DrawProperty("tcpTransform", "TCP Transform", "手动指定 TCP；为空时默认使用 link_6 末端法兰。");
        DrawProperty("ikTargetTransform", "IK Target Transform", "可选外部 IK Target Transform。为空时使用内部 Target 数据，并由 Scene 圆标拖动。");

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("TCP Offset / TCP 偏移", EditorStyles.boldLabel);
        DrawProperty("tcpLocalOffsetPosition", "TCP Local Offset Pos", "TCP 相对法兰中心的位置偏移，单位 meter。");
        DrawProperty("tcpLocalOffsetEulerDeg", "TCP Local Offset Euler", "TCP 相对法兰中心的姿态偏移，单位 degree。");

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Target / 目标", EditorStyles.boldLabel);
        DrawProperty("internalTargetPosition", "Internal Target Position", "内部 IK Target 世界坐标。未指定 IK Target Transform 时使用。");
        DrawProperty("internalTargetEulerDeg", "Internal Target Euler", "内部 IK Target 世界欧拉角。未指定 IK Target Transform 时使用。");

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Scene Handle / 场景拖动圆标", EditorStyles.boldLabel);
        DrawProperty("showIkHandle", "Show IK Handle", "是否在 Scene 中显示可拖动 IK Target 圆标。");
        DrawProperty("ikHandleSize", "Handle Size", "IK Target 圆标大小。");
        DrawProperty("tcpHandleColor", "TCP Color", "当前 TCP 圆标颜色。");
        DrawProperty("ikTargetHandleColor", "Target Color", "IK Target 圆标颜色。");
        DrawProperty("ikLineColor", "Line Color", "TCP 到 IK Target 连线颜色。");
        DrawProperty("autoSolveOnDrag", "Auto Solve On Drag", "拖动 IK Target 时是否自动求解。");
        DrawProperty("showSceneLabels", "Show Scene Labels", "是否显示 TCP / IK Target / error 标签。");
        DrawProperty("drawGizmo", "Draw Gizmo", "是否绘制 Gizmo。");

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Solver / 求解器", EditorStyles.boldLabel);
        DrawProperty("solvePosition", "Solve Position", "是否求解 TCP 位置。通常保持开启。");
        DrawProperty("solveRotation", "Solve Rotation", "是否同时求解 TCP 姿态。第一阶段建议关闭，只解位置更稳定。");
        DrawProperty("positionWeight", "Position Weight", "位置误差权重。");
        DrawProperty("rotationWeight", "Rotation Weight", "姿态误差权重。建议小于位置权重。");
        DrawProperty("maxIterations", "Max Iterations", "最大迭代次数。");
        DrawProperty("positionToleranceMeters", "Position Tolerance M", "位置误差阈值，单位 meter。");
        DrawProperty("rotationToleranceDeg", "Rotation Tolerance Deg", "姿态误差阈值，单位 degree。");
        DrawProperty("finiteStepDeg", "Finite Step Deg", "有限差分步长，单位 degree。");
        DrawProperty("damping", "Damping", "阻尼最小二乘阻尼。越大越稳但收敛更慢。");
        DrawProperty("gain", "Gain", "关节更新增益。");
        DrawProperty("maxStepDeg", "Max Step Deg", "每次迭代单关节最大变化，单位 degree。");
        DrawProperty("maxSolveTimeMs", "Max Solve Time Ms", "单次求解最大耗时，单位毫秒。防止 Play 模式长时间卡顿。");
        DrawProperty("runtimeSolveRateHz", "Runtime Solve Rate Hz", "Play 模式自动求解频率上限。");

        if (serializedObject.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(ik);
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space(8f);
        DrawButtons(ik);
        DrawResult(ik);
    }

    private void DrawButtons(RobotIkController ik)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Create Target At TCP"))
            {
                Undo.RecordObject(ik, "Create IK Target At TCP");
                ik.InitializeTargetAtTcp();
                MarkDirty(ik);
            }
            if (GUILayout.Button("Reset Target To TCP"))
            {
                Undo.RecordObject(ik, "Reset IK Target To TCP");
                ik.ResetTargetToTcp();
                MarkDirty(ik);
            }
            if (GUILayout.Button("Solve IK"))
            {
                Undo.RecordObject(ik, "Solve IK");
                if (ik.RobotModel != null) Undo.RecordObject(ik.RobotModel, "Apply IK Preview");
                ik.TrySolve(true);
                MarkDirty(ik);
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Apply Last Solution"))
            {
                Undo.RecordObject(ik, "Apply Last IK Solution");
                if (ik.RobotModel != null) Undo.RecordObject(ik.RobotModel, "Apply Last IK Solution");
                ik.ApplyLastSolutionPreview();
                MarkDirty(ik);
            }

            if (GUILayout.Button("Save Solution To Target Point"))
            {
                EditorRobotPoseTool poseTool = ik.GetComponent<EditorRobotPoseTool>();
                if (poseTool != null)
                {
                    Undo.RecordObject(poseTool, "Save IK Solution To Target Point");
                    poseTool.AddTargetPointFromIkSolution();
                    EditorUtility.SetDirty(poseTool);
                }
            }
        }

        if (Application.isPlaying && GUILayout.Button("Release Back To Live Feedback / 释放回实时反馈"))
        {
            Undo.RecordObject(ik, "Release IK To Live Feedback");
            if (ik.RobotModel != null) Undo.RecordObject(ik.RobotModel, "Release IK To Live Feedback");
            ik.ReleaseBackToLiveFeedback();
            MarkDirty(ik);
        }
    }

    private static void DrawResult(RobotIkController ik)
    {
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Result / 结果", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Effective TCP", ik.GetEffectiveTcpLabel());
        if (ik.TryGetCurrentTcpPose(out Vector3 tcpPos, out Quaternion tcpRot))
        {
            EditorGUILayout.LabelField("TCP Position", FormatVector3(tcpPos));
            EditorGUILayout.LabelField("TCP Euler", FormatVector3(tcpRot.eulerAngles));
        }
        ik.GetTargetPose(out Vector3 targetPos, out Quaternion targetRot);
        EditorGUILayout.LabelField("Target Position", FormatVector3(targetPos));
        EditorGUILayout.LabelField("Target Euler", FormatVector3(targetRot.eulerAngles));
        EditorGUILayout.LabelField("Last Status", ik.LastStatus.ToString());
        EditorGUILayout.LabelField("Last Position Error M", float.IsInfinity(ik.LastPositionError) ? "N/A" : ik.LastPositionError.ToString("0.######"));
        EditorGUILayout.LabelField("Last Rotation Error Deg", float.IsInfinity(ik.LastRotationErrorDeg) ? "N/A" : ik.LastRotationErrorDeg.ToString("0.###"));
        EditorGUILayout.LabelField("Last Iterations", ik.LastIterations.ToString());
        if (ik.HasLastSolution)
        {
            float[] solution = ik.GetLastSolutionDegreesCopy();
            for (int i = 0; i < solution.Length; i++)
            {
                EditorGUILayout.LabelField($"Solved J{i + 1}", solution[i].ToString("0.###"));
            }
        }
    }

    private void OnSceneGUI()
    {
        RobotIkController ik = (RobotIkController)target;
        if (!ik.EnableIk || !ik.ShowIkHandle) return;

        ik.GetTargetPose(out Vector3 targetPos, out Quaternion targetRot);
        bool hasTcp = ik.TryGetCurrentTcpPose(out Vector3 tcpPos, out _);

        Handles.color = ik.IkLineColor;
        if (hasTcp) Handles.DrawLine(tcpPos, targetPos);

        if (hasTcp)
        {
            Handles.color = ik.TcpHandleColor;
            Handles.SphereHandleCap(0, tcpPos, Quaternion.identity, ik.IkHandleSize * 0.75f, EventType.Repaint);
            if (ik.ShowSceneLabels) Handles.Label(tcpPos, "TCP / 当前末端");
        }

        EditorGUI.BeginChangeCheck();
        Handles.color = ik.IkTargetHandleColor;
        var fmh_179_61_639129923134069537 = Quaternion.identity; Vector3 nextPos = Handles.FreeMoveHandle(targetPos, ik.IkHandleSize, Vector3.zero, Handles.SphereHandleCap);
        Quaternion nextRot = Handles.RotationHandle(targetRot, nextPos);
        if (ik.ShowSceneLabels)
        {
            Handles.Label(nextPos, "IK Target / 可拖动");
        }

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(ik, "Move IK Target");
            ik.SetTargetPose(nextPos, nextRot);
            if (ik.AutoSolveOnDrag)
            {
                if (ik.RobotModel != null) Undo.RecordObject(ik.RobotModel, "IK Preview");
                ik.TrySolve(true);
            }
            MarkDirty(ik);
        }
    }

    private void DrawProperty(string propertyName, string label, string tooltip)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
        {
            EditorGUILayout.PropertyField(property, new GUIContent(label, tooltip));
        }
    }

    private static string FormatVector3(Vector3 v) => $"{v.x:0.###}, {v.y:0.###}, {v.z:0.###}";

    private static void MarkDirty(RobotIkController ik)
    {
        EditorUtility.SetDirty(ik);
        if (ik.RobotModel != null) EditorUtility.SetDirty(ik.RobotModel);
        SceneView.RepaintAll();
    }
}
#endif
