using UnityEngine;
using System;
using System.Collections.Generic;

public class ResponseAnalyzer : MonoBehaviour
{
    // 感情の種類を定義
    public enum Emotion
    {
        Neutral,
        Happy,
        Sad,
        Angry,
        Surprised,
        Thinking
    }

    // キーワードと感情の対応表
    private Dictionary<string, Emotion> emotionKeywords = new Dictionary<string, Emotion>()
    {
        // ポジティブな感情
        {"ありがとう", Emotion.Happy},
        {"嬉しい", Emotion.Happy},
        {"楽しい", Emotion.Happy},
        {"素晴らしい", Emotion.Happy},
        {"良い", Emotion.Happy},
        {"happy", Emotion.Happy},
        
        // ネガティブな感情
        {"申し訳", Emotion.Sad},
        {"ごめん", Emotion.Sad},
        {"悲しい", Emotion.Sad},
        {"難しい", Emotion.Sad},
        {"sad", Emotion.Sad},
        
        // 怒りの感情
        {"怒", Emotion.Angry},
        {"困る", Emotion.Angry},
        {"だめ", Emotion.Angry},
        {"angry", Emotion.Angry},
        
        // 驚きの感情
        {"え", Emotion.Surprised},
        {"まあ", Emotion.Surprised},
        {"すごい", Emotion.Surprised},
        {"驚", Emotion.Surprised},
        {"!!", Emotion.Surprised},
        {"！！", Emotion.Surprised},
        {"surprised", Emotion.Surprised},
        
        // 思考の感情
        {"考え", Emotion.Thinking},
        {"思う", Emotion.Thinking},
        {"検討", Emotion.Thinking},
        {"thinking", Emotion.Thinking}
    };

    // デバッグ設定
    [SerializeField] private bool debugMode = true;

    private void Awake()
    {
        DebugLog("ResponseAnalyzer: Initialized");
    }

    // 回答内容を解析して感情を判定
    public Emotion AnalyzeResponse(string response)
    {
        if (string.IsNullOrEmpty(response))
        {
            Debug.LogWarning("ResponseAnalyzer: Received empty response to analyze");
            return Emotion.Neutral;
        }
        
        DebugLog($"ResponseAnalyzer: 受信メッセージ '{response.Substring(0, System.Math.Min(50, response.Length))}...' を分析開始");

        // WebGLから直接送られた感情タグをチェック
        string responseLower = response.ToLower();
        
        if (responseLower.StartsWith("happy "))
        {
            Debug.Log("ResponseAnalyzer: 'happy'タグを検出 (WebGL)");
            return Emotion.Happy;
        }
        else if (responseLower.StartsWith("sad "))
        {
            Debug.Log("ResponseAnalyzer: 'sad'タグを検出 (WebGL)");
            return Emotion.Sad;
        }
        else if (responseLower.StartsWith("angry "))
        {
            Debug.Log("ResponseAnalyzer: 'angry'タグを検出 (WebGL)");
            return Emotion.Angry;
        }
        else if (responseLower.StartsWith("surprised "))
        {
            Debug.Log("ResponseAnalyzer: 'surprised'タグを検出 (WebGL)");
            return Emotion.Surprised;
        }
        else if (responseLower.StartsWith("thinking "))
        {
            Debug.Log("ResponseAnalyzer: 'thinking'タグを検出 (WebGL)");
            return Emotion.Thinking;
        }
        else if (responseLower.StartsWith("talkingneutral "))
        {
            // ★★★ 修正：TalkingNeutral状態の検出を追加 ★★★
            Debug.Log("ResponseAnalyzer: 'talkingneutral'タグを検出 (WebGL)");
            return Emotion.Neutral; // TalkingNeutralはNeutralの特殊版
        }

        // 直接感情指定があるかチェック
        if (response.Contains("EMOTION:"))
        {
            int startIndex = response.IndexOf("EMOTION:") + 8;
            int endIndex = response.IndexOf("\n", startIndex);
            if (endIndex == -1) endIndex = response.Length;
            
            string emotionTag = response.Substring(startIndex, endIndex - startIndex).Trim().ToLower();
            DebugLog($"ResponseAnalyzer: 感情タグを検出: '{emotionTag}'");
            
            switch (emotionTag)
            {
                case "happy":
                    DebugLog("ResponseAnalyzer: タグから'Happy'感情を検出");
                    return Emotion.Happy;
                case "sad":
                    DebugLog("ResponseAnalyzer: タグから'Sad'感情を検出");
                    return Emotion.Sad;
                case "angry":
                    DebugLog("ResponseAnalyzer: タグから'Angry'感情を検出");
                    return Emotion.Angry;
                case "surprised":
                    DebugLog("ResponseAnalyzer: タグから'Surprised'感情を検出");
                    return Emotion.Surprised;
                case "thinking":
                    DebugLog("ResponseAnalyzer: タグから'Thinking'感情を検出");
                    return Emotion.Thinking;
                case "talkingneutral":
                    // ★★★ 修正：TalkingNeutral状態の検出を追加 ★★★
                    DebugLog("ResponseAnalyzer: タグから'TalkingNeutral'感情を検出");
                    return Emotion.Neutral; // TalkingNeutralはNeutralの特殊版
            }
        }

        // デフォルトは中立
        Emotion detectedEmotion = Emotion.Neutral;
        
        // 各キーワードをチェック
        foreach (var keyword in emotionKeywords)
        {
            if (responseLower.Contains(keyword.Key.ToLower()))
            {
                detectedEmotion = keyword.Value;
                DebugLog($"ResponseAnalyzer: キーワード '{keyword.Key}' から '{detectedEmotion}' 感情を検出");
                break;
            }
        }

        DebugLog($"ResponseAnalyzer: 最終的に検出した感情: {detectedEmotion}");
        return detectedEmotion;
    }

    // 外部から直接感情を指定する場合（開発用）
    public Emotion GetEmotionFromString(string emotionString)
    {
        if (string.IsNullOrEmpty(emotionString)) return Emotion.Neutral;
        
        emotionString = emotionString.ToLower().Trim();
        
        switch (emotionString)
        {
            case "happy":
                return Emotion.Happy;
            case "sad":
                return Emotion.Sad;
            case "angry":
                return Emotion.Angry;
            case "surprised":
                return Emotion.Surprised;
            case "thinking":
                return Emotion.Thinking;
            // ★★★ 修正：TalkingNeutralの場合も対応 ★★★
            case "talkingneutral":
                return Emotion.Neutral; // TalkingNeutralはNeutralと同じ感情で会話状態のフラグを別途設定
            default:
                return Emotion.Neutral;
        }
    }

    // 感情に応じたアニメーション名を取得
    public string GetAnimationForEmotion(Emotion emotion)
    {
        switch (emotion)
        {
            case Emotion.Happy:
                return "Happy";
            case Emotion.Sad:
                return "Sad";
            case Emotion.Angry:
                return "Angry";
            case Emotion.Surprised:
                return "Surprised";
            case Emotion.Thinking:
                return "Thinking";
            default:
                return "Idle";
        }
    }

    // 感情に応じた表情パラメータを取得
    public int GetExpressionForEmotion(Emotion emotion)
    {
        switch (emotion)
        {
            case Emotion.Happy:
                return 1; // Happy expression index
            case Emotion.Sad:
                return 2; // Sad expression index
            case Emotion.Angry:
                return 3; // Angry expression index
            case Emotion.Surprised:
                return 4; // Surprised expression index
            case Emotion.Thinking:
                return 5; // Thinking expression index
            default:
                return 0; // Neutral expression index
        }
    }
    
    // デバッグログ
    private void DebugLog(string message)
    {
        if (debugMode)
        {
            Debug.Log(message);
        }
    }
}