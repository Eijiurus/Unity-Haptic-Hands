using System;
using UnityEngine;

/// <summary>
/// 根据 GloveDataReceiver 提供的五指弯曲值，直接旋转 Rigged Hand 骨骼。
/// 骨骼按 Blender 标准命名自动查找（如 thumb.01.L, finger_index.01.L 等）。
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

    [Header("平滑")]
    [Tooltip("数值越大越平滑，但延迟也越大。设为 0 关闭平滑。")]
    [SerializeField, Range(0f, 30f)] private float smoothSpeed = 12f;

    private FingerConfig[] _fingers;
    private float[] _currentValues = new float[5];
    private Quaternion[][] _initialRotations;
    private bool _bonesReady;

    void Start()
    {
        _fingers = new[] { thumb, index, middle, ring, pinky };
        AutoFindBones();
        CacheInitialRotations();
    }

    void Update()
    {
        if (!_bonesReady || gloveData == null) return;

        for (int i = 0; i < 5; i++)
        {
            float target = gloveData.FingerValues[i];
            _currentValues[i] = smoothSpeed > 0
                ? Mathf.Lerp(_currentValues[i], target, Time.deltaTime * smoothSpeed)
                : target;

            ApplyFingerBend(i, _currentValues[i]);
        }
    }

    /// <summary>
    /// 在当前 GameObject 的子层级中按名称自动查找骨骼 Transform。
    /// 命名规则：{boneName}.01{suffix}, {boneName}.02{suffix}, {boneName}.03{suffix}
    /// </summary>
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
