using TMPro;
using UnityEngine;
using DoorScript;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine.AI;

public class InputManager : MonoBehaviour
{
    public TMP_InputField inputField;   // CommandInput
    public TextMeshProUGUI resultText;  // Result
    public RobotMove robot;
    public RobotPick robotPick;
    public UpdateObject updater;

    // ���� ����
    private NavMeshAgent _agent;
    private bool _isProcessing;
    private readonly Queue<Command> _queue = new Queue<Command>();

    // RobotPick �̺�Ʈ�� �Ϸ� ���
    private bool _placeCompleted;
    private bool _pickAttached;

    void Start()
    {
        inputField.onEndEdit.AddListener(OnInputSubmitted);
        _agent = robot ? robot.GetComponent<NavMeshAgent>() : null;

        if (robotPick != null)
        {
            robotPick.OnPlace += () => { _placeCompleted = true; };
            robotPick.OnAttach += () => { _pickAttached = true; };
        }
    }

    void OnDestroy()
    {
        if (robotPick != null)
        {
            robotPick.OnPlace -= () => { _placeCompleted = true; };
            robotPick.OnAttach -= () => { _pickAttached = true; };
        }
    }

    void OnInputSubmitted(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        if (text.Trim().Equals("export", StringComparison.OrdinalIgnoreCase))
        {
#if UNITY_EDITOR
            SceneJsonExporter.ExportOrUpdate(askIfPathNotSet: true); // ù ���ุ ��� ����
            resultText.text = "Export updated (same JSON file).";
#else
        resultText.text = "Export is available only in the Unity Editor.";
#endif
            ResetInput();
            return;
        }

        // ��Ƽ�����̸� ��ũ��Ʈ�� ó��
        if (text.Contains("\n"))
        {
            if (updater != null) updater.BeginBatch();
            EnqueueScript(text);
            StartProcessIfIdle();
            ResetInput();
            return;
        }

        // ���� ��� ó��(���� ���� ����)
        string[] split = text.Trim().Split(' ', 2);
        string cmd = split[0].ToLower();

        if (cmd == "switchon" || cmd == "switchoff" || cmd == "switch")
        {
            if (split.Length < 2) { Fail($"format: {cmd} [switch_name]"); ResetInput(); return; }
            string switchName = split[1].Trim();
            HandleSwitchImmediate(cmd, switchName);
            ResetInput();
            return;
        }

        if (cmd == "move" || cmd == "pick" || cmd == "open")
        {
            if (split.Length < 2) { Fail("wrong format: target is needed."); ResetInput(); return; }
            string target = split[1].Trim();
            resultText.text = $"command: {cmd}, target: {target}";
            Enqueue(cmd, target);
            StartProcessIfIdle();
        }
        else if (cmd == "place")
        {
            if (split.Length < 2) { Fail("format: place [destination]"); ResetInput(); return; }
            string target = split[1].Trim();
            Enqueue("place", target);
            StartProcessIfIdle();
        }
        else
        {
            Fail($"Unknown command: '{cmd}'");
        }

        ResetInput();
    }

    // ===== ��ũ��Ʈ �ļ� =====
    void EnqueueScript(string multiline)
    {
        // �� ������ �Ľ�
        var lines = multiline.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // ���� ��ȣ/��/���� ����: "1.  move(x)" -> "move(x)"
            line = Regex.Replace(line, @"^\s*\d+\s*[\.\)]\s*", "");

            // ���� 1) cmd(arg)
            var m = Regex.Match(line, @"^([a-zA-Z_]+)\s*\(\s*([^)]+)\s*\)\s*$");
            if (m.Success)
            {
                string cmd = m.Groups[1].Value.ToLower();
                string arg = m.Groups[2].Value.Trim();
                Enqueue(cmd, arg);
                continue;
            }

            // ���� 2) cmd arg
            var m2 = Regex.Match(line, @"^([a-zA-Z_]+)\s+(.+)$");
            if (m2.Success)
            {
                string cmd = m2.Groups[1].Value.ToLower();
                string arg = m2.Groups[2].Value.Trim();
                Enqueue(cmd, arg);
                continue;
            }

            // ������ �� �ν��ϸ� ����(�α�)
            Debug.LogWarning($"[InputManager] Unrecognized line: {raw}");
        }
    }

    void Enqueue(string cmd, string arg)
    {
        _queue.Enqueue(new Command { cmd = cmd, arg = arg });
    }

    void StartProcessIfIdle()
    {
        if (!_isProcessing) StartCoroutine(ProcessQueue());
    }

    // ===== ���� ���� =====
    IEnumerator ProcessQueue()
    {
        _isProcessing = true;

        while (_queue.Count > 0)
        {
            var c = _queue.Dequeue();
            resultText.text = $"command: {c.cmd}, target: {c.arg}";

            // �� ��� ����
            switch (c.cmd)
            {
                case "move":
                    yield return ExecMove(c.arg);
                    break;

                case "pick":
                    yield return ExecPick(c.arg);
                    break;

                case "place":
                    yield return ExecPlace(c.arg);
                    break;

                case "open":
                    yield return ExecOpen(c.arg);
                    break;

                case "switch":
                case "switchon":
                case "switchoff":
                    HandleSwitchImmediate(c.cmd, c.arg);
                    break;

                default:
                    Fail($"Unknown command: {c.cmd}");
                    break;
            }

            yield return null;
        }

        _isProcessing = false;

        // ��ġ ���� �� ���
        if (updater != null) updater.EndBatchAndRender();
    }

    // ===== ���� ��� ���� =====

    IEnumerator ExecMove(string destination)
    {
        GameObject target = GameObject.Find(destination);
        if (target == null) { Fail($"'{destination}' can't find"); yield break; }

        // �̵� ����
        Vector3 goal = target.transform.position;
        if (robotPick != null && robotPick.IsHolding) robot.MoveWhileHolding(goal);
        else robot.MoveTo(goal);

        // �̵� �Ϸ���� ���
        if (_agent == null) _agent = robot.GetComponent<NavMeshAgent>();
        if (_agent == null) { Fail("NavMeshAgent not found on robot"); yield break; }

        // UpdateObject�� ���� ���� ��û(���� ������ ȭ��/���� ����)
        if (updater != null) updater.BeginTrackMoveCompletion(robot.transform, _agent);

        // ���� ���� ���
        // ��� ��� ���
        while (_agent.pathPending) yield return null;

        // ���� ����
        while (true)
        {
            bool arrived = !_agent.pathPending &&
                           _agent.remainingDistance <= _agent.stoppingDistance + 0.1f &&
                           (!_agent.hasPath || _agent.velocity.sqrMagnitude < 0.05f);
            if (arrived) break;
            yield return null;
        }

        Ok($"arrived: {destination}");
    }

    IEnumerator ExecPick(string name)
    {
        GameObject target = GameObject.Find(name);
        if (target == null) { Fail($"'{name}' can't find"); yield break; }

        if (robotPick == null) { Fail("RobotPick not set"); yield break; }
        if (robotPick.IsHolding) { Fail("Already holding an object."); yield break; }

        _pickAttached = false;
        robotPick.Pick(target);

        // ������ �տ� ���� ������ ��� (OnAttach �̺�Ʈ �Ǵ� State)
        float deadline = Time.time + 5f;
        while (!_pickAttached && !robotPick.IsHolding && Time.time < deadline)
            yield return null;

        if (robotPick.IsHolding || _pickAttached)
            Ok($"picked: {name}");
        else
            Fail("pick timeout");
    }

    IEnumerator ExecPlace(string targetSurface)
    {
        if (robotPick == null) { Fail("RobotPick not set"); yield break; }
        if (!robotPick.IsHolding) { Fail("Nothing in hand to place."); yield break; }

        GameObject surface = GameObject.Find(targetSurface);
        if (surface == null) { Fail($"'{targetSurface}' can't find"); yield break; }

        // UpdateObject�� �Ϸ� ��� Ÿ�� ����
        if (updater != null) updater.PreparePlaceTracking(surface);

        _placeCompleted = false;
        bool ok = robot.PlaceOn(surface);
        if (!ok) { Fail("place failed"); yield break; }

        // ���� �������� ������ ���
        float deadline = Time.time + 5f;
        while (!_placeCompleted && robotPick.IsHolding && Time.time < deadline)
            yield return null;

        if (_placeCompleted || !robotPick.IsHolding)
            Ok($"placed on {targetSurface}");
        else
            Fail("place timeout");
    }

    IEnumerator ExecOpen(string doorName)
    {
        GameObject doorGO = GameObject.Find(doorName);
        if (doorGO == null) { Fail($"'{doorName}' can't find"); yield break; }

        Door door = doorGO.GetComponentInChildren<Door>();
        if (door == null) { Fail($"'{doorName}' does not have a Door script."); yield break; }

        door.OpenDoor();
        Ok($"'{doorName}' opened.");
        if (updater != null) updater.RecordOpen(door, doorName);

        // ª�� ������ �纸
        yield return null;
    }

    void HandleSwitchImmediate(string cmd, string switchName)
    {
        GameObject go = GameObject.Find(switchName);
        SwitchDevice sw = null;
        if (go != null) sw = go.GetComponentInChildren<SwitchDevice>(true);
        if (sw == null)
        {
            foreach (var cand in GameObject.FindObjectsOfType<SwitchDevice>(true))
            {
                if (cand == null) continue;
                if (string.Equals(cand.gameObject.name, switchName, StringComparison.OrdinalIgnoreCase))
                {
                    sw = cand;
                    break;
                }
            }
        }
        if (sw == null) { Fail($"'{switchName}' switch not found"); return; }

        if (cmd == "switchon") sw.SwitchOn();
        else if (cmd == "switchoff") sw.SwitchOff();
        else sw.Toggle();

        Ok($"{cmd} {switchName}");
        if (updater != null) updater.RecordSwitch(sw, switchName);
    }

    void Ok(string msg) { resultText.text = msg; }
    void Fail(string msg) { resultText.text = msg; }

    void ResetInput()
    {
        inputField.text = "";
        inputField.ActivateInputField();
    }

    struct Command
    {
        public string cmd;
        public string arg;
    }
}
