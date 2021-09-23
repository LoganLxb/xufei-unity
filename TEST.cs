using UnityEngine;
using UnityEngine.UI;


public class  TEST : MonoBehaviour
{
    public Text text;
    // Start is called before the first frame update
    XunFeiYuYin xunfei;
   void  Start()
    {
        
        xunfei =XunFeiYuYin.Init("1f857e89", "YjM1OTA2NGFiZGM0NTY4YTQ4MzgyOTY3", "28d143ef56056a374ebf25984059661f");
        xunfei.RecognitionCompletionEvent += SpeechRecognitionResult;
    }


    

    public void StartRecognition()
    {
        xunfei.StartRecognition();
    }
    public void StopRecognition()
    {
        StartCoroutine(xunfei.StopRecognition());
    }
    
    public void SpeechRecognitionResult(string result)
    {
        text.text += "\n语音识别结束，结果:" + result;
    }

    


}
