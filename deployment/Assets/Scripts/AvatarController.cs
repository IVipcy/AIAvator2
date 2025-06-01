using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

// WebGL連携用のアバターコントローラー
// WebSocketやWeb APIとの連携部分を含む
public class AvatarController : MonoBehaviour
{
    // 感情コントローラーへの参照
    [SerializeField] private CompleteEmotionController emotionController;
    
    // WebGL通信用のブリッジ
    [SerializeField] private ImprovedWebGLBridge webGLBridge;
    
    // テスト用オプション
    [Header("テスト設定")]
    [SerializeField] private bool testMode = false;
    
    // デバッグモード
    [Header("デバッグ設定")]
    [SerializeField] private bool debugMode = true;
    [SerializeField] private bool logDetailedMessages = false;
    
    // 音声再生処理のバックグラウンド実行フラグ
    [Header("処理設定")]
    [SerializeField] private bool processingInBackground = false;
    [SerializeField] private float responseDelay = 0.1f; // 応答処理の遅延（秒）
    
    // マニュアルテスト用
    [Header("マニュアルテスト")]
    [SerializeField] private bool showTestButtons = true;
    [SerializeField] private Emotion testEmotion = Emotion.Neutral;
    [SerializeField] private bool testTalking = true;
    
    // 現在の表情設定
    private Emotion currentEmotion = Emotion.Neutral;
    private bool isTalking = false;
    
    // 通信・処理中のフラグ
    private bool isProcessingMessage = false;
    private string pendingMessage = "";
    private byte[] pendingAudioData;
    
    // WebSocketの接続状態
    private bool isWebSocketConnected = false;
    
    // 応答オプション
    [Header("応答設定")]
    [SerializeField] private bool useRandomDelay = true;
    [SerializeField] private float minResponseDelay = 0.2f;
    [SerializeField] private float maxResponseDelay = 0.8f;
    
    // WebGL接続状態監視用のタイマー
    private float connectionCheckTimer = 0f;
    private const float CONNECTION_CHECK_INTERVAL = 3f;
    
    // 表情・会話状態の監視
    private float emotionCheckTimer = 0f;
    private const float EMOTION_CHECK_INTERVAL = 0.5f;
    
    // 応答処理用のメッセージキュー
    private Queue<MessageInfo> messageQueue = new Queue<MessageInfo>();
    private Coroutine messageProcessingCoroutine;
    
    // 最後の応答時刻
    private float lastResponseTime = 0f;
    
    // 感情の種類
    public enum Emotion
    {
        Neutral,
        Happy,
        Sad,
        Angry,
        Surprised,
        TalkingNeutral
    }
    
    // メッセージ情報の構造体
    private struct MessageInfo
    {
        public string message;
        public byte[] audioData;
        public Emotion emotion;
        public bool talking;
        
        public MessageInfo(string msg, byte[] audio = null, Emotion emo = Emotion.Neutral, bool isTalking = true)
        {
            message = msg;
            audioData = audio;
            emotion = emo;
            talking = isTalking;
        }
    }
    
    private void Awake()
    {
        Debug.Log("AvatarController: Awake開始");
        
        // 参照確認
        if (emotionController == null)
        {
            emotionController = GetComponent<CompleteEmotionController>();
            if (emotionController == null)
            {
                emotionController = FindObjectOfType<CompleteEmotionController>();
                if (emotionController != null)
                {
                    Debug.Log("AvatarController: EmotionControllerをシーンから自動検出しました");
                }
                else
                {
                    Debug.LogError("AvatarController: EmotionControllerが見つかりません");
                }
            }
        }
        
        // WebGLブリッジの参照確認
        if (webGLBridge == null)
        {
            webGLBridge = FindObjectOfType<ImprovedWebGLBridge>();
            if (webGLBridge != null)
            {
                Debug.Log("AvatarController: WebGLBridgeをシーンから自動検出しました");
            }
        }
        
        // ダミー音声データを初期化（テスト用）
        pendingAudioData = new byte[44100 * 2]; // 44.1kHz, 16bit, 1秒
        
        Debug.Log("AvatarController: Awake完了");
    }
    
    private void Start()
    {
        StartCoroutine(DelayedInitialization());
    }
    
    // 遅延初期化（他のコンポーネントが初期化された後に実行）
    private IEnumerator DelayedInitialization()
    {
        yield return new WaitForSeconds(0.5f);
        
        if (emotionController != null)
        {
            // 初期表情をニュートラルに設定
            SetEmotion(Emotion.Neutral, false);
            
            Debug.Log("アバター状態を初期化しました: Neutral");
        }
        else
        {
            Debug.LogError("EmotionControllerへの参照がありません。表情制御ができません。");
            
            // 再度検索を試みる
            emotionController = FindObjectOfType<CompleteEmotionController>();
            if (emotionController != null)
            {
                Debug.Log("EmotionControllerを再検出しました");
                SetEmotion(Emotion.Neutral, false);
            }
        }
        
        // WebGLブリッジの再検索（必要に応じて）
        if (webGLBridge == null)
        {
            webGLBridge = FindObjectOfType<ImprovedWebGLBridge>();
            if (webGLBridge != null)
            {
                Debug.Log("WebGLBridgeを再検出しました");
            }
        }
    }
    
    private void Update()
    {
        // WebGL接続状態の監視
        if (webGLBridge != null)
        {
            connectionCheckTimer += Time.deltaTime;
            if (connectionCheckTimer >= CONNECTION_CHECK_INTERVAL)
            {
                connectionCheckTimer = 0f;
                CheckWebGLConnection();
            }
        }
        
        // 感情と会話状態の監視
        if (emotionController != null)
        {
            emotionCheckTimer += Time.deltaTime;
            if (emotionCheckTimer >= EMOTION_CHECK_INTERVAL)
            {
                emotionCheckTimer = 0f;
                CheckEmotionAndTalkingState();
            }
        }
        
        // キューに溜まったメッセージを処理
        if (!isProcessingMessage && messageQueue.Count > 0)
        {
            if (messageProcessingCoroutine == null)
            {
                messageProcessingCoroutine = StartCoroutine(ProcessMessageQueue());
            }
        }
        
        // マニュアルテスト用キーボード入力
        if (testMode)
        {
            HandleTestInput();
        }
    }
    
    // 接続状態の確認
    private void CheckWebGLConnection()
    {
        // WebGLブリッジがあれば接続状態を確認
        if (webGLBridge != null)
        {
            bool wasConnected = isWebSocketConnected;
            isWebSocketConnected = true; // 常に接続中と仮定（WebGLでは直接確認不可）
            
            if (isWebSocketConnected != wasConnected)
            {
                if (isWebSocketConnected)
                {
                    Debug.Log("WebSocket接続を確認しました");
                }
                else
                {
                    Debug.LogWarning("WebSocket接続が切断されています");
                }
            }
        }
    }
    
    // 感情と会話状態を確認・修正
    private void CheckEmotionAndTalkingState()
    {
        if (emotionController == null) return;
        
        // 現在の状態を取得
        bool currentTalking = emotionController.isTalking;
        string currentEmotionStr = emotionController.baseEmotion;
        
        // 会話状態のずれを検出
        if (isTalking != currentTalking)
        {
            // WebGLでは会話状態のチェックは簡易的に行う
            #if UNITY_WEBGL && !UNITY_EDITOR
            // WebGLでは基本的に現在の状態を優先（設定と異なる場合のみログ出力）
            if (debugMode)
            {
                Debug.Log($"会話状態に差異: 設定={isTalking}, 現在={currentTalking}");
            }
            #else
            // エディタや非WebGLビルドでは設定と状態に差異がある場合は修正
            if (isTalking && !currentTalking)
            {
                // 会話中なのに会話フラグがOFFになっている - 修正
                emotionController.SetTalkingFlag(true);
                Debug.Log("会話フラグをONに修正しました");
            }
            else if (!isTalking && currentTalking)
            {
                // 会話中でないのに会話フラグがONになっている - 修正
                emotionController.SetTalkingFlag(false);
                Debug.Log("会話フラグをOFFに修正しました");
            }
            #endif
        }
        
        // 感情状態のずれを検出
        Emotion mappedEmotion = MapStringToEmotion(currentEmotionStr, currentTalking);
        if (currentEmotion != mappedEmotion)
        {
            // 感情状態が設定と異なる場合はログ出力
            if (debugMode)
            {
                Debug.Log($"感情状態に差異: 設定={currentEmotion}, 現在={mappedEmotion} (baseEmotion={currentEmotionStr})");
            }
            
            // WebGLでは状態の修正は慎重に行う
            #if UNITY_WEBGL && !UNITY_EDITOR
            if ((currentEmotion == Emotion.TalkingNeutral || mappedEmotion == Emotion.TalkingNeutral) && 
                (currentEmotionStr == "neutral" && currentTalking))
            {
                // TalkingNeutral特殊ケースは放置（自然に修正される）
            }
            else if (Time.time - lastResponseTime > 10f) // 最後の応答から一定時間経過していれば修正
            {
                // 感情状態を再設定
                SetEmotion(currentEmotion, isTalking);
                Debug.Log($"感情状態を再設定: {currentEmotion}, talking={isTalking}");
            }
            #else
            // エディタでは常に修正
            SetEmotion(currentEmotion, isTalking);
            #endif
        }
    }
    
    // 文字列から感情を判断するヘルパーメソッド
    private Emotion MapStringToEmotion(string emotionStr, bool isTalking)
    {
        // 会話中のNeutralはTalkingNeutral扱い
        if (emotionStr.ToLower() == "neutral" && isTalking)
        {
            return Emotion.TalkingNeutral;
        }
        
        switch (emotionStr.ToLower())
        {
            case "happy": return Emotion.Happy;
            case "sad": return Emotion.Sad;
            case "angry": return Emotion.Angry;
            case "surprised": return Emotion.Surprised;
            default: return Emotion.Neutral;
        }
    }
    
    // メッセージをキューに追加
    public void EnqueueMessage(string message, byte[] audioData = null, Emotion emotion = Emotion.Neutral, bool talking = true)
    {
        if (string.IsNullOrEmpty(message))
        {
            Debug.LogWarning("空のメッセージは処理できません");
            return;
        }
        
        // メッセージをキューに追加
        MessageInfo info = new MessageInfo(message, audioData, emotion, talking);
        messageQueue.Enqueue(info);
        
        if (debugMode)
        {
            Debug.Log($"メッセージをキューに追加: {message.Substring(0, Math.Min(30, message.Length))}... (キュー数: {messageQueue.Count})");
        }
        
        // 処理が停止していれば再開
        if (messageProcessingCoroutine == null)
        {
            messageProcessingCoroutine = StartCoroutine(ProcessMessageQueue());
        }
    }
    
    // キューからメッセージを順次処理
    private IEnumerator ProcessMessageQueue()
    {
        while (messageQueue.Count > 0)
        {
            // キューから取り出し
            MessageInfo info = messageQueue.Dequeue();
            
            // 処理中フラグをオン
            isProcessingMessage = true;
            
            // ランダム遅延（自然な会話のため）
            if (useRandomDelay)
            {
                float delay = UnityEngine.Random.Range(minResponseDelay, maxResponseDelay);
                yield return new WaitForSeconds(delay);
            }
            
            // 応答を処理（音声再生と表情変更）
            if (emotionController != null)
            {
                #if UNITY_WEBGL && !UNITY_EDITOR
                // WebGL環境では非同期処理はタスクベースではなくコルーチンで実装
                Debug.Log($"応答処理開始 - 感情: {info.emotion}, 会話: {info.talking}");
                
                // 先に感情を設定してからメッセージを処理
                SetEmotion(info.emotion, info.talking);
                
                // 指定された遅延時間だけ待機
                yield return new WaitForSeconds(responseDelay);
                
                try
                {
                    // メッセージを処理
                    if (emotionController != null)
                    {
                        emotionController.ProcessResponse(info.message, info.audioData);
                    }
                    
                    lastResponseTime = Time.time;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"WebGL環境での応答処理エラー: {ex.Message}");
                }
                #else
                // 非WebGL環境では直接非同期処理を使用
                Debug.Log($"応答処理開始 - 感情: {info.emotion}, 会話: {info.talking}");
                
                // 先に感情を設定
                SetEmotion(info.emotion, info.talking);
                
                // 指定された遅延時間だけ待機
                yield return new WaitForSeconds(responseDelay);
                
                // タスク処理用の変数
                Task responseTask = null;
                bool taskStarted = false;
                
                // 非同期処理を開始
                try
                {
                    if (emotionController != null)
                    {
                        // 非同期メソッドを呼び出し
                        responseTask = emotionController.ProcessResponseAsync(info.message, info.audioData);
                        taskStarted = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"非同期処理の開始でエラー: {ex.Message}");
                    taskStarted = false;
                }
                
                // タスクの完了を待機
                if (taskStarted && responseTask != null)
                {
                    while (!responseTask.IsCompleted)
                    {
                        yield return null;
                    }
                    
                    // タスク完了後のエラーチェック
                    if (responseTask.IsFaulted)
                    {
                        Debug.LogError($"非同期応答処理でエラー: {responseTask.Exception}");
                    }
                }
                
                lastResponseTime = Time.time;
                #endif
            }
            else
            {
                Debug.LogError("EmotionControllerがnullです - 応答処理ができません");
            }
            
            // 次のメッセージ処理前に少し待機
            yield return new WaitForSeconds(0.1f);
            
            // 処理完了
            isProcessingMessage = false;
        }
        
        // コルーチン参照をクリア
        messageProcessingCoroutine = null;
    }
    
    // JavaScript側から呼び出されるメッセージ受信メソッド
    public void OnMessageReceived(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            Debug.LogWarning("空のメッセージを受信しました");
            return;
        }
        
        if (logDetailedMessages)
        {
            Debug.Log($"メッセージ受信: {message.Substring(0, Math.Min(50, message.Length))}...");
        }
        else
        {
            Debug.Log($"メッセージ受信: 長さ={message.Length}文字");
        }
        
        try
        {
            // JSON形式のメッセージの場合
            if (message.StartsWith("{") && message.EndsWith("}"))
            {
                // 感情指定だけのシンプルなJSONの場合
                EmotionMessage emotionMessage = JsonUtility.FromJson<EmotionMessage>(message);
                if (!string.IsNullOrEmpty(emotionMessage.emotion))
                {
                    Emotion parsedEmotion = ParseEmotion(emotionMessage.emotion);
                    SetEmotion(parsedEmotion, emotionMessage.talking);
                    Debug.Log($"感情を設定: {parsedEmotion}, 会話状態: {emotionMessage.talking}");
                    return;
                }
                
                // 完全なレスポンスJSONの場合
                ResponseMessage responseMsg = JsonUtility.FromJson<ResponseMessage>(message);
                
                // 音声データを変換
                byte[] audioData = null;
                if (!string.IsNullOrEmpty(responseMsg.audio))
                {
                    try
                    {
                        audioData = ProcessAudioData(responseMsg.audio);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"音声データ処理エラー: {ex.Message}");
                    }
                }
                
                // 感情を判断
                Emotion emotionValue = ParseEmotion(responseMsg.emotion);
                
                // メッセージをキューに追加
                EnqueueMessage(responseMsg.message, audioData, emotionValue, true);
            }
            else
            {
                // 通常のテキストメッセージ
                EnqueueMessage(message, null, Emotion.TalkingNeutral, true);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"メッセージ処理エラー: {ex.Message}");
            
            // エラーが発生しても最低限の処理を行う
            try
            {
                EnqueueMessage(message, null, Emotion.TalkingNeutral, true);
            }
            catch (Exception e)
            {
                Debug.LogError($"緊急フォールバック処理中にエラー: {e.Message}");
            }
        }
    }
    
    // 音声データを処理するメソッド
    private byte[] ProcessAudioData(string base64Audio)
    {
        if (string.IsNullOrEmpty(base64Audio))
        {
            return null;
        }
        
        try
        {
            // データURLのプレフィックスを削除
            string purifiedBase64 = base64Audio;
            if (base64Audio.StartsWith("data:"))
            {
                int commaIndex = base64Audio.IndexOf(',');
                if (commaIndex > 0)
                {
                    purifiedBase64 = base64Audio.Substring(commaIndex + 1);
                }
            }
            
            // Base64をバイトに変換
            return Convert.FromBase64String(purifiedBase64);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Base64音声データの変換エラー: {ex.Message}");
            return null;
        }
    }
    
    // 文字列から感情列挙型へ変換
    private Emotion ParseEmotion(string emotionStr)
    {
        if (string.IsNullOrEmpty(emotionStr))
        {
            return Emotion.Neutral;
        }
        
        switch (emotionStr.ToLower())
        {
            case "happy": return Emotion.Happy;
            case "sad": return Emotion.Sad;
            case "angry": return Emotion.Angry;
            case "surprised": return Emotion.Surprised;
            case "talkingneutral": return Emotion.TalkingNeutral;
            default: return Emotion.Neutral;
        }
    }
    
    // 口の形状を設定するメソッド（外部から呼び出し可）
    public void SetMouth(string mouthShape, float intensity = 100f)
    {
        if (emotionController == null)
        {
            Debug.LogError("EmotionControllerがnullです - 口の形状を設定できません");
            return;
        }
        
        Debug.Log($"口の形状を設定: {mouthShape}, 強度: {intensity}");
        
        try
        {
            emotionController.SetMouthShape(mouthShape, intensity);
        }
        catch (Exception ex)
        {
            Debug.LogError($"口の形状設定エラー: {ex.Message}");
        }
    }
    
    // 口を閉じるメソッド
    public void CloseMouth()
    {
        if (emotionController == null)
        {
            Debug.LogError("EmotionControllerがnullです - 口を閉じる処理ができません");
            return;
        }
        
        Debug.Log("口を閉じます");
        
        try
        {
            emotionController.CloseMouth();
        }
        catch (Exception ex)
        {
            Debug.LogError($"口を閉じる処理でエラー: {ex.Message}");
        }
    }
    
    // 感情と会話状態を設定するメソッド
    public void SetEmotion(Emotion emotion, bool talking = false)
    {
        if (emotionController == null)
        {
            Debug.LogError("EmotionControllerがnullです - 感情を設定できません");
            return;
        }
        
        // TalkingNeutralを特別処理
        if (emotion == Emotion.TalkingNeutral)
        {
            // TalkingNeutralは常に会話モード
            SetEmotionInternal("neutral", true);
            isTalking = true;
            currentEmotion = Emotion.TalkingNeutral;
            return;
        }
        
        // 感情と会話状態を更新
        string emotionStr = emotion.ToString().ToLower();
        SetEmotionInternal(emotionStr, talking);
        
        // 現在の感情を記録
        currentEmotion = emotion;
        isTalking = talking;
    }
    
    // 内部的な感情設定処理
    private void SetEmotionInternal(string emotion, bool talking)
    {
        if (emotionController == null) return;
        
        #if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL環境では特別な処理
        if (emotion == "neutral" && talking)
        {
            // TalkingNeutralの特別処理
            if (webGLBridge != null)
            {
                // WebGLBridgeを介した強制設定
                webGLBridge.ForceSetState("talkingneutral");
            }
            else
            {
                // 直接設定
                emotionController.SetEmotion(emotion, talking);
            }
        }
        else
        {
            // 通常の感情設定
            emotionController.SetEmotion(emotion, talking);
        }
        #else
        // 非WebGL環境では直接設定
        emotionController.SetEmotion(emotion, talking);
        #endif
    }
    
    // JavaScriptから呼び出せるシンプル版設定メソッド
    public void SetEmotionByName(string emotionName)
    {
        Emotion parsed = Emotion.Neutral;
        bool talking = false;

        if (emotionName.ToLower() == "talkingneutral")
        {
            parsed = Emotion.TalkingNeutral;
            talking = true;
        }
        else
        {
            parsed = ParseEmotion(emotionName);
            talking = false;
        }

        SetEmotion(parsed, talking);
    }
    
    // テスト入力の処理
    private void HandleTestInput()
    {
        // キーボード入力の処理
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            SetEmotion(Emotion.Neutral, false);
            Debug.Log("テスト: Neutral感情に設定");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            SetEmotion(Emotion.Happy, false);
            Debug.Log("テスト: Happy感情に設定");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            SetEmotion(Emotion.Sad, false);
            Debug.Log("テスト: Sad感情に設定");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            SetEmotion(Emotion.Angry, false);
            Debug.Log("テスト: Angry感情に設定");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha5))
        {
            SetEmotion(Emotion.Surprised, false);
            Debug.Log("テスト: Surprised感情に設定");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            SetEmotion(Emotion.TalkingNeutral);
            Debug.Log("テスト: TalkingNeutral感情に設定");
        }
        
        // 会話状態のトグル
        if (Input.GetKeyDown(KeyCode.T))
        {
            isTalking = !isTalking;
            SetEmotion(currentEmotion, isTalking);
            Debug.Log($"テスト: 会話状態を切替 - {isTalking}");
        }
        
        // テストメッセージ送信
        if (Input.GetKeyDown(KeyCode.Space))
        {
            string testMessage = "これはテストメッセージです。アバターの動作テストです。";
            OnMessageReceived(testMessage);
            Debug.Log("テスト: メッセージを送信");
        }
    }
    
    // OnGUIでマニュアルテスト用のUIを表示
    private void OnGUI()
    {
        if (!showTestButtons) return;
        
        float startY = 10;
        float startX = 10;
        float buttonWidth = 120;
        float buttonHeight = 30;
        float spacing = 10;
        
        GUI.Label(new Rect(startX, startY, 300, 20), "アバターテスト機能");
        startY += buttonHeight;
        
        if (GUI.Button(new Rect(startX, startY, buttonWidth, buttonHeight), "Neutral"))
        {
            SetEmotion(Emotion.Neutral, false);
        }
        
        if (GUI.Button(new Rect(startX + buttonWidth + spacing, startY, buttonWidth, buttonHeight), "Happy"))
        {
            SetEmotion(Emotion.Happy, false);
        }
        
        if (GUI.Button(new Rect(startX + (buttonWidth + spacing) * 2, startY, buttonWidth, buttonHeight), "Sad"))
        {
            SetEmotion(Emotion.Sad, false);
        }
        
        startY += buttonHeight + spacing;
        
        if (GUI.Button(new Rect(startX, startY, buttonWidth, buttonHeight), "Angry"))
        {
            SetEmotion(Emotion.Angry, false);
        }
        
        if (GUI.Button(new Rect(startX + buttonWidth + spacing, startY, buttonWidth, buttonHeight), "Surprised"))
        {
            SetEmotion(Emotion.Surprised, false);
        }
        
        if (GUI.Button(new Rect(startX + (buttonWidth + spacing) * 2, startY, buttonWidth, buttonHeight), "TalkingNeutral"))
        {
            SetEmotion(Emotion.TalkingNeutral);
        }
        
        startY += buttonHeight + spacing;
        
        // 会話状態のトグル
        bool newTalking = GUI.Toggle(new Rect(startX, startY, buttonWidth * 2, buttonHeight), isTalking, "会話モード");
        if (newTalking != isTalking)
        {
            isTalking = newTalking;
            SetEmotion(currentEmotion, isTalking);
        }
        
        startY += buttonHeight + spacing;
        
        // 口の形状テスト
        if (GUI.Button(new Rect(startX, startY, buttonWidth, buttonHeight), "口A"))
        {
            SetMouth("Fcl_MTH_A");
        }
        
        if (GUI.Button(new Rect(startX + buttonWidth + spacing, startY, buttonWidth, buttonHeight), "口I"))
        {
            SetMouth("Fcl_MTH_I");
        }
        
        if (GUI.Button(new Rect(startX + (buttonWidth + spacing) * 2, startY, buttonWidth, buttonHeight), "口を閉じる"))
        {
            CloseMouth();
        }
        
        startY += buttonHeight + spacing;
        
        // テストメッセージ送信
        if (GUI.Button(new Rect(startX, startY, buttonWidth * 2, buttonHeight), "テストメッセージ送信"))
        {
            string testMessage = "これはテストメッセージです。アバターの動作テストです。";
            OnMessageReceived(testMessage);
        }
    }
    
    [Serializable]
    private class EmotionMessage
    {
        public string type;
        public string emotion;
        public bool talking;
    }
    
    [Serializable]
    private class ResponseMessage
    {
        public string message;
        public string emotion;
        public string audio;
        public bool talking;
        public string error;
    }
}