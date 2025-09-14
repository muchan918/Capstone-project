// Assets/Scripts/RobotPick.cs
using System;
using System.Collections;
using UnityEngine;

public class RobotPick : MonoBehaviour
{
    [Header("Refs & Tunings")]
    public Transform handTransform;
    public float rotateSpeedDegPerSec = 360f;
    public float facingAngleThreshold = 5f;
    public float pickAttachDelay = 2.2f;
    public bool yawOnly = true;

    [Header("Place options")]
    [Tooltip("표면 위 배치 시 여유 거리(m)")]
    public float placeMargin = 0.02f;
    [Tooltip("배치 후 Yaw를 대상의 Yaw에 맞출지")]
    public bool alignYawToTarget = true;
    [Tooltip("상판 높이를 정확히 잡기 위해 위에서 아래로 쏘는 레이 높이")]
    public float surfaceRayHeight = 1.5f;

    Animator anim;
    GameObject heldObject;

    public bool IsHolding => heldObject != null;
    public event Action OnAttach;   // 집게에 붙은 순간
    public event Action OnPlace;    // 자리에 내려놓은 순간
    public GameObject LastPlacedObject { get; private set; }

    void Start() { anim = GetComponentInChildren<Animator>(); }

    // ───────────────────── Pick ─────────────────────
    public void Pick(GameObject target)
    {
        if (IsHolding || anim == null || target == null) return;
        StartCoroutine(FaceThenPick(target));
    }

    // ─────────────────── Place (on) ──────────────────
    // 요청: place 시 로봇이 대상 방향을 바라보고, 최종 좌표/회전 계산 후 내려놓기
    public bool PlaceOn(GameObject target)
    {
        if (!IsHolding || target == null) return false;
        StartCoroutine(CoFaceRobotAndPlace(target));  // 비동기 수행
        return true;
    }

    // 로봇 회전 → 최종 위치/자세 계산 → 내려놓기
    IEnumerator CoFaceRobotAndPlace(GameObject target)
    {
        // 1) 로봇을 타깃 방향으로 회전
        yield return RotateTowardsTarget(target.transform.position);

        // 2) 최종 배치 위치/자세 계산
        if (!TryGetWorldBounds(target, out var tb)) yield break;

        Bounds hb;
        if (!TryGetWorldBounds(heldObject, out hb))
            hb = new Bounds(heldObject.transform.position, heldObject.transform.lossyScale);

        Transform tt = target.transform;

        float HalfAlong(Bounds b, Vector3 dir)
        {
            dir = dir.normalized; var e = b.extents;
            return Mathf.Abs(dir.x) * e.x + Mathf.Abs(dir.y) * e.y + Mathf.Abs(dir.z) * e.z;
        }

        float halfWidth = HalfAlong(tb, tt.right);
        float halfDepth = HalfAlong(tb, tt.forward);
        float heldHalfUp = HalfAlong(hb, Vector3.up);

        float insetX = HalfAlong(hb, tt.right) + placeMargin;
        float insetZ = HalfAlong(hb, tt.forward) + placeMargin;

        Vector3 toRobot = transform.position - tb.center;
        float sideLocal = Vector3.Dot(toRobot, tt.right);
        float frontLocal = Vector3.Dot(toRobot, tt.forward);

        float xLocal = Mathf.Clamp(sideLocal, -halfWidth + insetX, halfWidth - insetX);
        float zLocal = (frontLocal >= 0f ? (halfDepth - insetZ) : -(halfDepth - insetZ));

        Vector3 topXZ = new Vector3(tb.center.x, tb.max.y, tb.center.z)
                        + tt.right * xLocal + tt.forward * zLocal;

        // 상판 실제 높이 보정(가능할 때만)
        float surfaceY = tb.max.y;
        if (Physics.Raycast(topXZ + Vector3.up * surfaceRayHeight, Vector3.down,
            out RaycastHit hit, surfaceRayHeight * 2f, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore))
        {
            if (IsDescendantOf(hit.collider.transform, tt))
                surfaceY = hit.point.y;
        }

        Vector3 pivotToBounds = hb.center - heldObject.transform.position;
        Vector3 desiredBoundsCenter = new Vector3(
            topXZ.x,
            surfaceY + heldHalfUp + placeMargin,
            topXZ.z
        );
        Vector3 finalPos = desiredBoundsCenter - pivotToBounds;

        Quaternion finalRot = heldObject.transform.rotation;
        if (alignYawToTarget)
        {
            Vector3 f = tt.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
            finalRot = Quaternion.LookRotation(f.normalized, Vector3.up);
        }

        // 3) 실제 내려놓기 (타깃의 room 기준으로 reparent까지)
        DoPlaceAt(finalPos, finalRot, tt);
    }

    // ───────────── 내부 유틸/로직 ─────────────
    static bool IsDescendantOf(Transform t, Transform ancestor)
    {
        while (t != null) { if (t == ancestor) return true; t = t.parent; }
        return false;
    }

    IEnumerator FaceThenPick(GameObject target)
    {
        yield return RotateTowardsTarget(target.transform.position);
        if (anim) anim.SetTrigger("IdleToPick");
        yield return new WaitForSeconds(pickAttachDelay);
        AttachToHand(target);
    }

    IEnumerator RotateTowardsTarget(Vector3 targetPos)
    {
        while (true)
        {
            Vector3 dir = targetPos - transform.position;
            if (yawOnly) dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) yield break;

            Quaternion targetRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            if (Quaternion.Angle(transform.rotation, targetRot) <= facingAngleThreshold) break;

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, targetRot, rotateSpeedDegPerSec * Time.deltaTime);
            yield return null;
        }
    }

    void AttachToHand(GameObject target)
    {
        heldObject = target;

        if (heldObject.TryGetComponent(out Rigidbody rb))
        {
            rb.isKinematic = true;
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        if (heldObject.TryGetComponent<Collider>(out var col))
            col.enabled = false;

        heldObject.transform.SetParent(handTransform, true);
        heldObject.transform.localPosition = Vector3.zero;
        heldObject.transform.localRotation = Quaternion.identity;

        if (anim) anim.SetTrigger("PickToPicking");
        OnAttach?.Invoke();
    }

    // ───────────────────── 놓기 + 계층 정리 ─────────────────────
    void DoPlaceAt(Vector3 worldPos, Quaternion worldRot, Transform targetTransformForRoom)
    {
        var obj = heldObject;
        if (obj == null) return;

        // 손에서 분리 후 최종 위치/자세 적용
        obj.transform.SetParent(null, true);
        obj.transform.SetPositionAndRotation(worldPos, worldRot);

        // 물리/콜라이더 복구
        if (obj.TryGetComponent(out Rigidbody rb)) rb.isKinematic = false;
        if (obj.TryGetComponent<Collider>(out var c)) c.enabled = true;

        // ▼ 타깃이 속한 room의 object 컨테이너로 reparent (월드 좌표 유지)
        var roomRoot = FindRoomRoot(targetTransformForRoom);
        if (roomRoot != null)
        {
            var objectNode = GetOrCreateObjectContainer(roomRoot);
            obj.transform.SetParent(objectNode, true);   // worldPositionStays = true
        }

        LastPlacedObject = obj;
        heldObject = null;
        OnPlace?.Invoke();
    }

    // ────────────── Room/Hierarchy 유틸 ──────────────
    static readonly string[] RoomRoots = { "lab", "classroom", "hallway", "library" };

    static Transform FindRoomRoot(Transform t)
    {
        while (t != null)
        {
            var n = t.name.ToLowerInvariant();
            foreach (var r in RoomRoots)
            {
                if (n == r || n.StartsWith(r + "_") || n.StartsWith(r + " ") || n.StartsWith(r + "("))
                    return t;
            }
            t = t.parent;
        }
        return null;
    }

    static Transform GetDirectChildByNameVariant(Transform parent, string target)
    {
        if (parent == null) return null;
        string t = target.ToLowerInvariant();
        foreach (Transform c in parent)
        {
            var n = c.name.ToLowerInvariant();
            if (n == t || n.StartsWith(t + "_") || n.StartsWith(t + " ") || n.StartsWith(t + "("))
                return c;
        }
        return null;
    }

    static Transform GetOrCreateObjectContainer(Transform roomRoot)
    {
        var obj = GetDirectChildByNameVariant(roomRoot, "object");
        if (obj != null) return obj;
        var go = new GameObject("object");
        go.transform.SetParent(roomRoot, false);
        return go.transform;
    }

    // ────────────── 바운딩 계산 ──────────────
    static bool TryGetWorldBounds(GameObject go, out Bounds b)
    {
        b = new Bounds();
        if (go == null) return false;

        var rends = go.GetComponentsInChildren<Renderer>(true);
        var cols = go.GetComponentsInChildren<Collider>(true);

        bool has = false;

        if (rends.Length > 0)
        {
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            has = true;
        }

        if (cols.Length > 0)
        {
            if (!has) { b = cols[0].bounds; has = true; }
            for (int i = has ? 0 : 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
        }

        return has;
    }
}
