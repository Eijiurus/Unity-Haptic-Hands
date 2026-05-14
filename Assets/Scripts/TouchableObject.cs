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

    public UnityEvent onTouchEnter; // 触摸进入事件
    public UnityEvent onTouchExit; // 触摸退出事件
    public UnityEvent onGrabbed; // 抓取事件
    public UnityEvent onReleased; // 释放事件

    Rigidbody _rb; // 刚体
    Renderer _rend; // 渲染器
    Color _originalColor; // 原始颜色
    bool _hasOriginal; // 是否有原始颜色
    int _touchRefCount; // 触摸引用计数
    bool _grabbed; // 是否被抓取
    public bool AllowPinchGrab => allowPinchGrab; // 获取allowPinchGrab

    void Awake() // Awake是唤醒
    {
        _rb = GetComponent<Rigidbody>(); // 获取刚体
        if (!allowPinchGrab) // 如果allowPinchGrab为false，则设置为true
        {
            _rb.isKinematic = true; // 设置为true
            _rb.useGravity = false; // 设置为false
        }
        _rend = GetComponentInChildren<Renderer>(); // 获取渲染器
        if (_rend != null) // 如果rend不为空，则设置为true
        {
            _originalColor = _rend.material.color; // 设置为rend的material的颜色
            _hasOriginal = true; // 设置为true
        }
    }

    public void NotifyFingerTouchEnter() // 通知手指触摸进入
    {
        if (_grabbed) return; // 如果grabbed为true，则返回
        _touchRefCount++; // 设置为touchRefCount+1
        if (_touchRefCount == 1) // 如果touchRefCount为1，则设置为true
        {
            ApplyColor(touchHighlightColor); // 设置为touchHighlightColor
            onTouchEnter?.Invoke(); // 调用onTouchEnter
        }
    } // 通知手指触摸退出   

    public void NotifyFingerTouchExit() // 通知手指触摸退出
    {
        if (_grabbed) return; // 如果grabbed为true，则返回
        _touchRefCount = Mathf.Max(0, _touchRefCount - 1); // 设置为touchRefCount-1
        if (_touchRefCount == 0)
        {
            ApplyColor(_originalColor); // 设置为originalColor
            onTouchExit?.Invoke();
        } // 通知手指触摸退出
    }

    /// <returns>是否实际进入抓取状态（不可捏取的物体返回 false）</returns>
    public bool Grab(Transform attachPoint) // 抓取
    {
        if (!allowPinchGrab) return false; // 如果allowPinchGrab为false，则返回false
        _grabbed = true;
        _touchRefCount = 0; // 设置为0
        _rb.isKinematic = true;
        transform.SetParent(attachPoint, true); // 设置为attachPoint
        ApplyColor(grabbedColor); // 设置为grabbedColor
        onGrabbed?.Invoke(); // 调用onGrabbed
        return true;
    }

    public void Release() // 释放
    {
        if (!_grabbed) return; // 如果grabbed为false，则返回
        _grabbed = false;
        transform.SetParent(null, true); // 设置为null
        _rb.isKinematic = false;
        if (_hasOriginal) // 如果hasOriginal为true，则设置为true
            ApplyColor(_originalColor); // 设置为originalColor
        onReleased?.Invoke(); // 调用onReleased 
    }

    void ApplyColor(Color c)
    {
        if (_rend == null) return; // 如果rend为空，则返回
        _rend.material.color = c; // 设置为c
    }

    public bool IsGrabbed => _grabbed; // 获取grabbed
}
