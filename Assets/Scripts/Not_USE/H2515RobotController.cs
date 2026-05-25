using UnityEngine;

[ExecuteAlways] // 关键：允许在非 Play 模式下运行
public class H2515RobotController : MonoBehaviour
{
    [Header("各关节角度控制 (度)")]
    [Range(-360, 360)] public float J1;
    [Range(-360, 360)] public float J2;
    [Range(-360, 360)] public float J3;
    [Range(-360, 360)] public float J4;
    [Range(-360, 360)] public float J5;
    [Range(-360, 360)] public float J6;

    [Header("配置")]
    public ArticulationBody[] jointBodies = new ArticulationBody[6];
    public bool useRosInput = true; // 运行时是否接受 ROS 消息

    // 缓存初始 Drive，提升效率
    private ArticulationDrive[] drives = new ArticulationDrive[6];

    void OnValidate()
    {
        // 当你在 Inspector 里拖动滑条时，此函数会被调用
        UpdateRobotPose();
    }

    void Start()
    {
        // 运行时初始化，确保引用正确
        if (Application.isPlaying)
        {
            UpdateRobotPose();
        }
    }

    // 供外部（如 ROS Subscriber）调用的接口
    public void SetJointAngles(float[] angles)
    {
        if (!useRosInput || angles.Length < 6) return;

        J1 = angles[0];
        J2 = angles[1];
        J3 = angles[2];
        J4 = angles[3];
        J5 = angles[4];
        J6 = angles[5];

        UpdateRobotPose();
    }

    public void UpdateRobotPose()
    {
        if (jointBodies == null || jointBodies.Length < 6) return;

        float[] targetAngles = { J1, J2, J3, J4, J5, J6 };

        for (int i = 0; i < 6; i++)
        {
            if (jointBodies[i] != null)
            {
                var drive = jointBodies[i].xDrive;
                drive.target = targetAngles[i];
                jointBodies[i].xDrive = drive;
            }
        }
    }
}