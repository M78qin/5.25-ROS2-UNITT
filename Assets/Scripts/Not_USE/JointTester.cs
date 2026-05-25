using UnityEngine;

public class JointTester : MonoBehaviour
{
    // 拖入你那 6 个 Link 节点
    public ArticulationBody[] robotJoints;
    public float testAngle = 45f; // 想要测试的角度

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space)) // 按空格键执行
        {
            Debug.Log("[测试] 正在驱动关节至 " + testAngle + " 度");
            foreach (var joint in robotJoints)
            {
                var drive = joint.xDrive;
                drive.target = testAngle; // 直接给物理马达发指令
                joint.xDrive = drive;
            }
        }
    }
}