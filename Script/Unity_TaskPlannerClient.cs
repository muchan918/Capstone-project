using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

/// <summary>
/// Unity client for TaskMaker API communication
/// Network communication module for team project
/// </summary>
public class TaskPlannerClient : MonoBehaviour
{
    [Header("Server Configuration")]
    [SerializeField] private string serverIP = "localhost";  // TaskMaker server IP (for local testing)
    [SerializeField] private int serverPort = 5000;
    [SerializeField] private float timeoutSeconds = 30f;

    [Header("Debug Settings")]
    [SerializeField] private bool enableDebugLogs = true;
    [SerializeField] private bool includeDebugInfo = false;

    // Event delegates
    public static event System.Action<PlanResponse> OnPlanReceived;
    public static event System.Action<string> OnPlanningError;
    public static event System.Action<bool> OnConnectionStatusChanged;

    // Properties
    public bool IsConnected { get; private set; }
    public string ServerURL => $"http://{serverIP}:{serverPort}";

    // Cached last response
    public PlanResponse LastResponse { get; private set; }

    void Start()
    {
        // Check server connection status
        StartCoroutine(CheckServerHealth());
    }

    /// <summary>
    /// Request robot task planning with natural language input
    /// </summary>
    /// <param name="userInput">User natural language input</param>
    /// <param name="sessionId">Session ID (optional)</param>
    public void RequestTaskPlanning(string userInput, string sessionId = "unity_session")
    {
        if (string.IsNullOrEmpty(userInput))
        {
            LogError("User input is empty.");
            OnPlanningError?.Invoke("Empty user input");
            return;
        }

        LogInfo($"Task planning request: '{userInput}'");
        StartCoroutine(SendPlanRequest(userInput, sessionId));
    }

    /// <summary>
    /// Process multiple natural language inputs at once
    /// </summary>
    public void RequestBatchPlanning(List<string> userInputs, string sessionId = "unity_batch")
    {
        if (userInputs == null || userInputs.Count == 0)
        {
            LogError("Batch input is empty.");
            return;
        }

        LogInfo($"Batch planning request: {userInputs.Count} items");
        StartCoroutine(SendBatchPlanRequest(userInputs, sessionId));
    }

    /// <summary>
    /// Check server status
    /// </summary>
    public void CheckServerConnection()
    {
        StartCoroutine(CheckServerHealth());
    }

    #region HTTP Communication Implementation

    private IEnumerator SendPlanRequest(string userInput, string sessionId)
    {
        // Configure request data
        var requestData = new PlanRequest
        {
            user_input = userInput,
            session_id = sessionId,
            options = new PlanOptions
            {
                verbose = enableDebugLogs,
                include_debug = includeDebugInfo
            }
        };

        string jsonData = JsonConvert.SerializeObject(requestData, Formatting.None);
        LogDebug($"Request JSON: {jsonData}");

        // Create HTTP request
        using (UnityWebRequest request = new UnityWebRequest($"{ServerURL}/plan", "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = (int)timeoutSeconds;

            // Allow HTTP connections (bypass SSL certificate validation)
            request.certificateHandler = new AcceptAllCertificates();

            // Send request
            yield return request.SendWebRequest();

            // Process response
            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseText = request.downloadHandler.text;
                LogDebug($"Response JSON: {responseText}");

                try
                {
                    var response = JsonConvert.DeserializeObject<PlanResponse>(responseText);
                    LastResponse = response;

                    if (response.success)
                    {
                        LogInfo($"Planning successful: {response.plan_sequence.Count} steps generated");
                        OnPlanReceived?.Invoke(response);
                    }
                    else
                    {
                        LogError($"Planning failed: {response.error}");
                        OnPlanningError?.Invoke(response.error);
                    }
                }
                catch (Exception e)
                {
                    LogError($"Response parsing failed: {e.Message}");
                    OnPlanningError?.Invoke($"Response parsing failed: {e.Message}");
                }
            }
            else
            {
                string errorMsg = $"HTTP request failed: {request.error}";
                LogError(errorMsg);
                OnPlanningError?.Invoke(errorMsg);
            }
        }
    }

    private IEnumerator SendBatchPlanRequest(List<string> userInputs, string sessionId)
    {
        var requestData = new BatchPlanRequest
        {
            batch_inputs = userInputs,
            session_id = sessionId
        };

        string jsonData = JsonConvert.SerializeObject(requestData);

        using (UnityWebRequest request = new UnityWebRequest($"{ServerURL}/batch_plan", "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = (int)(timeoutSeconds * userInputs.Count); // Longer timeout for batch

            // Allow HTTP connections (bypass SSL certificate validation)
            request.certificateHandler = new AcceptAllCertificates();

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var response = JsonConvert.DeserializeObject<BatchPlanResponse>(request.downloadHandler.text);
                LogInfo($"Batch planning completed: {response.summary.successful}/{response.summary.total_requests} successful");

                // Process individual results
                foreach (var result in response.results)
                {
                    if (result.status == "SUCCESS")
                    {
                        // Convert each to individual PlanResponse and trigger event
                        var planResponse = new PlanResponse
                        {
                            success = true,
                            plan_sequence = result.plan_sequence,
                            metadata = new PlanMetadata
                            {
                                original_input = result.input,
                                status = result.status,
                                processing_time = result.processing_time
                            }
                        };
                        OnPlanReceived?.Invoke(planResponse);
                    }
                }
            }
            else
            {
                LogError($"Batch request failed: {request.error}");
                OnPlanningError?.Invoke(request.error);
            }
        }
    }

    private IEnumerator CheckServerHealth()
    {
        LogInfo($"Starting server connection check: {ServerURL}");

        using (UnityWebRequest request = UnityWebRequest.Get($"{ServerURL}/health"))
        {
            request.timeout = 10; // Longer timeout (considering network environment)

            // Allow HTTP connections (bypass SSL certificate validation)
            request.certificateHandler = new AcceptAllCertificates();

            // Log status before request
            LogDebug($"Sending server request: {request.url}");

            yield return request.SendWebRequest();

            bool previousStatus = IsConnected;

            // More detailed connection status check
            bool newStatus = false;
            string statusDetails = "";

            switch (request.result)
            {
                case UnityWebRequest.Result.Success:
                    newStatus = true;
                    statusDetails = $"Success (response code: {request.responseCode})";
                    break;
                case UnityWebRequest.Result.ConnectionError:
                    newStatus = false;
                    statusDetails = $"Connection error: {request.error}";
                    break;
                case UnityWebRequest.Result.ProtocolError:
                    newStatus = false;
                    statusDetails = $"Protocol error: {request.error} (response code: {request.responseCode})";
                    break;
                case UnityWebRequest.Result.DataProcessingError:
                    newStatus = false;
                    statusDetails = $"Data processing error: {request.error}";
                    break;
                default:
                    newStatus = false;
                    statusDetails = $"Unknown error: {request.result}";
                    break;
            }

            IsConnected = newStatus;

            // Status change notification
            if (IsConnected != previousStatus)
            {
                LogInfo($"Server connection status changed: {(IsConnected ? "Connected" : "Disconnected")} - {statusDetails}");
                OnConnectionStatusChanged?.Invoke(IsConnected);
            }

            // Final result logging
            if (IsConnected)
            {
                LogInfo($"Server connection successful: {ServerURL} - {statusDetails}");
            }
            else
            {
                LogError($"Server connection failed: {ServerURL} - {statusDetails}");
                LogError($"   Check network settings: IP({serverIP}), Port({serverPort})");
                LogError($"   Please check if server is running.");
            }
        }
    }

    #endregion

    #region Data Classes

    [Serializable]
    public class PlanRequest
    {
        public string user_input;
        public string session_id;
        public PlanOptions options;
    }

    [Serializable]
    public class PlanOptions
    {
        public bool verbose;
        public bool include_debug;
    }

    [Serializable]
    public class BatchPlanRequest
    {
        public List<string> batch_inputs;
        public string session_id;
    }

    [Serializable]
    public class PlanResponse
    {
        public bool success;
        public List<string> plan_sequence;
        public PlanMetadata metadata;
        public string error;
        public object debug_info;
    }

    [Serializable]
    public class PlanMetadata
    {
        public float processing_time;
        public string status;
        public int steps_count;
        public string original_input;
        public string session_id;
        public string timestamp;
    }

    [Serializable]
    public class BatchPlanResponse
    {
        public bool success;
        public List<BatchPlanResult> results;
        public BatchSummary summary;
    }

    [Serializable]
    public class BatchPlanResult
    {
        public int index;
        public string input;
        public List<string> plan_sequence;
        public string status;
        public float processing_time;
        public string error;
    }

    [Serializable]
    public class BatchSummary
    {
        public int total_requests;
        public int successful;
        public int failed;
    }

    #endregion

    #region Logging Helper

    private void LogInfo(string message)
    {
        if (enableDebugLogs)
            Debug.Log($"[TaskPlannerClient] {message}");
    }

    private void LogError(string message)
    {
        Debug.LogError($"[TaskPlannerClient] {message}");
    }

    private void LogDebug(string message)
    {
        if (enableDebugLogs)
            Debug.Log($"[TaskPlannerClient] DEBUG: {message}");
    }

    #endregion
}

/// <summary>
/// Certificate handler that accepts all certificates (for HTTP development)
/// WARNING: Only use in development! Not secure for production!
/// </summary>
public class AcceptAllCertificates : CertificateHandler
{
    protected override bool ValidateCertificate(byte[] certificateData)
    {
        // Accept all certificates (bypass SSL validation)
        return true;
    }
}

/// <summary>
/// TaskPlannerClient usage example
/// </summary>
public class TaskPlannerExample : MonoBehaviour
{
    [SerializeField] private TaskPlannerClient plannerClient;
    [SerializeField] private string testInput = "pick laptop from lab";

    void Start()
    {
        // Subscribe to events
        TaskPlannerClient.OnPlanReceived += HandlePlanReceived;
        TaskPlannerClient.OnPlanningError += HandlePlanningError;
        TaskPlannerClient.OnConnectionStatusChanged += HandleConnectionChanged;
    }

    void OnDestroy()
    {
        // Unsubscribe from events
        TaskPlannerClient.OnPlanReceived -= HandlePlanReceived;
        TaskPlannerClient.OnPlanningError -= HandlePlanningError;
        TaskPlannerClient.OnConnectionStatusChanged -= HandleConnectionChanged;
    }

    [ContextMenu("Test Planning")]
    public void TestPlanning()
    {
        plannerClient.RequestTaskPlanning(testInput);
    }

    private void HandlePlanReceived(TaskPlannerClient.PlanResponse response)
    {
        Debug.Log($"Plan received: {response.plan_sequence.Count} steps");

        // Pass plan to robot control code
        foreach (string step in response.plan_sequence)
        {
            Debug.Log($"  Step to execute: {step}");
            // Call actual robot control logic
            // RobotController.ExecuteStep(step);
        }
    }

    private void HandlePlanningError(string error)
    {
        Debug.LogError($"Planning error: {error}");
        // Error handling logic
    }

    private void HandleConnectionChanged(bool isConnected)
    {
        Debug.Log($"Server connection status: {(isConnected ? "Connected" : "Disconnected")}");
        // UI updates etc.
    }
}
