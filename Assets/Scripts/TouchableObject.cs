using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 挂在可触摸/可拿捏的物体上（需非 Trigger 的 Collider + Rigidbody）。
/// 手指尖 Trigger 进入时高亮，可在 Inspector 绑定 UnityEvent 做触觉串口等扩展。
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class TouchableObject : MonoBehaviour
{
    [SerializeField] Color touchHighlightColor = new Color(0.4f, 0.85f, 1f, 1f);
    [SerializeField] Color grabbedColor = new Color(1f, 0.75f, 0.2f, 1f);

    [Tooltip("取消勾选：桌面、桌腿等只可触摸高亮，不可被捏取带走")]
    [SerializeField] bool allowPinchGrab = true;

    public UnityEvent onTouchEnter;
    public UnityEvent onTouchExit;
    public UnityEvent onGrabbed;
    public UnityEvent onReleased;

    Rigidbody _rb;
    Renderer _rend;
    Color _originalColor;
    bool _hasOriginal;
    int _touchRefCount;
    bool _grabbed;

    public bool AllowPinchGrab => allowPinchGrab;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (!allowPinchGrab)
        {
            _rb.isKinematic = true;
            _rb.useGravity = false;
        }
        _rend = GetComponentInChildren<Renderer>();
        if (_rend != null)
        {
            _originalColor = _rend.material.color;
            _hasOriginal = true;
        }
    }

    public void NotifyFingerTouchEnter()
    {
        if (_grabbed) return;
        _touchRefCount++;
        if (_touchRefCount == 1)
        {
            ApplyColor(touchHighlightColor);
            onTouchEnter?.Invoke();
        }
    }

    public void NotifyFingerTouchExit()
    {
        if (_grabbed) return;
        _touchRefCount = Mathf.Max(0, _touchRefCount - 1);
        if (_touchRefCount == 0)
        {
            ApplyColor(_originalColor);
            onTouchExit?.Invoke();
        }
    }

    public void Grab(Transform attachPoint)
    {
        if (!allowPinchGrab) return;
        _grabbed = true;
        _touchRefCount = 0;
        _rb.isKinematic = true;
        transform.SetParent(attachPoint, true);
        ApplyColor(grabbedColor);
        onGrabbed?.Invoke();
    }

    public void Release()
    {
        _grabbed = false;
        transform.SetParent(null, true);
        _rb.isKinematic = false;
        if (_hasOriginal)
            ApplyColor(_originalColor);
        onReleased?.Invoke();
    }

    void ApplyColor(Color c)
    {
        if (_rend == null) return;
        _rend.material.color = c;
    }

    public bool IsGrabbed => _grabbed;
}
