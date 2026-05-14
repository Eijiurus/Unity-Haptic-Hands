using System;
using UnityEngine;
using Assets.Device;

/// <summary>
/// 根据 GloveDataReceiver 提供的五指弯曲值旋转手指骨骼，
/// 根据 WitMotion WT9011DCL-BT50 传感器提供的姿态数据旋转手腕。
/// </summary>
/// <remarks>
/// 执行顺序：用 [DefaultExecutionOrder(-200)] 强制本驱动的 Update 在所有"普通"脚本之前跑。
/// 这样有两层好处：
///   1) RotaryKnob.Update 等下游脚本读取 RawWristTargetPosition 时，拿到的永远是当前帧最新值，
///      不会有 1 帧 lag。
///   2) lockWristPosition 写入 transform.localPosition 后，再没有任何 Update 可以晚于它跑去覆盖
///      手腕位置——彻底排除"锁被另一脚本顶掉"的帧序竞争（这是 v7 抓取期间手腕抖动的最常见原因）。
/// </remarks>
[DefaultExecutionOrder(-200)]
public class DataGloveHandDriver : MonoBehaviour
{
    [SerializeField] private GloveDataReceiver gloveData; // 手套数据接收器

    [Header("手指骨骼配置")] // 手指骨骼配置    
    [SerializeField] private FingerConfig thumb = new FingerConfig { boneName = "thumb", bendAxis = new Vector3(0, 0, -1), maxBendAngle = 80f }; // 拇指配置
    [SerializeField] private FingerConfig index = new FingerConfig { boneName = "finger_index", bendAxis = new Vector3(1, 0, 0), maxBendAngle = 90f }; // 食指配置
    [SerializeField] private FingerConfig middle = new FingerConfig { boneName = "finger_middle", bendAxis = new Vector3(1, 0, 0), maxBendAngle = 90f }; // 中指配置
    [SerializeField] private FingerConfig ring = new FingerConfig { boneName = "finger_ring", bendAxis = new Vector3(1, 0, 0), maxBendAngle = 90f }; // 无名指配置
    [SerializeField] private FingerConfig pinky = new FingerConfig { boneName = "finger_pinky", bendAxis = new Vector3(1, 0, 0), maxBendAngle = 90f }; // 小指配置

    [Header("骨骼查找")] // 骨骼查找
    [Tooltip("骨骼后缀，左手为 .L，右手为 .R")] // 骨骼后缀，左手为 .L，右手为 .R
    [SerializeField] private string boneSuffix = ".L"; // 骨骼后缀，左手为 .L，右手为 .R

    [Header("手指平滑")] // 手指平滑
    [Tooltip("数值越大越平滑，但延迟也越大。设为 0 关闭平滑。")] // 数值越大越平滑，但延迟也越大。设为 0 关闭平滑。
    [SerializeField, Range(0f, 30f)] private float smoothSpeed = 12f; // 手指平滑，数值越大越平滑，但延迟也越大。设为 0 关闭平滑。

    [Header("键盘握拳覆盖 (调试/演示)")] // 键盘握拳覆盖 (调试/演示)
    [Tooltip("按下指定按键时，强制五指全部弯曲为握拳。")] // 按下指定按键时，强制五指全部弯曲为握拳。
    [SerializeField] private bool enableKeyboardFistOverride = true; // 按下指定按键时，强制五指全部弯曲为握拳。

    [Tooltip("触发握拳的按键。默认空格。")] // 触发握拳的按键。默认空格。
    [SerializeField] private KeyCode fistOverrideKey = KeyCode.Space; // 触发握拳的按键。默认空格。

    [Header("Wrist Rotation Lock")] // 手腕旋转锁定
    [Tooltip("When true, the wrist's rotation is not updated this frame. Used by interactive elements (e.g., RotaryKnob) that need to freeze the hand's wrist orientation while the user manipulates them.")]
    public bool lockWristRotation = false; // 手腕旋转锁定，当为 true 时，手腕的旋转不会在本帧更新。用于交互元素（如 RotaryKnob）需要冻结手的手腕方向时使用。

    [Header("Wrist Position Lock")] // 手腕位置锁定
    [Tooltip("When true, the wrist's world position is hard-snapped to lockedWristPositionTarget every frame, ignoring the IMU/keyboard target. Used by GrabbableKnobAdapter to glue the virtual hand onto a knob during grab. The IMU pipeline keeps integrating internally, so RawWristTargetPosition still reflects real hand motion.")]
    public bool lockWristPosition = false; // 手腕位置锁定，当为 true 时，手腕的世界位置会被硬 snapped 到 lockedWristPositionTarget 每一帧，忽略 IMU/键盘目标。用于 GrabbableKnobAdapter 在抓取期间将虚拟手粘接到旋钮上。IMU 管道内部继续集成，所以 RawWristTargetPosition 仍然反映真实手部运动。

    [Tooltip("World-space position the wrist snaps to while lockWristPosition is true (e.g. a knob's center).")]
    public Vector3 lockedWristPositionTarget; // 手腕位置锁定，当为 true 时，手腕的世界位置会被硬 snapped 到 lockedWristPositionTarget 每一帧，忽略 IMU/键盘目标。用于 GrabbableKnobAdapter 在抓取期间将虚拟手粘接到旋钮上。IMU 管道内部继续集成，所以 RawWristTargetPosition 仍然反映真实手部运动。    Vector3是三维向量，表示位置

    /// <summary>
    /// World-space target position the IMU pipeline computed THIS frame, BEFORE smoothing and BEFORE any
    /// lockWristPosition override. Read by RotaryKnob (and similar interactive elements) so they can detect
    /// real hand displacement even while the visual wrist is snapped to a fixed object.
    /// Updated once per frame inside ApplyFinalWristPosition.
    /// </summary>
    public Vector3 RawWristTargetPosition => _rawWristTargetWorld; // 手腕目标位置，IMU 管道内部继续集成，所以 RawWristTargetPosition 仍然反映真实手部运动。

    [Header("手腕姿态 (WitMotion 传感器)")] // 手腕姿态 (WitMotion 传感器)
    [Tooltip("是否启用蓝牙传感器控制手腕旋转")] // 是否启用蓝牙传感器控制手腕旋转
    public bool enableWristRotation = true; // 是否启用蓝牙传感器控制手腕旋转

    [Tooltip("手腕跟随速度：数值越小越稳、越不飘，但延迟略大")] // 手腕跟随速度：数值越小越稳、越不飘，但延迟略大
    [SerializeField, Range(1f, 30f)] private float wristSmoothSpeed = 3f; // 手腕跟随速度：数值越小越稳、越不飘，但延迟略大

    [Tooltip("角度灵敏度：1=与传感器角度 1:1，调小则同样手腕动作虚拟手转得少、更不飘")]
    [SerializeField, Range(0.05f, 1f)] private float wristAngleScale = 0.1f; // 角度灵敏度：1=与传感器角度 1:1，调小则同样手腕动作虚拟手转得少、更不飘

    [Tooltip("Yaw 灵敏度：用于弱化 Z 轴旋转漂移；1=不弱化")] // Yaw 灵敏度：用于弱化 Z 轴旋转漂移；1=不弱化
    [SerializeField, Range(0f, 1f)] private float yawScale = 0.45f; // Yaw 灵敏度：用于弱化 Z 轴旋转漂移；1=不弱化

    [Tooltip("对传感器欧拉角再做一层低通（0=关闭）。略减抖动，略增延迟")]
    [SerializeField, Range(0f, 25f)] private float wristEulerFilterSpeed = 18f; // 对传感器欧拉角再做一层低通（0=关闭）。略减抖动，略增延迟

    [Tooltip("旋转死区（度）：先对原始角度做死区，再乘灵敏度，减少小角度抖动")]
    [SerializeField, Range(0f, 20f)] private float wristRotationDeadzone = 12f; // 旋转死区（度）：先对原始角度做死区，再乘灵敏度，减少小角度抖动

    [Tooltip("坐标系映射：传感器轴 → Unity 轴的符号翻转\n" +
             "传感器右手坐标系与 Unity 左手坐标系不同，佩戴方向也会影响映射。\n" +
             "如果旋转方向不对，尝试翻转各分量的正负号。")]
    [SerializeField] private Vector3 axisSign = new Vector3(-1f, -1f, 1f); // 坐标系映射：传感器轴 → Unity 轴的符号翻转

    [Tooltip("坐标系映射：传感器 XYZ → Unity XYZ 的轴重排\n" +
             "0=传感器X, 1=传感器Y, 2=传感器Z\n" +
             "默认 (0,2,1) 表示：Unity.X=传感器.X, Unity.Y=传感器.Z, Unity.Z=传感器.Y")]
    [SerializeField] private Vector3Int axisRemap = new Vector3Int(0, 2, 1); // 坐标系映射：传感器 XYZ → Unity XYZ 的轴重排

    [Header("手腕位置 (WitMotion 传感器)")]
    [Tooltip("是否启用蓝牙传感器控制手腕位置（更适合近场、短距离的相对位移增强，不适合远距离自由平移）")]
    public bool enableWristPosition = true; // 是否启用蓝牙传感器控制手腕位置

    [Tooltip("位置跟随平滑速度：越小越稳，越大越跟手")]
    [SerializeField, Range(1f, 30f)] private float wristPositionSmoothSpeed = 22f; // 位置跟随平滑速度：越小越稳，越大越跟手

    [Tooltip("线性加速度积分增益：越大位移变化越明显")]
    [SerializeField, Range(0.05f, 3f)] private float wristPositionGain = 2.2f; // 线性加速度积分增益：越大位移变化越明显

    [Tooltip("线性加速度死区（g）：抑制重力补偿后的微小噪声")]
    [SerializeField, Range(0f, 0.2f)] private float wristPositionDeadzone = 0.004f; // 线性加速度死区（g）：抑制重力补偿后的微小噪声

    [Tooltip("线性加速度低通速度（0=关闭）。用于抑制重力估计后的高频抖动")]
    [SerializeField, Range(0f, 30f)] private float wristLinearAccelFilterSpeed = 16f; // 线性加速度低通速度（0=关闭）。用于抑制重力估计后的高频抖动

    [Tooltip("重力估计低通速度：越小越不容易把真实平移误吸收到重力里；近场演示通常可适当调低")]
    [SerializeField, Range(0.1f, 10f)] private float gravityEstimateFilterSpeed = 1.0f; // 重力估计低通速度：越小越不容易把真实平移误吸收到重力里；近场演示通常可适当调低

    [Tooltip("静止时线性残差偏置学习速度，用于慢慢修正加速度零偏")]
    [SerializeField, Range(0f, 10f)] private float accelBiasLearningSpeed = 1.2f; // 静止时线性残差偏置学习速度，用于慢慢修正加速度零偏

    [Tooltip("是否启用启动静止标定与后续偏置学习")]
    [SerializeField] private bool useGravityEstimateCalibration = true; // 是否启用启动静止标定与后续偏置学习

    [Tooltip("启动静止标定时长（秒）：建议放在 1~2 秒之间")]
    [SerializeField, Range(0.2f, 3f)] private float startupCalibrationDuration = 1.2f; // 启动静止标定时长（秒）：建议放在 1~2 秒之间

    [Tooltip("角速度门控阈值（°/s）：转腕过快时暂停位置积分，避免假位移")]
    [SerializeField, Range(10f, 1080f)] private float wristAngularVelocityGate = 120f; // 角速度门控阈值（°/s）：转腕过快时暂停位置积分，避免假位移 

    [Tooltip("转动冻结窗口时长（秒）：角速度过大后继续冻结一小段时间")]
    [SerializeField, Range(0.12f, 0.3f)] private float positionFreezeDuration = 0.18f; // 转动冻结窗口时长（秒）：角速度过大后继续冻结一小段时间

    [Tooltip("冻结期间的额外速度阻尼倍数：越大越能压住假位移")]
    [SerializeField, Range(1f, 10f)] private float freezeVelocityDampingMultiplier = 4f; // 冻结期间的额外速度阻尼倍数：越大越能压住假位移

    [Tooltip("位置弹簧回中强度：防止偏移完全靠自由积分越飘越远；近场演示建议保持较温和回中")]
    [SerializeField, Range(1f, 20f)] private float wristPositionSpring = 4.5f; // 位置弹簧回中强度：防止偏移完全靠自由积分越飘越远；近场演示建议保持较温和回中

    [Tooltip("位置轴向权重：逐轴削弱最容易被转动污染的轴；近场演示建议优先保留最稳的两个轴")]
    [SerializeField] private Vector3 positionAxisWeight = new Vector3(1f, 0.8f, 0.55f); // 位置轴向权重：逐轴削弱最容易被转动污染的轴；近场演示建议优先保留最稳的两个轴

    [Tooltip("仅使用平面相对位移：自动压掉 positionAxisWeight 中最不稳定的那个轴，提升演示稳定性")]
    [SerializeField] private bool usePlanarPositionOnly = false; // 仅使用平面相对位移：自动压掉 positionAxisWeight 中最不稳定的那个轴，提升演示稳定性

    [Tooltip("持续缓慢回中速度：非冻结、非静止时也让偏移慢慢回到 0，防止长期累计")]
    [SerializeField, Range(0f, 5f)] private float positionRecenteringSpeed = 0.45f; // 持续缓慢回中速度：非冻结、非静止时也让偏移慢慢回到 0，防止长期累计

    [Tooltip("静止线性加速度阈值（g）：与角速度阈值共同用于 zero-velocity update")]
    [SerializeField, Range(0.001f, 0.2f)] private float wristStillAccelerationThreshold = 0.025f; // 静止线性加速度阈值（g）：与角速度阈值共同用于 zero-velocity update

    [Tooltip("静止角速度阈值（°/s）：与线性加速度阈值共同用于 zero-velocity update")]
    [SerializeField, Range(0.5f, 60f)] private float wristStillAngularSpeedThreshold = 6f; // 静止角速度阈值（°/s）：与线性加速度阈值共同用于 zero-velocity update

    [Tooltip("静止确认时间（秒）：连续满足静止条件后清零速度")]
    [SerializeField, Range(0f, 1f)] private float wristStillConfirmTime = 0.12f; // 静止确认时间（秒）：连续满足静止条件后清零速度

    [Tooltip("速度阻尼：越大越快停下来，越不漂；近场演示可略低一些以保留短距离响应")]
    [SerializeField, Range(0f, 20f)] private float wristVelocityDamping = 1.8f; // 速度阻尼：越大越快停下来，越不漂；近场演示可略低一些以保留短距离响应

    [Tooltip("最大相对位移（米）：建议保持较小范围，本系统更适合近场交互增强，不适合远距离自由平移")]
    [SerializeField, Range(0.02f, 0.8f)] private float wristMaxOffset = 0.18f; // 最大相对位移（米）：建议保持较小范围，本系统更适合近场交互增强，不适合远距离自由平移

    [Tooltip("位置坐标系映射：传感器轴 → Unity 轴的符号翻转")]
    [SerializeField] private Vector3 positionAxisSign = new Vector3(-1f, -1f, 1f); // 位置坐标系映射：传感器轴 → Unity 轴的符号翻转 

    [Tooltip("位置坐标系映射：传感器 XYZ → Unity XYZ 的轴重排（0=X,1=Y,2=Z）")]
    [SerializeField] private Vector3Int positionAxisRemap = new Vector3Int(0, 2, 1); // 位置坐标系映射：传感器 XYZ → Unity XYZ 的轴重排（0=X,1=Y,2=Z）

    [Header("键盘位置控制 (调试/演示)")]
    [Tooltip("启用键盘移动手的位置（与 WitMotion 位置偏移叠加）")]
    [SerializeField] private bool enableKeyboardPositionControl = true; // 启用键盘移动手的位置（与 WitMotion 位置偏移叠加）

    [Tooltip("键盘移动速度（米/秒）")]
    [SerializeField, Range(0.1f, 3f)] private float keyboardMoveSpeed = 0.9f; // 键盘移动速度（米/秒）

    [Tooltip("键盘偏移最大范围（米）")]
    [SerializeField, Range(0.05f, 3f)] private float keyboardMaxOffset = 1.5f; // 键盘偏移最大范围（米）

    [Tooltip("是否限制键盘位移范围（关闭后可持续移动）")]
    [SerializeField] private bool limitKeyboardOffset = false; // 是否限制键盘位移范围（关闭后可持续移动）

    private Quaternion _initialWristRotation; // 初始手腕旋转
    private Quaternion _currentWristRotation;
    private Quaternion _displayTargetWristRotation = Quaternion.identity; // 显示目标手腕旋转
    private Vector3 _filteredDisplayEuler;
    private bool _wristEulerFilterPrimed; // 手腕欧拉滤波器已准备好
    private int _lastWristSensorFrame = -1;

    private Vector3 _initialWristPosition; // 初始手腕位置
    private Vector3 _currentWristOffset;
    private Vector3 _wristVelocity; // 手腕速度
    private Vector3 _rawWristTargetWorld;
    private Vector3 _gravityEstimate; // 重力估计
    private bool _gravityEstimatePrimed;
    private Vector3 _filteredLinearAcceleration;
    private bool _linearAccelerationFilterPrimed; // 线性加速度滤波器已准备好
    private float _wristStillTimer;
    private float _positionFreezeTimer; // 位置冻结计时器

    private Vector3 accelerationBias; // 加速度偏置
    private bool isStartupCalibrating; // 是否启动静止标定
    private float startupCalibrationTimer; // 启动静止标定计时器
    private Vector3 startupGravityAccumulator; // 启动静止标定重力累积
    private Vector3 startupBiasAccumulator; // 启动静止标定偏置累积
    private int startupSampleCount; // 启动静止标定样本数

    private Vector3 _keyboardOffset; // 键盘偏移

    private FingerConfig[] _fingers; // 手指配置
    private readonly float[] _currentValues = new float[5];
    private Quaternion[][] _initialRotations; // 初始旋转
    private bool _bonesReady;

    void Start() // 开始
    {
        _fingers = new[] { thumb, index, middle, ring, pinky };
        AutoFindBones();
        CacheInitialRotations();

        _initialWristRotation = transform.localRotation; // 初始手腕旋转
        _currentWristRotation = Quaternion.identity; // 当前手腕旋转
        _displayTargetWristRotation = Quaternion.identity; // 显示目标手腕旋转
        _filteredDisplayEuler = Vector3.zero; // 过滤显示欧拉角
        _wristEulerFilterPrimed = false; // 手腕欧拉滤波器已准备好
        _lastWristSensorFrame = -1; // 最后手腕传感器帧

        _initialWristPosition = transform.localPosition; // 初始手腕位置
        _currentWristOffset = Vector3.zero; // 当前手腕偏移
        _wristVelocity = Vector3.zero; // 手腕速度
        _gravityEstimate = Vector3.zero; // 重力估计
        _gravityEstimatePrimed = false; // 重力估计已准备好
        _filteredLinearAcceleration = Vector3.zero; // 过滤线性加速度
        _linearAccelerationFilterPrimed = false; // 线性加速度滤波器已准备好
        _wristStillTimer = 0f; // 手腕静止计时器
        _positionFreezeTimer = 0f; // 位置冻结计时器
        accelerationBias = Vector3.zero; // 加速度偏置
        isStartupCalibrating = true;
        startupCalibrationTimer = 0f; // 启动静止标定计时器
        startupGravityAccumulator = Vector3.zero; // 启动静止标定重力累积
        startupBiasAccumulator = Vector3.zero; // 启动静止标定偏置累积
        startupSampleCount = 0; // 启动静止标定样本数
        _keyboardOffset = Vector3.zero; // 键盘偏移
    }

    void Update() // 更新
    {
        if (_bonesReady && gloveData != null) // 如果骨骼准备好且手套数据不为空，则更新手指弯曲
        {
            bool forceFist = enableKeyboardFistOverride && Input.GetKey(fistOverrideKey); // 强制握拳  enableKeyboardFistOverride是启用键盘握拳覆盖，fistOverrideKey是触发握拳的按键
            for (int i = 0; i < 5; i++) // 遍历手指
            {
                float target = forceFist ? 1f : gloveData.FingerValues[i]; // 目标值
                _currentValues[i] = smoothSpeed > 0f // 平滑速度 smoothSpeed是手指平滑，数值越大越平滑，但延迟也越大。设为 0 关闭平滑。
                    ? Mathf.Lerp(_currentValues[i], target, Time.deltaTime * smoothSpeed) // 平滑值 Mathf.Lerp是线性插值
                    : target; // 目标值
                ApplyFingerBend(i, _currentValues[i]); // 应用手指弯曲  ApplyFingerBend是应用手指弯曲  手指弯曲 = 初始旋转 * 弯曲旋转
            }
        }

        DeviceModel wtDevice = null; // 手腕设备  DeviceModel是手腕设备模型
        if (enableWristRotation || enableWristPosition) // 如果启用手腕旋转或手腕位置，则获取当前手腕设备
        {
            wtDevice = GetCurrentWristDevice(); // 获取当前手腕设备  GetCurrentWristDevice是获取当前手腕设备
            if (wtDevice != null) // 如果手腕设备不为空，则更新手腕传感器状态
                UpdateWristSensorState(wtDevice); // 更新手腕传感器状态  UpdateWristSensorState是更新手腕传感器状态
        }

        if (enableWristRotation) // 如果启用手腕旋转，则应用手腕旋转
            ApplyWristRotation(wtDevice); // 应用手腕旋转  ApplyWristRotation是应用手腕旋转

        if (enableWristPosition) // 如果启用手腕位置，则应用手腕位置
            ApplyWristPosition(wtDevice); // 应用手腕位置  ApplyWristPosition是应用手腕位置

        if (enableKeyboardPositionControl) // 如果启用键盘位置控制，则更新键盘位置偏移  
            UpdateKeyboardPositionOffset(); // 更新键盘位置偏移  UpdateKeyboardPositionOffset是更新键盘位置偏移

        ApplyFinalWristPosition(); // 应用最终手腕位置  最终手腕位置 = 初始手腕位置 + 当前手腕偏移 + 键盘偏移
    }

    /// <summary>
    /// 从 WitMotion SDK 获取欧拉角并应用到手腕根节点。
    /// SDK 的 DeviceModel 通过 GetDeviceData("AngX"/"AngY"/"AngZ") 提供欧拉角。
    /// </summary>
    private void ApplyWristRotation(DeviceModel wtDevice)
    {
        if (wtDevice == null) return; // 如果手腕设备为空，则返回

        _currentWristRotation = Quaternion.Slerp(
            _currentWristRotation, // 当前手腕旋转
            _displayTargetWristRotation, // 显示目标手腕旋转
            Time.deltaTime * wristSmoothSpeed); // 平滑速度

        if (!lockWristRotation) // 如果手腕旋转锁定为false，则应用手腕旋转
        {
            transform.localRotation = _initialWristRotation * _currentWristRotation; // 应用手腕旋转
        }
    }

    /// <summary>
    /// 使用 WitMotion 的加速度数据估算手腕相对位移。
    /// 这里不再依赖欧拉角旋转后的重力补偿，而是使用慢速低通估计重力，
    /// 再叠加启动标定、转动冻结窗口和弹簧回中来抑制假位移。
    /// </summary>
    private void ApplyWristPosition(DeviceModel wtDevice)
    {
        if (wtDevice == null) return; // 如果手腕设备为空，则返回

        float accX = (float)wtDevice.GetDeviceData("AccX"); // 加速度X
        float accY = (float)wtDevice.GetDeviceData("AccY"); // 加速度Y
        float accZ = (float)wtDevice.GetDeviceData("AccZ"); // 加速度Z

        float asX = (float)wtDevice.GetDeviceData("AsX"); // 角速度X
        float asY = (float)wtDevice.GetDeviceData("AsY"); // 角速度Y
        float asZ = (float)wtDevice.GetDeviceData("AsZ"); // 角速度Z    

        Vector3 mappedSensorAcc = MapSensorVector(accX, accY, accZ, positionAxisRemap, positionAxisSign); 
        Vector3 mappedAngularVelocity = MapSensorVector(asX, asY, asZ, axisRemap, axisSign);
        float angularSpeed = mappedAngularVelocity.magnitude; // 角速度
        float dt = Time.deltaTime;

        UpdateStartupCalibration(mappedSensorAcc, angularSpeed, dt); // 更新启动静止标定

        if (angularSpeed >= wristAngularVelocityGate) // 如果角速度大于等于手腕角速度门控阈值，则设置位置冻结计时器
            _positionFreezeTimer = positionFreezeDuration; // 设置位置冻结计时器

        if (_positionFreezeTimer > 0f) // 如果位置冻结计时器大于0，则设置位置冻结计时器
            _positionFreezeTimer = Mathf.Max(0f, _positionFreezeTimer - dt); // 设置位置冻结计时器

        bool isPositionFrozen = isStartupCalibrating || _positionFreezeTimer > 0f; // 如果启动静止标定或位置冻结计时器大于0，则设置位置冻结

        UpdateGravityEstimate(mappedSensorAcc, isPositionFrozen, dt); // 更新重力估计

        Vector3 rawLinearAcc = mappedSensorAcc - _gravityEstimate; // 原始线性加速度

        if (!isStartupCalibrating && useGravityEstimateCalibration && angularSpeed <= wristStillAngularSpeedThreshold) // 如果启动静止标定为false，使用重力估计标定，角速度小于等于手腕静止角速度阈值，则更新加速度偏置
        {
            float biasLerp = Mathf.Clamp01(dt * accelBiasLearningSpeed); // 加速度偏置学习速度
            accelerationBias = Vector3.Lerp(accelerationBias, rawLinearAcc, biasLerp); // 更新加速度偏置
        }

        Vector3 linearAcc = rawLinearAcc - accelerationBias;
        linearAcc = FilterLinearAcceleration(linearAcc); // 过滤线性加速度
        linearAcc = Vector3.Scale(linearAcc, positionAxisWeight); // 缩放线性加速度
        linearAcc = ApplyPlanarPositionConstraint(linearAcc); // 应用平面位置约束
        linearAcc = ApplyComponentDeadzone(linearAcc, wristPositionDeadzone); // 应用组件死区

        bool isStillCandidate =
            linearAcc.magnitude <= wristStillAccelerationThreshold &&
            angularSpeed <= wristStillAngularSpeedThreshold;

        _wristStillTimer = isStillCandidate ? _wristStillTimer + dt : 0f;
        bool isStill = _wristStillTimer >= wristStillConfirmTime;

        if (isStill) // 如果静止，则设置手腕速度为0，当前手腕偏移为0，更新重力估计
        {
            // 静止时执行 zero-velocity update，并轻微拉回偏移中心，
            // 让手在无输入时更稳定地停住，而不是保留残余漂移。
            _wristVelocity = Vector3.zero; // 设置手腕速度为0
            _currentWristOffset = Vector3.Lerp(
                _currentWristOffset, // 当前手腕偏移
                Vector3.zero, // 目标手腕偏移
                Mathf.Clamp01(dt * Mathf.Max(1f, wristPositionSpring * 0.35f))); // 平滑手腕偏移

            // 静止时允许更快重建重力基线，帮助恢复转动后的稳定状态。
            UpdateGravityEstimate(mappedSensorAcc, true, dt); // 更新重力估计
            return;
        }

        if (isPositionFrozen) // 如果位置冻结，则设置手腕速度为0，更新重力估计
        {
            // 关键改动：加入“转动冻结窗口”。
            // 只要刚发生明显转腕，就短暂停止位置积分，并加强速度阻尼，
            // 用时间窗口切断“角速度扰动 -> 假平移”的链路。
            float freezeDamping = wristVelocityDamping * Mathf.Max(1f, freezeVelocityDampingMultiplier);
            _wristVelocity = Vector3.Lerp(_wristVelocity, Vector3.zero, dt * freezeDamping);
            return;
        }

        _wristVelocity += linearAcc * (wristPositionGain * dt);
        // 关键改动：加入弹簧回中，让偏移更像“有限范围的相对位移”，
        // 而不是自由积分后越来越远。
        _wristVelocity -= _currentWristOffset * (wristPositionSpring * dt); // 减去当前手腕偏移
        _wristVelocity = Vector3.Lerp(_wristVelocity, Vector3.zero, dt * wristVelocityDamping); // 平滑手腕速度
        _currentWristOffset += _wristVelocity * dt; // 更新当前手腕偏移

        if (positionRecenteringSpeed > 0f) // 如果位置重中速度大于0，则更新当前手腕偏移
        {
            // 近场演示模式下，即便在运动过程中也让偏移非常缓慢地回中，
            // 这样不会吃掉短距离操作感，但能避免长期累计后越偏越远。
            float recenterT = Mathf.Clamp01(dt * positionRecenteringSpeed);
            _currentWristOffset = Vector3.Lerp(_currentWristOffset, Vector3.zero, recenterT);
        }

        _currentWristOffset = Vector3.ClampMagnitude(_currentWristOffset, wristMaxOffset); // 限制当前手腕偏移范围
    }

    private DeviceModel GetCurrentWristDevice() // 获取当前手腕设备
    {
        if (DevicesManager.Instance == null) return null; // 如果设备管理器为空，则返回空

        DeviceModel wtDevice = DevicesManager.Instance.GetCurrentDevice(); // 获取当前手腕设备
        if (wtDevice == null || !wtDevice.isOpen) return null; // 如果手腕设备为空或未打开，则返回空
        return wtDevice; // 返回手腕设备
    }

    private void UpdateWristSensorState(DeviceModel wtDevice)
    {
        if (wtDevice == null || _lastWristSensorFrame == Time.frameCount) return; // 如果手腕设备为空或最后手腕传感器帧等于当前帧，则返回

        float angX = (float)wtDevice.GetDeviceData("AngX"); // 角度X
        float angY = (float)wtDevice.GetDeviceData("AngY"); // 角度Y
        float angZ = (float)wtDevice.GetDeviceData("AngZ"); // 角度Z

        Vector3 mappedRawEuler = MapSensorVector(angX, angY, angZ, axisRemap, axisSign);

        // 关键改动：先对原始角度做死区，再乘显示灵敏度。
        Vector3 displayRawEuler = ApplyComponentDeadzone(mappedRawEuler, wristRotationDeadzone);
        Vector3 displayEuler = new Vector3(
            displayRawEuler.x * wristAngleScale, // 角度X
            displayRawEuler.y * wristAngleScale, // 角度Y
            displayRawEuler.z * wristAngleScale * yawScale); // 角度Z

        if (wristEulerFilterSpeed > 0f) // 如果手腕欧拉滤波速度大于0，则更新过滤显示欧拉角
        {
            if (!_wristEulerFilterPrimed) // 如果手腕欧拉滤波器未准备好，则更新过滤显示欧拉角
            {
                _filteredDisplayEuler = displayEuler;
                _wristEulerFilterPrimed = true; // 设置手腕欧拉滤波器已准备好
            }
            else
            {
                float t = Mathf.Clamp01(Time.deltaTime * wristEulerFilterSpeed); // 平滑手腕欧拉滤波
                _filteredDisplayEuler = Vector3.Lerp(_filteredDisplayEuler, displayEuler, t);
            }
        }
        else
        {
            _filteredDisplayEuler = displayEuler; // 设置过滤显示欧拉角
            _wristEulerFilterPrimed = true; // 设置手腕欧拉滤波器已准备好
        }

        _displayTargetWristRotation = Quaternion.Euler(_filteredDisplayEuler); // 设置显示目标手腕旋转
        _lastWristSensorFrame = Time.frameCount; // 设置最后手腕传感器帧
    }

    private void UpdateStartupCalibration(Vector3 mappedSensorAcc, float angularSpeed, float dt) // 更新启动静止标定
    {
        if (!isStartupCalibrating) return; // 如果启动静止标定为false，则返回

        if (angularSpeed > wristStillAngularSpeedThreshold) // 如果角速度大于等于手腕静止角速度阈值，则返回
            return;

        startupCalibrationTimer += dt; // 更新启动静止标定计时器
        startupSampleCount++; // 更新启动静止标定样本数
        startupGravityAccumulator += mappedSensorAcc; // 更新启动静止标定重力累积

        Vector3 avgGravity = startupGravityAccumulator / Mathf.Max(1, startupSampleCount); // 更新启动静止标定重力累积
        _gravityEstimate = avgGravity; // 更新重力估计
        _gravityEstimatePrimed = true; // 设置重力估计已准备好

        if (useGravityEstimateCalibration) // 如果使用重力估计标定，则更新加速度偏置
        {
            Vector3 residual = mappedSensorAcc - avgGravity; 
            startupBiasAccumulator += residual; // 更新启动静止标定偏置累积
            accelerationBias = startupBiasAccumulator / Mathf.Max(1, startupSampleCount); // 更新加速度偏置
        }

        if (startupCalibrationTimer >= startupCalibrationDuration) // 如果启动静止标定计时器大于等于启动静止标定时长，则设置启动静止标定为false，设置手腕速度为0，设置位置冻结计时器为0
        {
            isStartupCalibrating = false; // 设置启动静止标定为false
            _wristVelocity = Vector3.zero; // 设置手腕速度为0
            _positionFreezeTimer = 0f; // 设置位置冻结计时器为0
        }
    }

    private void UpdateGravityEstimate(Vector3 mappedSensorAcc, bool preferFastAdapt, float dt) // 更新重力估计
    {
        if (!_gravityEstimatePrimed) // 如果重力估计未准备好，则更新重力估计
        {
            _gravityEstimate = mappedSensorAcc; // 更新重力估计
            _gravityEstimatePrimed = true; // 设置重力估计已准备好
            return;
        }

        float filterSpeed = gravityEstimateFilterSpeed; // 重力估计滤波速度
        if (preferFastAdapt)
            filterSpeed = Mathf.Max(filterSpeed, gravityEstimateFilterSpeed * 3f); // 重力估计滤波速度

        float t = Mathf.Clamp01(dt * filterSpeed); // 平滑重力估计
        _gravityEstimate = Vector3.Lerp(_gravityEstimate, mappedSensorAcc, t); // 更新重力估计
    }

    private Vector3 FilterLinearAcceleration(Vector3 linearAcc) // 过滤线性加速度
    {
        if (wristLinearAccelFilterSpeed <= 0f) // 如果线性加速度滤波速度小于等于0，则设置过滤线性加速度为线性加速度，设置线性加速度滤波器已准备好
        {
            _filteredLinearAcceleration = linearAcc; // 设置过滤线性加速度
            _linearAccelerationFilterPrimed = true; // 设置线性加速度滤波器已准备好
            return linearAcc;
        }

        if (!_linearAccelerationFilterPrimed) // 如果线性加速度滤波器未准备好，则设置过滤线性加速度为线性加速度，设置线性加速度滤波器已准备好
        {
            _filteredLinearAcceleration = linearAcc; // 设置过滤线性加速度
            _linearAccelerationFilterPrimed = true; // 设置线性加速度滤波器已准备好
        }
        else
        {
            float t = Mathf.Clamp01(Time.deltaTime * wristLinearAccelFilterSpeed); // 平滑线性加速度
            _filteredLinearAcceleration = Vector3.Lerp(_filteredLinearAcceleration, linearAcc, t); // 更新过滤线性加速度
        }

        return _filteredLinearAcceleration; // 返回过滤线性加速度
    }

    private Vector3 ApplyPlanarPositionConstraint(Vector3 linearAcc) // 应用平面位置约束
    {
        if (!usePlanarPositionOnly) return linearAcc; // 如果使用平面位置约束为false，则返回线性加速度

        // 演示模式：自动压掉权重最小的那个轴，保留更稳定的两轴做近场位移增强。
        if (positionAxisWeight.x <= positionAxisWeight.y && positionAxisWeight.x <= positionAxisWeight.z)
            linearAcc.x = 0f; // 设置X轴为0
        else if (positionAxisWeight.y <= positionAxisWeight.x && positionAxisWeight.y <= positionAxisWeight.z)
            linearAcc.y = 0f; // 设置Y轴为0
        else
            linearAcc.z = 0f; // 设置Z轴为0

        return linearAcc; // 返回线性加速度
    }

    private static Vector3 MapSensorVector(float x, float y, float z, Vector3Int remap, Vector3 sign) // 映射传感器向量
    {
        float[] sensor = { x, y, z }; // 传感器向量
        return new Vector3(
            sensor[remap.x] * sign.x, // 映射传感器向量X
            sensor[remap.y] * sign.y, // 映射传感器向量Y
            sensor[remap.z] * sign.z); // 映射传感器向量Z
    }

    private static Vector3 ApplyComponentDeadzone(Vector3 value, float deadzone) // 应用组件死区
    {
        value.x = Mathf.Abs(value.x) < deadzone ? 0f : value.x; // 设置X轴为0
        value.y = Mathf.Abs(value.y) < deadzone ? 0f : value.y; // 设置Y轴为0
        value.z = Mathf.Abs(value.z) < deadzone ? 0f : value.z; // 设置Z轴为0
        return value; // 返回向量
    }

    private void UpdateKeyboardPositionOffset() // 更新键盘位置偏移
    {
        float x = 0f; // X轴偏移
        float y = 0f; // Y轴偏移
        float z = 0f; // Z轴偏移

        if (IsMoveLeftPressed()) x -= 1f; // 向左移动
        if (IsMoveRightPressed()) x += 1f; // 向右移动
        if (IsMoveUpPressed()) y += 1f; // 向上移动
        if (IsMoveDownPressed()) y -= 1f; // 向下移动
        if (IsMoveForwardPressed()) z += 1f; // 向前移动
        if (IsMoveBackPressed()) z -= 1f; // 向后移动

        Vector3 dir = new Vector3(x, y, z); // 方向向量

        if (dir.sqrMagnitude > 1f) dir.Normalize(); // 归一化方向向量

        _keyboardOffset += dir * (keyboardMoveSpeed * Time.deltaTime); // 更新键盘位置偏移
        if (limitKeyboardOffset) // 如果限制键盘位置偏移，则限制键盘位置偏移范围
            _keyboardOffset = Vector3.ClampMagnitude(_keyboardOffset, keyboardMaxOffset); // 限制键盘位置偏移范围

        if (IsResetPressedDown()) // 如果重置按下，则设置键盘位置偏移为0，设置当前手腕偏移为0，设置手腕速度为0，设置过滤线性加速度为0，设置线性加速度滤波器未准备好，设置手腕静止计时器为0，设置位置冻结计时器为0
        {
            _keyboardOffset = Vector3.zero; // 设置键盘位置偏移为0
            _currentWristOffset = Vector3.zero; // 设置当前手腕偏移为0
            _wristVelocity = Vector3.zero; // 设置手腕速度为0
            _filteredLinearAcceleration = Vector3.zero; // 设置过滤线性加速度为0
            _linearAccelerationFilterPrimed = false; // 设置线性加速度滤波器未准备好
            _wristStillTimer = 0f; // 设置手腕静止计时器为0
            _positionFreezeTimer = 0f; // 设置位置冻结计时器为0
        }
    }

    private static bool IsMoveLeftPressed() // 是否向左移动
    {
        return Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ||
               Input.GetKey(KeyCode.J) || Input.GetKey(KeyCode.Keypad4);
    }

    private static bool IsMoveRightPressed() // 是否向右移动
    {
        return Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ||
               Input.GetKey(KeyCode.L) || Input.GetKey(KeyCode.Keypad6);
    }

    private static bool IsMoveForwardPressed() // 是否向前移动
    {
        return Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ||
               Input.GetKey(KeyCode.I) || Input.GetKey(KeyCode.Keypad8);
    }

    private static bool IsMoveBackPressed() // 是否向后移动
    {
        return Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ||
               Input.GetKey(KeyCode.K) || Input.GetKey(KeyCode.Keypad2);
    }

    private static bool IsMoveUpPressed() // 是否向上移动
    {
        return Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.PageUp) ||
               Input.GetKey(KeyCode.U) || Input.GetKey(KeyCode.Keypad9);
    }

    private static bool IsMoveDownPressed() // 是否向下移动
    {
        return Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.PageDown) ||
               Input.GetKey(KeyCode.O) || Input.GetKey(KeyCode.Keypad3);
    }

    private static bool IsResetPressedDown() // 是否重置按下
    {
        return Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Keypad0);
    }

    private void ApplyFinalWristPosition() // 应用最终手腕位置
    {
        Vector3 combinedOffset = _currentWristOffset + _keyboardOffset; // 合并偏移
        Vector3 targetLocal = _initialWristPosition + combinedOffset; // 目标本地位置

        // Cache the unfiltered world-space IMU target every frame so RotaryKnob etc. can read
        // the "raw" hand position even while lockWristPosition is forcing the visual wrist
        // somewhere else. Using parent.TransformPoint keeps the value coherent regardless of
        // whether this transform has a parent rig.
        var parent = transform.parent;
        _rawWristTargetWorld = parent != null ? parent.TransformPoint(targetLocal) : targetLocal; // 设置原始手腕目标世界位置

        if (lockWristPosition) // 如果手腕位置锁定，则应用手腕位置
        {
            // Hard snap (no smoothing) — visual snap-to-grab is the intent here. The IMU pipeline
            // (_currentWristOffset / _wristVelocity) keeps integrating in ApplyWristPosition,
            // so on release the lerp below seamlessly resumes from the snapped pose toward the
            // current IMU target without resetting state.
            Vector3 lockedLocal = parent != null
                ? parent.InverseTransformPoint(lockedWristPositionTarget) // 锁定手腕位置目标本地位置
                : lockedWristPositionTarget;
            transform.localPosition = lockedLocal; // 设置手腕位置本地位置
            return;
        }

        float posSmooth = enableWristPosition ? wristPositionSmoothSpeed : 12f; // 平滑手腕位置
        transform.localPosition = Vector3.Lerp(transform.localPosition, targetLocal, Time.deltaTime * posSmooth); // 平滑手腕位置
    }

    public void ResetWristPositionOffset() // 重置手腕位置偏移
    {
        _currentWristOffset = Vector3.zero; // 设置当前手腕偏移为0  
        _wristVelocity = Vector3.zero; // 设置手腕速度为0
        _keyboardOffset = Vector3.zero; // 设置键盘位置偏移为0
        _filteredLinearAcceleration = Vector3.zero; // 设置过滤线性加速度为0
        _linearAccelerationFilterPrimed = false; // 设置线性加速度滤波器未准备好
        _wristStillTimer = 0f; // 设置手腕静止计时器为0
        _positionFreezeTimer = 0f; // 设置位置冻结计时器为0
        transform.localPosition = _initialWristPosition; // 设置手腕位置本地位置
    }

    /// <summary>
    /// 把 IMU 位置积分器的"中性原点"重新锚定到手腕当前所在的位置，并清空积分状态。
    /// 用法：在 lockWristPosition 即将变为 false 之前调用——这样解锁后，
    /// IMU 弹簧会把手拉回到"当前位置"（旋钮）而不是场景启动时的初始位置，
    /// 用户体验上就是"松开旋钮，手停在原地不会传送回去"。
    /// 调用时已经把 transform.localPosition 设为期望的新原点（例如锁定状态下就是 lockedWristPositionTarget），
    /// 本函数读取它并把它当作新的 _initialWristPosition。
    /// 同时清空 _currentWristOffset / _wristVelocity / _keyboardOffset / 加速度滤波器，
    /// 让接下来的 IMU 积分从全零状态、围绕新原点重新开始。
    /// 不影响旋转锁、不影响骨骼。
    /// </summary>
    public void RebaseWristAnchorToCurrent() // 重新锚定手腕锚点到当前位置
    {
        _initialWristPosition = transform.localPosition; // 设置初始手腕位置本地位置
        _currentWristOffset = Vector3.zero; // 设置当前手腕偏移为0
        _wristVelocity = Vector3.zero; // 设置手腕速度为0
        _keyboardOffset = Vector3.zero; // 设置键盘位置偏移为0
        _filteredLinearAcceleration = Vector3.zero; // 设置过滤线性加速度为0
        _linearAccelerationFilterPrimed = false; // 设置线性加速度滤波器未准备好
        _wristStillTimer = 0f; // 设置手腕静止计时器为0
        _positionFreezeTimer = 0f; // 设置位置冻结计时器为0
    }

    public void AutoFindBones() // 自动查找骨骼
    {
        if (_fingers == null)
            _fingers = new[] { thumb, index, middle, ring, pinky };

        _bonesReady = true; // 设置骨骼已准备好  true表示骨骼已准备好
        foreach (var finger in _fingers)
        {
            if (finger.joints != null && finger.joints.Length > 0 && finger.joints[0] != null)
                continue; // 如果骨骼不为空，则继续

            finger.joints = new Transform[3]; // 设置骨骼为3 个关节
            for (int j = 0; j < 3; j++)
            {
                string targetName = $"{finger.boneName}.{j + 1:D2}{boneSuffix}"; // 设置目标骨骼名称
                Transform bone = FindChildRecursive(transform, targetName); // 查找骨骼
                finger.joints[j] = bone; // 设置骨骼

                if (bone == null)
                {
                    Debug.LogWarning($"[DataGloveHandDriver] 未找到骨骼: {targetName}");
                    _bonesReady = false; // 设置骨骼已准备好为false
                }
            }
        }

        if (_bonesReady) // 如果骨骼已准备好，则打印所有手指骨骼已自动绑定完成
            Debug.Log("[DataGloveHandDriver] 所有手指骨骼已自动绑定完成");
    }

    private static Transform FindChildRecursive(Transform parent, string name) // 查找子骨骼
    {
        foreach (Transform child in parent) // 遍历子骨骼
        {
            if (child.name == name) // 如果子骨骼名称等于目标名称，则返回子骨骼
                return child; // 返回子骨骼

            Transform found = FindChildRecursive(child, name); // 查找子骨骼
            if (found != null) // 如果子骨骼不为空，则返回子骨骼
                return found; // 返回子骨骼
        }
        return null; // 返回空
    }

    private void CacheInitialRotations() // 缓存初始旋转
    {
        _initialRotations = new Quaternion[5][]; // 设置初始旋转为5个关节
        for (int i = 0; i < 5; i++)
        {
            Transform[] joints = _fingers[i].joints; // 获取关节
            if (joints == null) // 如果关节为空，则设置初始旋转为空
            {
                _initialRotations[i] = Array.Empty<Quaternion>(); // 设置初始旋转为空
                continue;
            }

            _initialRotations[i] = new Quaternion[joints.Length]; // 设置初始旋转为关节数量 Quaternion是四元数，表示旋转
            for (int j = 0; j < joints.Length; j++)
            {
                _initialRotations[i][j] = joints[j] != null // 如果关节不为空，则设置初始旋转为关节局部旋转
                    ? joints[j].localRotation // 设置初始旋转为关节局部旋转 
                    : Quaternion.identity; // 设置初始旋转为单位旋转
            }
        }
    }

    private void ApplyFingerBend(int fingerIndex, float bendValue) // 应用手指弯曲
    {
        FingerConfig cfg = _fingers[fingerIndex]; // 获取手指配置
        if (cfg.joints == null || cfg.joints.Length == 0) return; // 如果关节为空，则返回

        float totalAngle = bendValue * cfg.maxBendAngle; // 计算总角度
        float[] weights = GetJointWeights(cfg.joints.Length); // 获取关节权重

        for (int j = 0; j < cfg.joints.Length; j++) // 遍历关节
        {
            if (cfg.joints[j] == null) continue; // 如果关节为空，则继续

            float jointAngle = totalAngle * weights[j]; // 计算关节角度
            Quaternion bendRotation = Quaternion.AngleAxis(jointAngle, cfg.bendAxis); // 计算弯曲旋转
            cfg.joints[j].localRotation = _initialRotations[fingerIndex][j] * bendRotation; // 设置关节局部旋转
        }
    }

    private static float[] GetJointWeights(int count) // 获取关节权重
    {
        if (count == 1) return new[] { 1f }; // 如果关节数量为1，则返回1
        if (count == 2) return new[] { 0.55f, 0.45f }; // 如果关节数量为2，则返回0.55和0.45
        if (count == 3) return new[] { 0.40f, 0.35f, 0.25f }; // 如果关节数量为3，则返回0.40, 0.35和0.25

        float[] w = new float[count];
        float sum = 0f; // 总权重
        for (int i = 0; i < count; i++)
        {
            w[i] = count - i; // 计算权重   
            sum += w[i];
        }

        for (int i = 0; i < count; i++) // 遍历关节
            w[i] /= sum; // 计算权重

        return w; // 返回权重
    }

    [Serializable]
    public class FingerConfig // 手指配置
    {
        [Tooltip("骨骼基础名称（不含编号和后缀），如 thumb, finger_index")]
        public string boneName; // 骨骼基础名称（不含编号和后缀），如 thumb, finger_index

        [Tooltip("该手指的关节 Transform（自动查找或手动拖入，近端→远端）")]
        public Transform[] joints; // 该手指的关节 Transform（自动查找或手动拖入，近端→远端）

        [Tooltip("弯曲旋转轴（关节局部坐标系）")]
        public Vector3 bendAxis = Vector3.right; // 弯曲旋转轴（关节局部坐标系）

        [Tooltip("该手指从完全伸直到完全弯曲的总角度")]
        public float maxBendAngle = 90f; // 该手指从完全伸直到完全弯曲的总角度
    }
}
