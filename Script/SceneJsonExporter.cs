// Assets/Editor/SceneJsonExporter.cs
#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[Serializable] public struct Vec3 { public float x, y, z; public Vec3(Vector3 v) { x = v.x; y = v.y; z = v.z; } }

[Serializable]
public struct Bounds3
{
    public float x_min, x_max, y_min, y_max, z_min, z_max;
    public Bounds3(Bounds b)
    {
        x_min = b.min.x; x_max = b.max.x;
        y_min = b.min.y; y_max = b.max.y;
        z_min = b.min.z; z_max = b.max.z;
    }
}

[Serializable]
public class RoomRecord
{
    public string name;     // lab / classroom / hallway / library
    public Bounds3 bounds;  // world-space AABB
}

[Serializable]
public class ObjectRecord
{
    public string name;
    public Vec3 position;       // world or local
    public Vec3 localScale;     // Transform.localScale
    public Vec3 worldScale;     // world scale (matrix-based)
    public string current_room; // lab / classroom / hallway / library
    public string state;        // "open" | "close" | "on" | "off" | "static" | null
}

// 관계 레코드 (subject가 target '위에(on)' 있는 경우 등)
[Serializable]
public class RelationRecord
{
    public string subject;    // 예: "book_01"
    public string predicate;  // 예: "on"
    public string target;     // 예: "desk_01"
}

[Serializable]
public class AgentRecord
{
    public string name;
    public Vec3 position;
    public Vec3 forward;
    public Vec3 euler;
    public string current_room;
}

[Serializable]
public class ExportPayload
{
    public string version = "1.6-rooms+objects+agent+relations";
    public string scene;
    public List<RoomRecord> rooms = new List<RoomRecord>();
    public List<ObjectRecord> objects = new List<ObjectRecord>();
    public List<RelationRecord> relation_information = new List<RelationRecord>(); // ← NEW
    public AgentRecord agent;
}

public static class SceneJsonExporter
{
    private const bool IncludeInactive = true;
    private const bool UseLocalPosition = false;

    // ‘on’ 판정 파라미터
    private const float HeightEpsilon = 0.06f;      // 바닥-윗면 높이 오차 허용 (m)
    private const float MinXZOverlapRatio = 0.2f;   // XZ 교집합 면적 / subject XZ 면적

    private static readonly string[] RoomRootsAll = { "lab", "classroom", "hallway", "library" };
    private static readonly string[] RoomsForObjects = { "lab", "classroom", "library" }; // 필요 시 "hallway" 추가

    private const string LastPathKey = "SceneJsonExporter.LastExportPath";
    private static string _lastPath;

    [InitializeOnLoadMethod] private static void LoadLastPath() => _lastPath = EditorPrefs.GetString(LastPathKey, string.Empty);
    private static void SaveLastPath() => EditorPrefs.SetString(LastPathKey, _lastPath ?? string.Empty);

    [MenuItem("Tools/Export/Rooms + Objects + Relations")]
    public static void Export()
    {
        var payload = BuildPayload();
        var json = JsonUtility.ToJson(payload, true);

        var scene = SceneManager.GetActiveScene();
        var defaultName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{scene.name}_rooms_objects.json";
        var path = EditorUtility.SaveFilePanel("Save JSON", Application.dataPath, defaultName, "json");
        if (string.IsNullOrEmpty(path)) return;

        File.WriteAllText(path, json);
        _lastPath = path; SaveLastPath();
        Debug.Log($"Exported rooms={payload.rooms.Count}, objects={payload.objects.Count}, relations={payload.relation_information.Count}, agent={(payload.agent != null ? "1" : "0")} -> {path}");
        EditorUtility.RevealInFinder(path);
    }

    public static void ExportOrUpdate(bool askIfPathNotSet = true)
    {
        var payload = BuildPayload();
        var json = JsonUtility.ToJson(payload, true);

        if (string.IsNullOrEmpty(_lastPath) || (askIfPathNotSet && !File.Exists(_lastPath)))
        {
            var scene = SceneManager.GetActiveScene();
            var defaultName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{scene.name}_rooms_objects.json";
            var path = EditorUtility.SaveFilePanel("Choose JSON to write repeatedly", Application.dataPath, defaultName, "json");
            if (string.IsNullOrEmpty(path)) return;
            _lastPath = path; SaveLastPath();
        }

        try
        {
            File.WriteAllText(_lastPath, json);
            Debug.Log($"[SceneJsonExporter] Updated JSON -> {_lastPath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SceneJsonExporter] Write failed: {e.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 스냅샷 생성
    // ─────────────────────────────────────────────────────────────
    public static ExportPayload BuildPayload()
    {
        var scene = SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var payload = new ExportPayload { scene = scene.name };

        // 1) rooms (space AABB)
        foreach (var root in roots)
        {
            if (!IsNameMatchAny(root.name, RoomRootsAll)) continue;
            var roomName = CanonicalRoomName(root.name);
            var spaceNode = GetDirectChildByNameVariant(root.transform, "space");
            Bounds? b = GetBoundsDeep(spaceNode != null ? spaceNode : root.transform);
            if (b.HasValue) payload.rooms.Add(new RoomRecord { name = roomName, bounds = new Bounds3(b.Value) });
        }

        // 2) objects (object + static_object)
        //    또한 relation 계산을 위해 각 항목의 Bounds 캐시를 만든다.
        var movableList = new List<(string name, Bounds bounds)>(); // object 폴더
        var staticList = new List<(string name, Bounds bounds)>(); // static_object 폴더

        foreach (var root in roots)
        {
            if (!IsNameMatchAny(root.name, RoomsForObjects)) continue;

            // (a) object 폴더
            var objectNode = GetDirectChildByNameVariant(root.transform, "object");
            if (objectNode != null)
            {
                foreach (Transform t in objectNode)
                {
                    if (!IncludeInactive && !t.gameObject.activeInHierarchy) continue;
                    AddObjectRecordForObjectFolder(payload.objects, t);

                    var b = GetBoundsDeep(t);
                    if (b.HasValue) movableList.Add((t.gameObject.name, b.Value));
                }
            }

            // (b) static_object 폴더
            var staticNode = GetDirectChildByNameVariant(root.transform, "static_object");
            if (staticNode != null)
            {
                foreach (Transform t in staticNode)
                {
                    if (!IncludeInactive && !t.gameObject.activeInHierarchy) continue;
                    AddStaticObjectRecord(payload.objects, t);

                    var b = GetBoundsDeep(t);
                    if (b.HasValue) staticList.Add((t.gameObject.name, b.Value));
                }
            }
        }

        // 3) relations: object가 static_object '위에(on)' 있는지
        BuildOnRelations(payload.relation_information, movableList, staticList);

        // 4) agent + current_room (AABB 우선)
        var agentTf = FindAgentTransform(roots);
        if (agentTf != null)
        {
            var fwd = agentTf.forward; fwd.Normalize();
            var worldPos = UseLocalPosition ? agentTf.localPosition : agentTf.position;

            string roomByBounds = FindRoomNameByPoint(payload.rooms, worldPos);
            string roomFallback = GetRoomNameFromHierarchy(agentTf);

            payload.agent = new AgentRecord
            {
                name = agentTf.gameObject.name,
                position = new Vec3(worldPos),
                forward = new Vec3(fwd),
                euler = new Vec3(agentTf.eulerAngles),
                current_room = roomByBounds ?? roomFallback ?? "unknown"
            };
        }

        return payload;
    }

    // ─────────────────────────────────────────────────────────────
    // object / static_object 레코드 생성
    // ─────────────────────────────────────────────────────────────
    private static void AddObjectRecordForObjectFolder(List<ObjectRecord> outList, Transform t)
    {
        Vector3 pos = UseLocalPosition ? t.localPosition : t.position;
        Vector3 wScale = GetWorldScaleFromMatrix(t);
        string room = GetRoomNameFromHierarchy(t) ?? "unknown";

        var kind = GuessKind(t);
        string state = null;
        if (kind == Kind.Door)
        {
            if (TryGetDoorOpenStateDeep(t, out bool isOpen)) state = isOpen ? "open" : "close";
        }
        else if (kind == Kind.Light)
        {
            if (TryGetLightOnStateDeep(t, out bool isOn)) state = isOn ? "on" : "off";
        }
        // 기타는 null

        outList.Add(new ObjectRecord
        {
            name = t.gameObject.name,
            position = new Vec3(pos),
            localScale = new Vec3(t.localScale),
            worldScale = new Vec3(wScale),
            current_room = room,
            state = state
        });
    }

    private static void AddStaticObjectRecord(List<ObjectRecord> outList, Transform t)
    {
        Vector3 pos = UseLocalPosition ? t.localPosition : t.position;
        Vector3 wScale = GetWorldScaleFromMatrix(t);
        string room = GetRoomNameFromHierarchy(t) ?? "unknown";

        outList.Add(new ObjectRecord
        {
            name = t.gameObject.name,
            position = new Vec3(pos),
            localScale = new Vec3(t.localScale),
            worldScale = new Vec3(wScale),
            current_room = room,
            state = "static"
        });
    }

    // ─────────────────────────────────────────────────────────────
    // 관계 생성: "subject on target"
    // ─────────────────────────────────────────────────────────────
    private static void BuildOnRelations(List<RelationRecord> outList,
        List<(string name, Bounds bounds)> movable,
        List<(string name, Bounds bounds)> statics)
    {
        foreach (var m in movable)
        {
            foreach (var s in statics)
            {
                if (IsOn(m.bounds, s.bounds))
                {
                    outList.Add(new RelationRecord
                    {
                        subject = m.name,
                        predicate = "on",
                        target = s.name
                    });
                    // 한 개만 매칭하고 끝내고 싶다면 break; (필요 시 해제)
                }
            }
        }
    }

    private static bool IsOn(Bounds subject, Bounds support)
    {
        // 높이 조건: subject 바닥이 support 윗면과 거의 같은 높이
        bool heightOk = Mathf.Abs(subject.min.y - support.max.y) <= HeightEpsilon ||
                        (subject.min.y >= support.max.y - HeightEpsilon && subject.min.y <= support.max.y + HeightEpsilon);
        if (!heightOk) return false;

        // XZ 투영 교집합 비율 계산
        float interX = Mathf.Max(0f, Mathf.Min(subject.max.x, support.max.x) - Mathf.Max(subject.min.x, support.min.x));
        float interZ = Mathf.Max(0f, Mathf.Min(subject.max.z, support.max.z) - Mathf.Max(subject.min.z, support.min.z));
        float interArea = interX * interZ;

        float subjArea = Mathf.Max(0.0001f, (subject.size.x) * (subject.size.z));
        float ratio = interArea / subjArea;

        return ratio >= MinXZOverlapRatio;
    }

    // ─────────────────────────────────────────────────────────────
    // 상태/분류 판별
    // ─────────────────────────────────────────────────────────────
    private enum Kind { Other, Door, Light }

    private static Kind GuessKind(Transform t)
    {
        string n = t.name.ToLowerInvariant();
        if (n.Contains("door")) return Kind.Door;
        if (n.Contains("light") || n.Contains("lamp")) return Kind.Light;

        if (HasComponentDeep(t, "Door")) return Kind.Door;
        if (HasComponentDeep(t, "Light") || HasComponentDeep(t, "SwitchDevice")) return Kind.Light;

        return Kind.Other;
    }

    private static bool HasComponentDeep(Transform root, string typeName)
    {
        var mbs = root.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var mb in mbs)
        {
            if (mb == null) continue;
            if (string.Equals(mb.GetType().Name, typeName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        if (string.Equals(typeName, "Light", StringComparison.OrdinalIgnoreCase))
        {
            var lights = root.GetComponentsInChildren<Light>(true);
            return lights != null && lights.Length > 0;
        }
        return false;
    }

    private static bool TryGetDoorOpenStateDeep(Transform root, out bool isOpen)
        => TryGetBoolLikeDeep(root, new[] { "isOpen", "open", "_open" }, new[] { "Door" }, out isOpen);

    private static bool TryGetLightOnStateDeep(Transform root, out bool isOn)
    {
        if (TryGetBoolLikeDeep(root, new[] { "isOn", "on", "enabled" }, new[] { "SwitchDevice" }, out isOn))
            return true;

        var lights = root.GetComponentsInChildren<Light>(true);
        if (lights != null && lights.Length > 0) { isOn = lights[0].enabled; return true; }

        return false;
    }

    private static bool TryGetBoolLikeDeep(Transform root, string[] fieldNames, string[] componentTypeNames, out bool val)
    {
        val = false;
        var fnSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in fieldNames) fnSet.Add(f);

        var mbs = root.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var mb in mbs)
        {
            if (mb == null) continue;
            var type = mb.GetType();

            if (componentTypeNames != null && componentTypeNames.Length > 0)
            {
                bool match = false;
                foreach (var tn in componentTypeNames)
                    if (string.Equals(type.Name, tn, StringComparison.OrdinalIgnoreCase)) { match = true; break; }
                if (!match) continue;
            }

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var f in fields)
            {
                if (!fnSet.Contains(f.Name)) continue;
                try { if (TryCoerceBool(f.GetValue(mb), out val)) return true; } catch { }
            }

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var p in props)
            {
                if (!p.CanRead) continue;
                if (!fnSet.Contains(p.Name)) continue;
                try { if (TryCoerceBool(p.GetValue(mb, null), out val)) return true; } catch { }
            }
        }
        return false;
    }

    private static bool TryCoerceBool(object val, out bool b)
    {
        b = false; if (val == null) return false;
        switch (val)
        {
            case bool vb: b = vb; return true;
            case int vi: b = vi != 0; return true;
            case float vf: b = Mathf.Abs(vf) > float.Epsilon; return true;
            case string vs:
                b = vs.Equals("open", StringComparison.OrdinalIgnoreCase) ||
                                vs.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                                vs.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                                vs == "1"; return true;
            default: return false;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 공용 헬퍼
    // ─────────────────────────────────────────────────────────────
    private static bool IsNameMatchAny(string name, string[] keys)
    {
        var n = name.ToLowerInvariant();
        foreach (var k in keys)
        {
            if (n == k) return true;
            if (n.StartsWith(k + "_")) return true;
            if (n.StartsWith(k + " ")) return true;
            if (n.StartsWith(k + "(")) return true;
        }
        return false;
    }

    private static string CanonicalRoomName(string name)
    {
        var n = name.ToLowerInvariant();
        foreach (var k in RoomRootsAll)
            if (IsNameMatchAny(n, new[] { k })) return k;
        return n;
    }

    private static Transform GetDirectChildByNameVariant(Transform parent, string target)
    {
        string t = target.ToLowerInvariant();
        foreach (Transform c in parent)
        {
            var n = c.name.ToLowerInvariant();
            if (n == t || n.StartsWith(t + "_") || n.StartsWith(t + " ") || n.StartsWith(t + "("))
                return c;
        }
        return null;
    }

    private static string GetRoomNameFromHierarchy(Transform t)
    {
        while (t != null)
        {
            var n = t.name.ToLowerInvariant();
            if (IsNameMatchAny(n, RoomRootsAll))
                return CanonicalRoomName(n);
            t = t.parent;
        }
        return null;
    }

    private static Bounds? GetBoundsDeep(Transform root)
    {
        if (root == null) return null;
        var rends = root.GetComponentsInChildren<Renderer>(true);
        var cols = root.GetComponentsInChildren<Collider>(true);

        bool hasAny = false;
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);

        if (rends != null && rends.Length > 0)
        {
            b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            hasAny = true;
        }
        if (cols != null && cols.Length > 0)
        {
            if (!hasAny) { b = cols[0].bounds; hasAny = true; }
            for (int i = hasAny ? 0 : 1; i < cols.Length; i++) b.Encapsulate(cols[i].bounds);
        }
        return hasAny ? b : (Bounds?)null;
    }

    private static Vector3 GetWorldScaleFromMatrix(Transform t)
    {
        var m = t.localToWorldMatrix;
        float sx = new Vector3(m.m00, m.m10, m.m20).magnitude;
        float sy = new Vector3(m.m01, m.m11, m.m21).magnitude;
        float sz = new Vector3(m.m02, m.m12, m.m22).magnitude;
        return new Vector3(sx, sy, sz);
    }

    // 점 p가 Bounds3 안에 있는가
    private static bool Contains(in Bounds3 b, in Vector3 p, float eps = 1e-3f)
    {
        return (p.x >= b.x_min - eps && p.x <= b.x_max + eps) &&
               (p.y >= b.y_min - eps && p.y <= b.y_max + eps) &&
               (p.z >= b.z_min - eps && p.z <= b.z_max + eps);
    }

    // rooms에서 점 p가 속한 첫 방 이름
    private static string FindRoomNameByPoint(List<RoomRecord> rooms, in Vector3 p, float eps = 1e-3f)
    {
        foreach (var r in rooms) if (Contains(r.bounds, p, eps)) return r.name;
        return null;
    }

    private static Transform FindAgentTransform(GameObject[] roots)
    {
        foreach (var root in roots)
        {
            var mbs = root.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var mb in mbs)
            {
                if (mb == null) continue;
                var n = mb.GetType().Name.ToLowerInvariant();
                if (n == "robotmove" || n == "robotpick") return mb.transform;
            }
        }
        foreach (var root in roots)
        {
            var trs = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in trs) { try { if (t.CompareTag("Player")) return t; } catch { } }
        }
        foreach (var root in roots)
        {
            var trs = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in trs)
            {
                var n = t.name.ToLowerInvariant();
                if (n.Contains("robot") || n.Contains("agent")) return t;
            }
        }
        return null;
    }
}
#endif
