using UnityEngine;
using System.Runtime.InteropServices;
using System;
using System.Text;
using System.Collections;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

#if USING_WEBSOCKETSHARP
using WebSocketSharp;
#elif USING_NATIVEWEBSOCKET
using NativeWebSocket;
#else
// デフォルトのWebSocket実装がない場合
#endif

public class ImprovedWebGLBridge : MonoBehaviour
{
    // JavaScript関数のインポート（WebGLビルド用）
    [DllImport("__Internal")]
    private static extern void SendMessageToServer(string message);

    // シングルトンインスタンス
    private static ImprovedWebGLBridge instance;
    private static bool isQuitting = false;

    public static ImprovedWebGLBridge Instance
    {
        get
        {
            if (isQuitting)
            {
                return null;
            }

            if (instance == null)
            {
                instance = FindObjectOfType<ImprovedWebGLBridge>();
                if (instance == null)
                {
                    GameObject go = new GameObject("ImprovedWebGLBridge");
                    instance = go.AddComponent<ImprovedWebGLBridge>();
                    DontDestroyOnLoad(go);
                }
            }
            return instance;
        }
    }

    // Inspectorから手動で設定できるように追加
    [Header("References")]
    [SerializeField]
    public CompleteEmotionController manualEmotionController;

    // 感情コントローラーの参照
    private CompleteEmotionController emotionController;

    // WebSocket接続
#if USING_WEBSOCKETSHARP
    private WebSocket websocket;
#elif USING_NATIVEWEBSOCKET
    private WebSocket websocket;
#else
    // デフォルトのWebSocket実装がない場合
#endif

    // WebSocketサーバーのURL
    [SerializeField]
    private string serverUrl = "ws://localhost:5000/socket.io/?EIO=4&transport=websocket";

    // 接続状態
    private bool isConnected = false;
    
    // 再接続用
    [SerializeField]
    private float reconnectDelay = 5f;
    private bool isReconnecting = false;
    [SerializeField]
    private int maxRetryAttempts = 3;

    private Coroutine connectCoroutine;
    private bool isConnecting = false;

    // WebGLビルド用フラグ
    private bool isWebGLBuild = false;

    // デバッグ設定
    [SerializeField] private bool debugMode = true;
    
    // 最後の応答受信時刻
    private float lastResponseTime = 0f;
    
    // 接続確認用タイマー
    private float connectionCheckTimer = 0f;
    private const float CONNECTION_CHECK_INTERVAL = 10f;
    
    // 音声とアニメーション同期用の変数
    private bool isProcessingMessage = false;
    private Queue<MessageData> messageQueue = new Queue<MessageData>();
    
    // メッセージキュー管理用クラス
    private class MessageData
    {
        public string Message { get; set; }
        public byte[] AudioData { get; set; }
        
        public MessageData(string message, byte[] audioData = null)
        {
            Message = message;
            AudioData = audioData;
        }
    }

    // 会話状態の監視
    private bool isTalkSessionActive = false;
    private float sessionTimeout = 10f;  // 音声再生終了後に会話状態を維持する時間
    private float sessionTimer = 0f;    // タイマー
    
    // 強制同期用変数
    private float forceSyncTimer = 0f;
    private float forceSyncInterval = 0.5f; // 0.5秒ごとに強制同期（より頻繁に）
    private bool needsForcedSync = false;

    // 会話終了後の口リセットフラグ
    private bool needsMouthReset = false;
    private float mouthResetDelay = 0.5f; // より迅速に口をリセット
    private float mouthResetTimer = 0f;

    // 口パク用BlendShapeとインデックスの対応
    private Dictionary<string, int> mouthBlendShapes = new Dictionary<string, int>();
    
    // 母音BlendShapeの配列（複数の母音をランダムに使用）
    private string[] vowelBlendShapes = new string[] {
        "Fcl_MTH_A", "Fcl_MTH_I", "Fcl_MTH_U", "Fcl_MTH_E", "Fcl_MTH_O"
    };
    
    // 母音切り替え用タイマー
    private float vowelChangeTimer = 0f;
    private float vowelChangeDuration = 0.12f; // 母音切り替え間隔（より頻繁に）
    private string currentVowelShape = "Fcl_MTH_A";
    private float lastVowelIntensity = 80f;
    
    // 会話状態確認用
    private float talkingCheckTimer = 0f;
    private const float TALKING_CHECK_INTERVAL = 0.2f;
    
    // ダミー音声データ
    private byte[] dummyAudioData;
    
    // 口パク状態の監視と回復
    private bool mouthMovementEnabled = true;
    private int consecutiveErrorCount = 0;
    private const int MAX_ERROR_COUNT = 3;
    
    // 重要な状態リカバリ用
    private float recoveryCheckTimer = 0f;
    private const float RECOVERY_CHECK_INTERVAL = 1.0f;
    private bool needsStateRecovery = false;

    private void Awake()
    {
        // 最初にログを出力
        Debug.Log("ImprovedWebGLBridge: Awake開始");
        
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);

        // ダミー音声データを初期化（約1秒のデータ）
        dummyAudioData = new byte[44100 * 2]; // 44.1kHz, 16bit, 1秒

        // WebGLビルドチェック
        #if UNITY_WEBGL && !UNITY_EDITOR
        isWebGLBuild = true;
        Debug.Log("ImprovedWebGLBridge: WebGL build mode detected");
        Debug.Log("ImprovedWebGLBridge: JavaScriptからの呼び出しを待機中...");
        #endif

        // 感情コントローラーを探す
        FindEmotionController();

        // WebGLの場合はJavaScriptからの呼び出しを待つだけ
        if (!isWebGLBuild)
        {
            StartConnection();
        }
        
        Debug.Log("ImprovedWebGLBridge: Awake完了");
    }

    private void FindEmotionController()
    {
        // まず手動設定を確認
        if (manualEmotionController != null)
        {
            emotionController = manualEmotionController;
            Debug.Log("ImprovedWebGLBridge: Manual EmotionController reference found");
            return;
        }
        
        // 手動設定がない場合は自動検索
        emotionController = FindObjectOfType<CompleteEmotionController>();
        if (emotionController == null)
        {
            Debug.LogWarning("ImprovedWebGLBridge: CompleteEmotionController not found in the scene. Will try to find it later.");
            // シーン内のすべてのオブジェクトを検索
            CompleteEmotionController[] controllers = Resources.FindObjectsOfTypeAll<CompleteEmotionController>();
            if (controllers.Length > 0)
            {
                emotionController = controllers[0];
                Debug.Log($"ImprovedWebGLBridge: Found CompleteEmotionController via Resources: {emotionController.name}");
            }
        }
        else
        {
            Debug.Log($"ImprovedWebGLBridge: Found CompleteEmotionController in the scene: {emotionController.name}");
        }
    }

    private void Start()
    {
        Debug.Log("ImprovedWebGLBridge: Start method called");
        
        #if UNITY_WEBGL && !UNITY_EDITOR
        Debug.Log("ImprovedWebGLBridge: Running in WebGL build");
        Debug.Log($"ImprovedWebGLBridge: Is WebGL Build = {isWebGLBuild}");
        Debug.Log($"ImprovedWebGLBridge: Server URL = {serverUrl}");
        
        // 感情コントローラーの状態を確認
        if (emotionController != null)
        {
            Debug.Log($"ImprovedWebGLBridge: EmotionController is assigned: {emotionController.name}");
            // 初期状態を強制的にneutralに設定
            StartCoroutine(SetInitialEmotion());
            
            // 口パク用BlendShapeを初期化
            InitializeMouthBlendShapes();
        }
        else
        {
            Debug.LogError("ImprovedWebGLBridge: EmotionController is NULL at Start!");
            // 再度検索を試みる
            FindEmotionController();
        }
        #endif
        
        // 重要な状態を初期化
        needsStateRecovery = false;
        recoveryCheckTimer = 0f;
        mouthMovementEnabled = true;
        consecutiveErrorCount = 0;
    }

    // 口パク用BlendShapeを初期化する処理
    private void InitializeMouthBlendShapes()
    {
        if (emotionController == null) return;
        
        // 口パク用のBlendShape名とインデックスを取得
        mouthBlendShapes.Clear();
        
        // 指定された母音BlendShapeを探す
        foreach (string shapeName in vowelBlendShapes)
        {
            // EmotionControllerのBlendShapeマップから取得
            int index = -1;
            
            // それぞれの取得方法を試す
            try {
                index = emotionController.GetBlendShapeIndex(shapeName);
            } catch (Exception) { /* 例外は無視 */ }
            
            // インデックスを取得できなかった場合は直接検索
            if (index < 0)
            {
                SkinnedMeshRenderer faceMeshRenderer = emotionController.GetFaceMeshRenderer();
                if (faceMeshRenderer != null && faceMeshRenderer.sharedMesh != null)
                {
                    // BlendShape名からインデックスを取得
                    for (int i = 0; i < faceMeshRenderer.sharedMesh.blendShapeCount; i++)
                    {
                        if (faceMeshRenderer.sharedMesh.GetBlendShapeName(i) == shapeName)
                        {
                            index = i;
                            break;
                        }
                    }
                }
            }
            
            if (index >= 0)
            {
                mouthBlendShapes[shapeName] = index;
                Debug.Log($"口パク用BlendShape登録: {shapeName} (インデックス: {index})");
            }
        }
        
        // Fcl_MTH_Closeも取得
        int closeIndex = -1;
        try {
            closeIndex = emotionController.GetBlendShapeIndex("Fcl_MTH_Close");
        } catch (Exception) { /* 例外は無視 */ }
        
        if (closeIndex < 0)
        {
            SkinnedMeshRenderer faceMeshRenderer = emotionController.GetFaceMeshRenderer();
            if (faceMeshRenderer != null && faceMeshRenderer.sharedMesh != null)
            {
                for (int i = 0; i < faceMeshRenderer.sharedMesh.blendShapeCount; i++)
                {
                    if (faceMeshRenderer.sharedMesh.GetBlendShapeName(i) == "Fcl_MTH_Close")
                    {
                        closeIndex = i;
                        break;
                    }
                }
            }
        }
        
        if (closeIndex >= 0)
        {
            mouthBlendShapes["Fcl_MTH_Close"] = closeIndex;
            Debug.Log($"口パク用BlendShape登録: Fcl_MTH_Close (インデックス: {closeIndex})");
        }
        
        // 見つからない場合は自動検出に任せる
        if (mouthBlendShapes.Count == 0)
        {
            Debug.LogWarning("口パク用BlendShapeが見つかりませんでした。EmotionControllerの自動検出に任せます。");
        }
        else 
        {
            Debug.Log($"口パク用BlendShape初期化完了: {mouthBlendShapes.Count}個のBlendShapeを検出");
        }
    }

    // 新しいメソッドを追加
    private IEnumerator SetInitialEmotion()
    {
        yield return new WaitForSeconds(1f);
        
        if (emotionController != null)
        {
            Debug.Log("ImprovedWebGLBridge: 初期感情をneutralに設定");
            emotionController.SetEmotion("neutral");
            
            // 念のため口形状もリセット
            ResetMouthShape();
        }
    }

    private void StartConnection()
    {
        if (connectCoroutine == null)
        {
            connectCoroutine = StartCoroutine(ConnectToServer());
        }
    }

    private IEnumerator ConnectToServer()
    {
        if (isConnecting || isReconnecting || isQuitting)
            yield break;

        isConnecting = true;
        DebugLog($"ImprovedWebGLBridge: Connecting to {serverUrl}");

        // WebGLビルドの場合は即時接続成功とする
        if (isWebGLBuild)
        {
            DebugLog("ImprovedWebGLBridge: WebGL build detected, using JavaScript bridge");
            isConnected = true;
            isConnecting = false;
            connectCoroutine = null;
            yield break;
        }

        // WebSocketの初期化と再設定
        CloseExistingConnection();
        
        // 接続前の待機
        yield return new WaitForSeconds(0.1f);

        // WebSocketの接続試行
        bool connectionInitialized = TryConnectWebSocket();
        
        if (connectionInitialized)
        {
            DebugLog("ImprovedWebGLBridge: WebSocket connection initialized");
            
            // 接続完了待機（タイムアウト設定）
            float elapsed = 0f;
            float connectionTimeout = 15f;
            
            while (!isConnected && elapsed < connectionTimeout)
            {
                elapsed += 0.5f;
                yield return new WaitForSeconds(0.5f);
                
                if (elapsed % 5 == 0)
                {
                    DebugLog($"ImprovedWebGLBridge: Waiting for connection... {elapsed}/{connectionTimeout} seconds");
                }
                
                #if USING_WEBSOCKETSHARP
                if (websocket != null && websocket.IsAlive)
                {
                    isConnected = true;
                    break;
                }
                #endif
            }
            
            if (!isConnected)
            {
                Debug.LogError("ImprovedWebGLBridge: Connection timeout or failed");
                
                // ここでは再接続を試行するだけにし、他のモードへの切り替えは行わない
                if (!isQuitting)
                {
                    StartCoroutine(ReconnectCoroutine());
                }
            }
            else
            {
                DebugLog("ImprovedWebGLBridge: Connection successful!");
            }
        }
        else
        {
            Debug.LogError("ImprovedWebGLBridge: Failed to initialize WebSocket");
            
            // 再接続を試みる
            if (!isQuitting)
            {
                StartCoroutine(ReconnectCoroutine());
            }
        }

        isConnecting = false;
        connectCoroutine = null;
    }
    
    // 既存の接続を閉じる
    private bool CloseExistingConnection()
    {
        bool success = true;
        
        #if USING_WEBSOCKETSHARP
        if (websocket != null)
        {
            try
            {
                websocket.Close();
                websocket = null;
                DebugLog("ImprovedWebGLBridge: Closed existing WebSocketSharp connection");
            }
            catch (Exception e)
            {
                Debug.LogError($"ImprovedWebGLBridge: Error closing WebSocketSharp: {e.Message}");
                success = false;
            }
        }
        #elif USING_NATIVEWEBSOCKET
        if (websocket != null)
        {
            try
            {
                websocket.Close();
                websocket = null;
                DebugLog("ImprovedWebGLBridge: Closed existing NativeWebSocket connection");
            }
            catch (Exception e)
            {
                Debug.LogError($"ImprovedWebGLBridge: Error closing NativeWebSocket: {e.Message}");
                success = false;
            }
        }
        #endif
        
        return success;
    }
    
    // WebSocket接続を試みる
    private bool TryConnectWebSocket()
    {
        bool success = false;
        
        #if USING_WEBSOCKETSHARP
        try
        {
            websocket = new WebSocket(serverUrl);
            DebugLog($"ImprovedWebGLBridge: Created WebSocketSharp instance for {serverUrl}");
            
            websocket.EnableRedirection = true;
            
            websocket.OnMessage += (sender, e) =>
            {
                DebugLog($"ImprovedWebGLBridge: Message received: {e.Data}");
                ProcessMessage(e.Data);
                lastResponseTime = Time.time; // 応答受信時刻を記録
            };
            
            websocket.OnOpen += (sender, e) =>
            {
                DebugLog("ImprovedWebGLBridge: WebSocket connected!");
                isConnected = true;
                isReconnecting = false;
                lastResponseTime = Time.time; // 接続成功時刻を記録
            };
            
            websocket.OnError += (sender, e) =>
            {
                Debug.LogError($"ImprovedWebGLBridge: WebSocket error: {e.Message}");
            };
            
            websocket.OnClose += (sender, e) =>
            {
                DebugLog($"ImprovedWebGLBridge: WebSocket closed with code: {e.Code}, reason: {e.Reason}");
                isConnected = false;
                
                // 再接続を試みる
                if (!isQuitting)
                {
                    StartCoroutine(ReconnectCoroutine());
                }
            };
            
            websocket.Connect();
            DebugLog("ImprovedWebGLBridge: WebSocketSharp Connect() called");
            
            success = true;
        }
        catch (Exception e)
        {
            Debug.LogError($"ImprovedWebGLBridge: WebSocketSharp connection error: {e.Message}");
        }
        #elif USING_NATIVEWEBSOCKET
        try
        {
            websocket = new WebSocket(serverUrl);
            DebugLog($"ImprovedWebGLBridge: Created NativeWebSocket instance for {serverUrl}");
            
            websocket.OnOpen += () =>
            {
                DebugLog("ImprovedWebGLBridge: WebSocket connected!");
                isConnected = true;
                isReconnecting = false;
                lastResponseTime = Time.time; // 接続成功時刻を記録
            };
            
            websocket.OnError += (e) =>
            {
                Debug.LogError($"ImprovedWebGLBridge: WebSocket error: {e}");
            };
            
            websocket.OnClose += (e) =>
            {
                DebugLog($"ImprovedWebGLBridge: WebSocket closed with code: {e}");
                isConnected = false;
                
                // 再接続を試みる
                if (!isQuitting)
                {
                    StartCoroutine(ReconnectCoroutine());
                }
            };
            
            websocket.OnMessage += (bytes) =>
            {
                var message = Encoding.UTF8.GetString(bytes);
                DebugLog($"ImprovedWebGLBridge: Message received: {message}");
                ProcessMessage(message);
                lastResponseTime = Time.time; // 応答受信時刻を記録
            };
            
            // 接続開始
            websocket.Connect();
            DebugLog("ImprovedWebGLBridge: NativeWebSocket Connect() called");
            
            success = true;
        }
        catch (Exception e)
        {
            Debug.LogError($"ImprovedWebGLBridge: NativeWebSocket connection error: {e.Message}");
        }
        #else
        DebugLog("ImprovedWebGLBridge: No WebSocket implementation available.");
        #endif
        
        return success;
    }

    // NativeWebSocketの場合のメッセージキュー処理とアバターコントローラーの監視
    private void Update()
    {
        #if USING_NATIVEWEBSOCKET
        #if !UNITY_WEBGL || UNITY_EDITOR
        if (websocket != null)
        {
            try
            {
                websocket.DispatchMessageQueue();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"ImprovedWebGLBridge: DispatchMessageQueue error: {e.Message}");
            }
        }
        #endif
        #endif

        // 感情コントローラーがなければ再度検索
        if (emotionController == null)
        {
            FindEmotionController();
        }
        
        // 接続状態の定期確認
        if (!isWebGLBuild && isConnected)
        {
            connectionCheckTimer += Time.deltaTime;
            if (connectionCheckTimer >= CONNECTION_CHECK_INTERVAL)
            {
                connectionCheckTimer = 0f;
                
                // 最後の応答から一定時間経過した場合、接続状態を確認
                float timeSinceLastResponse = Time.time - lastResponseTime;
                if (timeSinceLastResponse > 30f) // 30秒以上応答がない場合
                {
                    DebugLog($"ImprovedWebGLBridge: No response for {timeSinceLastResponse:F1} seconds, checking connection...");
                    
                    #if USING_WEBSOCKETSHARP
                    if (websocket != null && !websocket.IsAlive)
                    {
                        Debug.LogWarning("ImprovedWebGLBridge: WebSocket connection appears to be dead, reconnecting...");
                        isConnected = false;
                        StartCoroutine(ReconnectCoroutine());
                    }
                    #endif
                }
            }
        }
        
        // キューに溜まったメッセージを処理
        if (!isProcessingMessage && messageQueue.Count > 0)
        {
            ProcessNextQueuedMessage();
        }
        
        // 会話セッションタイマー
        if (isTalkSessionActive)
        {
            sessionTimer += Time.deltaTime;
            if (sessionTimer >= sessionTimeout)
            {
                // タイムアウト時に会話状態を終了
                EndTalkSession();
            }
            
            // 会話状態の確認
            talkingCheckTimer += Time.deltaTime;
            if (talkingCheckTimer >= TALKING_CHECK_INTERVAL && emotionController != null)
            {
                talkingCheckTimer = 0f;
                CheckAndUpdateTalkingState();
            }
            
            // 母音切り替えタイマーの更新
            vowelChangeTimer += Time.deltaTime;
            if (vowelChangeTimer >= vowelChangeDuration && emotionController != null && 
                emotionController.isTalking && mouthMovementEnabled)
            {
                vowelChangeTimer = 0f;
                SwitchToRandomVowel();
            }
        }
        
        // 強制同期タイマー
        if (needsForcedSync && emotionController != null)
        {
            forceSyncTimer += Time.deltaTime;
            if (forceSyncTimer >= forceSyncInterval)
            {
                forceSyncTimer = 0f;
                ForceSyncMouthMovement();
            }
        }
        
        // 口のリセットタイマー
        if (needsMouthReset && emotionController != null)
        {
            mouthResetTimer += Time.deltaTime;
            if (mouthResetTimer >= mouthResetDelay)
            {
                mouthResetTimer = 0f;
                needsMouthReset = false;
                ResetMouthShape();
            }
        }
        
        // 状態リカバリチェック
        if (needsStateRecovery && emotionController != null)
        {
            recoveryCheckTimer += Time.deltaTime;
            if (recoveryCheckTimer >= RECOVERY_CHECK_INTERVAL)
            {
                recoveryCheckTimer = 0f;
                PerformStateRecovery();
            }
        }
    }
    
    // 会話状態を確認・更新する
    private void CheckAndUpdateTalkingState()
    {
        if (emotionController == null) return;
        
        // アニメーターパラメータを確認
        bool isTalkingInAnimator = emotionController.IsTalkingInAnimator();
        
        // TalkingNeutralモードかどうか確認
        bool isInTalkingNeutralMode = emotionController.IsInTalkingNeutralMode();
        
        // 会話中なのにフラグがfalseの場合は修正
        if (isTalkSessionActive && !isTalkingInAnimator)
        {
            // 会話フラグを再設定
            emotionController.SetTalkingFlag(true);
            Debug.Log("会話セッション中にフラグがOFFだったため、再設定しました");
            
            // TalkingNeutralの確認
            if (emotionController.baseEmotion == "neutral" && !isInTalkingNeutralMode)
            {
                // TalkingNeutralモードを再設定
                emotionController.ForceSetState("talkingneutral");
                Debug.Log("TalkingNeutralモードを再設定しました");
            }
        }
        
        // 母音も確認し、口が開いていなければ開く
        if (isTalkSessionActive && emotionController.isTalking && !IsMouthOpen())
        {
            SwitchToRandomVowel();
            Debug.Log("口が開いていないため、母音を切り替えました");
        }
    }
    
    // 口が開いているかをチェック
    private bool IsMouthOpen()
    {
        if (emotionController == null) return false;
        
        // EmotionControllerのIsMouthOpenメソッドを使用
        return emotionController.IsMouthOpen();
    }
    
    // ランダムな母音に切り替える（完全に修正したバージョン）
    private void SwitchToRandomVowel()
    {
        if (emotionController == null || !emotionController.isTalking || !mouthMovementEnabled) return;
        
        try
        {
            // 前回と異なる母音を選択
            string[] availableVowels = vowelBlendShapes.Where(v => v != currentVowelShape).ToArray();
            
            if (availableVowels.Length > 0)
            {
                // 新しい母音をランダムに選択
                string newVowel = availableVowels[UnityEngine.Random.Range(0, availableVowels.Length)];
                float intensity = UnityEngine.Random.Range(70f, 100f);
                
                // 前回の状態を保存
                string previousVowel = currentVowelShape;
                float previousIntensity = lastVowelIntensity;
                
                // 新しい状態を設定
                currentVowelShape = newVowel;
                lastVowelIntensity = intensity;
                
                // 指定されたBlendShapeがある場合
                if (mouthBlendShapes.ContainsKey(newVowel))
                {
                    int vowelIndex = mouthBlendShapes[newVowel];
                    
                    // 前の母音をリセット
                    if (mouthBlendShapes.ContainsKey(previousVowel))
                    {
                        int prevIndex = mouthBlendShapes[previousVowel];
                        emotionController.SetBlendShapeDirectly(prevIndex, 0f);
                    }
                    
                    // すべての母音BlendShapeをリセット（確実に）
                    foreach (var pair in mouthBlendShapes)
                    {
                        if (pair.Key != newVowel && pair.Key != "Fcl_MTH_Close")
                        {
                            emotionController.SetBlendShapeDirectly(pair.Value, 0f);
                        }
                    }
                    
                    // 新しい母音を設定
                    emotionController.SetBlendShapeDirectly(vowelIndex, intensity);
                    
                    // レンダラーを強制更新
                    emotionController.ForceUpdateRenderer();
                    
                    Debug.Log($"母音切り替え: {previousVowel}({previousIntensity:F1}) → {newVowel}({intensity:F1})");
                    
                    // 連続エラーカウントをリセット
                    consecutiveErrorCount = 0;
                }
                else
                {
                    // EmotionControllerの口パク機能を使用
                    emotionController.ForceMouthMovement();
                    Debug.Log($"EmotionControllerの口パク機能を使用して母音を切り替え");
                }
            }
            else
            {
                // 利用可能な母音がなければ、現在の母音の強度だけ変える
                if (mouthBlendShapes.ContainsKey(currentVowelShape))
                {
                    float newIntensity = UnityEngine.Random.Range(70f, 100f);
                    int vowelIndex = mouthBlendShapes[currentVowelShape];
                    
                    // 強度を変更して適用
                    emotionController.SetBlendShapeDirectly(vowelIndex, newIntensity);
                    emotionController.ForceUpdateRenderer();
                    
                    Debug.Log($"同じ母音で強度変更: {currentVowelShape} ({lastVowelIntensity:F1} → {newIntensity:F1})");
                    lastVowelIntensity = newIntensity;
                }
                else
                {
                    // 緊急時の代替手段
                    emotionController.ForceMouthMovement();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"母音切り替えエラー: {ex.Message}");
            
            // エラーカウント増加
            consecutiveErrorCount++;
            
            // 連続エラーが多い場合は口パク機能を一時的に無効化
            if (consecutiveErrorCount >= MAX_ERROR_COUNT)
            {
                Debug.LogWarning($"連続エラー検出: 口パク機能を一時的に無効化します");
                mouthMovementEnabled = false;
                StartCoroutine(ReenableMouthMovementAfterDelay(5f));
            }
        }
    }
    
    // 一定時間後に口パク機能を再有効化
    private IEnumerator ReenableMouthMovementAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        // 口パク機能を再有効化
        mouthMovementEnabled = true;
        consecutiveErrorCount = 0;
        
        Debug.Log("口パク機能を再有効化しました");
        
        // 状態をチェック・修復
        if (emotionController != null && isTalkSessionActive)
        {
            CheckAndUpdateTalkingState();
            
            // 口が開いているか確認し、閉じている場合は開く
            if (!IsMouthOpen())
            {
                SwitchToRandomVowel();
            }
        }
    }
    
    // WebGL対応の遅延リセットコルーチン
    private IEnumerator WebGLSafeDelayedResetToNeutral()
    {
        // WebGLではフレーム単位の待機が安全
        for (int i = 0; i < 15; i++) // 約0.25秒 (60FPS想定)
        {
            yield return null;
        }
        
        if (emotionController != null)
        {
            // BlendShapeをリセット
            emotionController.ResetAllBlendShapes();
            
            // 感情をニュートラルに設定
            emotionController.SetEmotion("neutral", false);
            
            // 確実に会話フラグをOFFに
            if (emotionController.bodyAnimator != null)
            {
                emotionController.bodyAnimator.SetBool("Is Talking", false);
                emotionController.bodyAnimator.Update(0f);
            }
            
            Debug.Log("すべてのBlendShapeをリセットし、Neutralに戻しました");
        }
    }
    
    // 状態リカバリ処理
    private void PerformStateRecovery()
    {
        if (emotionController == null) return;
        
        Debug.Log("アバター状態リカバリを実行");
        
        try
        {
            // 現在の状態を確認
            string currentEmotion = emotionController.baseEmotion;
            bool isTalking = emotionController.isTalking;
            
            // 会話セッション中ならTalkingNeutralを確認
            if (isTalkSessionActive)
            {
                if (currentEmotion == "neutral" && !emotionController.IsInTalkingNeutralMode())
                {
                    // TalkingNeutralモードを再設定
                    emotionController.ForceSetState("talkingneutral");
                    
                    // 会話フラグも確認
                    if (!emotionController.IsTalkingInAnimator())
                    {
                        emotionController.SetTalkingFlag(true);
                    }
                    
                    Debug.Log("リカバリ: TalkingNeutralモードと会話フラグを再設定しました");
                }
                else if (!isTalking)
                {
                    // 会話状態を再設定
                    emotionController.SetTalkingFlag(true);
                    Debug.Log("リカバリ: 会話フラグを再設定しました");
                }
                
                // 口パク動作も確認
                if (!IsMouthOpen() && mouthMovementEnabled)
                {
                    SwitchToRandomVowel();
                    Debug.Log("リカバリ: 口パク動作を再開しました");
                }
            }
            else if (needsMouthReset)
            {
                // 口のリセットが必要なら実行
                ResetMouthShape();
                needsMouthReset = false;
                Debug.Log("リカバリ: 口形状をリセットしました");
            }
            
            // 状態リカバリ完了
            needsStateRecovery = false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"状態リカバリ実行エラー: {ex.Message}");
        }
    }
    
    // 口形状をリセットする
    private void ResetMouthShape()
    {
        if (emotionController == null) return;
        
        Debug.Log("口の形状をリセットします");
        
        try
        {
            // すべての母音BlendShapeをゼロに設定
            foreach (var pair in mouthBlendShapes)
            {
                if (pair.Key != "Fcl_MTH_Close")
                {
                    emotionController.SetBlendShapeDirectly(pair.Value, 0f);
                }
            }
            
            // Fcl_MTH_Closeがあれば使用して口を閉じる
            if (mouthBlendShapes.ContainsKey("Fcl_MTH_Close"))
            {
                emotionController.SetBlendShapeDirectly(mouthBlendShapes["Fcl_MTH_Close"], 100f);
                Debug.Log($"Fcl_MTH_Close で口を閉じました");
            }
            
            // レンダラーを強制更新
            emotionController.ForceUpdateRenderer();
            
            // 少し待ってからニュートラルに戻す
            StartCoroutine(WebGLSafeDelayedResetToNeutral());
        }
        catch (Exception ex)
        {
            Debug.LogError($"口形状リセットエラー: {ex.Message}");
            
            // エラー発生時は代替手段
            if (emotionController != null)
            {
                emotionController.CloseMouth();
                emotionController.SetEmotion("neutral", false);
            }
        }
    }
    
    // 口パク状態の強制同期
    private void ForceSyncMouthMovement()
    {
        if (emotionController == null || !isTalkSessionActive) return;
        
        // 口パク状態を強制的に再設定
        if (emotionController.baseEmotion == "neutral")
        {
            // TalkingNeutralモードを再設定
            if (!emotionController.IsInTalkingNeutralMode())
            {
                emotionController.ForceSetState("talkingneutral");
                Debug.Log("TalkingNeutralモードを強制同期しました");
            }
            
            // 会話フラグも確認
            if (!emotionController.IsTalkingInAnimator())
            {
                emotionController.SetTalkingFlag(true);
                Debug.Log("会話フラグを強制的にONに設定");
            }
        }
        else
        {
            // 他の感情でも会話状態を再設定
            if (!emotionController.IsTalkingInAnimator())
            {
                emotionController.ForceSetState(emotionController.baseEmotion);
                emotionController.SetTalkingFlag(true);
                Debug.Log($"感情 {emotionController.baseEmotion} で会話フラグを強制同期");
            }
        }
        
        // 口パクしていない場合は母音を切り替え
        if (!IsMouthOpen() && mouthMovementEnabled)
        {
            SwitchToRandomVowel();
            Debug.Log("口パク状態を強制同期：母音を切り替えました");
        }
    }
    
    // 会話セッション開始
    private void StartTalkSession()
    {
        if (!isTalkSessionActive)
        {
            isTalkSessionActive = true;
            sessionTimer = 0f;
            needsForcedSync = true;
            forceSyncTimer = 0f;
            Debug.Log("ImprovedWebGLBridge: 会話セッションを開始しました");
            
            // 口リセットフラグをキャンセル
            needsMouthReset = false;
            mouthResetTimer = 0f;
            
            // 口パク動作を有効化
            mouthMovementEnabled = true;
            consecutiveErrorCount = 0;
            
            // 口パクを開始する準備をする
            if (emotionController != null)
            {
                // 会話状態をONにするメソッド呼び出しを追加
                emotionController.SetEmotion(emotionController.baseEmotion, true);
                emotionController.SetTalkingFlag(true);
            }
        }
        else
        {
            // 既に会話中ならタイマーをリセット
            sessionTimer = 0f;
            Debug.Log("ImprovedWebGLBridge: 会話セッションタイマーをリセットしました");
        }
    }
    
    // 会話セッション終了
    private void EndTalkSession()
    {
        if (isTalkSessionActive)
        {
            isTalkSessionActive = false;
            sessionTimer = 0f;
            needsForcedSync = false;
            Debug.Log("ImprovedWebGLBridge: 会話セッションを終了しました");
            
            // 口リセットフラグを設定
            needsMouthReset = true;
            mouthResetTimer = 0f;
            
            // 口パクを停止
            if (emotionController != null)
            {
                // 会話状態を終了
                emotionController.SetTalkingFlag(false);
                
                // すべての母音BlendShapeを即座にリセット
                foreach (var pair in mouthBlendShapes)
                {
                    if (pair.Key != "Fcl_MTH_Close")
                    {
                        emotionController.SetBlendShapeDirectly(pair.Value, 0f);
                    }
                }
                
                // 口を閉じる
                if (mouthBlendShapes.ContainsKey("Fcl_MTH_Close"))
                {
                    emotionController.SetBlendShapeDirectly(mouthBlendShapes["Fcl_MTH_Close"], 100f);
                }
                
                // レンダラーを強制更新
                emotionController.ForceUpdateRenderer();
                
                // 少し待ってから感情を設定
                StartCoroutine(DelayedEmotionChange());
            }
        }
    }
    
    // 少し待ってから感情を変える
    private IEnumerator DelayedEmotionChange()
    {
        yield return new WaitForSeconds(0.2f);
        
        if (emotionController != null)
        {
            try
            {
                // Neutralに戻す
                emotionController.SetEmotion("neutral", false);
                
                // アニメーターパラメータを確実にリセット
                if (emotionController.bodyAnimator != null)
                {
                    emotionController.bodyAnimator.SetBool("Is Talking", false);
                    emotionController.bodyAnimator.SetInteger("BodyAnimation", 0);
                    emotionController.bodyAnimator.Update(0f);
                }
                
                Debug.Log("会話終了後に感情とアニメーションをneutralに設定");
            }
            catch (Exception ex)
            {
                Debug.LogError($"感情リセットエラー: {ex.Message}");
                
                // エラー発生時は直接設定を試みる
                if (emotionController.bodyAnimator != null)
                {
                    emotionController.bodyAnimator.SetBool("Is Talking", false);
                    emotionController.bodyAnimator.Update(0f);
                }
            }
        }
    }
    
    // キューに溜まったメッセージを順次処理
    private void ProcessNextQueuedMessage()
    {
        if (messageQueue.Count == 0 || isProcessingMessage) return;
        
        isProcessingMessage = true;
        
        // キューからメッセージを取得
        MessageData data = messageQueue.Dequeue();
        
        // 非同期処理を開始
        StartCoroutine(ProcessMessageAsyncInternal(data.Message, data.AudioData));
    }
    
    // 安全にメッセージを非同期処理するメソッド
    private IEnumerator ProcessMessageAsyncInternal(string message, byte[] audioData = null)
    {
        if (string.IsNullOrEmpty(message))
        {
            Debug.LogWarning("ProcessMessageAsyncInternal: 空のメッセージを受信");
            isProcessingMessage = false;
            yield break;
        }

        isProcessingMessage = true;
        Debug.Log($"ProcessMessageAsyncInternal: メッセージ処理開始: {message.Substring(0, Math.Min(50, message.Length))}...");

        // 会話セッションを開始
        StartTalkSession();

        // メッセージにJSONフォーマットのデータが含まれているか確認
        ResponseData responseData = null;
        try
        {
            responseData = JsonUtility.FromJson<ResponseData>(message);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"ProcessMessageAsyncInternal: JSON解析エラー: {ex.Message}");
        }

        // 音声データがあるか確認
        if (responseData != null && !string.IsNullOrEmpty(responseData.audio))
        {
            try
            {
                audioData = ProcessAudioDataSafe(responseData.audio);
            }
            catch (Exception ex)
            {
                Debug.LogError($"ProcessMessageAsyncInternal: 音声データ処理エラー: {ex.Message}");
            }
        }

        // 抽出したメッセージを処理
        string displayMessage = responseData?.message ?? message;
        
        // WebGLでのメッセージ処理は特に慎重に
        #if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL環境では、まず会話状態と感情をセットアップ
        if (emotionController != null)
        {
            // メッセージから感情を抽出
            string emotion = ExtractEmotionFromMessage(displayMessage);
            bool isTalking = true; // 常に会話モードで開始
            
            // TalkingNeutralの場合は特別な遷移
            if (emotion.ToLower() == "neutral")
            {
                // まず会話フラグを設定
                emotionController.SetTalkingFlag(true);
                
                // TalkingNeutralモードを設定
                StartCoroutine(ForceRecheckTalkingState(0.1f));
                
                // 母音も切り替える
                if (mouthMovementEnabled)
                {
                    SwitchToRandomVowel();
                }
            }
            else
            {
                // 通常の感情設定
                emotionController.SetEmotion(emotion, true);
            }
            
            // 少し待機してから次の処理へ
            yield return new WaitForSeconds(0.1f);
        }
        #endif

        // アバターコントローラーで応答を処理
        if (emotionController != null)
        {
            try
            {
                // 会話セッションを継続させるためのフラグをリセット
                vowelChangeTimer = 0f;
                talkingCheckTimer = 0f;
                
                // 明示的に音声データが存在するかどうかを確認し、適切な応答処理を実行
                if (audioData != null && audioData.Length > 0)
                {
                    Debug.Log("音声データありで応答処理を開始");
                    
                    // WebGLでの特殊処理
                    #if UNITY_WEBGL && !UNITY_EDITOR
                    // WebGL環境では直接メソッドを呼ぶ
                    emotionController.ProcessResponse(displayMessage, audioData);
                    
                    // 直接口パクを開始
                    if (mouthMovementEnabled)
                    {
                        SwitchToRandomVowel();
                    }
                    #else
                    // 非WebGL環境では通常通り処理
                    emotionController.ProcessResponse(displayMessage, audioData);
                    #endif
                }
                else
                {
                    Debug.Log("音声データなしで応答処理を開始");
                    // 音声なしの場合は通常の感情設定のみ
                    string emotion = ExtractEmotionFromMessage(displayMessage);
                    emotionController.SetEmotion(emotion, true); // 応答モードをtrueに
                    
                    // 口パクを開始（複数の母音を使用）
                    StartCoroutine(SimulateMouthMovementWithoutAudio());
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"ProcessMessageAsyncInternal: 応答処理開始エラー: {ex.Message}\n{ex.StackTrace}");
            }
        }
        else
        {
            Debug.LogError("ProcessMessageAsyncInternal: アバターコントローラーがnullです");
        }

        // 感情設定も送信
        if (responseData != null && !string.IsNullOrEmpty(responseData.emotion))
        {
            // 感情と会話状態に基づいて状態を設定
            bool isTalking = responseData.talking || (audioData != null && audioData.Length > 0);
            
            if (emotionController != null)
            {
                try
                {
                    // TalkingNeutralの特殊ケース
                    if (responseData.emotion.ToLower() == "neutral" && isTalking)
                    {
                        // 感情設定前の強制口パク適用
                        if (emotionController.isTalking)
                        {
                            StartCoroutine(ForceRecheckTalkingState(0.1f));
                        }
                        
                        emotionController.ForceSetState("talkingneutral");
                        
                        // 母音も切り替える
                        if (mouthMovementEnabled)
                        {
                            SwitchToRandomVowel();
                        }
                    }
                    else
                    {
                        emotionController.ForceSetState(responseData.emotion);
                        
                        // 会話状態ならランダムに母音も切り替える
                        if (isTalking && mouthMovementEnabled)
                        {
                            SwitchToRandomVowel();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"ProcessMessageAsyncInternal: 感情設定エラー: {ex.Message}");
                }
            }
        }

        // 音声再生が終了した場合、会話タイマーをリセット
        if (audioData != null && audioData.Length > 0)
        {
            sessionTimer = 0; // タイマーをリセット - 再生後にタイムアウトで会話終了
        }
        else
        {
            // 音声がない場合でも、少しの間は会話状態を維持
            sessionTimer = sessionTimeout - 3.0f; // 3秒間だけ会話状態を維持
            Debug.Log("音声なしですが会話状態を短時間維持します");
        }

        isProcessingMessage = false;
        Debug.Log("ProcessMessageAsyncInternal: メッセージ処理完了");
    }

    // 音声なしで口パク動作をシミュレート
    private IEnumerator SimulateMouthMovementWithoutAudio()
    {
        if (emotionController == null || !mouthMovementEnabled) yield break;
        
        Debug.Log("音声なしの口パク動作をシミュレート開始");
        
        // 口パクシミュレーション時間
        float simulationDuration = 3.0f;
        float startTime = Time.time;
        
        // 口パク状態を開始
        emotionController.SetTalkingFlag(true);
        
        // 母音切り替えタイマーをリセット
        vowelChangeTimer = 0f;
        
        // シミュレーション時間中は定期的に母音を切り替え
        while (Time.time - startTime < simulationDuration)
        {
            // ランダムな母音に切り替え
            SwitchToRandomVowel();
            
            // 次の母音切り替えまで待機
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.1f, 0.3f));
        }
        
        // 口パク状態を終了
        emotionController.SetTalkingFlag(false);
        
        // 口を閉じる
        ResetMouthShape();
        
        Debug.Log("音声なしの口パク動作シミュレーション終了");
    }

    // 会話状態を強制的に再確認するコルーチン
    private IEnumerator ForceRecheckTalkingState(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        // 会話状態を強制的に再確認
        if (emotionController != null)
        {
            Debug.Log("会話状態を強制的に再確認します");
            
            // 会話セッションが終了していたら再開始
            if (!isTalkSessionActive)
            {
                StartTalkSession();
                sessionTimer = 0f;
            }
            
            // EmotionControllerの状態を確認・再設定
            emotionController.ForceSetState("talkingneutral");
            emotionController.SetTalkingFlag(true);
            
            // WebGLではさらに強制的に設定
            #if UNITY_WEBGL && !UNITY_EDITOR
            // アニメーションパラメータを強制設定
            emotionController.SetAnimatorParameter("Is Talking", true);
            emotionController.SetAnimatorParameter("BodyAnimation", 5); // TalkingNeutral
            emotionController.SetAnimatorParameter("Expression", 0); // neutral
            #endif
            
            // 母音も切り替える
            if (mouthMovementEnabled)
            {
                SwitchToRandomVowel();
            }
            
            // 状態確認を少し後に再度実行（保険）
            StartCoroutine(ConfirmTalkingStateAfterDelay(0.5f));
        }
    }
    
    // 会話状態の確認を遅延実行（追加保険）
    private IEnumerator ConfirmTalkingStateAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        
        if (emotionController != null && isTalkSessionActive)
        {
            // アニメーターパラメータを確認
            bool isTalkingInAnimator = emotionController.IsTalkingInAnimator();
            bool isInTalkingNeutralMode = emotionController.IsInTalkingNeutralMode();
            
            // 状態が正しくなければ再設定
            if (!isTalkingInAnimator || (emotionController.baseEmotion == "neutral" && !isInTalkingNeutralMode))
            {
                Debug.Log("会話状態の確認: 状態が正しくないため再設定します");
                
                // もう一度確実に設定
                emotionController.SetTalkingFlag(true);
                
                if (emotionController.baseEmotion == "neutral")
                {
                    emotionController.ForceSetState("talkingneutral");
                }
                
                // WebGLでは直接設定も行う
                #if UNITY_WEBGL && !UNITY_EDITOR
                if (emotionController.bodyAnimator != null)
                {
                    emotionController.bodyAnimator.SetBool("Is Talking", true);
                    
                    if (emotionController.baseEmotion == "neutral")
                    {
                        emotionController.bodyAnimator.SetInteger("BodyAnimation", 5); // TalkingNeutral
                    }
                    
                    emotionController.bodyAnimator.Update(0f);
                }
                #endif
            }
        }
    }

    // メッセージから感情を抽出するヘルパーメソッド
    private string ExtractEmotionFromMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return "neutral";
            
        // メッセージから感情を判断
        string lowercaseMessage = message.ToLower();
        
        if (lowercaseMessage.StartsWith("happy ")) return "happy";
        if (lowercaseMessage.StartsWith("sad ")) return "sad";
        if (lowercaseMessage.StartsWith("angry ")) return "angry";
        if (lowercaseMessage.StartsWith("surprised ")) return "surprised";
        
        // タグがない場合はneutralを設定
        return "neutral";
    }

    // 音声データを安全に処理するメソッド
    private byte[] ProcessAudioDataSafe(string base64Audio)
    {
        if (string.IsNullOrEmpty(base64Audio))
        {
            Debug.LogWarning("ProcessAudioDataSafe: 空のbase64音声データ");
            return null;
        }

        try
        {
            // Base64文字列をバイト配列に変換
            string purifiedBase64 = base64Audio;
            
            // データURLの場合は接頭辞を削除
            if (base64Audio.StartsWith("data:"))
            {
                int commaIndex = base64Audio.IndexOf(',');
                if (commaIndex > 0)
                {
                    purifiedBase64 = base64Audio.Substring(commaIndex + 1);
                }
            }
            
            return Convert.FromBase64String(purifiedBase64);
        }
        catch (Exception ex)
        {
            Debug.LogError($"ProcessAudioDataSafe: 音声データ変換エラー: {ex.Message}");
            return null;
        }
    }

    // メッセージを処理する関数
    private void ProcessMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            Debug.LogWarning("ImprovedWebGLBridge: Received empty message");
            return;
        }

        // メッセージをクリーンアップ
        string cleanedMessage = "";
        
        try
        {
            cleanedMessage = message.Trim();
            DebugLog($"ImprovedWebGLBridge: Raw message received: {cleanedMessage}");

            // メッセージをキューに追加
            messageQueue.Enqueue(new MessageData(cleanedMessage));
            
            // メッセージ処理を開始
            if (!isProcessingMessage)
            {
                ProcessNextQueuedMessage();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"ImprovedWebGLBridge: Error processing message: {e.Message}");
        }
    }

    // デバッグログ用メソッド
    private void DebugLog(string message)
    {
        if (debugMode)
        {
            Debug.Log(message);
        }
    }
    
    // 外部からのアニメーション状態強制設定用メソッド（WebGLBridge用）
    public void ForceSetState(string stateName)
    {
        Debug.Log($"状態を強制設定: {stateName}");
        
        // TalkingNeutralの場合は特殊処理
        if (stateName.ToLower() == "talkingneutral")
        {
            Debug.Log("★★★ TalkingNeutralを外部から直接強制設定 ★★★");
            
            // 会話セッションを開始
            StartTalkSession();
            
            // 強制同期フラグをON
            needsForcedSync = true;
            forceSyncTimer = 0f;
            
            // アニメーター状態をリセット
            if (emotionController != null)
            {
                // 口パクを強制適用
                StartCoroutine(ProcessTalkingNeutralSafe());
            }
        }
        else
        {
            if (emotionController != null)
            {
                // 通常の感情設定
                bool isTalkingEmotion = stateName.ToLower() == "happy" || 
                                       stateName.ToLower() == "angry" ||
                                       stateName.ToLower() == "talking";
                
                // 会話を伴う感情の場合は会話セッションを開始
                if (isTalkingEmotion)
                {
                    StartTalkSession();
                    needsForcedSync = true;
                    forceSyncTimer = 0f;
                }
                
                // 感情を設定
                emotionController.SetEmotion(stateName, isTalkingEmotion);
                
                // 会話状態に応じてフラグを設定
                emotionController.SetTalkingFlag(isTalkingEmotion);
            }
        }
    }

    // TalkingNeutral状態を安全に設定するシンプルなコルーチン
    private IEnumerator ProcessTalkingNeutralSafe()
    {
        if (emotionController == null)
        {
            yield break;
        }
        
        // 感情を設定（Neutral + 応答モード）
        emotionController.SetEmotion("neutral", true);
        
        // 会話フラグを確実に設定
        emotionController.SetTalkingFlag(true);
        
        // TalkingNeutralモードの強制設定
        emotionController.ForceSetState("talkingneutral");
        
        // WebGLではさらに強制的に設定
        #if UNITY_WEBGL && !UNITY_EDITOR
        // アニメーターパラメータを直接設定
        emotionController.SetAnimatorParameter("Is Talking", true);
        emotionController.SetAnimatorParameter("BodyAnimation", 5); // TalkingNeutral = 5
        emotionController.SetAnimatorParameter("Expression", 0);    // Neutral = 0
        #endif
        
        yield return new WaitForSeconds(0.1f);
        
        // 口パクを開始
        StartCoroutine(ForceMouthMovement());
    }

    // 口パクを強制的に開始するコルーチン
    private IEnumerator ForceMouthMovement()
    {
        yield return new WaitForSeconds(0.1f);
        
        if (emotionController == null || !mouthMovementEnabled) yield break;
        
        // 既に口パク中なら何もしない
        if (emotionController.isTalking)
        {
            // 母音を切り替えるだけ
            SwitchToRandomVowel();
            Debug.Log("既に口パク中なので母音のみ切り替えました");
            yield break;
        }
        
        // TalkingNeutralの特殊処理
        if (emotionController.baseEmotion == "neutral")
        {
            emotionController.ForceSetState("talkingneutral");
        }
        
        Debug.Log("口パクを強制的に開始します");
        
        // 会話状態をONに
        emotionController.SetTalkingFlag(true);
        
        // WebGLでの特殊処理
        #if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL環境では直接アニメーターパラメータも設定
        emotionController.SetAnimatorParameter("Is Talking", true);
        #endif
        
        // 口パク動作を開始
        StartCoroutine(SimulateMouthMovementWithoutAudio());
    }

    // レスポンスデータの構造体
    [Serializable]
    private class ResponseData
    {
        public string message;
        public string emotion;
        public string audio;
        public string error;  // エラーメッセージ用のフィールドを追加
        public bool talking;  // 会話中かどうかのフラグを追加
    }

    // シンプルな感情メッセージクラス
    [Serializable]
    public class SimpleEmotionMessage
    {
        public string type;
        public string emotion;
        public bool talking; // 会話中かどうかのフラグを追加
    }

    // JavaScriptから呼び出されるメソッド（WebGLビルド用）
    public void OnMessage(string message)
    {
        Debug.Log($"ImprovedWebGLBridge: OnMessage呼び出し - メッセージ長: {message?.Length ?? 0}");
        
        if (string.IsNullOrEmpty(message))
        {
            Debug.LogWarning("ImprovedWebGLBridge: OnMessageで空のメッセージを受信");
            return;
        }

        // メッセージをクリーンアップ
        string cleanedMessage = "";
        
        try
        {
            cleanedMessage = message.Trim();
            Debug.Log($"ImprovedWebGLBridge: OnMessageクリーン後: {cleanedMessage.Substring(0, Math.Min(50, cleanedMessage.Length))}...");

            // 感情コントローラーの確認
            if (emotionController == null)
            {
                Debug.LogError("ImprovedWebGLBridge: EmotionControllerがnull！再検索します");
                FindEmotionController();
                
                if (emotionController != null)
                {
                    Debug.Log("ImprovedWebGLBridge: EmotionControllerを再発見しました");
                }
                else
                {
                    Debug.LogError("ImprovedWebGLBridge: EmotionControllerが見つかりません！");
                    return;
                }
            }

            // JSON形式のメッセージか確認
            if (cleanedMessage.StartsWith("{") && cleanedMessage.EndsWith("}"))
            {
                // 感情情報だけのJSONの場合
                SimpleEmotionMessage simpleMessage = null;
                try
                {
                    simpleMessage = JsonUtility.FromJson<SimpleEmotionMessage>(cleanedMessage);
                }
                catch (Exception) { /* JSON解析に失敗した場合は無視 */ }
                
                if (simpleMessage != null && simpleMessage.type == "emotion")
                {
                    // 感情設定だけのメッセージを処理
                    ProcessEmotionMessage(simpleMessage.emotion, simpleMessage.talking);
                    return;
                }
                
                // 会話が含まれるか音声が含まれるメッセージの場合
                if (HasAudioData(cleanedMessage) || cleanedMessage.Contains("\"message\":"))
                {
                    // 会話セッションを開始
                    StartTalkSession();
                    
                    // 強制同期フラグをON
                    needsForcedSync = true;
                    forceSyncTimer = 0f;
                }
            }

            // メッセージ処理をキューに追加
            messageQueue.Enqueue(new MessageData(cleanedMessage));
            
            // メッセージ処理を開始
            if (!isProcessingMessage)
            {
                ProcessNextQueuedMessage();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"ImprovedWebGLBridge: OnMessageエラー: {e.Message}");
            Debug.LogError($"スタックトレース: {e.StackTrace}");
        }
    }
    
    // 感情メッセージだけを処理するヘルパーメソッド
    private void ProcessEmotionMessage(string emotion, bool isTalking)
    {
        Debug.Log($"感情メッセージを処理: {emotion}, 会話状態: {isTalking}");
        
        if (emotionController == null) return;
        
        // 重要: 会話状態がONならセッション開始 (状態を明示的に開始)
        if (isTalking)
        {
            StartTalkSession();
            // セッションタイマーをリセット
            sessionTimer = 0f;
            
            // 強制同期フラグをON
            needsForcedSync = true;
            forceSyncTimer = 0f;
            
            // 口リセットフラグをキャンセル
            needsMouthReset = false;
            mouthResetTimer = 0f;
        }
        else
        {
            // 会話状態がオフの場合は口を閉じる処理を開始
            needsMouthReset = true;
            mouthResetTimer = 0f;
        }
        
        // TalkingNeutralの特殊ケース
        if (emotion.ToLower() == "neutral" && isTalking)
        {
            // 特殊処理 - 状態を明示的に設定（即時終了を防止）
            emotionController.ForceSetState("talkingneutral");
            Debug.Log("TalkingNeutral状態を強制設定しました");
            
            // 会話状態チェックを1秒後に強制的に設定（保険）
            StartCoroutine(ForceRecheckTalkingState(0.5f));
            
            // 母音も切り替える
            if (mouthMovementEnabled)
            {
                SwitchToRandomVowel();
            }
            
            // WebGLでは特に注意して設定
            #if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL環境では直接アニメーターパラメータを設定
            emotionController.SetAnimatorParameter("Is Talking", true);
            emotionController.SetAnimatorParameter("BodyAnimation", 5); // TalkingNeutral
            emotionController.SetAnimatorParameter("Expression", 0);    // Neutral
            #endif
        }
        else
        {
            // 通常の感情設定
            emotionController.SetEmotion(emotion, isTalking);
            
            // 会話状態を設定
            emotionController.SetTalkingFlag(isTalking);
            
            // 会話状態なら口パクも強制的に開始
            if (isTalking)
            {
                // ダミー音声で口パクを強制開始
                StartCoroutine(ForceMouthMovement());
            }
            else if (!isTalking)
            {
                // 口を閉じる
                ResetMouthShape();
            }
        }
    }

    // Unityからサーバーにメッセージを送信
    public async void SendMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            Debug.LogWarning("ImprovedWebGLBridge: Cannot send empty message");
            return;
        }
        
        DebugLog($"ImprovedWebGLBridge: Sending message: {message}");

        // WebGLビルドの場合はJavaScriptブリッジを使用
        if (isWebGLBuild)
        {
            #if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                SendMessageToServer(message);
                DebugLog("ImprovedWebGLBridge: Message sent via JavaScript bridge");
            }
            catch (Exception e)
            {
                Debug.LogError($"ImprovedWebGLBridge: Error sending message via JavaScript bridge: {e.Message}");
            }
            #endif
            return;
        }

        // 通常のWebSocket接続での送信
        #if USING_WEBSOCKETSHARP
        if (websocket != null && isConnected)
        {
            try
            {
                websocket.Send(message);
                DebugLog("ImprovedWebGLBridge: Message sent via WebSocketSharp");
            }
            catch (Exception e)
            {
                Debug.LogError($"ImprovedWebGLBridge: Error sending message: {e.Message}");
                if (!isReconnecting)
                {
                    StartCoroutine(ReconnectCoroutine());
                }
            }
        }
        else
        {
            Debug.LogWarning("ImprovedWebGLBridge: WebSocket not connected, message not sent");
            if (!isReconnecting)
            {
                StartCoroutine(ReconnectCoroutine());
            }
        }
        #elif USING_NATIVEWEBSOCKET
        if (websocket != null && isConnected)
        {
            try
            {
                await websocket.SendText(message);
                DebugLog("ImprovedWebGLBridge: Message sent via NativeWebSocket");
            }
            catch (Exception e)
            {
                Debug.LogError($"ImprovedWebGLBridge: Error sending message: {e.Message}");
                if (!isReconnecting)
                {
                    StartCoroutine(ReconnectCoroutine());
                }
            }
        }
        else
        {
            Debug.LogWarning("ImprovedWebGLBridge: WebSocket not connected, message not sent");
            if (!isReconnecting)
            {
                StartCoroutine(ReconnectCoroutine());
            }
        }
        #else
        DebugLog($"ImprovedWebGLBridge: Message would be sent to server: {message}");
        #endif
    }

    private bool AttemptConnection()
    {
        try
        {
            connectCoroutine = StartCoroutine(ConnectToServer());
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"ImprovedWebGLBridge: Error during reconnection attempt: {e.Message}");
            return false;
        }
    }

    private IEnumerator ReconnectCoroutine()
    {
        if (isReconnecting || isQuitting)
            yield break;

        isReconnecting = true;
        int retryCount = 0;

        while (retryCount < maxRetryAttempts && !isConnected && !isQuitting)
        {
            DebugLog($"ImprovedWebGLBridge: Attempting to reconnect (attempt {retryCount + 1}/{maxRetryAttempts})...");
            
            // 指数バックオフで待機時間を増やす
            float waitTime = Mathf.Min(reconnectDelay * Mathf.Pow(2, retryCount), 30f);
            yield return new WaitForSeconds(waitTime);
            
            if (isQuitting)
            {
                break;
            }

            // 接続試行
            bool success = AttemptConnection();
            if (!success)
            {
                Debug.LogWarning("ImprovedWebGLBridge: Reconnection attempt initialization failed");
            }
            
            // 接続が確立されるまで待機
            float elapsed = 0f;
            float timeout = 5f;
            while (!isConnected && elapsed < timeout)
            {
                elapsed += 0.1f;
                yield return new WaitForSeconds(0.1f);
            }
            
            if (isConnected)
            {
                DebugLog("ImprovedWebGLBridge: Reconnection successful!");
                break;
            }

            retryCount++;
        }

        if (!isConnected && !isQuitting)
        {
            Debug.LogError("ImprovedWebGLBridge: Failed to reconnect after maximum retries");
        }

        isReconnecting = false;
    }

    private void OnApplicationQuit()
    {
        isQuitting = true;
        CloseExistingConnection();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }

        if (connectCoroutine != null)
        {
            StopCoroutine(connectCoroutine);
            connectCoroutine = null;
        }
    }
    
    // 処理中のメッセージに何らかの音声データが含まれているか確認
    private bool HasAudioData(string message)
    {
        // "audio"フィールドがあり、空または"null"でない場合
        return message.Contains("\"audio\":") && 
               !message.Contains("\"audio\":\"\"") && 
               !message.Contains("\"audio\":null");
    }
}