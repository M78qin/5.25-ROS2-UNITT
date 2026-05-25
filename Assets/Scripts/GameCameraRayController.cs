using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public interface IGameRaycastReceiver
{
    void OnRayClick(GameRaycastContext context);
    void OnRayDragBegin(GameRaycastContext context);
    void OnRayDrag(GameRaycastContext context);
    void OnRayDragEnd(GameRaycastContext context);
}

public struct GameRaycastContext
{
    public Camera Camera;
    public Ray Ray;
    public RaycastHit Hit;
    public Vector3 WorldPoint;
    public Vector3 WorldDelta;
    public Vector2 ScreenPosition;
    public float HoldTime;
    public bool IsLongPress;
    public bool IsDragging;
}

[Serializable]
public class RaycastHitUnityEvent : UnityEvent<RaycastHit> { }

[Serializable]
public class Vector3UnityEvent : UnityEvent<Vector3> { }

[DisallowMultipleComponent]
[AddComponentMenu("Robot Control/Game Camera Ray Controller")]
public class GameCameraRayController : MonoBehaviour
{
    private enum DragPlaneMode
    {
        CameraFacing,
        WorldXY,
        WorldXZ,
        WorldYZ,
        HitNormal
    }

    [Header("一、相机移动设置")]
    [Tooltip("开启后，只有按住鼠标右键时，WASD/QE 才会移动相机。推荐开启，行为接近 Unity Scene 视图。")]
    [SerializeField] private bool moveOnlyWhenRightMouseHeld = true;

    [Tooltip("相机基础移动速度。数值越大，WASD/QE 移动越快。可以在运行时用鼠标滚轮动态调节。")]
    [Min(0.01f)]
    [SerializeField] private float moveSpeed = 3.5f;

    [Tooltip("按住 Shift 时的加速倍率。例如基础速度 3.5，倍率 4，则快速移动速度为 14。")]
    [Min(1f)]
    [SerializeField] private float fastMultiplier = 4f;

    [Tooltip("按住 Ctrl 时的减速倍率。例如基础速度 3.5，倍率 0.25，则慢速移动速度为 0.875。")]
    [Range(0.01f, 1f)]
    [SerializeField] private float slowMultiplier = 0.25f;

    [Tooltip("鼠标右键旋转视角的灵敏度。数值越大，鼠标移动同样距离时视角旋转越快。")]
    [Range(0.05f, 10f)]
    [SerializeField] private float mouseLookSensitivity = 2.2f;

    [Tooltip("相机移动的平滑强度。数值越大越跟手，数值越小越有缓动感。推荐 10 到 20。")]
    [Range(1f, 40f)]
    [SerializeField] private float moveSmooth = 14f;

    [Tooltip("是否让 Q/E 按世界坐标上下移动。开启后 Q 永远向世界下方，E 永远向世界上方；推荐开启。")]
    [SerializeField] private bool useWorldVerticalMove = true;

    [Tooltip("相机最低俯仰角。防止相机向下翻转过头。")]
    [Range(-89f, 0f)]
    [SerializeField] private float minPitch = -85f;

    [Tooltip("相机最高俯仰角。防止相机向上翻转过头。")]
    [Range(0f, 89f)]
    [SerializeField] private float maxPitch = 85f;

    [Tooltip("按住鼠标右键旋转视角时是否锁定并隐藏鼠标。推荐开启，体验接近 Unity Scene 视图。")]
    [SerializeField] private bool lockCursorWhenRotating = true;

    [Header("二、鼠标滚轮调速")]
    [Tooltip("开启后，运行时滚动鼠标滚轮可以调节 moveSpeed。适合在大场景和精细操作之间快速切换。")]
    [SerializeField] private bool enableWheelSpeedControl = true;

    [Tooltip("每滚动一格鼠标滚轮，移动速度变化多少。数值越大，滚轮调速越明显。")]
    [Min(0.01f)]
    [SerializeField] private float wheelSpeedStep = 0.8f;

    [Tooltip("滚轮调速允许的最小移动速度。防止速度被调到太小导致相机几乎不动。")]
    [Min(0.001f)]
    [SerializeField] private float minMoveSpeed = 0.05f;

    [Tooltip("滚轮调速允许的最大移动速度。防止速度被调得过大导致相机飞出场景。")]
    [Min(0.01f)]
    [SerializeField] private float maxMoveSpeed = 50f;

    [Header("三、射线检测设置")]
    [Tooltip("左键点击或拖拽时，射线允许命中的 Layer。建议把机器人控制点、IK Target、3D UI 放到专门 Layer 后在这里勾选。")]
    [SerializeField] private LayerMask raycastMask = ~0;

    [Tooltip("射线最大检测距离。相机到物体距离超过该值时不会命中。")]
    [Min(0.1f)]
    [SerializeField] private float raycastMaxDistance = 200f;

    [Tooltip("射线是否检测 Trigger 碰撞体。Ignore 表示忽略 Trigger；Collide 表示可以命中 Trigger。")]
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Tooltip("鼠标在 Unity UI 上时，是否阻止 3D 世界射线。开启后点击 UI 不会误操作机器人；推荐开启。")]
    [SerializeField] private bool ignoreWorldRaycastWhenPointerOverUI = true;

    [Header("四、左键点击 / 长按 / 拖拽")]
    [Tooltip("是否启用鼠标左键射线功能。关闭后左键不会做 3D 射线点击和拖拽。")]
    [SerializeField] private bool enableLeftClickRaycast = true;

    [Tooltip("是否启用左键长按拖拽。关闭后只保留左键单击检测。")]
    [SerializeField] private bool enableLongPressDrag = true;

    [Tooltip("左键按住超过这个时间后，会被视为长按拖拽。单位：秒。推荐 0.15 到 0.25。")]
    [Range(0.01f, 1f)]
    [SerializeField] private float longPressTime = 0.18f;

    [Tooltip("左键按下后，鼠标移动超过这个像素距离，也会立即进入拖拽。数值越小越容易触发拖拽。")]
    [Range(0f, 30f)]
    [SerializeField] private float dragStartPixelDistance = 4f;

    [Header("五、IK Target 拖拽设置")]
    [Tooltip("开启后，如果左键点中的物体 Tag 等于 draggableTag，则直接拖动该物体。适合拖机器人末端 IK Target。")]
    [SerializeField] private bool enableHitTransformDrag = true;

    [Tooltip("可拖动物体的 Tag 名称。默认是 IKTarget。你需要在 Unity 的 Tag Manager 里创建这个 Tag，并给末端 IK 控制点设置该 Tag。")]
    [SerializeField] private string draggableTag = "IKTarget";

    [Tooltip("强制指定要拖动的 Transform。填了之后，只要左键射线命中有效物体，就优先拖这个 Transform。适合只允许拖某一个机器人末端 IK 目标。")]
    [SerializeField] private Transform overrideDragTarget;

    [Tooltip("拖拽时是否保持鼠标点击点与物体中心之间的初始偏移。开启后拖拽更自然，不会一点击就把物体中心吸到鼠标位置。")]
    [SerializeField] private bool keepInitialDragOffset = true;

    [Tooltip("拖拽时使用的平面模式。CameraFacing：面向相机，适合拖 IK；WorldXZ：地面平面，适合地面点选；HitNormal：用点击表面法线。")]
    [SerializeField] private DragPlaneMode dragPlaneMode = DragPlaneMode.CameraFacing;

    [Header("六、事件回调")]
    [Tooltip("左键单击命中物体时触发。可在 Inspector 里绑定 UI 面板、机器人控制脚本或调试函数。")]
    public RaycastHitUnityEvent onLeftClickHit;

    [Tooltip("左键刚按下并命中物体时触发。适合做选中高亮、显示操作面板。")]
    public RaycastHitUnityEvent onLeftDownHit;

    [Tooltip("左键拖拽刚开始时触发。适合记录初始状态、进入 IK 拖拽模式。")]
    public RaycastHitUnityEvent onDragBeginHit;

    [Tooltip("左键拖拽过程中每帧触发，输出当前拖拽平面上的世界坐标。适合驱动 IK Target 或显示坐标。")]
    public Vector3UnityEvent onDragWorldPoint;

    [Tooltip("左键单击但没有命中任何物体时触发。适合取消选择、隐藏控制面板。")]
    public UnityEvent onLeftClickMiss;

    private const int RaycastBufferSize = 16;

    private readonly RaycastHit[] _raycastHits = new RaycastHit[RaycastBufferSize];

    private Camera _camera;

    private Vector3 _moveVelocity;

    private float _yaw;
    private float _pitch;

    private bool _rightMouseHeld;
    private bool _leftMouseHeld;
    private bool _isDragging;
    private bool _leftDownHitValid;

    private float _leftDownTime;
    private Vector2 _leftDownScreenPos;

    private RaycastHit _leftDownHit;

    private IGameRaycastReceiver _activeReceiver;
    private Transform _activeDragTarget;

    private Plane _dragPlane;

    private Vector3 _lastDragWorldPoint;
    private Vector3 _dragOffset;

    private void Awake()
    {
        _camera = GetComponent<Camera>();

        if (_camera == null)
        {
            _camera = Camera.main;
        }

        if (_camera == null)
        {
            Debug.LogError("[GameCameraRayController] 当前物体上没有 Camera，场景中也没有 MainCamera。脚本已禁用。");
            enabled = false;
            return;
        }

        Vector3 euler = transform.rotation.eulerAngles;
        _yaw = euler.y;
        _pitch = NormalizePitch(euler.x);
    }

    private void OnDisable()
    {
        RestoreCursor();
        ResetLeftState();
    }

    private void Update()
    {
        HandleCameraInput();
        HandleRaycastInput();
    }

    private void HandleCameraInput()
    {
        _rightMouseHeld = Input.GetMouseButton(1);

        bool canMove = !moveOnlyWhenRightMouseHeld || _rightMouseHeld;

        if (_rightMouseHeld)
        {
            if (lockCursorWhenRotating)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            float mouseX = Input.GetAxisRaw("Mouse X");
            float mouseY = Input.GetAxisRaw("Mouse Y");

            _yaw += mouseX * mouseLookSensitivity;
            _pitch -= mouseY * mouseLookSensitivity;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }
        else
        {
            RestoreCursor();
        }

        if (enableWheelSpeedControl)
        {
            float wheel = Input.mouseScrollDelta.y;

            if (Mathf.Abs(wheel) > 0.001f)
            {
                moveSpeed = Mathf.Clamp(moveSpeed + wheel * wheelSpeedStep, minMoveSpeed, maxMoveSpeed);
            }
        }

        Vector3 localInput = Vector3.zero;
        float verticalInput = 0f;

        if (canMove)
        {
            if (Input.GetKey(KeyCode.W)) localInput += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) localInput += Vector3.back;
            if (Input.GetKey(KeyCode.A)) localInput += Vector3.left;
            if (Input.GetKey(KeyCode.D)) localInput += Vector3.right;
            if (Input.GetKey(KeyCode.E)) verticalInput += 1f;
            if (Input.GetKey(KeyCode.Q)) verticalInput -= 1f;
        }

        if (localInput.sqrMagnitude > 1f)
        {
            localInput.Normalize();
        }

        Vector3 desiredDirection = transform.TransformDirection(localInput);

        if (Mathf.Abs(verticalInput) > 0.001f)
        {
            desiredDirection += useWorldVerticalMove
                ? Vector3.up * verticalInput
                : transform.TransformDirection(Vector3.up * verticalInput);
        }

        if (desiredDirection.sqrMagnitude > 1f)
        {
            desiredDirection.Normalize();
        }

        float speed = moveSpeed;

        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            speed *= fastMultiplier;
        }

        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            speed *= slowMultiplier;
        }

        Vector3 desiredVelocity = desiredDirection * speed;

        float lerpFactor = 1f - Mathf.Exp(-moveSmooth * Time.unscaledDeltaTime);

        _moveVelocity = Vector3.Lerp(_moveVelocity, desiredVelocity, lerpFactor);

        transform.position += _moveVelocity * Time.unscaledDeltaTime;
    }

    private void HandleRaycastInput()
    {
        if (!enableLeftClickRaycast)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            OnLeftMouseDown();
        }

        if (Input.GetMouseButton(0))
        {
            OnLeftMouseHold();
        }

        if (Input.GetMouseButtonUp(0))
        {
            OnLeftMouseUp();
        }
    }

    private void OnLeftMouseDown()
    {
        if (ignoreWorldRaycastWhenPointerOverUI && IsPointerOverUI())
        {
            ResetLeftState();
            return;
        }

        _leftMouseHeld = true;
        _isDragging = false;
        _leftDownTime = Time.unscaledTime;
        _leftDownScreenPos = Input.mousePosition;

        _activeReceiver = null;
        _activeDragTarget = null;
        _dragOffset = Vector3.zero;

        Ray ray = _camera.ScreenPointToRay(Input.mousePosition);

        _leftDownHitValid = TryRaycast(ray, out _leftDownHit);

        if (!_leftDownHitValid)
        {
            return;
        }

        onLeftDownHit?.Invoke(_leftDownHit);

        _activeReceiver = _leftDownHit.collider.GetComponentInParent<IGameRaycastReceiver>();

        if (overrideDragTarget != null)
        {
            _activeDragTarget = overrideDragTarget;
        }
        else if (enableHitTransformDrag)
        {
            Transform hitTransform = _leftDownHit.collider.transform;

            if (HasTag(hitTransform, draggableTag))
            {
                _activeDragTarget = hitTransform;
            }
            else if (hitTransform.root != null && HasTag(hitTransform.root, draggableTag))
            {
                _activeDragTarget = hitTransform.root;
            }
        }

        SetupDragPlane(_leftDownHit, ray);

        Vector3 initialPoint = GetPointOnDragPlane(ray, _leftDownHit.point);

        _lastDragWorldPoint = initialPoint;

        if (_activeDragTarget != null && keepInitialDragOffset)
        {
            _dragOffset = _activeDragTarget.position - initialPoint;
        }
    }

    private void OnLeftMouseHold()
    {
        if (!_leftMouseHeld || !_leftDownHitValid || !enableLongPressDrag)
        {
            return;
        }

        float holdTime = Time.unscaledTime - _leftDownTime;
        float pixelDistance = Vector2.Distance(_leftDownScreenPos, Input.mousePosition);

        bool shouldDrag =
            holdTime >= longPressTime ||
            pixelDistance >= dragStartPixelDistance;

        if (!shouldDrag)
        {
            return;
        }

        Ray ray = _camera.ScreenPointToRay(Input.mousePosition);

        Vector3 worldPoint = GetPointOnDragPlane(ray, _lastDragWorldPoint);
        Vector3 worldDelta = worldPoint - _lastDragWorldPoint;

        GameRaycastContext context = CreateContext(
            ray,
            _leftDownHit,
            worldPoint,
            worldDelta,
            holdTime,
            true,
            true);

        if (!_isDragging)
        {
            _isDragging = true;

            onDragBeginHit?.Invoke(_leftDownHit);

            _activeReceiver?.OnRayDragBegin(context);
        }

        _activeReceiver?.OnRayDrag(context);

        if (_activeDragTarget != null)
        {
            _activeDragTarget.position = worldPoint + _dragOffset;
        }

        onDragWorldPoint?.Invoke(worldPoint);

        _lastDragWorldPoint = worldPoint;
    }

    private void OnLeftMouseUp()
    {
        if (!_leftMouseHeld)
        {
            ResetLeftState();
            return;
        }

        float holdTime = Time.unscaledTime - _leftDownTime;

        if (_leftDownHitValid)
        {
            Ray ray = _camera.ScreenPointToRay(Input.mousePosition);

            Vector3 worldPoint = _isDragging
                ? _lastDragWorldPoint
                : _leftDownHit.point;

            GameRaycastContext context = CreateContext(
                ray,
                _leftDownHit,
                worldPoint,
                Vector3.zero,
                holdTime,
                holdTime >= longPressTime,
                _isDragging);

            if (_isDragging)
            {
                _activeReceiver?.OnRayDragEnd(context);
            }
            else
            {
                _activeReceiver?.OnRayClick(context);
                onLeftClickHit?.Invoke(_leftDownHit);
            }
        }
        else
        {
            onLeftClickMiss?.Invoke();
        }

        ResetLeftState();
    }

    private bool TryRaycast(Ray ray, out RaycastHit bestHit)
    {
        int count = Physics.RaycastNonAlloc(
            ray,
            _raycastHits,
            raycastMaxDistance,
            raycastMask,
            triggerInteraction);

        bestHit = default;

        if (count <= 0)
        {
            return false;
        }

        float bestDistance = float.MaxValue;
        int bestIndex = -1;
        int safeCount = Mathf.Min(count, _raycastHits.Length);

        for (int i = 0; i < safeCount; i++)
        {
            if (_raycastHits[i].distance < bestDistance)
            {
                bestDistance = _raycastHits[i].distance;
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
        {
            return false;
        }

        bestHit = _raycastHits[bestIndex];
        return true;
    }

    private void SetupDragPlane(RaycastHit hit, Ray ray)
    {
        Vector3 normal;

        switch (dragPlaneMode)
        {
            case DragPlaneMode.WorldXY:
                normal = Vector3.forward;
                break;

            case DragPlaneMode.WorldXZ:
                normal = Vector3.up;
                break;

            case DragPlaneMode.WorldYZ:
                normal = Vector3.right;
                break;

            case DragPlaneMode.HitNormal:
                normal = hit.normal.sqrMagnitude > 0.0001f
                    ? hit.normal.normalized
                    : -ray.direction;
                break;

            case DragPlaneMode.CameraFacing:
            default:
                normal = -transform.forward;
                break;
        }

        _dragPlane = new Plane(normal, hit.point);
    }

    private Vector3 GetPointOnDragPlane(Ray ray, Vector3 fallback)
    {
        if (_dragPlane.Raycast(ray, out float enter))
        {
            return ray.GetPoint(enter);
        }

        return fallback;
    }

    private GameRaycastContext CreateContext(
        Ray ray,
        RaycastHit hit,
        Vector3 worldPoint,
        Vector3 worldDelta,
        float holdTime,
        bool isLongPress,
        bool isDragging)
    {
        return new GameRaycastContext
        {
            Camera = _camera,
            Ray = ray,
            Hit = hit,
            WorldPoint = worldPoint,
            WorldDelta = worldDelta,
            ScreenPosition = Input.mousePosition,
            HoldTime = holdTime,
            IsLongPress = isLongPress,
            IsDragging = isDragging
        };
    }

    private bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private bool HasTag(Transform target, string tagName)
    {
        if (target == null || string.IsNullOrEmpty(tagName))
        {
            return false;
        }

        return target.gameObject.tag == tagName;
    }

    private void ResetLeftState()
    {
        _leftMouseHeld = false;
        _isDragging = false;
        _leftDownHitValid = false;

        _activeReceiver = null;
        _activeDragTarget = null;

        _dragOffset = Vector3.zero;
    }

    private void RestoreCursor()
    {
        if (!lockCursorWhenRotating)
        {
            return;
        }

        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private float NormalizePitch(float pitch)
    {
        if (pitch > 180f)
        {
            pitch -= 360f;
        }

        return pitch;
    }
}