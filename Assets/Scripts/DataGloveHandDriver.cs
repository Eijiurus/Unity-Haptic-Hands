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
    [SerializeField] private FingerConfig thumb  = new FingerConfig { boneName = "thumb",        bendAxis = new Vector3(0, 0, -1), maxBendAngle = 80f };
    [SerializeField] private FingerConfig index  = new FingerConfig { boneName = "finger_index",  bendAxis = new Vector3(1, 0, 0),  maxBendAngle = 90f };
    [SerializeField] private FingerConfig middle = new FingerConfig { boneName = "finger_middle", bendAxis = new Vector3(1, 0, 0),  maxBendAngle = 90f };
    [SerializeField] private FingerConfig ring   = new FingerConfig { boneName = "finger_ring",   bendAxis = new Vector3(1, 0, 0),  maxBendAngle = 90f };
    [SerializeField] private FingerConfig pinky  = new FingerConfig { boneName = "finger_pinky",  bendAxis = new Vector3(1, 0, 0),  maxBendAngle = 90f };

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

    [Tooltip("对传感器欧拉角再做一层低通（0=关闭）。略减抖动，略增延迟")]
    [SerializeField, Range(0f, 25f)] private float wristEulerFilterSpeed = 18f;

    [Tooltip("旋转死区（度）：小于该角度变化会被忽略，减少平移时的误转动")]
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

    [Tooltip("加速度积分增益：越大位移变化越明显")]
    [SerializeField, Range(0.05f, 3f)] private float wristPositionGain = 2.2f;

    [Tooltip("加速度死区（g）：抑制静止时微抖动")]
    [SerializeField, Range(0f, 0.2f)] private float wristPositionDeadzone = 0.004f;

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
    private float _filtUx, _filtUy, _filtUz;
    private bool _wristEulerFilterPrimed;
    private Vector3 _initialWristPosition;
    private Vector3 _currentWristOffset;
    private Vector3 _wristVelocity;
    private Vector3 _keyboardOffset;
    private Vector3 _editorInjectedInput;
    private bool _editorResetRequested;

    private FingerConfig[] _fingers;
    private float[] _currentValues = new float[5];
    private Quaternion[][] _initialRotations;
    private bool _bonesReady;

    void Start()
    {
        _fingers = new[] { thumb, index, middle, ring, pinky };
        AutoFindBones();
        CacheInitialRotations();
        _initialWristRotation = transform.localRotation;
        _currentWristRotation = Quaternion.identity;
        _filtUx = _filtUy = _filtUz = 0f;
        _wristEulerFilterPrimed = false;
        _initialWristPosition = transform.localPosition;
        _currentWristOffset = Vector3.zero;
        _wristVelocity = Vector3.zero;
        _keyboardOffset = Vector3.zero;
    }

    void Update()
    {
        if (_bonesReady && gloveData != null)
        {
            for (int i = 0; i < 5; i++)
            {
                float target = gloveData.FingerValues[i];
                _currentValues[i] = smoothSpeed > 0
                    ? Mathf.Lerp(_currentValues[i], target, Time.deltaTime * smoothSpeed)
                    : target;
                ApplyFingerBend(i, _currentValues[i]);
            }
        }

        if (enableWristRotation)
        {
            ApplyWristRotation();
        }
        if (enableWristPosition)
        {
            ApplyWristPosition();
        }
        if (enableKeyboardPositionControl)
        {
            UpdateKeyboardPositionOffset();
        }

        ApplyFinalWristPosition();
    }

    /// <summary>
    /// 从 WitMotion SDK 获取欧拉角并应用到手腕根节点。
    /// SDK 的 DeviceModel 通过 GetDeviceData("AngX"/"AngY"/"AngZ") 提供欧拉角。
    /// </summary>
    private void ApplyWristRotation()
    {
        DeviceModel wtDevice = DevicesManager.Instance.GetCurrentDevice();
        if (wtDevice == null || !wtDevice.isOpen) return;

        float angX = (float)wtDevice.GetDeviceData("AngX");
        float angY = (float)wtDevice.GetDeviceData("AngY");
        float angZ = (float)wtDevice.GetDeviceData("AngZ");

        float[] sensorAngles = { angX, angY, angZ };
        float ux = sensorAngles[axisRemap.x] * axisSign.x * wristAngleScale;
        float uy = sensorAngles[axisRemap.y] * axisSign.y * wristAngleScale;
        float uz = sensorAngles[axisRemap.z] * axisSign.z * wristAngleScale;

        // Position-first mode: ignore tiny wrist angle changes to reduce unintended rotation.
        ux = Mathf.Abs(ux) < wristRotationDeadzone ? 0f : ux;
        uy = Mathf.Abs(uy) < wristRotationDeadzone ? 0f : uy;
        uz = Mathf.Abs(uz) < wristRotationDeadzone ? 0f : uz;

        if (wristEulerFilterSpeed > 0f)
        {
            if (!_wristEulerFilterPrimed)
            {
                _filtUx = ux;
                _filtUy = uy;
                _filtUz = uz;
                _wristEulerFilterPrimed = true;
            }
            else
            {
                float t = Mathf.Clamp01(Time.deltaTime * wristEulerFilterSpeed);
                _filtUx = Mathf.Lerp(_filtUx, ux, t);
                _filtUy = Mathf.Lerp(_filtUy, uy, t);
                _filtUz = Mathf.Lerp(_filtUz, uz, t);
            }
            ux = _filtUx;
            uy = _filtUy;
            uz = _filtUz;
        }

        Quaternion targetRot = Quaternion.Euler(ux, uy, uz);
        _currentWristRotation = Quaternion.Slerp(_currentWristRotation, targetRot,
            Time.deltaTime * wristSmoothSpeed);

        transform.localRotation = _initialWristRotation * _currentWristRotation;
    }

    /// <summary>
    /// 使用 WitMotion 的加速度数据估算手腕相对位移。
    /// 注意：IMU 无绝对位置，长时间会漂移；建议在实验流程中定期归零。
    /// </summary>
    private void ApplyWristPosition()
    {
        DeviceModel wtDevice = DevicesManager.Instance.GetCurrentDevice();
        if (wtDevice == null || !wtDevice.isOpen) return;

        float accX = (float)wtDevice.GetDeviceData("AccX");
        float accY = (float)wtDevice.GetDeviceData("AccY");
        float accZ = (float)wtDevice.GetDeviceData("AccZ");

        float[] sensorAcc = { accX, accY, accZ };
        float ux = sensorAcc[positionAxisRemap.x] * positionAxisSign.x;
        float uy = sensorAcc[positionAxisRemap.y] * positionAxisSign.y;
        float uz = sensorAcc[positionAxisRemap.z] * positionAxisSign.z;

        Vector3 acc = new Vector3(ux, uy, uz);
        acc.x = Mathf.Abs(acc.x) < wristPositionDeadzone ? 0f : acc.x;
        acc.y = Mathf.Abs(acc.y) < wristPositionDeadzone ? 0f : acc.y;
        acc.z = Mathf.Abs(acc.z) < wristPositionDeadzone ? 0f : acc.z;

        float dt = Time.deltaTime;
        _wristVelocity += acc * (wristPositionGain * dt);
        _wristVelocity = Vector3.Lerp(_wristVelocity, Vector3.zero, dt * wristVelocityDamping);
        _currentWristOffset += _wristVelocity * dt;
        _currentWristOffset = Vector3.ClampMagnitude(_currentWristOffset, wristMaxOffset);

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
        float sum = 0;
        for (int i = 0; i < count; i++)
        {
            w[i] = count - i;
            sum += w[i];
        }
        for (int i = 0; i < count; i++) w[i] /= sum;
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
