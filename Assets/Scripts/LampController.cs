using UnityEngine;

/// <summary>
/// 控制一盏灯的开关与亮度（点光源 + 灯泡 Mesh 的 Emission）。
/// </summary>
public class LampController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("作为灯泡的点光源")]
    public Light pointLight;

    [Tooltip("可见的灯泡 Mesh（Sphere 等）")]
    public Renderer bulbRenderer;

    [Header("Emission (bulb material)")]
    [Tooltip("开启时自发光的基础颜色")]
    public Color onEmissionColor = Color.white;

    [Tooltip("与 onEmissionColor 相乘的自发光强度系数")]
    public float onEmissionIntensity = 2f;

    [Header("Light")]
    [Tooltip("全开时点光源 intensity（再乘以 _brightness）")]
    public float maxLightIntensity = 1.5f;

    bool _isOn;
    float _brightness = 1f;

    void Start()
    {
        _brightness = 1f;
        _isOn = false;
        UpdateVisuals();
    }

    /// <summary>切换开/关状态并刷新显示。</summary>
    public void Toggle()
    {
        _isOn = !_isOn;
        UpdateVisuals();
    }

    /// <summary>设置开/关并刷新显示。</summary>
    public void SetOn(bool on)
    {
        _isOn = on;
        UpdateVisuals();
    }

    /// <summary>设置亮度（0~1）。仅在灯为开启状态时影响光强与自发光；亮度值会始终被保存。</summary>
    public void SetBrightness(float value)
    {
        _brightness = Mathf.Clamp01(value);
        UpdateVisuals();
    }

    void UpdateVisuals()
    {
        if (pointLight != null)
            pointLight.intensity = _isOn ? maxLightIntensity * _brightness : 0f;

        if (bulbRenderer == null)
            return;

        Material mat = bulbRenderer.material;
        if (_isOn)
        {
            mat.EnableKeyword("_EMISSION");
            Color emission = onEmissionColor * (onEmissionIntensity * _brightness);
            mat.SetColor("_EmissionColor", emission);
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", Color.black);
        }
    }
}
