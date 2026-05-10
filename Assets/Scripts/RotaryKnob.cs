using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 虚拟旋钮：由"用户手部世界位置的运动"驱动，采用 v12 的速率累加模式（rate accumulation）。
///
/// 工作原理：
/// 抓取瞬间记录旋钮当前角度作为基准；之后每帧只看"自上一帧到这一帧的手部位移"
/// （而不是"自抓取瞬间起的总位移"），把它沿指定世界轴投影后叠加到一个累加器里，
/// 再乘以 degreesPerMeter 转成角度增量。最终角度 = 抓取时角度 + 累加器，并钳制到 [minAngle, maxAngle]。
///
/// 为什么是 rate accumulation 而不是简单的"绝对位移→角度"：
/// DataGloveHandDriver 的 IMU 位置积分器内置了弹簧回中（wristPositionSpring）+
/// 持续回中（positionRecenteringSpeed）+ 静止 ZUPT 拉回。这些机制会把 _currentWristOffset
/// 在用户停手后慢慢拉回 0，所以"绝对位移"会衰减——表现就是"手挥一下灯亮一下又暗回来"。
/// 改用速率累加 + 速度阈值过滤后：用户主动挥手时位移被累加进角度并保持；
/// 用户停手时弹簧造成的慢速漂移因低于 driftVelocityThreshold 被丢弃，角度自然保持。
///
/// 这与"读手腕旋转"或"绕旋钮中心的弧度"无关——纯粹是手部位移（速率）→角度的映射。
///
/// 注意：本脚本不会锁定手腕旋转。手腕锁定由 GrabbableKnobAdapter
/// 通过 DataGloveHandDriver.lockWristRotation 单独处理。
/// </summary>
[DisallowMultipleComponent]
public class RotaryKnob : MonoBehaviour
{
    public enum WorldAxis { X, Y, Z }
    public enum KnobAxis { X, Y, Z }

    [System.Serializable]
    public class FloatEvent : UnityEvent<float> { }

    [Header("Limits")]
    [Tooltip("旋钮最小角度（度）")]
    [SerializeField] private float minAngle = 0f;

    [Tooltip("旋钮最大角度（度）")]
    [SerializeField] private float maxAngle = 270f;

    [Tooltip("步进角度（度）：每跨过一个步进，触发一次 onStepClicked，可用于触觉/音效反馈")]
    [SerializeField] private float angleStep = 15f;

    [Header("References")]
    [Tooltip("手部 Transform：每帧读取其 .position（世界坐标）。" +
             "通常拖入 DataGloveHandDriver/HandInteractionRig 所在的手根节点。" +
             "当 positionSource + useRawWristPosition 启用时，本字段会被忽略。")]
    [SerializeField] private Transform handTransform;

    [Header("Position Source")]
    [Tooltip("可选：从 DataGloveHandDriver.RawWristTargetPosition 读取手部位置。" +
             "为空时回退到 handTransform.position。")]
    public DataGloveHandDriver positionSource;

    [Tooltip("启用后，若 positionSource 已指派，则使用其 RawWristTargetPosition 而非 handTransform.position。" +
             "意义：GrabbableKnobAdapter 抓取期间会把虚拟手 snap 到旋钮，此时 handTransform.position 会被冻结，" +
             "但 IMU 计算出的 RawWristTargetPosition 仍随真实手部位移更新——只有读取它，旋钮才能继续转动。")]
    public bool useRawWristPosition = true;

    [Header("Input Axis")]
    [Tooltip("驱动旋转的世界轴：手沿该世界轴的位移会被映射成旋钮角度变化。\n" +
             "如何选对轴：把 logAxisDiagnostics 打开，运行一次抓取并左右挥手，\n" +
             "Console 里 |Δx|/|Δy|/|Δz| 三个值最大的那个就是你手的『左右』在 Unity 里对应的轴。")]
    [SerializeField] private WorldAxis displacementAxis = WorldAxis.X;

    [Header("Sensitivity")]
    [Tooltip("灵敏度：每米手部位移对应的旋钮角度（度/米）。" +
             "默认 3000 = 30°/cm，约 9cm 的手部行程即可覆盖 0~270° 的完整量程。" +
             "若实际手部行程更小，可调大此值。")]
    [SerializeField] private float degreesPerMeter = 3000f;

    [Tooltip("v12 速度阈值（米/秒）：每帧位移换算成的轴向速度低于此值时不累加进角度。\n" +
             "目的：过滤掉 DataGloveHandDriver 弹簧回中（wristPositionSpring + positionRecenteringSpeed）\n" +
             "在用户停手后产生的慢速漂移——这就是『亮一下又暗下来』的根源。\n" +
             "实测建议：0.05~0.12。低于 0.05 弹簧漂移会被当成有效输入；高于 0.15 会吃掉缓慢的微调动作。\n" +
             "0 关闭过滤（不推荐——亮度会无法保持）。")]
    [SerializeField, Range(0f, 0.3f)] private float driftVelocityThreshold = 0.08f;

    [Header("Sign")]
    [Tooltip("方向反转：1 = 正方向，-1 = 反向。用于让旋钮的旋转方向与手的运动方向匹配。")]
    [SerializeField] private float signFlip = 1f;

    [Header("Knob Visual Axis")]
    [Tooltip("旋钮可视化旋转轴（局部坐标）：旋钮围绕其本地坐标系的哪个轴旋转")]
    [SerializeField] private KnobAxis knobRotationAxis = KnobAxis.Y;

    [Header("Events")]
    [Tooltip("角度变化事件：参数为归一化值 0~1（minAngle→0，maxAngle→1）")]
    public FloatEvent onValueChanged;

    [Tooltip("步进事件：每跨过一个 angleStep 触发一次，可用于挡位音效/触觉反馈")]
    public UnityEvent onStepClicked;

    [Header("Diagnostics")]
    [Tooltip("启用后，抓取期间每 logIntervalSeconds 秒在 Console 打印一次三个轴各自的累计位移、" +
             "当前帧速度、累加器值。\n" +
             "用法：开启 → Play → 抓住旋钮 → 只往左右方向挥手；\n" +
             "Console 里看哪个轴的 |sumΔ| 增长最明显，就把 displacementAxis 设成那个轴。")]
    [SerializeField] private bool logAxisDiagnostics = false;

    [Tooltip("诊断日志的最小间隔（秒），避免刷屏。")]
    [SerializeField, Range(0.05f, 2f)] private float logIntervalSeconds = 0.3f;

    private bool _isGrabbed;
    private Vector3 _lastHandPos;
    private float _accumulatedAxisDisplacement;
    private float _grabStartKnobAngle;
    private float _currentAngle;
    private int _lastStepIndex;
    private Quaternion _initialLocalRotation;
    private Vector3 _diagSumAbs;
    private float _diagLastLogTime;

    /// <summary>当前角度归一化到 [0,1]（minAngle→0，maxAngle→1）。</summary>
    public float NormalizedValue
    {
        get
        {
            return Mathf.InverseLerp(minAngle, maxAngle, _currentAngle);
        }
    }

    /// <summary>当前旋钮角度（度），范围 [minAngle, maxAngle]。</summary>
    public float CurrentAngle
    {
        get { return _currentAngle; }
    }

    /// <summary>是否正在被抓取。</summary>
    public bool IsGrabbed
    {
        get { return _isGrabbed; }
    }

    private void Awake()
    {
        if (handTransform == null)
        {
            var driver = FindFirstObjectByType<DataGloveHandDriver>();
            if (driver != null)
                handTransform = driver.transform;
        }

        _initialLocalRotation = transform.localRotation;
        _currentAngle = Mathf.Clamp(_currentAngle, minAngle, maxAngle);
        ApplyKnobRotation();
    }

    private void Update()
    {
        if (!_isGrabbed) return;
        if (handTransform == null && !(useRawWristPosition && positionSource != null)) return;

        Vector3 currentHandPos = GetCurrentHandPosition();
        Vector3 frameDelta = currentHandPos - _lastHandPos;
        _lastHandPos = currentHandPos;

        float dt = Time.deltaTime;
        float axisDelta = GetAxisComponent(frameDelta, displacementAxis);

        // v12 速率累加 + 速度阈值：
        // 只把"明显在动"的帧位移累加进角度。停手时 IMU 弹簧造成的慢速漂移
        // (|velocity| < driftVelocityThreshold) 被丢弃 —— 这是"亮度保持不住"的根治办法。
        // 角度本身保持在累加器里，绝不会被弹簧漂移衰减。
        if (dt > 0f)
        {
            float axisVelocity = Mathf.Abs(axisDelta / dt);
            if (axisVelocity >= driftVelocityThreshold)
            {
                _accumulatedAxisDisplacement += axisDelta;
            }
        }

        float gain = degreesPerMeter * signFlip;
        float deltaAngle = _accumulatedAxisDisplacement * gain;
        float newAngle = Mathf.Clamp(_grabStartKnobAngle + deltaAngle, minAngle, maxAngle);

        // 钳制后回写累加器：到了上下限就不再继续累计这个方向，
        // 否则用户继续往同方向推手会"积负债"，反向时要先把负债转完才能掉头，体感很差。
        // gain==0 时直接保留原累加器（虽然该参数组合下旋钮不会动，但避免除零）。
        if (Mathf.Abs(gain) > Mathf.Epsilon)
            _accumulatedAxisDisplacement = (newAngle - _grabStartKnobAngle) / gain;

        _currentAngle = newAngle;
        ApplyKnobRotation();

        if (onValueChanged != null)
            onValueChanged.Invoke(NormalizedValue);

        if (angleStep > 0f)
        {
            int currentStepIndex = Mathf.RoundToInt(_currentAngle / angleStep);
            if (currentStepIndex != _lastStepIndex)
            {
                _lastStepIndex = currentStepIndex;
                if (onStepClicked != null)
                    onStepClicked.Invoke();
            }
        }

        if (logAxisDiagnostics)
        {
            _diagSumAbs.x += Mathf.Abs(frameDelta.x);
            _diagSumAbs.y += Mathf.Abs(frameDelta.y);
            _diagSumAbs.z += Mathf.Abs(frameDelta.z);

            if (Time.time - _diagLastLogTime >= logIntervalSeconds)
            {
                _diagLastLogTime = Time.time;
                Vector3 frameVel = (dt > 0f) ? (frameDelta / dt) : Vector3.zero;
                Debug.Log(
                    $"[RotaryKnob:{name}] axis={displacementAxis} | " +
                    $"sum|Δ|=({_diagSumAbs.x * 100f:F1},{_diagSumAbs.y * 100f:F1},{_diagSumAbs.z * 100f:F1}) cm | " +
                    $"vel=({frameVel.x:F3},{frameVel.y:F3},{frameVel.z:F3}) m/s | " +
                    $"accum={_accumulatedAxisDisplacement * 100f:F2} cm | " +
                    $"angle={_currentAngle:F1}° (norm={NormalizedValue:F2})",
                    this);
            }
        }
    }

    /// <summary>抓取开始：把累加器清零并把当前手位置作为下一帧 frameDelta 的基准。</summary>
    public void OnGrab()
    {
        _isGrabbed = true;

        _lastHandPos = GetCurrentHandPosition();
        _accumulatedAxisDisplacement = 0f;

        _grabStartKnobAngle = _currentAngle;
        _lastStepIndex = angleStep > 0f ? Mathf.RoundToInt(_currentAngle / angleStep) : 0;

        _diagSumAbs = Vector3.zero;
        _diagLastLogTime = Time.time;
    }

    /// <summary>
    /// 当前用于位移采样的手部位置。优先使用 DataGloveHandDriver.RawWristTargetPosition，
    /// 否则回退到 handTransform.position；都没配置时返回 Vector3.zero（与同样的零基准 _grabStartHandPos
    /// 配合，相当于"不旋转"——可见行为保持稳定）。
    /// </summary>
    private Vector3 GetCurrentHandPosition()
    {
        if (useRawWristPosition && positionSource != null)
            return positionSource.RawWristTargetPosition;
        if (handTransform != null)
            return handTransform.position;
        return Vector3.zero;
    }

    /// <summary>释放抓取：停止跟随手部位移。</summary>
    public void OnRelease()
    {
        _isGrabbed = false;
    }

    /// <summary>外部强制设置角度（例如初始化、恢复存档），会触发 onValueChanged。</summary>
    public void SetAngle(float angle)
    {
        _currentAngle = Mathf.Clamp(angle, minAngle, maxAngle);
        ApplyKnobRotation();
        if (onValueChanged != null)
            onValueChanged.Invoke(NormalizedValue);
        if (angleStep > 0f)
            _lastStepIndex = Mathf.RoundToInt(_currentAngle / angleStep);
    }

    private static float GetAxisComponent(Vector3 v, WorldAxis axis)
    {
        switch (axis)
        {
            case WorldAxis.X: return v.x;
            case WorldAxis.Y: return v.y;
            case WorldAxis.Z: return v.z;
            default: return v.x;
        }
    }

    private void ApplyKnobRotation()
    {
        Vector3 axis;
        switch (knobRotationAxis)
        {
            case KnobAxis.X: axis = Vector3.right; break;
            case KnobAxis.Y: axis = Vector3.up; break;
            case KnobAxis.Z: axis = Vector3.forward; break;
            default: axis = Vector3.up; break;
        }

        transform.localRotation = _initialLocalRotation * Quaternion.AngleAxis(_currentAngle, axis);
    }
}
