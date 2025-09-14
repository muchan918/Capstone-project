using UnityEngine;
using System.Net.Sockets;
using System.Text;
using TMPro;

public class CalcClient : MonoBehaviour
{
    public TMP_InputField inputField;    // Inspector에 연결
    public TMP_Text resultText;          // Inspector에 연결
    TcpClient client;
    NetworkStream nstream;

    void Start()
    {
        client = new TcpClient("127.0.0.1", 25001);
        nstream = client.GetStream();
        inputField.onEndEdit.AddListener(OnInputSubmitted);
    }

    void OnInputSubmitted(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        byte[] buffer = Encoding.UTF8.GetBytes(text + "\n");
        nstream.Write(buffer, 0, buffer.Length);

        // Python에서 결과 수신 (한줄 읽기)
        StringBuilder sb = new StringBuilder();
        int b;
        while ((b = nstream.ReadByte()) != -1)
        {
            char c = (char)b;
            if (c == '\n') break;
            sb.Append(c);
        }
        string result = sb.ToString();
        resultText.text = result;
        inputField.text = ""; // 입력창 초기화
    }

    void OnDestroy()
    {
        nstream?.Close();
        client?.Close();
    }
}
