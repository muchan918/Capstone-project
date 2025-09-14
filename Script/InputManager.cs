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

    // 내부 상태
    private NavMeshAgent _agent;
    private bool _isProcessing;
    private readonly Queue<Command> _queue = new Queue<Command>();

    // RobotPick 이벤트로 완료 대기
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
            SceneJsonExporter.ExportOrUpdate(askIfPathNotSet: true); // 첫 실행만 경로 선택
            resultText.text = "Export updated (same JSON file).";
#else
        resultText.text = "Export is available only in the Unity Editor.";
#endif
            ResetInput();
            return;
        }

        // 멀티라인이면 스크립트로 처리
        if (text.Contains("\n"))
        {
            if (updater != null) updater.BeginBatch();
            EnqueueScript(text);
            StartProcessIfIdle();
            ResetInput();
            return;
        }

        // 단일 명령 처리(기존 로직 유지)
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

    // ===== 스크립트 파서 =====
    void EnqueueScript(string multiline)
    {
        // 줄 단위로 파싱
        var lines = multiline.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // 앞의 번호/점/공백 제거: "1.  move(x)" -> "move(x)"
            line = Regex.Replace(line, @"^\s*\d+\s*[\.\)]\s*", "");

            // 형식 1) cmd(arg)
            var m = Regex.Match(line, @"^([a-zA-Z_]+)\s*\(\s*([^)]+)\s*\)\s*$");
            if (m.Success)
            {
                string cmd = m.Groups[1].Value.ToLower();
                string arg = m.Groups[2].Value.Trim();
                Enqueue(cmd, arg);
                continue;
            }

            // 형식 2) cmd arg
            var m2 = Regex.Match(line, @"^([a-zA-Z_]+)\s+(.+)$");
            if (m2.Success)
            {
                string cmd = m2.Groups[1].Value.ToLower();
                string arg = m2.Groups[2].Value.Trim();
                Enqueue(cmd, arg);
                continue;
            }

            // 형식을 못 인식하면 무시(로그)
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

    // ===== 실행 루프 =====
    IEnumerator ProcessQueue()
    {
        _isProcessing = true;

        while (_queue.Count > 0)
        {
            var c = _queue.Dequeue();
            resultText.text = $"command: {c.cmd}, target: {c.arg}";

            // 각 명령 실행
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

        // 배치 종료 후 출력
        if (updater != null) updater.EndBatchAndRender();
    }

    // ===== 개별 명령 구현 =====

    IEnumerator ExecMove(string destination)
    {
        GameObject target = GameObject.Find(destination);
        if (target == null) { Fail($"'{destination}' can't find"); yield break; }

        // 이동 시작
        Vector3 goal = target.transform.position;
        if (robotPick != null && robotPick.IsHolding) robot.MoveWhileHolding(goal);
        else robot.MoveTo(goal);

        // 이동 완료까지 대기
        if (_agent == null) _agent = robot.GetComponent<NavMeshAgent>();
        if (_agent == null) { Fail("NavMeshAgent not found on robot"); yield break; }

        // UpdateObject에 도착 추적 요청(도착 시점에 화면/저장 갱신)
        if (updater != null) updater.BeginTrackMoveCompletion(robot.transform, _agent);

        // 실제 도착 대기
        // 경로 계산 대기
        while (_agent.pathPending) yield return null;

        // 도착 판정
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

        // 실제로 손에 붙을 때까지 대기 (OnAttach 이벤트 또는 State)
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

        // UpdateObject에 완료 기록 타깃 설정
        if (updater != null) updater.PreparePlaceTracking(surface);

        _placeCompleted = false;
        bool ok = robot.PlaceOn(surface);
        if (!ok) { Fail("place failed"); yield break; }

        // 실제 내려놓을 때까지 대기
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

        // 짧은 프레임 양보
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
