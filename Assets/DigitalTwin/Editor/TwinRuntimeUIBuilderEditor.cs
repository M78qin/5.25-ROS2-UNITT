using DigitalTwin;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class TwinRuntimeUIBuilderEditor
{
    private const string RobotId = TwinRuntimeUIFactory.DefaultRobotId;
    private static readonly Vector2 DefaultPanelPosition = new Vector2(-28f, -28f);
    private static readonly Vector2 DefaultPanelSize = new Vector2(520f, 920f);

    [MenuItem("GameObject/Digital Twin/Create Runtime HUD", false, 10)]
    private static void CreateFromContextMenu(MenuCommand cmd)
    {
        GameObject selected = ResolveSelectedGameObject(cmd);
        TwinUIController controller = selected == null ? null : selected.GetComponent<TwinUIController>();
        if (controller == null)
        {
            Debug.LogWarning("DigitalTwin HUD: 请先选中带有 TwinUIController 的 GameObject。");
            return;
        }

        GenerateAndBind(controller);
    }

    [MenuItem("GameObject/Digital Twin/Create Runtime HUD", true)]
    private static bool ValidateCreateFromContextMenu()
    {
        GameObject selected = Selection.activeGameObject;
        return selected != null;
    }

    [MenuItem("GameObject/Digital Twin/Create Runtime UI For Selected Runtime", false, 11)]
    private static void CreateRuntimeUIForSelectedRuntime(MenuCommand cmd)
    {
        CreateFromContextMenu(cmd);
    }

    [MenuItem("GameObject/Digital Twin/Create Runtime UI For Selected Runtime", true)]
    private static bool ValidateCreateRuntimeUIForSelectedRuntime()
    {
        return ValidateCreateFromContextMenu();
    }

    [MenuItem("CONTEXT/TwinUIController/Generate Bound Runtime HUD")]
    private static void GenerateBoundRuntimeHUD(MenuCommand command)
    {
        TwinUIController controller = command.context as TwinUIController;
        if (controller != null)
        {
            GenerateAndBind(controller);
        }
    }

    private static GameObject ResolveSelectedGameObject(MenuCommand cmd)
    {
        GameObject selected = cmd.context as GameObject;
        return selected != null ? selected : Selection.activeGameObject;
    }

    private static void GenerateAndBind(TwinUIController controller)
    {
        if (controller == null)
        {
            return;
        }

        Transform parent = controller.transform.parent;
        CleanupOldCanvas();

        TwinRuntimeUIBindings bindings = TwinRuntimeUIFactory.CreateRuntimeCanvas(DefaultPanelPosition, DefaultPanelSize, RobotId, parent);
        Undo.RegisterCreatedObjectUndo(bindings.CanvasRoot, "Create DigitalTwin Runtime HUD");

        Bind(controller, bindings);
        ValidateBindings(bindings);

        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(bindings);
        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        Selection.activeGameObject = bindings.PanelRoot;
    }

    private static void CleanupOldCanvas()
    {
        string newName = TwinRuntimeUIFactory.GetCanvasName(RobotId);
        string[] names =
        {
            newName,
            TwinRuntimeUIFactory.CanvasName
        };

        for (int i = 0; i < names.Length; i++)
        {
            GameObject oldCanvas = GameObject.Find(names[i]);
            if (oldCanvas == null)
            {
                continue;
            }

            Undo.DestroyObjectImmediate(oldCanvas);
        }
    }

    private static void Bind(TwinUIController controller, TwinRuntimeUIBindings bindings)
    {
        SerializedObject so = new SerializedObject(controller);
        SetObject(so, "jointText", bindings.JointText);
        SetObject(so, "forceText", bindings.ForceText);
        SetObject(so, "metricsText", bindings.MetricsText);
        SetObject(so, "recordText", bindings.RecordText);
        SetObject(so, "connectionText", bindings.ConnectionText);
        SetObject(so, "commandText", bindings.CommandText);
        SetObject(so, "idleButton", bindings.IdleButton);
        SetObject(so, "mode1Button", bindings.Mode1Button);
        SetObject(so, "planButton", bindings.PlanButton);
        SetObject(so, "executeButton", bindings.ExecuteButton);
        SetObject(so, "haltButton", bindings.HaltButton);
        SetObject(so, "sliderGroup", bindings.SliderGroup);
        SetArray(so, "jointSliders", bindings.JointSliders);
        SetArray(so, "jointSliderLabels", bindings.JointSliderLabels);
        so.ApplyModifiedProperties();
    }

    private static void ValidateBindings(TwinRuntimeUIBindings bindings)
    {
        if (bindings == null)
        {
            Debug.LogError("DigitalTwin HUD: bindings is null.");
            return;
        }

        if (bindings.Validate(out string[] missing))
        {
            Debug.Log("DigitalTwin HUD: bindings validation passed.");
            return;
        }

        Debug.LogWarning("DigitalTwin HUD: missing bindings -> " + string.Join(", ", missing));
    }

    private static void SetObject(SerializedObject so, string propertyName, Object value)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property != null)
        {
            property.objectReferenceValue = value;
        }
    }

    private static void SetArray(SerializedObject so, string propertyName, Slider[] values)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null)
        {
            return;
        }

        property.arraySize = values == null ? 0 : values.Length;
        for (int i = 0; values != null && i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }

    private static void SetArray(SerializedObject so, string propertyName, TMP_Text[] values)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null)
        {
            return;
        }

        property.arraySize = values == null ? 0 : values.Length;
        for (int i = 0; values != null && i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
