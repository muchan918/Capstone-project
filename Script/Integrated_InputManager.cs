using TMPro;
using UnityEngine;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Integrated input manager for TaskMaker server communication
/// Receives natural language input, sends to server, and displays plan results
/// </summary>
public class IntegratedInputManager : MonoBehaviour
{
    [Header("UI Components")]
    public TMP_InputField inputField;      // CommandInput (TMP_InputField)
    public TextMeshProUGUI resultText;     // Result (Text Mesh Pro UGUI)

    [Header("TaskMaker Integration")]
    public TaskPlannerClient taskPlannerClient;  // TaskPlannerClient reference

    [Header("Display Settings")]
    [SerializeField] private bool showDetailedInfo = true;
    [SerializeField] private bool showProcessingTime = true;
    [SerializeField] private Color successColor = Color.green;
    [SerializeField] private Color errorColor = Color.red;
    [SerializeField] private Color processingColor = Color.yellow;

    private string currentSessionId;
    private int requestCounter = 0;

    void Start()
    {
        // Initialize
        currentSessionId = $"unity_session_{System.DateTime.Now:yyyyMMdd_HHmmss}";

        // Subscribe to UI events
        if (inputField != null)
        {
            inputField.onEndEdit.AddListener(OnInputSubmitted);
        }

        // Subscribe to TaskPlannerClient events
        TaskPlannerClient.OnPlanReceived += HandlePlanReceived;
        TaskPlannerClient.OnPlanningError += HandlePlanningError;
        TaskPlannerClient.OnConnectionStatusChanged += HandleConnectionChanged;

        // Show initial status
        ShowMessage("TaskMaker system initializing...", processingColor);

        // Start delayed initialization (async processing issue fix)
        StartCoroutine(DelayedInitialization());
    }

    /// <summary>
    /// Delayed initialization - check server connection after all components are ready
    /// </summary>
    private System.Collections.IEnumerator DelayedInitialization()
    {
        // Wait 1 frame - wait for all components to initialize
        yield return null;

        // Check TaskPlannerClient reference and retry
        int retryCount = 0;
        while (taskPlannerClient == null && retryCount < 5)
        {
            Debug.LogWarning("[IntegratedInputManager] TaskPlannerClient not set. Retrying...");
            ShowMessage($"Finding TaskPlannerClient... ({retryCount + 1}/5)", processingColor);

            // Try to find automatically
            taskPlannerClient = FindObjectOfType<TaskPlannerClient>();

            retryCount++;
            yield return new WaitForSeconds(0.5f);
        }

        if (taskPlannerClient != null)
        {
            ShowMessage("TaskMaker system ready. Enter natural language commands.", processingColor);

            // Wait additional 1 second then check server connection
            yield return new WaitForSeconds(1f);
            taskPlannerClient.CheckServerConnection();
        }
        else
        {
            ShowMessage("TaskPlannerClient not found. Please set reference in Inspector.", errorColor);
        }
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        TaskPlannerClient.OnPlanReceived -= HandlePlanReceived;
        TaskPlannerClient.OnPlanningError -= HandlePlanningError;
        TaskPlannerClient.OnConnectionStatusChanged -= HandleConnectionChanged;
    }

    /// <summary>
    /// Called when Enter key is pressed in input field
    /// </summary>
    void OnInputSubmitted(string text)
    {
        // Check for empty input
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowMessage("Input is empty. Please enter natural language commands.", errorColor);
            return;
        }

        // Check TaskPlannerClient
        if (taskPlannerClient == null)
        {
            ShowMessage("TaskPlannerClient is not set.", errorColor);
            return;
        }

        // Clean input
        string userInput = text.Trim();
        requestCounter++;

        // Show processing status
        ShowMessage($"Processing... '{userInput}'", processingColor);

        // Check connection status again and send request
        StartCoroutine(ProcessUserInput(userInput));

        // Reset and reactivate input field
        inputField.text = "";
        inputField.ActivateInputField();
    }

    /// <summary>
    /// Process user input coroutine - recheck connection status then send request
    /// </summary>
    private System.Collections.IEnumerator ProcessUserInput(string userInput)
    {
        // Recheck connection status
        if (!taskPlannerClient.IsConnected)
        {
            ShowMessage("Checking server connection status...", processingColor);
            taskPlannerClient.CheckServerConnection();

            // Wait briefly for connection check
            yield return new WaitForSeconds(2f);
        }

        // Send request after final connection status check
        if (taskPlannerClient.IsConnected)
        {
            string sessionId = $"{currentSessionId}_req{requestCounter:000}";
            taskPlannerClient.RequestTaskPlanning(userInput, sessionId);
        }
        else
        {
            ShowMessage("Cannot connect to server. Please check network and server status.", errorColor);

            // Retry guidance
            yield return new WaitForSeconds(1f);
            ShowMessage("To retry, enter again or call RetryConnection() method.", Color.cyan);
        }
    }

    /// <summary>
    /// 플랜 수신 성공 시 호출
    /// </summary>
    private void HandlePlanReceived(TaskPlannerClient.PlanResponse response)
    {
        Debug.Log($"[IntegratedInputManager] 플랜 수신: {response.plan_sequence.Count}개 스텝");

        // 결과 화면에 표시
        StringBuilder result = new StringBuilder();

        // 헤더 정보
        if (showDetailedInfo && response.metadata != null)
        {
            result.AppendLine($"플래닝 성공!");
            result.AppendLine($"입력: {response.metadata.original_input}");
            result.AppendLine($"상태: {response.metadata.status}");

            if (showProcessingTime)
            {
                result.AppendLine($"처리시간: {response.metadata.processing_time:F2}초");
            }

            result.AppendLine($"생성된 스텝: {response.plan_sequence.Count}개");
            result.AppendLine("");
        }

        // 플랜 스텝들 표시
        result.AppendLine("실행 계획:");
        for (int i = 0; i < response.plan_sequence.Count; i++)
        {
            result.AppendLine($"  {i + 1}. {response.plan_sequence[i]}");
        }

        // 추가 정보
        if (response.plan_sequence.Count > 0)
        {
            result.AppendLine("");
            result.AppendLine("로봇이 위 순서대로 작업을 수행합니다.");
        }

        ShowMessage(result.ToString(), successColor);

        // 실제 로봇 제어는 여기서 구현 가능
        // ExecuteRobotPlan(response.plan_sequence);
    }

    /// <summary>
    /// 플래닝 오류 시 호출
    /// </summary>
    private void HandlePlanningError(string error)
    {
        Debug.LogError($"[IntegratedInputManager] 플래닝 오류: {error}");

        StringBuilder result = new StringBuilder();
        result.AppendLine("플래닝 실패");
        result.AppendLine($"오류: {error}");
        result.AppendLine("");
        result.AppendLine("다시 시도해보세요:");
        result.AppendLine("'pick laptop from lab'");
        result.AppendLine("'move to classroom'");
        result.AppendLine("'place book on desk'");

        ShowMessage(result.ToString(), errorColor);
    }

    /// <summary>
    /// Called when server connection status changes
    /// </summary>
    private void HandleConnectionChanged(bool isConnected)
    {
        Debug.Log($"[IntegratedInputManager] Server connection status: {(isConnected ? "Connected" : "Disconnected")}");

        if (isConnected)
        {
            ShowMessage("Connected to TaskMaker server!", successColor);
        }
        else
        {
            ShowMessage("Server connection lost. Attempting to reconnect...", errorColor);
        }
    }

    /// <summary>
    /// Display message in result text
    /// </summary>
    private void ShowMessage(string message, Color color)
    {
        if (resultText != null)
        {
            resultText.text = message;
            resultText.color = color;
        }

        Debug.Log($"[IntegratedInputManager] {message}");
    }

    /// <summary>
    /// Retry server connection (can be called from buttons etc.)
    /// </summary>
    public void RetryConnection()
    {
        if (taskPlannerClient != null)
        {
            ShowMessage("Retrying server connection...", processingColor);
            taskPlannerClient.CheckServerConnection();
        }
    }

    /// <summary>
    /// Input example command (can be called from buttons etc.)
    /// </summary>
    public void InputExampleCommand(string command)
    {
        if (inputField != null)
        {
            inputField.text = command;
            inputField.ActivateInputField();
        }
    }

    /// <summary>
    /// Execute actual robot control (implement when needed)
    /// </summary>
    private void ExecuteRobotPlan(List<string> planSequence)
    {
        // Implement actual robot control logic here
        Debug.Log($"[IntegratedInputManager] Executing robot plan: {planSequence.Count} steps");

        foreach (string step in planSequence)
        {
            Debug.Log($"  Executing: {step}");
            // TODO: Add actual robot control code
            // Example: ParseAndExecuteStep(step);
        }
    }

    /// <summary>
    /// For testing in Unity Inspector
    /// </summary>
    [ContextMenu("Test Natural Language Input")]
    public void TestNaturalLanguageInput()
    {
        string[] testCommands = {
            "pick laptop from lab",
            "move to classroom",
            "place laptop on desk",
            "prepare experiment setup"
        };

        string randomCommand = testCommands[Random.Range(0, testCommands.Length)];
        OnInputSubmitted(randomCommand);
    }

    #region UI Helper Methods

    /// <summary>
    /// Focus on input field
    /// </summary>
    public void FocusInputField()
    {
        if (inputField != null)
        {
            inputField.ActivateInputField();
        }
    }

    /// <summary>
    /// Clear input field
    /// </summary>
    public void ClearInputField()
    {
        if (inputField != null)
        {
            inputField.text = "";
        }
    }

    /// <summary>
    /// Clear result text
    /// </summary>
    public void ClearResultText()
    {
        ShowMessage("Waiting for input...", Color.white);
    }

    #endregion
}

/// <summary>
/// Example command component for UI buttons
/// </summary>
public class ExampleCommandButton : MonoBehaviour
{
    [SerializeField] private IntegratedInputManager inputManager;
    [SerializeField] private string exampleCommand = "pick laptop from lab";

    public void OnButtonClick()
    {
        if (inputManager != null)
        {
            inputManager.InputExampleCommand(exampleCommand);
        }
    }
}
