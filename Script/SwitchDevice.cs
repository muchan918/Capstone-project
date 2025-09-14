using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class SwitchDevice : MonoBehaviour
{
    [Header("수동 지정(비워두면 자동 바인딩)")]
    public List<Light> targets = new List<Light>();

    [Header("자동 바인딩 옵션")]
    [Tooltip("자동으로 방 루트를 찾아 같은 이름의 그룹(예: light_01)에서 Light 수집")]
    public bool autoBind = true;

    [Tooltip("방 루트 후보 이름들(씬 최상위 혹은 조상에 존재)")]
    public string[] roomRootNames = { "lab", "library", "classroom", "hallway" };

    [Tooltip("그룹 키. 비우면 이 오브젝트 이름 사용(예: light_01)")]
    public string groupKeyOverride = "";

    [Tooltip("그룹을 못 찾으면 반경으로 Light 수집")]
    public bool searchByRadiusIfNoGroup = true;
    public float searchRadius = 8f;
    public LayerMask lightLayerMask = ~0;

    [Header("상태")]
    public bool startOn = false;
    public bool IsOn { get; private set; }
    void Start() // SwitchDevice.Awake 이후 실행
    {
        foreach (var sw in FindObjectsOfType<SwitchDevice>(true))
            sw.SwitchOn();
    }
    void Awake()
    {
        if (targets.Count == 0 && autoBind)
            AutoBind();

        SetState(startOn);
    }

    // ===== 외부 제어 API =====
    public void SwitchOn() => SetState(true);
    public void SwitchOff() => SetState(false);
    public void Toggle() => SetState(!IsOn);

    void SetState(bool on)
    {
        IsOn = on;
        foreach (var l in targets)
            if (l) l.enabled = on;
#if UNITY_EDITOR
        // 디버깅 도움
        // Debug.Log($"[SwitchDevice:{name}] {(on?"ON":"OFF")} - lights: {targets.Count}");
#endif
    }

    // ===== 자동 바인딩 =====
    void AutoBind()
    {
        targets.Clear();

        // 1) 조상에서 방 루트 찾기
        Transform roomRoot = FindAncestorRoomRoot(transform);
        // 2) 없다면 씬 최상위에서 검색
        if (roomRoot == null) roomRoot = FindSceneRoomRoot();

        string key = string.IsNullOrEmpty(groupKeyOverride) ? name : groupKeyOverride;

        // 3) 방 루트 아래에서 같은 이름의 그룹 찾기
        if (roomRoot != null)
        {
            Transform group = FindChildByExactName(roomRoot, key);
            if (group != null)
            {
                targets.AddRange(group.GetComponentsInChildren<Light>(true));
                if (targets.Count > 0) return;
            }
        }

        // 4) 폴백: 반경 내 Light 자동 수집(같은 방 내만)
        if (searchByRadiusIfNoGroup)
        {
            foreach (var l in GameObject.FindObjectsOfType<Light>(true))
            {
                if (((1 << l.gameObject.layer) & lightLayerMask) == 0) continue;
                if (roomRoot != null && !IsDescendantOf(l.transform, roomRoot)) continue;

                if ((l.transform.position - transform.position).sqrMagnitude <= searchRadius * searchRadius)
                    targets.Add(l);
            }
        }
    }

    Transform FindAncestorRoomRoot(Transform t)
    {
        while (t != null)
        {
            string n = t.name.ToLowerInvariant();
            foreach (var room in roomRootNames)
                if (n == room || n.StartsWith(room + " ") || n.StartsWith(room + "_") || n.StartsWith(room + "("))
                    return t;
            t = t.parent;
        }
        return null;
    }

    Transform FindSceneRoomRoot()
    {
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (var go in roots)
        {
            string n = go.name.ToLowerInvariant();
            foreach (var room in roomRootNames)
                if (n == room || n.StartsWith(room + " ") || n.StartsWith(room + "_") || n.StartsWith(room + "("))
                    return go.transform;
        }
        return null;
    }

    Transform FindChildByExactName(Transform root, string key)
    {
        key = key.ToLowerInvariant();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.name.ToLowerInvariant() == key) return t;
        return null;
    }

    static bool IsDescendantOf(Transform t, Transform ancestor)
    {
        while (t != null) { if (t == ancestor) return true; t = t.parent; }
        return false;
    }
}
