using UnityEngine;
using System.Net.Sockets;
using System.Text;
using TMPro;

public class CalcClient : MonoBehaviour
{
    public TMP_InputField inputField;    // Inspector�� ����
    public TMP_Text resultText;          // Inspector�� ����
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

        // Python���� ��� ���� (���� �б�)
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
        inputField.text = ""; // �Է�â �ʱ�ȭ
    }

    void OnDestroy()
    {
        nstream?.Close();
        client?.Close();
    }
}
