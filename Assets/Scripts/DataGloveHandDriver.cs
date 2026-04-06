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

    [Tooltip("手腕旋转平滑速度")]
    [SerializeField, Range(1f, 30f)] private float wristSmoothSpeed = 10f;

    [Tooltip("坐标系映射：传感器轴 → Unity 轴的符号翻转\n" +
             "传感器右手坐标系与 Unity 左手坐标系不同，佩戴方向也会影响映射。\n" +
             "如果旋转方向不对，尝试翻转各分量的正负号。")]
    [SerializeField] private Vector3 axisSign = new Vector3(-1f, -1f, 1f);

    [Tooltip("坐标系映射：传感器 XYZ → Unity XYZ 的轴重排\n" +
             "0=传感器X, 1=传感器Y, 2=传感器Z\n" +
             "默认 (0,2,1) 表示：Unity.X=传感器.X, Unity.Y=传感器.Z, Unity.Z=传感器.Y")]
    [SerializeField] private Vector3Int axisRemap = new Vector3Int(0, 2, 1);

    private Quaternion _initialWristRotation;
    private Quaternion _currentWristRotation;

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
        float ux = sensorAngles[axisRemap.x] * axisSign.x;
        float uy = sensorAngles[axisRemap.y] * axisSign.y;
        float uz = sensorAngles[axisRemap.z] * axisSign.z;

        Quaternion targetRot = Quaternion.Euler(ux, uy, uz);
        _currentWristRotation = Quaternion.Slerp(_currentWristRotation, targetRot,
            Time.deltaTime * wristSmoothSpeed);

        transform.localRotation = _initialWristRotation * _currentWristRotation;
    }

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
