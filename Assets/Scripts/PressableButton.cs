using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 可按压按钮：指尖进入时下沉并变色，离开时弹回。
/// </summary>
public class PressableButton : MonoBehaviour
{
    [Header("Visual")]
    [Tooltip("用于改色的 Renderer（通常为按钮立方体）")]
    public Renderer buttonRenderer;

    [Tooltip("未按下时的颜色")]
    public Color idleColor = new Color(0.5f, 0.5f, 0.5f);

    [Tooltip("按下时的颜色")]
    public Color pressedColor = Color.green;

    [Header("Motion")]
    [Tooltip("沿本地 -Y 方向下沉的距离（米）")]
    public float pressDepth = 0.02f;

    [Tooltip("位置插值速度（与 Time.deltaTime 相乘后作为 Lerp 系数）")]
    public float pressSpeed = 10f;

    [Header("Events")]
    public UnityEvent onPressed;
    public UnityEvent onReleased;

    bool _isPressed;
    int _fingersInside;
    Vector3 _idleLocalPos;
    Vector3 _pressedLocalPos;
    MaterialPropertyBlock _block;

    void Start()
    {
        _idleLocalPos = transform.localPosition;
        _pressedLocalPos = _idleLocalPos + Vector3.down * pressDepth;
        _block = new MaterialPropertyBlock();
        ApplyColor();
    }

    void Update()
    {
        Vector3 target = _fingersInside > 0 ? _pressedLocalPos : _idleLocalPos;
        transform.localPosition = Vector3.Lerp(
            transform.localPosition,
            target,
            Mathf.Clamp01(Time.deltaTime * pressSpeed));
    }

    /// <summary>由 ButtonTriggerZone 在指尖进入触发体时调用。</summary>
    public void OnFingerEnter()
    {
        _fingersInside++;
        if (_fingersInside != 1)
            return;
        _isPressed = true;
        onPressed?.Invoke();
        ApplyColor();
    }

    /// <summary>由 ButtonTriggerZone 在指尖离开触发体时调用。</summary>
    public void OnFingerExit()
    {
        _fingersInside = Mathf.Max(0, _fingersInside - 1);
        if (_fingersInside != 0)
            return;
        _isPressed = false;
        onReleased?.Invoke();
        ApplyColor();
    }

    void ApplyColor()
    {
        if (buttonRenderer == null)
            return;
        Color c = _isPressed ? pressedColor : idleColor;
        if (buttonRenderer.sharedMaterial != null && buttonRenderer.sharedMaterial.HasProperty("_BaseColor"))
            _block.SetColor("_BaseColor", c);
        if (buttonRenderer.sharedMaterial != null && buttonRenderer.sharedMaterial.HasProperty("_Color"))
            _block.SetColor("_Color", c);
        buttonRenderer.SetPropertyBlock(_block);
    }
}
