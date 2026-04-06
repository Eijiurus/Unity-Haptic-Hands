using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 左手交互：为五指尖端添加球形 Trigger，检测 TouchableObject；
/// 拇指+食指弯曲超过阈值（或按住 G）时捏取最近接触物体并吸附到食指指尖。
/// 需在根节点有 Kinematic Rigidbody 以便 Trigger 与动态刚体正确交互。
/// </summary>
public class HandInteractionRig : MonoBehaviour
{
    [SerializeField] GloveDataReceiver gloveData;

    [Tooltip("骨骼后缀，与 DataGloveHandDriver 一致")]
    [SerializeField] string boneSuffix = ".L";

    [Tooltip("指尖球半径（米），可按模型比例在 Inspector 调整")]
    [SerializeField] float tipRadius = 0.018f;

    [Tooltip("指尖相对远端骨骼的局部偏移（常沿骨轴向指尖）")]
    [SerializeField] Vector3 tipLocalOffset = new Vector3(0f, 0f, 0.012f);

    [Tooltip("捏取：拇指与食指弯曲均超过该值（0~1）")]
    [SerializeField, Range(0.2f, 1f)] float pinchThreshold = 0.5f;

    [Tooltip("无手套数据时，按住此键也可捏取（便于测试）")]
    [SerializeField] KeyCode pinchKey = KeyCode.G;

    static readonly string[] s_distalBones =
    {
        "thumb.03",
        "finger_index.03",
        "finger_middle.03",
        "finger_ring.03",
        "finger_pinky.03"
    };

    readonly HashSet<TouchableObject> _touching = new HashSet<TouchableObject>();
    Transform _indexTip;
    TouchableObject _held;
    bool _pinchWasDown;

    void Start()
    {
        EnsureKinematicRigidbody();
        BuildFingerTips();
    }

    void EnsureKinematicRigidbody()
    {
        var rb = GetComponent<Rigidbody>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    void BuildFingerTips()
    {
        foreach (string baseName in s_distalBones)
        {
            string full = baseName + boneSuffix;
            Transform bone = FindChildRecursive(transform, full);
            if (bone == null)
            {
                Debug.LogWarning($"[HandInteractionRig] 未找到骨骼 {full}，跳过该指尖 Trigger");
                continue;
            }

            var tipGo = new GameObject("TipTrigger_" + baseName);
            tipGo.transform.SetParent(bone, false);
            tipGo.transform.localPosition = tipLocalOffset;
            tipGo.transform.localRotation = Quaternion.identity;

            var col = tipGo.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = tipRadius;

            var relay = tipGo.AddComponent<FingerTipRelay>();
            relay.Rig = this;

            if (baseName == "finger_index.03")
                _indexTip = tipGo.transform;
        }

        if (_indexTip == null)
            _indexTip = transform;
    }

    void Update()
    {
        bool pinch = Input.GetKey(pinchKey);
        if (gloveData != null && gloveData.FingerValues != null && gloveData.FingerValues.Length >= 2)
        {
            if (gloveData.FingerValues[0] >= pinchThreshold &&
                gloveData.FingerValues[1] >= pinchThreshold)
                pinch = true;
        }

        if (pinch && !_pinchWasDown && _held == null)
        {
            var pick = PickClosestTouchable();
            if (pick != null)
            {
                _held = pick;
                _touching.Remove(pick);
                pick.Grab(_indexTip);
            }
        }
        else if (!pinch && _pinchWasDown && _held != null)
        {
            _held.Release();
            _held = null;
        }

        _pinchWasDown = pinch;
    }

    TouchableObject PickClosestTouchable()
    {
        TouchableObject best = null;
        float bestD = float.MaxValue;
        Vector3 refPos = _indexTip.position;
        foreach (var t in _touching)
        {
            if (t == null || t.IsGrabbed || !t.AllowPinchGrab) continue;
            float d = (t.transform.position - refPos).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = t;
            }
        }
        return best;
    }

    public void OnTipTriggerEnter(Collider other)
    {
        if (_held != null) return;
        var touchable = other.GetComponentInParent<TouchableObject>();
        if (touchable == null || touchable.IsGrabbed) return;
        if (!_touching.Add(touchable)) return;
        touchable.NotifyFingerTouchEnter();
    }

    public void OnTipTriggerExit(Collider other)
    {
        var touchable = other.GetComponentInParent<TouchableObject>();
        if (touchable == null) return;
        if (touchable == _held) return;
        if (!_touching.Remove(touchable)) return;
        touchable.NotifyFingerTouchExit();
    }

    static Transform FindChildRecursive(Transform parent, string boneName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == boneName)
                return child;
            Transform found = FindChildRecursive(child, boneName);
            if (found != null)
                return found;
        }
        return null;
    }
}
