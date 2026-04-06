using UnityEngine;

/// <summary>
/// 挂在指尖球体 Trigger 上，把触发事件交给 HandInteractionRig。
/// </summary>
public class FingerTipRelay : MonoBehaviour
{
    public HandInteractionRig Rig;

    void OnTriggerEnter(Collider other)
    {
        if (Rig != null)
            Rig.OnTipTriggerEnter(other);
    }

    void OnTriggerExit(Collider other)
    {
        if (Rig != null)
            Rig.OnTipTriggerExit(other);
    }
}
