using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class CalcManager : MonoBehaviour
{
    public TMP_InputField inputField;  // CommandInput
    public TextMeshProUGUI resultText; // Result
    public RobotMove robot;

    // Start is called before the first frame update
    void Start()
    {
        inputField.onEndEdit.AddListener(OnInputSubmitted);
    }
    void OnInputSubmitted(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        string cmd = text.ToLower().Trim();
        resultText.text = cmd;

        if (cmd == "move")
        {
            robot.MoveForward();
        }

        inputField.text = "";
        inputField.ActivateInputField();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
