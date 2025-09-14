using System.Collections.Generic;
using System.Text;               // ← 추가
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using DoorScript;

public class UpdateObject : MonoBehaviour
{
    [System.Serializable]
    public struct UpdateRecord
    {
        public string type;      // "move" | "place" | "open" | "switch" | "switchon" | "switchoff"
        public string subject;   // "robot", "desk_01", ...
        public Vector3 position; // move/place일 때 좌표
        public bool? state;      // open/switch일 때 상태
        public float time;       // Time.time
    }

    [Header("UI: 최근 업데이트(단일/배치) 표시")]
    public TextMeshProUGUI UpdateText;

    [Header("Refs")]
    public RobotPick robotPick;

    public List<UpdateRecord> updates = new List<UpdateRecord>(64);

    public Vector3 lastRobotPos;
    public Dictionary<string, Vector3> lastPlacedPos = new Dictionary<string, Vector3>(System.StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> doorStates = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> switchStates = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);

    bool waitingMove;
    Transform robotTr;
    NavMeshAgent agent;
    GameObject pendingPlaceTarget;

    // ===== 배치 모드 =====
    bool batching = false;
    // 같은 (type,subject) 는 마지막 변경만 남기기
    readonly Dictionary<string, UpdateRecord> pendingChanges =
        new Dictionary<string, UpdateRecord>(System.StringComparer.OrdinalIgnoreCase);

    string Key(string type, string subject) => $"{type}:{subject}";

    public void BeginBatch()
    {
        batching = true;
        pendingChanges.Clear();
    }

    public void EndBatchAndRender()
    {
        if (!batching)
        {
            // 배치 아님: 직전 한 줄 그대로 유지
            return;
        }

        // 모인 변경들을 한 번에 출력
        var sb = new StringBuilder();
        foreach (var kv in pendingChanges)
            sb.AppendLine(FormatRecord(kv.Value));

        if (UpdateText) UpdateText.text = sb.ToString().TrimEnd();

        batching = false;
        pendingChanges.Clear();
    }

    void Awake()
    {
        if (robotPick != null)
            robotPick.OnPlace += HandleOnPlace;
    }
    void OnDestroy()
    {
        if (robotPick != null)
            robotPick.OnPlace -= HandleOnPlace;
    }

    void Update()
    {
        if (!waitingMove || robotTr == null || agent == null) return;

        bool arrived =
            !agent.pathPending &&
            agent.remainingDistance <= agent.stoppingDistance + 0.1f &&
            (!agent.hasPath || agent.velocity.sqrMagnitude < 0.05f);

        if (arrived)
        {
            waitingMove = false;
            lastRobotPos = robotTr.position;
            AddRecord("move", "robot", lastRobotPos, null);
        }
    }

    public void BeginTrackMoveCompletion(Transform robotTransform, NavMeshAgent nav)
    {
        robotTr = robotTransform;
        agent = nav;
        waitingMove = (robotTr != null && agent != null);
    }

    public void PreparePlaceTracking(GameObject targetSurfaceOrAnchor)
    {
        pendingPlaceTarget = targetSurfaceOrAnchor;
    }

    public void RecordOpen(Door door, string nameOverride = null)
    {
        string doorName = string.IsNullOrEmpty(nameOverride) ? door.gameObject.name : nameOverride;
        bool? state = TryGetBoolProperty(door, "IsOpen");
        bool final = state ?? !(doorStates.TryGetValue(doorName, out var cur) && cur);
        doorStates[doorName] = final;
        AddRecord("open", doorName, Vector3.zero, final);
    }

    public void RecordSwitch(SwitchDevice sw, string nameOverride = null)
    {
        string swName = string.IsNullOrEmpty(nameOverride) ? sw.gameObject.name : nameOverride;
        bool? state = TryGetBoolProperty(sw, "IsOn");
        bool final = state ?? !(switchStates.TryGetValue(swName, out var cur) && cur);
        switchStates[swName] = final;
        AddRecord("switch", swName, Vector3.zero, final);
    }

    void HandleOnPlace()
    {
        GameObject placed = robotPick != null ? robotPick.LastPlacedObject : null;

        if (placed != null)
        {
            Vector3 p = placed.transform.position;
            lastPlacedPos[placed.name] = p;
            AddRecord("place", placed.name, p, null);
        }
        else if (pendingPlaceTarget != null)
        {
            Vector3 p = pendingPlaceTarget.transform.position;
            lastPlacedPos[pendingPlaceTarget.name] = p;
            AddRecord("place", pendingPlaceTarget.name, p, null);
        }

        pendingPlaceTarget = null;
    }

    void AddRecord(string type, string subject, Vector3 pos, bool? state)
    {
        var rec = new UpdateRecord
        {
            type = type,
            subject = subject,
            position = pos,
            state = state,
            time = Time.time
        };
        updates.Add(rec);

        if (batching)
        {
            // 같은 키는 최신 것으로 덮어쓰기
            pendingChanges[Key(type, subject)] = rec;
        }
        else
        {
            // 단건 모드: 바로 1줄 출력
            string line = FormatRecord(rec);
            if (UpdateText) UpdateText.text = line;
            Debug.Log("[UpdateObject] " + line);
        }
    }

    public string FormatRecord(UpdateRecord r)
    {
        string s = $"{r.type}:{r.subject}";

        if (r.type == "move" || r.type == "place")
            s += $" pos=({r.position.x:F2},{r.position.y:F2},{r.position.z:F2})";

        if (r.state.HasValue)
        {
            if (r.type == "open")
                s += $" state={(r.state.Value ? "OPEN" : "CLOSED")}";
            else if (r.type == "switch" || r.type == "switchon" || r.type == "switchoff")
                s += $" state={(r.state.Value ? "ON" : "OFF")}";
        }

        return s;
    }

    static bool? TryGetBoolProperty(object obj, string propName)
    {
        if (obj == null) return null;
        var prop = obj.GetType().GetProperty(propName);
        if (prop != null && prop.PropertyType == typeof(bool))
            return (bool)prop.GetValue(obj);
        return null;
    }
}
