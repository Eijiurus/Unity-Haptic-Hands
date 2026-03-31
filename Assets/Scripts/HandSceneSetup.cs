using UnityEngine;

/// <summary>
/// 挂载在主摄像机上，运行时自动计算手部网格的实际位置并对准。
/// 通过 Renderer.bounds 动态确定观察点，保持场景可见。
/// </summary>
public class HandSceneSetup : MonoBehaviour
{
    [Tooltip("跟踪目标（LeftHand 的 Transform）")]
    public Transform target;

    [Tooltip("观察距离倍数（越大摄像机越远，能看到更多场景）")]
    [SerializeField] float viewDistanceMultiplier = 4f;

    [Tooltip("观察方向（从目标中心到摄像机的方向向量）")]
    [SerializeField] Vector3 viewDirection = new Vector3(0f, 0.6f, -1f);

    Vector3 _boundsOffset;
    float _viewDistance = 1f;
    bool _ready;

    void Start()
    {
        if (target == null)
        {
            var hand = GameObject.Find("LeftHand");
            if (hand != null) target = hand.transform;
        }

        if (target == null)
        {
            Debug.LogError("[HandSceneSetup] LeftHand 未找到！请先运行 Tools → Setup Rigged Hand Prefab");
            return;
        }

        foreach (var smr in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.updateWhenOffscreen = true;
        }

        ComputeViewParameters();
        ApplyCamera();
        _ready = true;
    }

    void LateUpdate()
    {
        if (!_ready) return;
        ApplyCamera();
    }

    void ComputeViewParameters()
    {
        Bounds totalBounds = new Bounds(target.position, Vector3.zero);
        bool first = true;

        foreach (var r in target.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (first) { totalBounds = r.bounds; first = false; }
            else totalBounds.Encapsulate(r.bounds);
        }

        _boundsOffset = totalBounds.center - target.position;
        _viewDistance = Mathf.Max(totalBounds.extents.magnitude * viewDistanceMultiplier, 0.5f);

        Debug.Log($"[HandSceneSetup] 模型中心: {totalBounds.center}, " +
                  $"大小: {totalBounds.size}, 距离: {_viewDistance:F3}");
    }

    void ApplyCamera()
    {
        Vector3 center = target.position + _boundsOffset;
        transform.position = center + viewDirection.normalized * _viewDistance;
        transform.LookAt(center);
    }
}
