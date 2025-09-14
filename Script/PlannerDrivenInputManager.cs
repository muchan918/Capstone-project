using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using DoorScript; // Door.OpenDoor()

/// <summary>
/// �ڿ�� ����(TaskPlannerClient)�� ������,
/// ������ ������ plan_sequence("move(xxx)" ��)�� ���� ����.
/// - ���� ȣ���� InputManager�� �����ϰ� �ܼ� ȣ��( MoveTo / Pick / OpenDoor / PlaceOn )
/// - �Ϸ� ���/Ÿ�Ӿƿ�/�α�/���� �� �����̴� ����
/// - move ���� ������ �˳��ϰ�(�浹 ������ ������ + ���� �Ÿ�)
/// </summary>
public class PlannerDrivenInputManager : MonoBehaviour
{
    [Header("UI")]
    public TMP_InputField inputField;
    public TextMeshProUGUI resultText;

    [Header("Robot")]
    public RobotMove robot;
    public RobotPick robotPick;

    [Header("Planner")]
    public TaskPlannerClient taskPlannerClient;
    public bool autoExecutePlan = true;

    [Header("Execution Tunings")]
    [Tooltip("move ���� ���� �⺻ �Ÿ�(m). �˳��ϰ� ����")]
    public float arriveThreshold = 2.5f;         // �������� �а�
    [Tooltip("NavMeshAgent.remainingDistance �� ���� �� ����(m)")]
    public float agentExtraStop = 1.0f;          // ������Ʈ ���� ����
    [Tooltip("��ǥ���� �ʹ� ���� �ʵ���, ��ǥ�� ������ �ݴ������� �� ���� �������� ����(m)")]
    public float standBackDistance = 0.6f;

    [Tooltip("�� ���ܺ� Ÿ�Ӿƿ�(��)")]
    public float stepTimeoutSec = 15f;
    [Tooltip("open �� ª�� ����ȭ(��)")]
    public float doorSettleWait = 0.25f;
    [Tooltip("move �� ª�� ����ȭ(��)")]
    public float moveSettleWait = 0.5f;
    [Tooltip("���ܰ� ���� ���� ������(��)")]
    public float stepGapDelay = 0.2f;            // ��û��� 0.2�� �⺻

    // ���� ����
    Coroutine running;
    bool isRunning;
    bool attachFlag;   // RobotPick.OnAttach
    bool placeFlag;    // RobotPick.OnPlace
    bool _stepOk;
    NavMeshAgent _agent;

    // �ļ�
    static readonly Regex kStepRx = new Regex(@"^\s*([A-Za-z_]+)\s*\(\s*([^\)]*)\s*\)\s*$");

    void Start()
    {
        if (inputField) inputField.onEndEdit.AddListener(OnInputSubmitted);

        _agent = robot ? robot.GetComponent<NavMeshAgent>() : null;

        if (robotPick != null)
        {
            robotPick.OnAttach += () => attachFlag = true;
            robotPick.OnPlace += () => placeFlag = true;
        }

        TaskPlannerClient.OnPlanReceived += HandlePlanReceived;
        TaskPlannerClient.OnPlanningError += HandlePlanningError;
        TaskPlannerClient.OnConnectionStatusChanged += HandleConnectionChanged;

        LogToUI("TaskMaker system ready. Enter natural language commands.", Color.yellow);
    }

    void OnDestroy()
    {
        TaskPlannerClient.OnPlanReceived -= HandlePlanReceived;
        TaskPlannerClient.OnPlanningError -= HandlePlanningError;
        TaskPlannerClient.OnConnectionStatusChanged -= HandleConnectionChanged;
    }

    // ���������������������������������� 1) �Է� �� ���� ��û ����������������������������������
    void OnInputSubmitted(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        if (taskPlannerClient == null)
        {
            LogToUI("TaskPlannerClient is not set.", Color.red);
            ResetInput();
            return;
        }

        LogToUI($"Processing... '{text.Trim()}'", Color.yellow);
        StartCoroutine(RequestPlan(text.Trim()));
        ResetInput();
    }

    IEnumerator RequestPlan(string userInput)
    {
        if (!taskPlannerClient.IsConnected)
            taskPlannerClient.CheckServerConnection();

        yield return new WaitForSeconds(0.5f);

        string sessionId = $"unity_session_{DateTime.Now:yyyyMMdd_HHmmss}";
        taskPlannerClient.RequestTaskPlanning(userInput, sessionId);
    }

    // �������������������������������� 2) ���� ���� ���� �� ǥ�� + ���� ��������������������������������
    void HandlePlanReceived(TaskPlannerClient.PlanResponse res)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Command > {res?.metadata?.original_input}");
        sb.AppendLine();
        sb.AppendLine("Status: SUCCESS");
        if (res?.metadata != null) sb.AppendLine($"Time: {res.metadata.processing_time:F2}s");
        if (res?.plan_sequence != null)
        {
            sb.AppendLine($"Output ({res.plan_sequence.Count} steps):");
            for (int i = 0; i < res.plan_sequence.Count; i++)
                sb.AppendLine($"  {i + 1}. {res.plan_sequence[i]}");
        }
        LogToUI(sb.ToString(), Color.green);

        if (autoExecutePlan && res?.plan_sequence != null && res.plan_sequence.Count > 0)
        {
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(ExecuteRobotPlan(res.plan_sequence));
        }
    }

    void HandlePlanningError(string err)
    {
        LogToUI($"Planning FAILED\nError: {err}", Color.red);
    }

    void HandleConnectionChanged(bool connected)
    {
        LogToUI(connected ? "Connected to TaskMaker server." : "Server disconnected.",
                connected ? Color.green : Color.red);
    }

    // �������������������������������������� 3) �÷� ���� ��������������������������������������
    IEnumerator ExecuteRobotPlan(List<string> steps)
    {
        if (isRunning) { LogToUI("Already executing a plan.", Color.yellow); yield break; }
        isRunning = true;

        float startT = Time.time;

        for (int i = 0; i < steps.Count; i++)
        {
            string step = steps[i]?.Trim();
            if (string.IsNullOrEmpty(step)) continue;

            if (!TryParseStep(step, out string verb, out string arg))
            {
                FailStep(i, step, "parse error");
                isRunning = false; yield break;
            }

            bool ok = false;
            switch (verb)
            {
                case "move":
                    yield return StartCoroutine(DoMove(arg, i, steps.Count));
                    ok = _stepOk; break;

                case "pick":
                    yield return StartCoroutine(DoPick(arg, i, steps.Count));
                    ok = _stepOk; break;

                case "open":
                    yield return StartCoroutine(DoOpen(arg, i, steps.Count));
                    ok = _stepOk; break;

                case "place": // on ����
                    yield return StartCoroutine(DoPlace(arg, i, steps.Count));
                    ok = _stepOk; break;

                case "switchon":
                case "switchoff":
                    yield return StartCoroutine(DoSwitch(arg, verb == "switchon", i, steps.Count));
                    ok = _stepOk; break;

                default:
                    FailStep(i, step, $"unknown verb '{verb}'");
                    isRunning = false; yield break;
            }

            if (!ok) { isRunning = false; yield break; }
            if (stepGapDelay > 0f) yield return new WaitForSeconds(stepGapDelay);
        }

        float elapsed = Time.time - startT;
        LogAppend($"\n\nExecution COMPLETE ({steps.Count}/{steps.Count} steps, {elapsed:F2}s)", Color.cyan);
        isRunning = false;
    }

    // ������������������ Step ����(������ �ܼ� ȣ��, �Ϸ�� ���) ������������������

    IEnumerator DoMove(string targetName, int idx, int total)
    {
        _stepOk = false;

        GameObject t = FindGO(targetName);
        if (t == null) { FailStep(idx, $"move({targetName})", "target not found"); yield break; }

        LogAppend($"\n[{idx + 1}/{total}] move �� {targetName}", Color.white);

        // ������ ���: ���� ����� ǥ���� + �� ���� �ڷ�
        Vector3 goal = t.transform.position;

        // �κ� ���� ����
        if (robot) goal.y = robot.transform.position.y;

        // Collider�� ������ �κ����� ���� ����� ǥ��������
        var col = t.GetComponentInChildren<Collider>();
        if (col)
        {
            Vector3 closest = col.ClosestPoint(robot.transform.position);
            if (robot) closest.y = robot.transform.position.y;

            // ��ǥ�� �ʿ� �ʹ� ���� �ʵ��� �� ���� �ڷ�
            Vector3 toClosest = closest - robot.transform.position;
            toClosest.y = 0f;
            if (toClosest.sqrMagnitude > 1e-6f)
                closest -= toClosest.normalized * standBackDistance;

            goal = closest;
        }

        // NavMesh ���� ����
        if (NavMesh.SamplePosition(goal, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            goal = hit.position;

        // �� InputManager�� �����ϰ� �ܼ� ȣ��
        if (robotPick != null && robotPick.IsHolding) robot.MoveWhileHolding(goal);
        else robot.MoveTo(goal);

        // ��� ��� ���� ���
        if (_agent != null)
        {
            yield return new WaitForSeconds(0.1f);
            float w = 0f;
            while (_agent.pathPending && w < 3f) { w += Time.deltaTime; yield return null; }
        }

        // ���� ���(���� �ְ�)
        float t0 = Time.time;
        while (Time.time - t0 < stepTimeoutSec)
        {
            if (_agent != null)
            {
                bool arrived =
                    !_agent.pathPending &&
                    _agent.remainingDistance <= (_agent.stoppingDistance + agentExtraStop) &&
                    (!_agent.hasPath || _agent.velocity.sqrMagnitude < 0.05f);

                if (arrived || Vector3.SqrMagnitude(robot.transform.position - goal) <= arriveThreshold * arriveThreshold)
                {
                    yield return new WaitForSeconds(moveSettleWait);
                    _stepOk = true; yield break;
                }
            }
            else
            {
                if ((robot.transform.position - goal).sqrMagnitude <= arriveThreshold * arriveThreshold)
                {
                    yield return new WaitForSeconds(moveSettleWait);
                    _stepOk = true; yield break;
                }
            }
            yield return null;
        }

        FailStep(idx, $"move({targetName})", "timeout");
    }

    IEnumerator DoPick(string targetName, int idx, int total)
    {
        _stepOk = false;

        GameObject t = FindGO(targetName);
        if (t == null) { FailStep(idx, $"pick({targetName})", "target not found"); yield break; }
        if (robotPick == null) { FailStep(idx, $"pick({targetName})", "RobotPick missing"); yield break; }

        LogAppend($"\n[{idx + 1}/{total}] pick �� {targetName}", Color.white);

        attachFlag = false;

        // �� �ܼ� ȣ�� (InputManager�� ����)
        robotPick.Pick(t);

        // ���� ������ ���
        float t0 = Time.time;
        while (Time.time - t0 < stepTimeoutSec)
        {
            if (attachFlag || robotPick.IsHolding)
            { _stepOk = true; yield break; }
            yield return null;
        }

        FailStep(idx, $"pick({targetName})", "timeout");
    }

    IEnumerator DoOpen(string targetName, int idx, int total)
    {
        _stepOk = false;

        GameObject t = FindGO(targetName);
        if (t == null) { FailStep(idx, $"open({targetName})", "target not found"); yield break; }

        var door = t.GetComponentInChildren<Door>();
        if (door == null) { FailStep(idx, $"open({targetName})", "Door script missing"); yield break; }

        LogAppend($"\n[{idx + 1}/{total}] open �� {targetName}", Color.white);

        // �� �ܼ� ȣ��
        door.OpenDoor();

        yield return new WaitForSeconds(doorSettleWait);
        _stepOk = true;
    }

    IEnumerator DoPlace(string targetName, int idx, int total)
    {
        _stepOk = false;

        GameObject t = FindGO(targetName);
        if (t == null) { FailStep(idx, $"place({targetName})", "target not found"); yield break; }
        if (robotPick == null || !robotPick.IsHolding) { FailStep(idx, $"place({targetName})", "nothing in hand"); yield break; }

        LogAppend($"\n[{idx + 1}/{total}] place(on) �� {targetName}", Color.white);

        placeFlag = false;

        // �� �ܼ� ȣ��
        bool ok = robot.PlaceOn(t);
        if (!ok) { FailStep(idx, $"place({targetName})", "PlaceOn failed"); yield break; }

        // �������� ������ ���
        float t0 = Time.time;
        while (Time.time - t0 < stepTimeoutSec)
        {
            if (placeFlag || !robotPick.IsHolding)
            { _stepOk = true; yield break; }
            yield return null;
        }

        FailStep(idx, $"place({targetName})", "timeout");
    }

    IEnumerator DoSwitch(string switchName, bool turnOn, int idx, int total)
    {
        _stepOk = false;

        GameObject go = FindGO(switchName);
        if (go == null) { FailStep(idx, $"switch({switchName})", "target not found"); yield break; }

        var switchDevice = go.GetComponentInChildren<SwitchDevice>(true);
        if (switchDevice == null) { FailStep(idx, $"switch({switchName})", "SwitchDevice missing"); yield break; }

        LogAppend($"\n[{idx + 1}/{total}] switch{(turnOn ? "on" : "off")} �� {switchName}", Color.white);

        // �� �ܼ� ȣ��
        if (turnOn) switchDevice.SwitchOn();
        else switchDevice.SwitchOff();

        _stepOk = true;
        yield return null;
    }

    // �������������������������� Helpers ��������������������������
    bool TryParseStep(string s, out string verb, out string arg)
    {
        verb = arg = null;
        var m = kStepRx.Match(s);
        if (!m.Success) return false;
        verb = m.Groups[1].Value.ToLowerInvariant();
        arg = m.Groups[2].Value.Trim();
        return true;
    }

    GameObject FindGO(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        // ������� "lab/object/desk_01" �켱
        var go = GameObject.Find(name);
        if (go != null) return go;

        // ��/�ҹ��� ���� ���� Ž��
        var all = Resources.FindObjectsOfTypeAll<Transform>();
        for (int i = 0; i < all.Length; i++)
            if (string.Equals(all[i].name, name, StringComparison.OrdinalIgnoreCase))
                return all[i].gameObject;

        return null;
    }

    void LogToUI(string msg, Color c)
    {
        if (resultText)
        {
            resultText.text = msg;
            resultText.color = c;
        }
        Debug.Log($"[Planner] {msg}");
    }

    void LogAppend(string line, Color? overrideColor = null)
    {
        if (!resultText) { Debug.Log(line); return; }
        resultText.text += line;
        if (overrideColor.HasValue) resultText.color = overrideColor.Value;
    }

    void FailStep(int idx, string step, string reason)
    {
        LogAppend($"\n\nFAILED at step {idx + 1}: {step} ({reason})", Color.red);
    }

    void ResetInput()
    {
        if (inputField)
        {
            inputField.text = "";
            inputField.ActivateInputField();
        }
    }
}
