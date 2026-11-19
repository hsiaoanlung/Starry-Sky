using UnityEngine;

/// <summary>
/// 潮汐鎖定（只影響畫面，不影響物理）：讓衛星永遠用同一面朝向它的行星，就像月球永遠用同一面對著地球。
/// 每幀直接把物件轉到「貼圖 lon 0 那一面朝向 parent」，所以不管播放速度多快都不會跑掉（BodySpin 有轉速上限，做不到）。
/// 貼圖的哪個方向是 lon 0，是在 Start 時從網格的 UV 找出來的（找 UV = (0.5, 0.5) 的頂點），不必事先知道 Unity 球體的 UV 怎麼排。
/// 由 SolarSystemBuilder 依 CSV rotationPeriod 欄填 lock 時掛上。說明見 Assets/Docs/衛星Prefab說明.md。
/// </summary>
[DefaultExecutionOrder(50)]   // 在 NBodySimulator 更新位置之後
public class TidalLock : MonoBehaviour
{
    [System.NonSerialized] public CelestialBody body;
    [System.NonSerialized] public SolarSystemBuilder builder;

    [Tooltip("額外的轉角（度，繞自轉軸）。0 = 貼圖中央（lon 0）正對行星")]
    public float offsetDegrees = 0f;

    Quaternion meshToFace = Quaternion.identity;   // 把「lon 0 方向」轉到物件 +z 的修正

    void Start()
    {
        // 找出網格上 UV 最接近 (0.5, 0.5)（貼圖中央、赤道、lon 0）和 (0.25, 0.5)（lon +90，前導面）的頂點。
        // 衛星貼圖存檔時左右翻轉過（Unity 球體的 u 從北極看是順時針增加），所以 lon +90 在 u = 0.25
        var mf = GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) return;
        Vector3 face = LocalDirectionOfUV(mf, new Vector2(0.5f, 0.5f));
        Vector3 lead = LocalDirectionOfUV(mf, new Vector2(0.25f, 0.5f));
        face.y = 0f; lead.y = 0f;
        if (face.sqrMagnitude < 1e-6f) return;
        meshToFace = Quaternion.Inverse(Quaternion.LookRotation(face.normalized, Vector3.up));

        // 貼圖的 lon +90 應該在公轉前進的方向。LateUpdate 讓 +z 朝向行星時，+x = Cross(up, +z) 剛好是逆時針公轉的前進方向，
        // 所以 lon +90 轉完後應該在 +x。如果網格的 UV 是鏡像的，兩個半球會左右顛倒，這裡只能提醒，不能用旋轉修正
        Vector3 leadAfter = meshToFace * lead;
        if (leadAfter.x < -0.1f)
            Debug.LogWarning($"[TidalLock] {name} 的網格 UV 方向和貼圖的慣例相反：前導面 / 後隨面會左右顛倒（moon_texture_gen.py 存檔時的左右翻轉要拿掉）");
    }

    /// <summary>網格上 UV 最接近 target 的頂點，換算成這個物件（根物件）的區域座標方向。</summary>
    Vector3 LocalDirectionOfUV(MeshFilter mf, Vector2 target)
    {
        var mesh = mf.sharedMesh;
        var uv = mesh.uv;
        var verts = mesh.vertices;
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < uv.Length; i++)
        {
            float d = (uv[i] - target).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        Vector3 world = mf.transform.TransformPoint(verts[best]);
        return transform.InverseTransformPoint(world);
    }

    void LateUpdate()
    {
        if (body == null || body.parent == null) return;
        if (builder != null && !builder.addSpin) return;   // 和自轉共用開關

        Vector3 toParent = body.parent.transform.position - transform.position;
        toParent.y = 0f;                                   // 所有軌道都在 XZ 平面，自轉軸是 y
        if (toParent.sqrMagnitude < 1e-12f) return;

        // 先把 lon 0 轉到 +z，再把 +z 轉向行星
        transform.rotation = Quaternion.LookRotation(toParent, Vector3.up)
                           * Quaternion.AngleAxis(offsetDegrees, Vector3.up)
                           * meshToFace;
    }
}
