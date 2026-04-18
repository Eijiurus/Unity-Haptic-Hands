using System;
using UnityEngine;
using Assets.Device;

/// <summary>
/// 根据 GloveDataReceiver 提供的五指弯曲值旋转手指骨骼，
/// 根据 WitMotion WT9011DCL-BT50 传感器提供的姿态数据旋转手腕。
/// </summary>
public class DataGloveHandDriver : MonoBehaviour
{
    [SerializeField] private GloveDataReceiver gloveData;

    [Header("手指骨骼配置")]
    [SerializeField] private FingerConfig thumb = new FingerConfig { boneName = "thumb", bendAxis = new Vector3(0, 0, -1), maxBendAngle = 80f };
    [SerializeField] private FingerConfig index = new FingerConfig { boneName = "finger_index", bendAxis = new Vector3(1, 0, 0), maxBendAngle = 90f };
    [SerializeField] private FingerConfig middle = new FingerConfig { boneName = "finger_middle", bendAxis = new Vector3(1, 0, 0), maxBendAngle = 90f };
    [SerializeField] private FingerConfig ring = new FingerConfig { boneName = "finger_ring", bendAxis = new Vector3(1, 0, 0), maxBendAngle = 90f };
    [SerializeField] private FingerConfig pinky = new FingerConfig { boneName = "finger_pinky", bendAxis = new Vector3(1, 0, 0), maxBendAngle = 90f };

    [Header("骨骼查找")]
    [Tooltip("骨骼后缀，左手为 .L，右手为 .R")]
    [SerializeField] private string boneSuffix = ".L";

    [Header("手指平滑")]
    [Tooltip("数值越大越平滑，但延迟也越大。设为 0 关闭平滑。")]
    [SerializeField, Range(0f, 30f)] private float smoothSpeed = 12f;

    [Header("手腕姿态 (WitMotion 传感器)")]
    [Tooltip("是否启用蓝牙传感器控制手腕旋转")]
    public bool enableWristRotation = true;

    [Tooltip("手腕跟随速度：数值越小越稳、越不飘，但延迟略大")]
    [SerializeField, Range(1f, 30f)] private float wristSmoothSpeed = 3f;

    [Tooltip("角度灵敏度：1=与传感器角度 1:1，调小则同样手腕动作虚拟手转得少、更不飘")]
    [SerializeField, Range(0.05f, 1f)] private float wristAngleScale = 0.1f;

    [Tooltip("Yaw 灵敏度：用于弱化 Z 轴旋转漂移；1=不弱化")]
    [SerializeField, Range(0f, 1f)] private float yawScale = 0.45f;

    [Tooltip("对传感器欧拉角再做一层低通（0=关闭）。略减抖动，略增延迟")]
    [SerializeField, Range(0f, 25f)] private float wristEulerFilterSpeed = 18f;

    [Tooltip("旋转死区（度）：先对原始角度做死区，再乘灵敏度，减少小角度抖动")]
    [SerializeField, Range(0f, 20f)] private float wristRotationDeadzone = 12f;

    [Tooltip("坐标系映射：传感器轴 → Unity 轴的符号翻转\n" +
             "传感器右手坐标系与 Unity 左手坐标系不同，佩戴方向也会影响映射。\n" +
             "如果旋转方向不对，尝试翻转各分量的正负号。")]
    [SerializeField] private Vector3 axisSign = new Vector3(-1f, -1f, 1f);

    [Tooltip("坐标系映射：传感器 XYZ → Unity XYZ 的轴重排\n" +
             "0=传感器X, 1=传感器Y, 2=传感器Z\n" +
             "默认 (0,2,1) 表示：Unity.X=传感器.X, Unity.Y=传感器.Z, Unity.Z=传感器.Y")]
    [SerializeField] private Vector3Int axisRemap = new Vector3Int(0, 2, 1);

    [Header("手腕位置 (WitMotion 传感器)")]
    [Tooltip("是否启用蓝牙传感器控制手腕位置（基于 AccX/Y/Z 估算相对位移）")]
    public bool enableWristPosition = true;

    [Tooltip("位置跟随平滑速度：越小越稳，越大越跟手")]
    [SerializeField, Range(1f, 30f)] private float wristPositionSmoothSpeed = 22f;

    [Tooltip("线性加速度积分增益：越大位移变化越明显")]
    [SerializeField, Range(0.05f, 3f)] private float wristPositionGain = 2.2f;

    [Tooltip("线性加速度死区（g）：抑制重力补偿后的微小噪声")]
    [SerializeField, Range(0f, 0.2f)] private float wristPositionDeadzone = 0.004f;

    [Tooltip("线性加速度低通速度（0=关闭）。用于抑制重力估计后的高频抖动")]
    [SerializeField, Range(0f, 30f)] private float wristLinearAccelFilterSpeed = 16f;

    [Tooltip("重力估计低通速度：越小越不容易把真实平移误吸收到重力里")]
    [SerializeField, Range(0.1f, 10f)] private float gravityEstimateFilterSpeed = 1.4f;

    [Tooltip("静止时线性残差偏置学习速度，用于慢慢修正加速度零偏")]
    [SerializeField, Range(0f, 10f)] private float accelBiasLearningSpeed = 1.2f;

    [Tooltip("是否启用启动静止标定与后续偏置学习")]
    [SerializeField] private bool useGravityEstimateCalibration = true;

    [Tooltip("启动静止标定时长（秒）：建议放在 1~2 秒之间")]
    [SerializeField, Range(0.2f, 3f)] private float startupCalibrationDuration = 1.2f;

    [Tooltip("角速度门控阈值（°/s）：转腕过快时暂停位置积分，避免假位移")]
    [SerializeField, Range(10f, 1080f)] private float wristAngularVelocityGate = 120f;

    [Tooltip("转动冻结窗口时长（秒）：角速度过大后继续冻结一小段时间")]
    [SerializeField, Range(0.12f, 0.3f)] private float positionFreezeDuration = 0.18f;

    [Tooltip("冻结期间的额外速度阻尼倍数：越大越能压住假位移")]
    [SerializeField, Range(1f, 10f)] private float freezeVelocityDampingMultiplier = 4f;

    [Tooltip("位置弹簧回中强度：防止偏移完全靠自由积分越飘越远")]
    [SerializeField, Range(1f, 20f)] private float wristPositionSpring = 6f;

    [Tooltip("位置轴向权重：逐轴削弱最容易被转动污染的轴")]
    [SerializeField] private Vector3 positionAxisWeight = new Vector3(1f, 0.7f, 0.8f);

    [Tooltip("静止线性加速度阈值（g）：与角速度阈值共同用于 zero-velocity update")]
    [SerializeField, Range(0.001f, 0.2f)] private float wristStillAccelerationThreshold = 0.025f;

    [Tooltip("静止角速度阈值（°/s）：与线性加速度阈值共同用于 zero-velocity update")]
    [SerializeField, Range(0.5f, 60f)] private float wristStillAngularSpeedThreshold = 6f;

    [Tooltip("静止确认时间（秒）：连续满足静止条件后清零速度")]
    [SerializeField, Range(0f, 1f)] private float wristStillConfirmTime = 0.12f;

    [Tooltip("速度阻尼：越大越快停下来，越不漂")]
    [SerializeField, Range(0f, 20f)] private float wristVelocityDamping = 2.2f;

    [Tooltip("最大相对位移（米）")]
    [SerializeField, Range(0.02f, 0.8f)] private float wristMaxOffset = 0.55f;

    [Tooltip("位置坐标系映射：传感器轴 → Unity 轴的符号翻转")]
    [SerializeField] private Vector3 positionAxisSign = new Vector3(-1f, -1f, 1f);

    [Tooltip("位置坐标系映射：传感器 XYZ → Unity XYZ 的轴重排（0=X,1=Y,2=Z）")]
    [SerializeField] private Vector3Int positionAxisRemap = new Vector3Int(0, 2, 1);

    [Header("键盘位置控制 (调试/演示)")]
    [Tooltip("启用键盘移动手的位置（与 WitMotion 位置偏移叠加）")]
    [SerializeField] private bool enableKeyboardPositionControl = true;

    [Tooltip("键盘移动速度（米/秒）")]
    [SerializeField, Range(0.1f, 3f)] private float keyboardMoveSpeed = 0.9f;

    [Tooltip("键盘偏移最大范围（米）")]
    [SerializeField, Range(0.05f, 3f)] private float keyboardMaxOffset = 1.5f;

    [Tooltip("是否限制键盘位移范围（关闭后可持续移动）")]
    [SerializeField] private bool limitKeyboardOffset = false;

    private Quaternion _initialWristRotation;
    private Quaternion _currentWristRotation;
    private Quaternion _displayTargetWristRotation = Quaternion.identity;
    private Vector3 _filteredDisplayEuler;
    private bool _wristEulerFilterPrimed;
    private int _lastWristSensorFrame = -1;

    private Vector3 _initialWristPosition;
    private Vector3 _currentWristOffset;
    private Vector3 _wristVelocity;
    private Vector3 _gravityEstimate;
    private bool _gravityEstimatePrimed;
    private Vector3 _filteredLinearAcceleration;
    private bool _linearAccelerationFilterPrimed;
    private float _wristStillTimer;
    private float _positionFreezeTimer;

    private Vector3 accelerationBias;
    private bool isStartupCalibrating;
    private float startupCalibrationTimer;
    private Vector3 startupGravityAccumulator;
    private Vector3 startupBiasAccumulator;
    private int startupSampleCount;

    private Vector3 _keyboardOffset;
    private Vector3 _editorInjectedInput;
    private bool _editorResetRequested;

    private FingerConfig[] _fingers;
    private readonly float[] _currentValues = new float[5];
    private Quaternion[][] _initialRotations;
    private bool _bonesReady;

    void Start()
    {
        _fingers = new[] { thumb, index, middle, ring, pinky };
        AutoFindBones();
        CacheInitialRotations();

        _initialWristRotation = transform.localRotation;
        _currentWristRotation = Quaternion.identity;
        _displayTargetWristRotation = Quaternion.identity;
        _filteredDisplayEuler = Vector3.zero;
        _wristEulerFilterPrimed = false;
        _lastWristSensorFrame = -1;

        _initialWristPosition = transform.localPosition;
        _currentWristOffset = Vector3.zero;
        _wristVelocity = Vector3.zero;
        _gravityEstimate = Vector3.zero;
        _gravityEstimatePrimed = false;
        _filteredLinearAcceleration = Vector3.zero;
        _linearAccelerationFilterPrimed = false;
        _wristStillTimer = 0f;
        _positionFreezeTimer = 0f;
        accelerationBias = Vector3.zero;
        isStartupCalibrating = true;
        startupCalibrationTimer = 0f;
        startupGravityAccumulator = Vector3.zero;
        startupBiasAccumulator = Vector3.zero;
        startupSampleCount = 0;
        _keyboardOffset = Vector3.zero;
    }

    void Update()
    {
        if (_bonesReady && gloveData != null)
        {
            for (int i = 0; i < 5; i++)
            {
                float target = gloveData.FingerValues[i];
                _currentValues[i] = smoothSpeed > 0f
                    ? Mathf.Lerp(_currentValues[i], target, Time.deltaTime * smoothSpeed)
                    : target;
                ApplyFingerBend(i, _currentValues[i]);
            }
        }

        DeviceModel wtDevice = null;
        if (enableWristRotation || enableWristPosition)
        {
            wtDevice = GetCurrentWristDevice();
            if (wtDevice != null)
                UpdateWristSensorState(wtDevice);
        }

        if (enableWristRotation)
            ApplyWristRotation(wtDevice);

        if (enableWristPosition)
            ApplyWristPosition(wtDevice);

        if (enableKeyboardPositionControl)
            UpdateKeyboardPositionOffset();

        ApplyFinalWristPosition();
    }

    /// <summary>
    /// 从 WitMotion SDK 获取欧拉角并应用到手腕根节点。
    /// SDK 的 DeviceModel 通过 GetDeviceData("AngX"/"AngY"/"AngZ") 提供欧拉角。
    /// </summary>
    private void ApplyWristRotation(DeviceModel wtDevice)
    {
        if (wtDevice == null) return;

        _currentWristRotation = Quaternion.Slerp(
            _currentWristRotation,
            _displayTargetWristRotation,
            Time.deltaTime * wristSmoothSpeed);

        transform.localRotation = _initialWristRotation * _currentWristRotation;
    }

    /// <summary>
    /// 使用 WitMotion 的加速度数据估算手腕相对位移。
    /// 这里不再依赖欧拉角旋转后的重力补偿，而是使用慢速低通估计重力，
    /// 再叠加启动标定、转动冻结窗口和弹簧回中来抑制假位移。
    /// </summary>
    private void ApplyWristPosition(DeviceModel wtDevice)
    {
        if (wtDevice == null) return;

        float accX = (float)wtDevice.GetDeviceData("AccX");
        float accY = (float)wtDevice.GetDeviceData("AccY");
        float accZ = (float)wtDevice.GetDeviceData("AccZ");

        float asX = (float)wtDevice.GetDeviceData("AsX");
        float asY = (float)wtDevice.GetDeviceData("AsY");
        float asZ = (float)wtDevice.GetDeviceData("AsZ");

        Vector3 mappedSensorAcc = MapSensorVector(accX, accY, accZ, positionAxisRemap, positionAxisSign);
        Vector3 mappedAngularVelocity = MapSensorVector(asX, asY, asZ, axisRemap, axisSign);
        float angularSpeed = mappedAngularVelocity.magnitude;
        float dt = Time.deltaTime;

        UpdateStartupCalibration(mappedSensorAcc, angularSpeed, dt);

        if (angularSpeed >= wristAngularVelocityGate)
            _positionFreezeTimer = positionFreezeDuration;

        if (_positionFreezeTimer > 0f)
            _positionFreezeTimer = Mathf.Max(0f, _positionFreezeTimer - dt);

        bool isPositionFrozen = isStartupCalibrating || _positionFreezeTimer > 0f;

        // 关键改动：位置链路改成“慢速低通估计重力”。
        // 相比直接依赖 Quaternion.Euler(...) 扣重力，这种方式对欧拉角误差和姿态漂移更不敏感，
        // 更适合当前“只求稳定相对偏移，不追求纯 IMU 自由积分”的目标。
        UpdateGravityEstimate(mappedSensorAcc, isPositionFrozen, dt);

        Vector3 rawLinearAcc = mappedSensorAcc - _gravityEstimate;

        if (!isStartupCalibrating && useGravityEstimateCalibration && angularSpeed <= wristStillAngularSpeedThreshold)
        {
            float biasLerp = Mathf.Clamp01(dt * accelBiasLearningSpeed);
            accelerationBias = Vector3.Lerp(accelerationBias, rawLinearAcc, biasLerp);
        }

        Vector3 linearAcc = rawLinearAcc - accelerationBias;
        linearAcc = FilterLinearAcceleration(linearAcc);
        linearAcc = Vector3.Scale(linearAcc, positionAxisWeight);
        linearAcc = ApplyComponentDeadzone(linearAcc, wristPositionDeadzone);

        bool isStillCandidate =
            linearAcc.magnitude <= wristStillAccelerationThreshold &&
            angularSpeed <= wristStillAngularSpeedThreshold;

        _wristStillTimer = isStillCandidate ? _wristStillTimer + dt : 0f;
        bool isStill = _wristStillTimer >= wristStillConfirmTime;

        if (isStill)
        {
            // 静止时执行 zero-velocity update，并轻微拉回偏移中心，
            // 让手在无输入时更稳定地停住，而不是保留残余漂移。
            _wristVelocity = Vector3.zero;
            _currentWristOffset = Vector3.Lerp(
                _currentWristOffset,
                Vector3.zero,
                Mathf.Clamp01(dt * Mathf.Max(1f, wristPositionSpring * 0.35f)));

            // 静止时允许更快重建重力基线，帮助恢复转动后的稳定状态。
            UpdateGravityEstimate(mappedSensorAcc, true, dt);
            return;
        }

        if (isPositionFrozen)
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
        _wristVelocity -= _currentWristOffset * (wristPositionSpring * dt);
        _wristVelocity = Vector3.Lerp(_wristVelocity, Vector3.zero, dt * wristVelocityDamping);
        _currentWristOffset += _wristVelocity * dt;
        _currentWristOffset = Vector3.ClampMagnitude(_currentWristOffset, wristMaxOffset);
    }

    private DeviceModel GetCurrentWristDevice()
    {
        if (DevicesManager.Instance == null) return null;

        DeviceModel wtDevice = DevicesManager.Instance.GetCurrentDevice();
        if (wtDevice == null || !wtDevice.isOpen) return null;
        return wtDevice;
    }

    private void UpdateWristSensorState(DeviceModel wtDevice)
    {
        if (wtDevice == null || _lastWristSensorFrame == Time.frameCount) return;

        float angX = (float)wtDevice.GetDeviceData("AngX");
        float angY = (float)wtDevice.GetDeviceData("AngY");
        float angZ = (float)wtDevice.GetDeviceData("AngZ");

        Vector3 mappedRawEuler = MapSensorVector(angX, angY, angZ, axisRemap, axisSign);

        // 关键改动：先对原始角度做死区，再乘显示灵敏度。
        Vector3 displayRawEuler = ApplyComponentDeadzone(mappedRawEuler, wristRotationDeadzone);
        Vector3 displayEuler = new Vector3(
            displayRawEuler.x * wristAngleScale,
            displayRawEuler.y * wristAngleScale,
            displayRawEuler.z * wristAngleScale * yawScale);

        if (wristEulerFilterSpeed > 0f)
        {
            if (!_wristEulerFilterPrimed)
            {
                _filteredDisplayEuler = displayEuler;
                _wristEulerFilterPrimed = true;
            }
            else
            {
                float t = Mathf.Clamp01(Time.deltaTime * wristEulerFilterSpeed);
                _filteredDisplayEuler = Vector3.Lerp(_filteredDisplayEuler, displayEuler, t);
            }
        }
        else
        {
            _filteredDisplayEuler = displayEuler;
            _wristEulerFilterPrimed = true;
        }

        _displayTargetWristRotation = Quaternion.Euler(_filteredDisplayEuler);
        _lastWristSensorFrame = Time.frameCount;
    }

    private void UpdateStartupCalibration(Vector3 mappedSensorAcc, float angularSpeed, float dt)
    {
        if (!isStartupCalibrating) return;

        if (angularSpeed > wristStillAngularSpeedThreshold)
            return;

        startupCalibrationTimer += dt;
        startupSampleCount++;
        startupGravityAccumulator += mappedSensorAcc;

        Vector3 avgGravity = startupGravityAccumulator / Mathf.Max(1, startupSampleCount);
        _gravityEstimate = avgGravity;
        _gravityEstimatePrimed = true;

        if (useGravityEstimateCalibration)
        {
            Vector3 residual = mappedSensorAcc - avgGravity;
            startupBiasAccumulator += residual;
            accelerationBias = startupBiasAccumulator / Mathf.Max(1, startupSampleCount);
        }

        if (startupCalibrationTimer >= startupCalibrationDuration)
        {
            isStartupCalibrating = false;
            _wristVelocity = Vector3.zero;
            _positionFreezeTimer = 0f;
        }
    }

    private void UpdateGravityEstimate(Vector3 mappedSensorAcc, bool preferFastAdapt, float dt)
    {
        if (!_gravityEstimatePrimed)
        {
            _gravityEstimate = mappedSensorAcc;
            _gravityEstimatePrimed = true;
            return;
        }

        float filterSpeed = gravityEstimateFilterSpeed;
        if (preferFastAdapt)
            filterSpeed = Mathf.Max(filterSpeed, gravityEstimateFilterSpeed * 3f);

        float t = Mathf.Clamp01(dt * filterSpeed);
        _gravityEstimate = Vector3.Lerp(_gravityEstimate, mappedSensorAcc, t);
    }

    private Vector3 FilterLinearAcceleration(Vector3 linearAcc)
    {
        if (wristLinearAccelFilterSpeed <= 0f)
        {
            _filteredLinearAcceleration = linearAcc;
            _linearAccelerationFilterPrimed = true;
            return linearAcc;
        }

        if (!_linearAccelerationFilterPrimed)
        {
            _filteredLinearAcceleration = linearAcc;
            _linearAccelerationFilterPrimed = true;
        }
        else
        {
            float t = Mathf.Clamp01(Time.deltaTime * wristLinearAccelFilterSpeed);
            _filteredLinearAcceleration = Vector3.Lerp(_filteredLinearAcceleration, linearAcc, t);
        }

        return _filteredLinearAcceleration;
    }

    private static Vector3 MapSensorVector(float x, float y, float z, Vector3Int remap, Vector3 sign)
    {
        float[] sensor = { x, y, z };
        return new Vector3(
            sensor[remap.x] * sign.x,
            sensor[remap.y] * sign.y,
            sensor[remap.z] * sign.z);
    }

    private static Vector3 ApplyComponentDeadzone(Vector3 value, float deadzone)
    {
        value.x = Mathf.Abs(value.x) < deadzone ? 0f : value.x;
        value.y = Mathf.Abs(value.y) < deadzone ? 0f : value.y;
        value.z = Mathf.Abs(value.z) < deadzone ? 0f : value.z;
        return value;
    }

    private void UpdateKeyboardPositionOffset()
    {
        float x = 0f;
        float y = 0f;
        float z = 0f;

        if (IsMoveLeftPressed()) x -= 1f;
        if (IsMoveRightPressed()) x += 1f;
        if (IsMoveUpPressed()) y += 1f;
        if (IsMoveDownPressed()) y -= 1f;
        if (IsMoveForwardPressed()) z += 1f;
        if (IsMoveBackPressed()) z -= 1f;

        Vector3 dir = new Vector3(x, y, z);

#if UNITY_EDITOR
        dir += _editorInjectedInput;
#endif

        if (dir.sqrMagnitude > 1f) dir.Normalize();

        _keyboardOffset += dir * (keyboardMoveSpeed * Time.deltaTime);
        if (limitKeyboardOffset)
            _keyboardOffset = Vector3.ClampMagnitude(_keyboardOffset, keyboardMaxOffset);

        if (IsResetPressedDown()
#if UNITY_EDITOR
            || _editorResetRequested
#endif
           )
        {
            _keyboardOffset = Vector3.zero;
            _currentWristOffset = Vector3.zero;
            _wristVelocity = Vector3.zero;
            _filteredLinearAcceleration = Vector3.zero;
            _linearAccelerationFilterPrimed = false;
            _wristStillTimer = 0f;
            _positionFreezeTimer = 0f;
        }

#if UNITY_EDITOR
        _editorInjectedInput = Vector3.zero;
        _editorResetRequested = false;
#endif
    }

    private static bool IsMoveLeftPressed()
    {
        return Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ||
               Input.GetKey(KeyCode.J) || Input.GetKey(KeyCode.Keypad4);
    }

    private static bool IsMoveRightPressed()
    {
        return Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ||
               Input.GetKey(KeyCode.L) || Input.GetKey(KeyCode.Keypad6);
    }

    private static bool IsMoveForwardPressed()
    {
        return Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ||
               Input.GetKey(KeyCode.I) || Input.GetKey(KeyCode.Keypad8);
    }

    private static bool IsMoveBackPressed()
    {
        return Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ||
               Input.GetKey(KeyCode.K) || Input.GetKey(KeyCode.Keypad2);
    }

    private static bool IsMoveUpPressed()
    {
        return Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.PageUp) ||
               Input.GetKey(KeyCode.U) || Input.GetKey(KeyCode.Keypad9);
    }

    private static bool IsMoveDownPressed()
    {
        return Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.PageDown) ||
               Input.GetKey(KeyCode.O) || Input.GetKey(KeyCode.Keypad3);
    }

    private static bool IsResetPressedDown()
    {
        return Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Keypad0);
    }

    private void ApplyFinalWristPosition()
    {
        Vector3 combinedOffset = _currentWristOffset + _keyboardOffset;
        Vector3 targetPos = _initialWristPosition + combinedOffset;
        float posSmooth = enableWristPosition ? wristPositionSmoothSpeed : 12f;
        transform.localPosition = Vector3.Lerp(transform.localPosition, targetPos, Time.deltaTime * posSmooth);
    }

    public void ResetWristPositionOffset()
    {
        _currentWristOffset = Vector3.zero;
        _wristVelocity = Vector3.zero;
        _keyboardOffset = Vector3.zero;
        _filteredLinearAcceleration = Vector3.zero;
        _linearAccelerationFilterPrimed = false;
        _wristStillTimer = 0f;
        _positionFreezeTimer = 0f;
        transform.localPosition = _initialWristPosition;
    }

#if UNITY_EDITOR
    public void SetEditorKeyboardInput(Vector3 moveDir, bool reset)
    {
        _editorInjectedInput = moveDir;
        if (reset) _editorResetRequested = true;
    }
#endif

    public void AutoFindBones()
    {
        if (_fingers == null)
            _fingers = new[] { thumb, index, middle, ring, pinky };

        _bonesReady = true;
        foreach (var finger in _fingers)
        {
            if (finger.joints != null && finger.joints.Length > 0 && finger.joints[0] != null)
                continue;

            finger.joints = new Transform[3];
            for (int j = 0; j < 3; j++)
            {
                string targetName = $"{finger.boneName}.{j + 1:D2}{boneSuffix}";
                Transform bone = FindChildRecursive(transform, targetName);
                finger.joints[j] = bone;

                if (bone == null)
                {
                    Debug.LogWarning($"[DataGloveHandDriver] 未找到骨骼: {targetName}");
                    _bonesReady = false;
                }
            }
        }

        if (_bonesReady)
            Debug.Log("[DataGloveHandDriver] 所有手指骨骼已自动绑定完成");
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
                return child;

            Transform found = FindChildRecursive(child, name);
            if (found != null)
                return found;
        }

        return null;
    }

    private void CacheInitialRotations()
    {
        _initialRotations = new Quaternion[5][];
        for (int i = 0; i < 5; i++)
        {
            Transform[] joints = _fingers[i].joints;
            if (joints == null)
            {
                _initialRotations[i] = Array.Empty<Quaternion>();
                continue;
            }

            _initialRotations[i] = new Quaternion[joints.Length];
            for (int j = 0; j < joints.Length; j++)
            {
                _initialRotations[i][j] = joints[j] != null
                    ? joints[j].localRotation
                    : Quaternion.identity;
            }
        }
    }

    private void ApplyFingerBend(int fingerIndex, float bendValue)
    {
        FingerConfig cfg = _fingers[fingerIndex];
        if (cfg.joints == null || cfg.joints.Length == 0) return;

        float totalAngle = bendValue * cfg.maxBendAngle;
        float[] weights = GetJointWeights(cfg.joints.Length);

        for (int j = 0; j < cfg.joints.Length; j++)
        {
            if (cfg.joints[j] == null) continue;

            float jointAngle = totalAngle * weights[j];
            Quaternion bendRotation = Quaternion.AngleAxis(jointAngle, cfg.bendAxis);
            cfg.joints[j].localRotation = _initialRotations[fingerIndex][j] * bendRotation;
        }
    }

    private static float[] GetJointWeights(int count)
    {
        if (count == 1) return new[] { 1f };
        if (count == 2) return new[] { 0.55f, 0.45f };
        if (count == 3) return new[] { 0.40f, 0.35f, 0.25f };

        float[] w = new float[count];
        float sum = 0f;
        for (int i = 0; i < count; i++)
        {
            w[i] = count - i;
            sum += w[i];
        }

        for (int i = 0; i < count; i++)
            w[i] /= sum;

        return w;
    }

    [Serializable]
    public class FingerConfig
    {
        [Tooltip("骨骼基础名称（不含编号和后缀），如 thumb, finger_index")]
        public string boneName;

        [Tooltip("该手指的关节 Transform（自动查找或手动拖入，近端→远端）")]
        public Transform[] joints;

        [Tooltip("弯曲旋转轴（关节局部坐标系）")]
        public Vector3 bendAxis = Vector3.right;

        [Tooltip("该手指从完全伸直到完全弯曲的总角度")]
        public float maxBendAngle = 90f;
    }
}
