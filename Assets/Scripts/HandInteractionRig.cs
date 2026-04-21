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

    [Tooltip("开启后为指尖增加非 Trigger 物理碰撞球，减少手指穿透物体")]
    [SerializeField] bool enablePhysicalTipCollision = true;

    [Tooltip("物理碰撞球半径（建议略小于 Trigger 半径）")]
    [SerializeField] float physicalTipRadius = 0.013f;

    [Tooltip("指尖相对远端骨骼的局部偏移（常沿骨轴向指尖）")]
    [SerializeField] Vector3 tipLocalOffset = new Vector3(0f, 0f, 0.012f);

    [Tooltip("捏取：拇指与食指弯曲均超过该值（0~1）")]
    [SerializeField, Range(0.2f, 1f)] float pinchThreshold = 0.5f;

    [Tooltip("释放阈值（0~1）：低于该值就释放。小于捏取阈值可避免抖动反复抓放")]
    [SerializeField, Range(0.05f, 0.95f)] float releaseThreshold = 0.35f;

    [Tooltip("启用拇指尖-食指尖距离捏合检测（不依赖手套弯曲值）")]
    [SerializeField] bool useTipDistancePinch = true;

    [Tooltip("距离捏合阈值（米）：小于该值视为捏合")]
    [SerializeField, Range(0.005f, 0.08f)] float pinchDistance = 0.028f;

    [Tooltip("距离释放阈值（米）：大于该值视为松开；建议略大于捏合阈值")]
    [SerializeField, Range(0.01f, 0.12f)] float releaseDistance = 0.04f;

    [Tooltip("握拳阈值（0~1）：五指平均弯曲超过该值也视为抓取")]
    [SerializeField, Range(0.2f, 0.95f)] float fistGripThreshold = 0.55f;

    [Tooltip("抓取兜底半径（米）：捏合时在食指指尖附近搜索可抓物")]
    [SerializeField, Range(0.01f, 0.2f)] float grabAssistRadius = 0.06f;

    [Tooltip("无手套数据时，按住此键也可捏取（便于测试）")]
    [SerializeField] KeyCode pinchKey = KeyCode.G;

    [Tooltip("宽松抓握阈值（0~1）：任意手指弯曲超过该值即视为可抓握状态")]
    [SerializeField, Range(0.05f, 0.9f)] float bentGrabThreshold = 0.25f;

    [Tooltip("抓取后挂载到手掌根节点（更稳，不容易被推开）")]
    [SerializeField] bool attachToPalm = true;

    [Header("调试日志")]
    [SerializeField] bool showGrabDebugLog = true;

    [Tooltip("低值弯曲阈值（用于有些手套“弯曲=接近0”的映射）")]
    [SerializeField, Range(0f, 0.4f)] float bentGrabLowThreshold = 0.12f;

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
    Transform _thumbTip;
    TouchableObject _held;
    bool _pinchWasDown;
    bool _pinchIsDown;
    float _nextDebugLogTime;

    void Start()
    {
        AutoBindGloveData();
        EnsureKinematicRigidbody();
        BuildFingerTips();
    }

    void AutoBindGloveData()
    {
        if (gloveData != null) return;
        gloveData = FindFirstObjectByType<GloveDataReceiver>();
        if (gloveData == null)
            Log("[Grab] 未找到 GloveDataReceiver，抓取将回退到按键/指尖距离判定。");
        else
            Log("[Grab] 已自动绑定 GloveDataReceiver。");
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

            if (enablePhysicalTipCollision)
            {
                var solidCol = tipGo.AddComponent<SphereCollider>();
                solidCol.isTrigger = false;
                solidCol.radius = Mathf.Min(physicalTipRadius, tipRadius);
            }

            var relay = tipGo.AddComponent<FingerTipRelay>();
            relay.Rig = this;

            if (baseName == "finger_index.03")
                _indexTip = tipGo.transform;
            else if (baseName == "thumb.03")
                _thumbTip = tipGo.transform;
        }

        if (_indexTip == null)
            _indexTip = transform;
    }

    void Update()
    {
        bool pinchByKey = Input.GetKey(pinchKey);
        bool pinchByFinger = false;
        bool releaseByFinger = true;
        bool pinchByFist = false;
        bool pinchByAnyBent = false;
        bool pinchByAnyBentLow = false;
        if (gloveData != null && gloveData.FingerValues != null && gloveData.FingerValues.Length >= 2)
        {
            float thumb = gloveData.FingerValues[0];
            float index = gloveData.FingerValues[1];
            pinchByFinger = thumb >= pinchThreshold && index >= pinchThreshold;
            releaseByFinger = thumb <= releaseThreshold && index <= releaseThreshold;

            float sum = 0f;
            int count = Mathf.Min(5, gloveData.FingerValues.Length);
            for (int i = 0; i < count; i++)
            {
                float v = gloveData.FingerValues[i];
                sum += v;
                if (v >= bentGrabThreshold) pinchByAnyBent = true;
                if (v <= bentGrabLowThreshold) pinchByAnyBentLow = true;
            }
            float avg = count > 0 ? sum / count : 0f;
            pinchByFist = avg >= fistGripThreshold;
        }

        bool pinchByDistance = false;
        bool releaseByDistance = true;
        if (useTipDistancePinch && _thumbTip != null && _indexTip != null)
        {
            float d = Vector3.Distance(_thumbTip.position, _indexTip.position);
            pinchByDistance = d <= pinchDistance;
            releaseByDistance = d >= releaseDistance;
        }

        bool pinch = _pinchWasDown
            ? (pinchByKey || !releaseByFinger || !releaseByDistance)
            : (pinchByKey || pinchByFinger || pinchByDistance || pinchByFist || pinchByAnyBent || pinchByAnyBentLow);

        // Fallback when glove data is missing: touching + thumb-index distance relaxed threshold also counts.
        if (gloveData == null && _touching.Count > 0 && _thumbTip != null && _indexTip != null)
        {
            float d = Vector3.Distance(_thumbTip.position, _indexTip.position);
            if (d <= releaseDistance)
                pinch = true;
        }
        _pinchIsDown = pinch;

        if (pinch && _held == null)
        {
            var pick = PickClosestTouchable();
            if (pick == null)
                pick = FindTouchableNearIndexTip();
            if (pick != null)
            {
                _touching.Remove(pick);
                if (pick.Grab(GetGrabAttachPoint()))
                {
                    _held = pick;
                    Log($"[Grab] 抓取成功: {pick.name} | attach={GetGrabAttachPoint().name}");
                }
                else
                    _touching.Add(pick);
            }
        }
        else if (!pinch && _pinchWasDown && _held != null)
        {
            Log($"[Grab] 释放: {_held.name}");
            _held.Release();
            _held = null;
        }

        if (_held == null && _touching.Count > 0 && Time.time >= _nextDebugLogTime)
        {
            _nextDebugLogTime = Time.time + 0.35f;
            string fingerInfo = gloveData != null && gloveData.FingerValues != null && gloveData.FingerValues.Length >= 5
                ? $"T:{gloveData.FingerValues[0]:F2} I:{gloveData.FingerValues[1]:F2} M:{gloveData.FingerValues[2]:F2} R:{gloveData.FingerValues[3]:F2} P:{gloveData.FingerValues[4]:F2}"
                : "no gloveData";
            Log($"[Grab] 已接触{_touching.Count}个物体但未抓取 | pinch={pinch} | {fingerInfo}");
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

    TouchableObject FindTouchableNearIndexTip()
    {
        Vector3 center = _indexTip != null ? _indexTip.position : transform.position;
        Collider[] cols = Physics.OverlapSphere(center, grabAssistRadius, ~0, QueryTriggerInteraction.Collide);
        TouchableObject best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < cols.Length; i++)
        {
            var t = cols[i].GetComponentInParent<TouchableObject>();
            if (t == null || t.IsGrabbed || !t.AllowPinchGrab) continue;
            float d = (t.transform.position - center).sqrMagnitude;
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

        // If fingers are already pinching when contact happens, grab immediately.
        if (_pinchIsDown && touchable.AllowPinchGrab)
        {
            _touching.Remove(touchable);
            if (touchable.Grab(GetGrabAttachPoint()))
            {
                _held = touchable;
                Log($"[Grab] 触碰即抓取: {touchable.name} | attach={GetGrabAttachPoint().name}");
            }
            else
                _touching.Add(touchable);
        }
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

    Transform GetGrabAttachPoint()
    {
        if (attachToPalm) return transform;
        return _indexTip != null ? _indexTip : transform;
    }

    void Log(string msg)
    {
        if (showGrabDebugLog)
            Debug.Log($"[HandInteractionRig] {msg}");
    }
}
