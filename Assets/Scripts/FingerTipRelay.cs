using UnityEngine;

/// <summary>
/// 挂在指尖球体 Trigger 上，把触发事件交给 HandInteractionRig。
/// </summary>
public class FingerTipRelay : MonoBehaviour
{
    public HandInteractionRig Rig; // 手交互Rig

    void OnTriggerEnter(Collider other) // 触发进入
    {
        if (Rig != null) // 如果Rig不为空，则调用OnTipTriggerEnter
            Rig.OnTipTriggerEnter(other); // 调用OnTipTriggerEnter
    } // 触发进入

    void OnTriggerExit(Collider other) // 触发退出
    {
        if (Rig != null) // 如果Rig不为空，则调用OnTipTriggerExit
            Rig.OnTipTriggerExit(other); // 调用OnTipTriggerExit
    } // 触发退出
} // 指尖触发器
