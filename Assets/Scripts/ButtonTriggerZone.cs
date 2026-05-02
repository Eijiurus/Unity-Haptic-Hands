using UnityEngine;

/// <summary>
/// 挂在按钮的 Trigger 碰撞体上，将指尖进入/离开转发给 <see cref="PressableButton"/>。
/// </summary>
[RequireComponent(typeof(Collider))]
public class ButtonTriggerZone : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("接收按压逻辑的按钮（一般在同级的 Visual 上）")]
    [SerializeField] PressableButton targetButton;

    [Header("Filter")]
    [Tooltip("视为指尖的 Collider 的 Tag")]
    [SerializeField] string fingerTipTag = "FingerTip";

    public PressableButton TargetButton
    {
        get => targetButton;
        set => targetButton = value;
    }

    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (targetButton == null || !other.CompareTag(fingerTipTag))
            return;
        targetButton.OnFingerEnter();
    }

    void OnTriggerExit(Collider other)
    {
        if (targetButton == null || !other.CompareTag(fingerTipTag))
            return;
        targetButton.OnFingerExit();
    }
}
