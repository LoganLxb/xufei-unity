using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Android;

public class XunFeiYuYin : MonoBehaviour
{
    
    string APPID = "1f857e89";
    string APISecret = "YjM1OTA2NGFiZGM0NTY4YTQ4MzgyOTY3";
    string APIKey = "28d143ef56056a374ebf25984059661f";
    public static XunFeiYuYin yuyin;
    public event Action<string> RecognitionCompletionEvent;   //语音识别回调事件
    private AudioClip RecordedClip;
    ClientWebSocket RecognitionWebSocket;
    private void Awake()
    {
        if (yuyin == null)
        {
            yuyin = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        DontDestroyOnLoad(gameObject);
    }
    public static XunFeiYuYin Init(String appkey, string APISecret, string APIKey)
    {
        string name = "讯飞语音";
        if (yuyin == null)
        {
            GameObject g = new GameObject(name);
            g.AddComponent<XunFeiYuYin>();
        }
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);
        }
        yuyin.APPID = appkey;
        yuyin.APISecret = APISecret;
        yuyin.APIKey = APIKey;
        //if (!yuyin.讯飞语音加)Debug.LogWarning("未安装或正确设置讯飞语音+将使用在线收费版讯飞引擎");

        //yuyin.javaClass.CallStatic("设置语音识别参数", new object[] { "language", "zh_cn" });//设置语音识别为中文
        return yuyin;
    }

    string GetUrl(string uriStr)
    {
        Uri uri = new Uri(uriStr);
        string date = DateTime.Now.ToString("r");
        string signature_origin = string.Format("host: " + uri.Host + "\ndate: " + date + "\nGET " + uri.AbsolutePath + " HTTP/1.1");
        HMACSHA256 mac = new HMACSHA256(Encoding.UTF8.GetBytes(APISecret));
        string signature = Convert.ToBase64String(mac.ComputeHash(Encoding.UTF8.GetBytes(signature_origin)));
        string authorization_origin = string.Format("api_key=\"{0}\",algorithm=\"hmac-sha256\",headers=\"host date request-line\",signature=\"{1}\"", APIKey, signature);
        string authorization = Convert.ToBase64String(Encoding.UTF8.GetBytes(authorization_origin));
        string url = string.Format("{0}?authorization={1}&date={2}&host={3}", uri, authorization, date, uri.Host);
        return url;
    }
    #region 语音识别

    public void StartRecognition()
    {
        if (RecognitionWebSocket != null && RecognitionWebSocket.State == WebSocketState.Open)
        {
            Debug.LogWarning("开始语音识别失败！，等待上次识别连接结束");
            return;
        }
        ConnectRecognitionWebSocketWebSocket();
        RecordedClip = Microphone.Start(null, false, 60, 16000);
    }

    public IEnumerator StopRecognition()
    {   
        Microphone.End(null);
        yield return new WaitUntil(()=> RecognitionWebSocket.State != WebSocketState.Open);
        Debug.Log("识别结束，停止录音");
    }

    async void ConnectRecognitionWebSocketWebSocket()
    {
        using (RecognitionWebSocket = new ClientWebSocket())
        {
            CancellationToken ct = new CancellationToken();
            Uri url = new Uri(GetUrl("wss://iat-api.xfyun.cn/v2/iat"));
            await RecognitionWebSocket.ConnectAsync(url, ct);
            Debug.Log("连接成功");
            StartCoroutine(SendRecordingDataStream(RecognitionWebSocket));
            StringBuilder stringBuilder = new StringBuilder();
            while (RecognitionWebSocket.State == WebSocketState.Open)
            {
                var result = new byte[4096];
                await RecognitionWebSocket.ReceiveAsync(new ArraySegment<byte>(result), ct);//接受数据
                List<byte> list = new List<byte>(result);while (list[list.Count - 1] == 0x00) list.RemoveAt(list.Count - 1);//去除空字节
                string str = Encoding.UTF8.GetString(list.ToArray());
                Debug.Log("接收消息：" + str);
                if (string.IsNullOrEmpty(str))
                {
                    return;
                }
                Recognizeddata data = JsonUtility.FromJson<Recognizeddata>(str);
                stringBuilder.Append(GetRecognizedWords(data));
                int status = data.data.status;
                if (status == 2)
                {
                    RecognitionWebSocket.Abort();
                }
            }
            Debug.LogWarning("断开连接");
            string s = stringBuilder.ToString();
            if (!string.IsNullOrEmpty(s))
            {
                RecognitionCompletionEvent?.Invoke(s);
                Debug.LogWarning("识别到声音：" + s);
            }
        }
    }

    [Serializable]
    public class Recognizeddata
    {
        [Serializable]
        public class Data
        {
            [Serializable]
            public class Result
            {
                [Serializable]
                public class Ws
                {
                    [Serializable]
                    public class Cw
                    {
                        public string w;
                    }
                    public Cw[] cw;
                }
                public Ws[] ws;
            }
            public int status;
            public Result result;
        }
        public Data data;
    }

    string GetRecognizedWords(Recognizeddata data)
    {
        StringBuilder stringBuilder = new StringBuilder();
        var ws = data.data.result.ws;
        foreach (var item in ws)
        {
            var cw = item.cw;
            foreach (var w in cw)
            {
                stringBuilder.Append(w.w);
            }
        }    
        return stringBuilder.ToString();
    }

    void SendData(byte[] audio, int status, ClientWebSocket socket)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }
        string audioStr = audio==null ?"": Convert.ToBase64String(audio);
        string message = "{\"common\":{\"app_id\":\"" + APPID + "\"},\"business\":{\"language\":\"zh_cn\",\"domain\":\"iat\",\"accent\":\"mandarin\",\"vad_eos\":2000}," +
            "\"data\":{\"status\":" + status + ",\"encoding\":\"raw\",\"format\":\"audio/L16;rate=16000\",\"audio\":\""+ audioStr + "\"}}";
        Debug.Log("发送消息:" + message);
        socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)), WebSocketMessageType.Binary, true, new CancellationToken()); //发送数据
    }

    IEnumerator SendRecordingDataStream(ClientWebSocket socket)
    {
        yield return new WaitWhile(() => Microphone.GetPosition(null) <= 0);
        float t = 0;
        int position = Microphone.GetPosition(null);
        const float waitTime = 0.04f;//每隔40ms发送音频
        int status = 0;
        int lastPosition = 0;
        const int Maxlength = 640;//最大发送长度
        while (position < RecordedClip.samples && socket.State == WebSocketState.Open)
        {
            t += waitTime;
            yield return new WaitForSecondsRealtime(waitTime);
            if (Microphone.IsRecording(null)) position = Microphone.GetPosition(null);
            Debug.Log("录音时长：" + t + "position=" + position + ",lastPosition=" + lastPosition);
            if (position <= lastPosition)
            {
                Debug.LogWarning("字节流发送完毕！强制结束！");
                break;
            }
            int length = position - lastPosition > Maxlength ? Maxlength : position - lastPosition;
            byte[] date = GetAudioStreamFragment(lastPosition, length, RecordedClip);
            SendData(date, status, socket);
            lastPosition = lastPosition + length;
            status = 1;
        }
        SendData(null, 2, socket);
        //WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "关闭WebSocket连接",new CancellationToken());
        Microphone.End(null);
    }
    public static byte[] GetAudioStreamFragment(int star, int length, AudioClip recordedClip)
    {
        float[] soundata = new float[length];
        recordedClip.GetData(soundata, star);
        int rescaleFactor = 32767;
        byte[] outData = new byte[soundata.Length * 2];
        for (int i = 0; i < soundata.Length; i++)
        {
            short temshort = (short)(soundata[i] * rescaleFactor);
            byte[] temdata = BitConverter.GetBytes(temshort);
            outData[i * 2] = temdata[0];
            outData[i * 2 + 1] = temdata[1];
        }
        return outData;
    }

    #endregion

   


}
