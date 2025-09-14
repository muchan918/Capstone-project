using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class SwitchDevice : MonoBehaviour
{
    [Header("���� ����(����θ� �ڵ� ���ε�)")]
    public List<Light> targets = new List<Light>();

    [Header("�ڵ� ���ε� �ɼ�")]
    [Tooltip("�ڵ����� �� ��Ʈ�� ã�� ���� �̸��� �׷�(��: light_01)���� Light ����")]
    public bool autoBind = true;

    [Tooltip("�� ��Ʈ �ĺ� �̸���(�� �ֻ��� Ȥ�� ���� ����)")]
    public string[] roomRootNames = { "lab", "library", "classroom", "hallway" };

    [Tooltip("�׷� Ű. ���� �� ������Ʈ �̸� ���(��: light_01)")]
    public string groupKeyOverride = "";

    [Tooltip("�׷��� �� ã���� �ݰ����� Light ����")]
    public bool searchByRadiusIfNoGroup = true;
    public float searchRadius = 8f;
    public LayerMask lightLayerMask = ~0;

    [Header("����")]
    public bool startOn = false;
    public bool IsOn { get; private set; }
    void Start() // SwitchDevice.Awake ���� ����
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

    // ===== �ܺ� ���� API =====
    public void SwitchOn() => SetState(true);
    public void SwitchOff() => SetState(false);
    public void Toggle() => SetState(!IsOn);

    void SetState(bool on)
    {
        IsOn = on;
        foreach (var l in targets)
            if (l) l.enabled = on;
#if UNITY_EDITOR
        // ����� ����
        // Debug.Log($"[SwitchDevice:{name}] {(on?"ON":"OFF")} - lights: {targets.Count}");
#endif
    }

    // ===== �ڵ� ���ε� =====
    void AutoBind()
    {
        targets.Clear();

        // 1) ���󿡼� �� ��Ʈ ã��
        Transform roomRoot = FindAncestorRoomRoot(transform);
        // 2) ���ٸ� �� �ֻ������� �˻�
        if (roomRoot == null) roomRoot = FindSceneRoomRoot();

        string key = string.IsNullOrEmpty(groupKeyOverride) ? name : groupKeyOverride;

        // 3) �� ��Ʈ �Ʒ����� ���� �̸��� �׷� ã��
        if (roomRoot != null)
        {
            Transform group = FindChildByExactName(roomRoot, key);
            if (group != null)
            {
                targets.AddRange(group.GetComponentsInChildren<Light>(true));
                if (targets.Count > 0) return;
            }
        }

        // 4) ����: �ݰ� �� Light �ڵ� ����(���� �� ����)
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
