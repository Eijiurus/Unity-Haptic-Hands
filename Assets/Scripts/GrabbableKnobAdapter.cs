using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 挂在旋钮的 GrabZone（与 TouchableObject 同 GameObject）。负责：
/// 1) 监听 TouchableObject.onGrabbed / onReleased；
/// 2) 把它们转发为 RotaryKnob.OnGrab / OnRelease；
/// 3) 持续把自身 transform 钉回原 parent / localPos / localRot，
///    抵消 TouchableObject.Grab() 内部的 SetParent(handAttach) —— 旋钮固定在面板上，不会被手抓走；
/// 4) 抓取期间在 DataGloveHandDriver 上同时打开两把锁：
///    a. lockWristRotation       —— 冻结手腕朝向；
///    b. lockWristPosition + lockedWristPositionTarget
///                                —— 把虚拟手"贴"到 handLockTarget 上（snap-to-grab）。
///    用户视觉上看到手停在旋钮上，但真实手腕的微小位移仍然会驱动旋钮旋转。
///
/// 推荐场景结构：
///   Knob_01
///   ├── GrabZone     ← 本 adapter / TouchableObject / Rigidbody / Collider 在这里
///   └── Visual       ← RotaryKnob 在这里
/// 默认 handLockTarget 指向自身 transform（GrabZone 通常正好在旋钮中心），
/// 必要时在 Inspector 里改成 Visual 或另设的 SnapPoint。
///
/// 依赖：DataGloveHandDriver 必须暴露 lockWristRotation / lockWristPosition / lockedWristPositionTarget
/// 三个字段（T5.5 + T5.6）。任何一个缺失都会导致本脚本编译失败，这是 T5.6 是否落地的诊断信号。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TouchableObject))]
[RequireComponent(typeof(Rigidbody))]
public class GrabbableKnobAdapter : MonoBehaviour
{
    [Header("References")]
    [Tooltip("本 GameObject 上的 TouchableObject。OnEnable 时订阅其 onGrabbed/onReleased。")]
    public TouchableObject touchable;

    [Tooltip("被本 adapter 转发抓取/释放的 RotaryKnob。通常在兄弟物体（Visual）上。")]
    public RotaryKnob targetKnob;

    [Tooltip("驱动虚拟手的 DataGloveHandDriver。抓取期间会被设置 lockWristRotation / lockWristPosition；释放时复位。")]
    public DataGloveHandDriver handDriver;

    [Tooltip("抓取期间虚拟手要 snap 到的位置。通常设为旋钮中心；为空时回退到 this.transform.position。")]
    public Transform handLockTarget;

    [Header("Behavior")]
    [Tooltip("抓取期间持续把本 transform 恢复到原 parent / localPos / localRot，确保旋钮始终钉在面板上。")]
    public bool keepInPlace = true;

    [Tooltip("是否在抓取时锁定手腕。关闭以便调试 RotaryKnob 的角度采样行为。")]
    public bool lockWristDuringGrab = true;

    [Tooltip("v7：是否在抓取期间把虚拟手位置 snap 到 handLockTarget。关闭则只锁旋转。")]
    public bool snapHandToKnob = true;

    [Tooltip("v10：抓取瞬间把虚拟手锁在『当前手腕位置』而不是 handLockTarget（旋钮中心）。\n" +
             "true（推荐，默认）：手碰到旋钮 → 原地停住，不会被吸到旋钮中心。这是用户期望的『触碰即冻结』体验。\n" +
             "false（旧行为）：手会瞬间 snap 到 handLockTarget 上，视觉上像被磁吸到旋钮。\n" +
             "注意：snapHandToKnob 必须为 true 此项才生效（否则 lockWristPosition 根本不会启用）。")]
    public bool freezeAtCurrentHandPosition = true;

    [Header("Grab Stability (v8)")]
    [Tooltip("一旦 lock 启用，至少保持这么长时间（秒）才允许真正解除——期间的 release 事件会被去抖，" +
             "期间到来的 re-grab 会取消解除计划。\n" +
             "解决场景：HandInteractionRig 的 pinch 检测在阈值附近抖动时，会快速地 grab/release 反复触发，" +
             "如果不去抖，手腕就会在'锁定（snap 到旋钮）—未锁定（回到 IMU 位置）'之间反复跳。\n" +
             "0 关闭去抖；典型值 0.15~0.30。如果你松手时感觉有可见延迟，调小这个值。")]
    [Range(0f, 1f)]
    public float minLockHoldDuration = 0.20f;

    [Tooltip("v9：松开旋钮的瞬间，把 DataGloveHandDriver 的 IMU 位置积分器重新锚定到旋钮位置。\n" +
             "不开这个：解锁后 IMU 弹簧会把手拉回场景启动时的初始位置（你看到的'传送回胸前'）。\n" +
             "开这个：解锁后手停在旋钮处不动，真手继续动时 IMU 在'旋钮 = 新原点'的基础上继续积分，\n" +
             "        手会从旋钮处自然地移开——这是 IMU-only 系统里实现'松手即原地'的标准做法。")]
    public bool rebaseHandAnchorOnRelease = true;

    [Header("Direct Finger-Bend Grab (v11, alt path)")]
    [Tooltip("启用后，本 adapter 直接监听 GloveDataReceiver.FingerValues：\n" +
             "  · 手指弯曲度（按 fingerBendMode 聚合）≥ grabBendThreshold → 自动 HandleGrab\n" +
             "  · 手指弯曲度 ≤ releaseBendThreshold → 自动 HandleRelease\n" +
             "完全不依赖 TouchableObject / 触碰 trigger / HandInteractionRig 的捏合检测。\n" +
             "适合：键盘演示模式（用 GloveDataReceiver.useKeyboardSimulation=Space 全握拳）、\n" +
             "      快速单元测试旋钮链路、不想配齐触发器但想验证手指→灯泡链路是否通。\n" +
             "注意：本路径不做位置距离判断 —— 只要弯曲达标就抓取（一旦抓取，仍然按原逻辑把虚拟手锁在当前位置）。")]
    public bool useDirectFingerBendGrab = false;

    [Tooltip("直接抓取模式要监听的手指弯曲数据源。Reset / OnEnable 时如果为空会自动 FindFirstObjectByType。")]
    public GloveDataReceiver gloveData;

    public enum FingerBendMode
    {
        AverageOfAll,        // 五指平均（握拳手势最自然）
        ThumbAndIndexBoth,   // 拇指 && 食指都达标（捏合手势）
        AnyFinger,           // 任一手指达标（最敏感）
        IndexOnly,           // 仅食指（单指扳机）
    }

    [Tooltip("如何把 5 个手指的弯曲值聚合成一个 0~1 标量来与阈值比较。\n" +
             "AverageOfAll：五指平均（握拳，推荐）\n" +
             "ThumbAndIndexBoth：拇指和食指都需 ≥ grabBendThreshold（捏合）\n" +
             "AnyFinger：任一手指 ≥ 阈值即抓取（最敏感）\n" +
             "IndexOnly：仅看食指")]
    public FingerBendMode fingerBendMode = FingerBendMode.AverageOfAll;

    [Tooltip("聚合后的弯曲度达到该值即触发抓取（0=完全伸直，1=完全弯曲）。")]
    [Range(0f, 1f)]
    public float grabBendThreshold = 0.5f;

    [Tooltip("聚合后的弯曲度低于该值即触发释放。略小于 grabBendThreshold 形成迟滞，避免阈值附近抖动。")]
    [Range(0f, 1f)]
    public float releaseBendThreshold = 0.25f;

    [Header("Events (haptic hooks)")]
    [Tooltip("成功抓取后额外触发，用于挂触觉/音效。")]
    public UnityEvent onGrabbed;

    [Tooltip("释放时额外触发。")]
    public UnityEvent onReleased;

    [Header("Debug")]
    [Tooltip("启用后，每次抓取/释放都会在 Console 打印关键引用状态——快速定位 'lock 没生效 / 灯不变' 这类配线问题。")]
    public bool logGrabReleaseDiagnostics = true;

    private Transform _originalParent;
    private Vector3 _originalLocalPos;
    private Quaternion _originalLocalRot;
    private bool _isGrabbed;

    // v8 grab-stability：HandleRelease 不立刻解锁，而是把"应解锁时间点"记下来，
    // 由 Update 在到点后真正解锁。期间到来的 HandleGrab 会清掉这个时间戳，
    // 让 lock 一直保持，从而吃掉底层 pinch 检测的快速抖动。
    private float _lockReleaseDeadline = -1f;

    /// <summary>
    /// 编辑器中添加组件时调用，做一些零配置的合理默认。仅在 Edit 模式生效，运行时不会调用。
    /// </summary>
    private void Reset()
    {
        if (touchable == null)
            touchable = GetComponent<TouchableObject>();

        var rb = GetComponent<Rigidbody>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        if (targetKnob == null)
        {
            var parent = transform.parent;
            if (parent != null)
                targetKnob = parent.GetComponentInChildren<RotaryKnob>();
        }

        if (handDriver == null)
            handDriver = FindFirstObjectByType<DataGloveHandDriver>();

        if (handLockTarget == null)
            handLockTarget = transform;

        if (gloveData == null)
            gloveData = FindFirstObjectByType<GloveDataReceiver>();
    }

    private void OnEnable()
    {
        // 在订阅事件之前抓快照——OnEnable 在场景加载时早于任何抓取事件触发，
        // 所以此时 transform.parent 还是面板，没有被 TouchableObject.Grab() 的 SetParent(handAttach) 污染。
        _originalParent = transform.parent;
        _originalLocalPos = transform.localPosition;
        _originalLocalRot = transform.localRotation;

        if (touchable != null)
        {
            touchable.onGrabbed.AddListener(HandleGrab);
            touchable.onReleased.AddListener(HandleRelease);
        }
        else
        {
            Debug.LogWarning($"[GrabbableKnobAdapter] {name}: touchable 未指派，无法订阅 grab/release 事件。", this);
        }

        // 直接抓取模式启用时自动找一个 GloveDataReceiver。运行时也能补救漏配。
        if (useDirectFingerBendGrab && gloveData == null)
        {
            gloveData = FindFirstObjectByType<GloveDataReceiver>();
            if (gloveData == null)
            {
                Debug.LogWarning(
                    $"[GrabbableKnobAdapter] {name}: useDirectFingerBendGrab=true 但场景里没有 GloveDataReceiver。" +
                    "本路径将不会触发抓取——挂一个 GloveDataReceiver（或拖到 gloveData 字段）后再 Play。",
                    this);
            }
        }
    }

    private void OnDisable()
    {
        if (touchable != null)
        {
            touchable.onGrabbed.RemoveListener(HandleGrab);
            touchable.onReleased.RemoveListener(HandleRelease);
        }

        // 安全网：如果 GameObject 在抓取过程中被禁用，必须强制释放两把锁，
        // 否则手腕会永远冻结在旋钮上。同样需要先 rebase 再解锁，避免手"传送回初始位置"。
        if (handDriver != null)
        {
            if (rebaseHandAnchorOnRelease && handDriver.lockWristPosition)
            {
                handDriver.RebaseWristAnchorToCurrent();
            }
            handDriver.lockWristRotation = false;
            handDriver.lockWristPosition = false;
        }
        if (targetKnob != null && targetKnob.IsGrabbed)
        {
            targetKnob.OnRelease();
        }
        _isGrabbed = false;
        _lockReleaseDeadline = -1f;
    }

    private void Update()
    {
        if (_isGrabbed && keepInPlace)
        {
            if (transform.parent != _originalParent)
            {
                transform.SetParent(_originalParent, false);
            }
            transform.localPosition = _originalLocalPos;
            transform.localRotation = _originalLocalRot;
        }

        // v11：直接读手指弯曲数据决定抓取/释放。完全独立于 TouchableObject / HandInteractionRig 的捏合检测。
        // 适合键盘演示模式（按 Space 一键全握拳）和无触发器的最小化测试。
        UpdateDirectFingerBendGrab();

        // 去抖：HandleRelease 只是把"应解锁时间"记下来，真正解锁在这里发生。
        // 如果在 deadline 到达前又有 HandleGrab，deadline 会被清掉，永远不到这里。
        if (!_isGrabbed && _lockReleaseDeadline > 0f && Time.time >= _lockReleaseDeadline)
        {
            ReleaseLocksNow();
        }
    }

    /// <summary>
    /// 直接抓取路径：每帧把 GloveDataReceiver.FingerValues 聚合成一个 0~1 标量，
    /// 越过 grabBendThreshold 就 HandleGrab，跌破 releaseBendThreshold 就 HandleRelease。
    /// 不做位置距离判断、不依赖 TouchableObject。
    /// </summary>
    private void UpdateDirectFingerBendGrab()
    {
        if (!useDirectFingerBendGrab) return;
        if (gloveData == null || gloveData.FingerValues == null || gloveData.FingerValues.Length < 5) return;

        float bend = AggregateBend(gloveData.FingerValues, fingerBendMode);

        if (!_isGrabbed && bend >= grabBendThreshold)
        {
            HandleGrab();
        }
        else if (_isGrabbed && bend <= releaseBendThreshold)
        {
            HandleRelease();
        }
    }

    /// <summary>
    /// 把 5 个手指（[0]Thumb [1]Index [2]Middle [3]Ring [4]Pinky）聚合成一个标量，
    /// 用于和阈值比较。各模式的语义见 FingerBendMode 的 tooltip。
    /// </summary>
    private static float AggregateBend(float[] f, FingerBendMode mode)
    {
        switch (mode)
        {
            case FingerBendMode.AverageOfAll:
                return (f[0] + f[1] + f[2] + f[3] + f[4]) * 0.2f;

            case FingerBendMode.ThumbAndIndexBoth:
                // 取两个里更小的那个 —— 这样只有"两指都达标"时聚合值才达标，模拟捏合手势。
                return Mathf.Min(f[0], f[1]);

            case FingerBendMode.AnyFinger:
                return Mathf.Max(Mathf.Max(Mathf.Max(f[0], f[1]), Mathf.Max(f[2], f[3])), f[4]);

            case FingerBendMode.IndexOnly:
                return f[1];

            default:
                return 0f;
        }
    }

    /// <summary>
    /// 真正把两把手腕锁解开 + 通知 RotaryKnob 释放。仅由 Update（去抖到点）或 OnDisable（安全网）调用。
    /// 顺序很关键：先 rebase（在 lockWristPosition 还是 true 时，transform.localPosition 还固定在旋钮上，
    /// 此时 rebase 会把"新原点"记成旋钮位置），然后才把 lock 关掉。
    /// </summary>
    private void ReleaseLocksNow()
    {
        bool didRebase = false;
        if (handDriver != null)
        {
            // 重锚：在解开 position lock 之前调用，把 IMU 积分器的零点设到当前 transform 位置（=旋钮位置）。
            // 否则解锁瞬间 ApplyFinalWristPosition 会用 _initialWristPosition + 几乎为 0 的 offset 作为新目标，
            // 手就会平滑漂回到场景启动时的初始位置——这就是用户报告的"传送到初始位置"。
            if (rebaseHandAnchorOnRelease && handDriver.lockWristPosition)
            {
                handDriver.RebaseWristAnchorToCurrent();
                didRebase = true;
            }

            if (lockWristDuringGrab)
            {
                handDriver.lockWristRotation = false;
                handDriver.lockWristPosition = false;
            }
        }
        if (targetKnob != null && targetKnob.IsGrabbed)
        {
            targetKnob.OnRelease();
        }
        _lockReleaseDeadline = -1f;

        if (logGrabReleaseDiagnostics)
        {
            Debug.Log(
                $"[GrabbableKnobAdapter] {name} LOCK-RELEASED (after {minLockHoldDuration:F2}s hold)" +
                $"{(didRebase ? " | rebased anchor to current pos" : "")}",
                this);
        }
    }

    private void HandleGrab()
    {
        // 关键：如果当前正处在"待解锁"窗口里（即上一次 release 还没真正解锁就又来了一次 grab，
        // 典型的 pinch 抖动），把 deadline 清掉，让 lock 继续保持。
        bool wasDebounced = _lockReleaseDeadline > 0f && !_isGrabbed;
        _lockReleaseDeadline = -1f;

        _isGrabbed = true;

        // 抖动期间 RotaryKnob 实际上一直处于 grabbed 状态——重复调用 OnGrab() 会重置 _grabStartHandPos，
        // 让旋钮"丢角度"。所以只在它真正没被抓时才转发。
        if (targetKnob != null && !targetKnob.IsGrabbed)
        {
            targetKnob.OnGrab();
        }
        else if (targetKnob == null)
        {
            Debug.LogWarning($"[GrabbableKnobAdapter] {name}: targetKnob 未指派，抓取事件无法转发到旋钮。", this);
        }

        bool willLockRotation = lockWristDuringGrab && handDriver != null;
        bool willLockPosition = willLockRotation && snapHandToKnob;
        Vector3 lockTarget = Vector3.zero;

        if (willLockRotation)
        {
            handDriver.lockWristRotation = true;

            if (snapHandToKnob)
            {
                // v10：默认锁在『手腕当前世界位置』，让用户感觉手"碰到旋钮就停住"，
                // 而不是被瞬移到旋钮中心。需要旧的吸附行为时把 freezeAtCurrentHandPosition 关掉即可。
                if (freezeAtCurrentHandPosition)
                {
                    lockTarget = handDriver.transform.position;
                }
                else
                {
                    lockTarget = (handLockTarget != null) ? handLockTarget.position : transform.position;
                }
                handDriver.lockedWristPositionTarget = lockTarget;
                handDriver.lockWristPosition = true;
            }
        }

        if (logGrabReleaseDiagnostics)
        {
            // 单行日志，避免 Unity Console 默认 row 高度截断后看不全。
            // 哪一项是 NULL 就说明对应的引用没接好。
            string posSrc = (targetKnob != null)
                ? (targetKnob.positionSource != null ? targetKnob.positionSource.name : "NULL")
                : "n/a";
            string lockMode = willLockPosition
                ? (freezeAtCurrentHandPosition ? "freeze@hand" : "snap@knob")
                : "<no-lock>";
            Debug.Log(
                $"[GrabbableKnobAdapter] {name} GRAB{(wasDebounced ? " [debounced]" : "")} | " +
                $"targetKnob={(targetKnob   != null ? targetKnob.name   : "NULL")} | " +
                $"handDriver={(handDriver   != null ? handDriver.name   : "NULL")} | " +
                $"handLockTarget={(handLockTarget != null ? handLockTarget.name : "NULL")} | " +
                $"positionSource={posSrc} | " +
                $"willLockPos={willLockPosition} | " +
                $"lockMode={lockMode} | " +
                $"lockTarget={(willLockPosition ? lockTarget.ToString("F3") : "<no-lock>")}",
                this);
        }

        onGrabbed?.Invoke();
    }

    private void HandleRelease()
    {
        _isGrabbed = false;

        // v8：不立即解锁。把 deadline 记下来，让 Update 在 minLockHoldDuration 之后真正解锁。
        // 期间任何 HandleGrab 都会清掉 deadline，从而吃掉底层 pinch 检测的快速抖动，
        // 防止手腕在"snap 到旋钮 ↔ 回到 IMU 位置"之间反复跳。
        // RotaryKnob.OnRelease() 也一同延后到 ReleaseLocksNow()，避免 _grabStartHandPos
        // 在抖动期间被重新捕获导致旋钮跳变。
        if (minLockHoldDuration > 0f)
        {
            _lockReleaseDeadline = Time.time + minLockHoldDuration;
        }
        else
        {
            ReleaseLocksNow();
        }

        // 但 GrabZone 自身的归位必须立刻做：TouchableObject.Release() 已经把它
        //   1) SetParent(null, true)            → 踢到了场景根；
        //   2) _rb.isKinematic = false          → 现在会被重力拉走。
        // 这两个不能等——一帧后 GrabZone 就掉下去了。把它马上钉回 Knob_01 下并恢复 kinematic。
        if (transform.parent != _originalParent)
        {
            transform.SetParent(_originalParent, false);
        }
        transform.localPosition = _originalLocalPos;
        transform.localRotation = _originalLocalRot;

        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        if (logGrabReleaseDiagnostics)
        {
            string lockState = minLockHoldDuration > 0f
                ? $"locks DEFERRED ({minLockHoldDuration:F2}s)"
                : "locks released";
            Debug.Log(
                $"[GrabbableKnobAdapter] {name} RELEASE | " +
                $"restored under='{(_originalParent != null ? _originalParent.name : "<scene root>")}' | " +
                $"localPos={_originalLocalPos.ToString("F3")} | " +
                $"kinematic={(rb != null ? rb.isKinematic.ToString() : "no-rb")} | {lockState}",
                this);
        }

        onReleased?.Invoke();
    }
}
