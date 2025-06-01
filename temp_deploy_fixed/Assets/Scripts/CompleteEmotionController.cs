using UnityEngine;
using System;
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

#if VRM_IMPORTED
using VRM;
#endif

public class CompleteEmotionController : MonoBehaviour
{
    // 顔のメッシュ
    [Header("顔のメッシュ")]
    public SkinnedMeshRenderer faceMeshRenderer;

    // アニメーション参照（Animator版）
    [Header("体のアニメーション")]
    public Animator bodyAnimator;
    
    // VRMサポート
    [Header("VRM設定")]
    public bool useVRM = true;
    
#if VRM_IMPORTED
    // VRMモデルとの連携用の変数
    private VRMBlendShapeProxy vrmProxy;
#endif
    
    // BlendShape強度設定用のクラス
    [System.Serializable]
    public class BlendShapeIntensity
    {
        public string blendShapeName;
        [Range(0, 100)]
        public float intensity = 100f;
    }
    
    // 顔のBlendShape名のグループ管理
    [System.Serializable]
    public class BlendShapeGroup
    {
        public string groupName; // "happy", "sad"などの感情名
        public string[] blendShapeNames; // この感情に対応するBlendShape名の配列
        public BlendShapeIntensity[] intensities; // 個別のBlendShape強度設定
    }
    
    // 各感情のBlendShapeグループを登録
    [Header("顔のBlendShapeグループ")]
    public BlendShapeGroup[] blendShapeGroups;
    
    // BlendShapeの個別強度設定
    [Header("BlendShape個別強度設定")]
    public BlendShapeIntensity[] customIntensities;
    
    // 口パク用BlendShape
    [Header("口パク用BlendShape")]
    public string[] mouthOpenBlendShapes = { "Fcl_MTH_A", "Fcl_MTH_I", "Fcl_MTH_U", "Fcl_MTH_E", "Fcl_MTH_O" };  // 多様な口の形状を使用
    public string[] mouthCloseBlendShapes = { "Fcl_MTH_Close" };
    
    // 瞬き用BlendShape
    [Header("瞬き用BlendShape")]
    public string[] blinkBlendShapes = { "Fcl_EYE_Close" };
    
    // 瞬き設定
    [Header("瞬き設定")]
    public bool enableBlinking = true;
    [Range(1f, 10f)]
    public float minBlinkInterval = 2.5f;
    [Range(1f, 10f)]
    public float maxBlinkInterval = 5f;
    [Range(0.1f, 1f)]
    public float blinkDuration = 0.12f;
    
    // 口パク設定
    [Header("口パク設定")]
    [Range(50f, 100f)]
    public float mouthOpenIntensity = 100f; // 強度を最大に変更
    [Range(0.05f, 0.5f)]
    public float mouthOpenDuration = 0.13f;
    [Range(0.05f, 0.3f)]
    public float mouthCloseDuration = 0.09f;
    public bool enableLipSync = true;  // 口パク有効/無効フラグ
    
    // 体のアニメーション名
    [Header("体のアニメーション名")]
    public string idleBodyAnimName = "Body_Idle";
    public string happyBodyAnimName = "Body_Happy";
    public string sadBodyAnimName = "Body_Sad";
    public string angryBodyAnimName = "Body_Angry";
    public string surprisedBodyAnimName = "Body_Surprised";
    public string talkingNeutralBodyAnimName = "TalkingNeutral"; // 会話中のニュートラルアニメーション

    // BlendShape名とインデックスのマッピング
    private Dictionary<string, int> blendShapeIndexMap = new Dictionary<string, int>();

    // 感情名とBlendShapeグループのマッピング
    private Dictionary<string, List<string>> emotionBlendShapeMap = new Dictionary<string, List<string>>();
    
    // BlendShape名と強度のマッピング
    private Dictionary<string, float> blendShapeIntensityMap = new Dictionary<string, float>();

    // BlendShapeの設定値
    [Header("表情BlendShape設定")]
    [Range(0, 100)]
    public float defaultBlendShapeIntensity = 100f; // デフォルト強度
    [Range(0, 5)]
    public float blendShapeTransitionSpeed = 3.0f;

    // 音声再生用
    private AudioSource audioSource;
    private Coroutine talkingCoroutine;
    private Coroutine blendShapeCoroutine;
    private Coroutine emotionMaintainCoroutine;
    private Coroutine blinkCoroutine; // 瞬きコルーチン

    // 現在のBlendShape値
    private Dictionary<string, float> currentBlendShapeValues = new Dictionary<string, float>();
    private Dictionary<string, float> targetBlendShapeValues = new Dictionary<string, float>();

    // 現在の感情と状態
    private string currentEmotion = "neutral";
    public string baseEmotion { get; private set; } = "neutral";  // 基本感情を保持（読み取り専用プロパティ）
    public bool isTalking { get; private set; } = false;          // 話している状態かどうか（読み取り専用プロパティ）
    private bool isResponding = false;                           // 質問に応答中かどうか

    // 自動口パク検出用
    private Dictionary<string, int> autoDetectedMouthShapes = new Dictionary<string, int>();
    private bool hasInitializedMouthShapes = false;

    // デバッグモード
    [Header("設定")]
    public bool debugMode = true;
    
    // テストモード
    [Header("テスト設定")]
    public bool testMode = false;
    private float testTimer = 0f;
    private int testEmotionIndex = 0;
    private string[] testEmotions = { "neutral", "happy", "sad", "angry", "surprised" };
    
    // テスト用音声データ
    private byte[] dummyAudioData;
    
    // 会話状態監視タイマー
    private float talkingCheckTimer = 0f;
    private bool isInTalkingSession = false;  // 会話セッション中かどうか

    // 問題修正：アニメーション遷移の安定性向上のために追加
    private bool isAnimationTransitioning = false;
    private float transitionTime = 0.3f; // アニメーション遷移にかかる時間を延長
    private bool mouthMovementActive = false; // 口パク動作中フラグ

    // 問題修正：口パク強制更新用のタイマー
    private float mouthUpdateTimer = 0f;
    private const float MOUTH_UPDATE_INTERVAL = 0.08f; // より頻繁にチェック（0.2秒→0.08秒）

    // 問題修正：口パク優先度の高いBlendShape名
    private string[] priorityMouthShapes = { 
        "Fcl_MTH_A", "A", "あ", "Mouth_A", "MouthOpen", "OpenMouth", "JawOpen",
        "MTH_A", "MTH_Open", "Mouth", "Fcl_MTH_O", "O", "お",
        // 指定されたBlendShape名を優先リストに追加
        "Fcl_MTH_I", "Fcl_MTH_U", "Fcl_MTH_E", "Fcl_MTH_Close"
    };

    // 修正: 口パク状態管理の強化
    private bool isMouthMoving = false;
    private int activeMouthBlendShapeIndex = -1;
    private string activeMouthBlendShapeName = "";

    // 追加: ダイレクト口パク適用のための保存リスト
    private List<KeyValuePair<int, float>> mouthBlendShapeDirectValues = new List<KeyValuePair<int, float>>();
    
    // 追加: 口パク動作の強制適用間隔
    private float forceMouthMovementInterval = 0.1f;
    private float forceMouthMovementTimer = 0f;
    
    // 追加: WebGL環境での口パク強制適用
    private bool forceWebGLMouthMovement = true;
    
    // 追加: 指定されたBlendShape名とインデックスのマッピング
    private Dictionary<string, int> specificMouthShapes = new Dictionary<string, int>();

    // 追加: 母音BlendShapeの配列（口パクのバリエーション用）
    private string[] vowelBlendShapes = { "Fcl_MTH_A", "Fcl_MTH_I", "Fcl_MTH_U", "Fcl_MTH_E", "Fcl_MTH_O" };
    
    // 追加: 現在のアクティブな母音BlendShape
    private string currentVowelBlendShape = "Fcl_MTH_A";
    
    // 追加: 母音切り替えタイマー
    private float vowelChangeTimer = 0f;
    private float vowelChangeDuration = 0.3f; // 母音切り替え間隔

    private void Awake()
    {
        #if UNITY_WEBGL && !UNITY_EDITOR
        // WebGLビルドではテストモードを強制的に無効化
        testMode = false;
        Debug.Log("WebGLビルドのためテストモードを無効化しました");
        #endif
        
        // ダミー音声データを初期化（テスト用）
        dummyAudioData = new byte[44100 * 2]; // 44.1kHz, 16bit, 1秒
        
        // AudioSourceの初期化
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            DebugLog("AudioSourceコンポーネントを追加しました");
        }
        audioSource.playOnAwake = false;

        // フェイスメッシュが設定されていない場合は自動検索
        if (faceMeshRenderer == null)
        {
            faceMeshRenderer = GetComponentInChildren<SkinnedMeshRenderer>();
            if (faceMeshRenderer != null)
            {
                DebugLog($"自動検出したSkinnedMeshRenderer: {faceMeshRenderer.name}");
            }
            else
            {
                Debug.LogWarning("SkinnedMeshRendererが見つかりません。手動で設定してください。");
            }
        }
        
        // Animatorが設定されていない場合は自動検索
        if (bodyAnimator == null)
        {
            bodyAnimator = GetComponent<Animator>();
            if (bodyAnimator == null)
            {
                bodyAnimator = GetComponentInChildren<Animator>();
            }
            
            if (bodyAnimator != null)
            {
                DebugLog($"自動検出したAnimator: {bodyAnimator.name}");
            }
            else
            {
                Debug.LogWarning("Animatorコンポーネントが見つかりません。手動で設定してください。");
            }
        }
        
#if VRM_IMPORTED
        // VRMのBlendShapeProxyを検索
        if (useVRM)
        {
            vrmProxy = GetComponent<VRMBlendShapeProxy>();
            if (vrmProxy == null)
            {
                vrmProxy = GetComponentInChildren<VRMBlendShapeProxy>();
            }
            
            if (vrmProxy != null)
            {
                DebugLog($"VRMBlendShapeProxyを検出: {vrmProxy.name}");
            }
            else
            {
                Debug.LogWarning("VRMBlendShapeProxyが見つかりません。VRMモデルではないかもしれません。");
                useVRM = false;
            }
        }
#else
        useVRM = false;
#endif

        // BlendShapeインデックスをマッピング
        if (faceMeshRenderer != null)
        {
            InitializeBlendShapeMap();
            // BlendShape値の初期化
            InitializeBlendShapeValues();
        }
        
        // カスタム強度設定を初期化
        InitializeBlendShapeIntensities();
        
        // 感情BlendShapeグループのマッピングを初期化
        InitializeEmotionBlendShapeMap();
        
        // 口パクBlendShapeの自動検出
        AutoDetectMouthShapes();
        
        // アニメーションの確認
        CheckAnimatorSetup();
        
        // Animatorパラメータの検証
        ValidateAnimatorParameters();
        
        // 初期表情設定
        StartCoroutine(InitializeAnimatorDelayed());
    }
    
    // ここから追加する新しいメソッド群
    
    // BlendShapeを直接設定するメソッド
    public void SetBlendShapeDirectly(int index, float value)
    {
        if (faceMeshRenderer == null || index < 0) return;
        
        try
        {
            faceMeshRenderer.SetBlendShapeWeight(index, value);
        }
        catch (Exception ex)
        {
            Debug.LogError($"BlendShape直接設定エラー: {ex.Message}");
        }
    }

    // レンダラーを強制更新するメソッド
    public void ForceUpdateRenderer()
    {
        if (faceMeshRenderer == null) return;
        
        try
        {
            // WebGL環境では特別な処理
            #if UNITY_WEBGL && !UNITY_EDITOR
            // 軽量な方法：Enableの切り替えのみ
            bool wasEnabled = faceMeshRenderer.enabled;
            faceMeshRenderer.enabled = !wasEnabled;
            faceMeshRenderer.enabled = wasEnabled;
            
            // バウンド変更でダーティフラグを立てる（軽量版）
            Bounds originalBounds = faceMeshRenderer.localBounds;
            Bounds tempBounds = new Bounds(
                originalBounds.center + Vector3.one * 0.001f,
                originalBounds.size
            );
            faceMeshRenderer.localBounds = tempBounds;
            faceMeshRenderer.localBounds = originalBounds;
            #else
            // 非WebGL環境では従来の方法を使用
            bool wasEnabled = faceMeshRenderer.enabled;
            faceMeshRenderer.enabled = false;
            System.Threading.Thread.Sleep(1);
            faceMeshRenderer.enabled = wasEnabled;
            
            // 他の重い処理も実行（WebGL以外）
            // ...既存の実装...
            #endif
            
            // 保存されたBlendShape値の再適用（重要）
            if (mouthMovementActive && mouthBlendShapeDirectValues.Count > 0)
            {
                foreach (var pair in mouthBlendShapeDirectValues)
                {
                    float currentValue = faceMeshRenderer.GetBlendShapeWeight(pair.Key);
                    if (Math.Abs(currentValue - pair.Value) > 1.0f)
                    {
                        faceMeshRenderer.SetBlendShapeWeight(pair.Key, pair.Value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"レンダラー更新エラー: {ex.Message}");
        }
    }

    // 会話フラグを設定するメソッド
    public void SetTalkingFlag(bool talking)
    {
        isTalking = talking;
        
        if (bodyAnimator != null)
        {
            bodyAnimator.SetBool("Is Talking", talking);
            bodyAnimator.Update(0f);
            
            // WebGL環境では特別な処理
            #if UNITY_WEBGL && !UNITY_EDITOR
            if (talking && baseEmotion == "neutral")
            {
                bodyAnimator.SetInteger("BodyAnimation", 5); // TalkingNeutral = 5
                bodyAnimator.Update(0f);
            }
            #endif
        }
    }

    // アニメーターパラメータを直接設定するメソッド
    public void SetAnimatorParameter(string paramName, object value)
    {
        if (bodyAnimator == null) return;
        
        try
        {
            if (value is bool boolValue)
            {
                bodyAnimator.SetBool(paramName, boolValue);
            }
            else if (value is int intValue)
            {
                bodyAnimator.SetInteger(paramName, intValue);
            }
            else if (value is float floatValue)
            {
                bodyAnimator.SetFloat(paramName, floatValue);
            }
            
            bodyAnimator.Update(0f);
        }
        catch (Exception ex)
        {
            Debug.LogError($"アニメーターパラメータ設定エラー: {ex.Message}");
        }
    }

    // 会話状態のチェックメソッド
    public bool IsTalkingInAnimator()
    {
        if (bodyAnimator == null) return false;
        return bodyAnimator.GetBool("Is Talking");
    }

    // TalkingNeutralモードのチェックメソッド
    public bool IsInTalkingNeutralMode()
    {
        if (bodyAnimator == null) return false;
        return bodyAnimator.GetInteger("BodyAnimation") == 5; // TalkingNeutralは5
    }

    // 口が開いている状態かチェックするメソッド
    public bool IsMouthOpen()
    {
        if (faceMeshRenderer == null) return false;
        
        try
        {
            // 全てのBlendShapeをチェック
            foreach (var pair in autoDetectedMouthShapes)
            {
                float value = faceMeshRenderer.GetBlendShapeWeight(pair.Value);
                if (value > 10f) // しきい値を10に設定
                {
                    return true;
                }
            }
            
            // 指定されたBlendShapeもチェック
            foreach (var pair in specificMouthShapes)
            {
                if (pair.Key != "Fcl_MTH_Close")
                {
                    float value = faceMeshRenderer.GetBlendShapeWeight(pair.Value);
                    if (value > 10f)
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception) { /* 例外は無視 */ }
        
        return false;
    }

    // BlendShapeのインデックスを取得するメソッド
    public int GetBlendShapeIndex(string blendShapeName)
    {
        return GetBlendShapeIndexDirect(blendShapeName);
    }

    // BlendShapeのインデックスを直接取得するメソッド
    public int GetBlendShapeIndexDirect(string blendShapeName)
    {
        if (faceMeshRenderer == null || faceMeshRenderer.sharedMesh == null) 
            return -1;
        
        // ブレンドシェイプ名からインデックスを取得
        for (int i = 0; i < faceMeshRenderer.sharedMesh.blendShapeCount; i++)
        {
            if (faceMeshRenderer.sharedMesh.GetBlendShapeName(i) == blendShapeName)
            {
                return i;
            }
        }
        
        return -1;
    }

    // 強制的に口を動かすメソッド
    public void ForceMouthMovement()
    {
        if (faceMeshRenderer == null) return;
        
        try
        {
            // ランダムな母音BlendShapeを選択
            string[] vowels = { "Fcl_MTH_A", "Fcl_MTH_I", "Fcl_MTH_U", "Fcl_MTH_E", "Fcl_MTH_O" };
            string selectedVowel = vowels[UnityEngine.Random.Range(0, vowels.Length)];
            
            // インデックスを取得
            int index = GetBlendShapeIndexDirect(selectedVowel);
            
            if (index >= 0)
            {
                // 他のBlendShapeをリセット
                foreach (string vowel in vowels)
                {
                    int vIndex = GetBlendShapeIndexDirect(vowel);
                    if (vIndex >= 0 && vIndex != index)
                    {
                        faceMeshRenderer.SetBlendShapeWeight(vIndex, 0f);
                    }
                }
                
                // 選択した母音BlendShapeを設定
                float intensity = UnityEngine.Random.Range(70f, 100f);
                faceMeshRenderer.SetBlendShapeWeight(index, intensity);
                
                // ダイレクト値リストに保存
                mouthBlendShapeDirectValues.Clear();
                AddDirectMouthValue(index, intensity);
                
                // レンダラーを強制更新
                ForceUpdateRenderer();
                
                Debug.Log($"強制口パク: {selectedVowel} = {intensity}");
            }
            else
            {
                // インデックスが見つからない場合は自動検出したものを使用
                if (autoDetectedMouthShapes.Count > 0)
                {
                    var first = autoDetectedMouthShapes.First();
                    faceMeshRenderer.SetBlendShapeWeight(first.Value, 80f);
                    
                    // ダイレクト値リストに保存
                    mouthBlendShapeDirectValues.Clear();
                    AddDirectMouthValue(first.Value, 80f);
                    
                    ForceUpdateRenderer();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"強制口パクエラー: {ex.Message}");
        }
    }

    // 口を閉じるメソッド
    public void CloseMouth()
    {
        if (faceMeshRenderer == null) return;
        
        try
        {
            // 口パク用BlendShapeをすべてリセット
            foreach (var pair in autoDetectedMouthShapes)
            {
                faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
            }
            
            // 母音BlendShapeもリセット
            foreach (var pair in specificMouthShapes)
            {
                if (pair.Key != "Fcl_MTH_Close")
                {
                    faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
                }
            }
            
            // Fcl_MTH_Closeがあれば使用
            int closeIndex = GetBlendShapeIndexDirect("Fcl_MTH_Close");
            if (closeIndex >= 0)
            {
                faceMeshRenderer.SetBlendShapeWeight(closeIndex, 100f);
                
                // ダイレクト値リストに保存
                mouthBlendShapeDirectValues.Clear();
                AddDirectMouthValue(closeIndex, 100f);
            }
            
            // レンダラーを強制更新
            ForceUpdateRenderer();
            
            Debug.Log("口を閉じました");
        }
        catch (Exception ex)
        {
            Debug.LogError($"口を閉じる処理でエラー: {ex.Message}");
        }
    }

    // すべてのBlendShapeをリセットするメソッド
    public void ResetAllBlendShapes()
    {
        if (faceMeshRenderer == null || faceMeshRenderer.sharedMesh == null) return;
        
        try
        {
            for (int i = 0; i < faceMeshRenderer.sharedMesh.blendShapeCount; i++)
            {
                faceMeshRenderer.SetBlendShapeWeight(i, 0f);
            }
            
            // レンダラーを強制更新
            ForceUpdateRenderer();
            
            Debug.Log("すべてのBlendShapeをリセットしました");
        }
        catch (Exception ex)
        {
            Debug.LogError($"BlendShapeリセットエラー: {ex.Message}");
        }
    }

    // FaceMeshRendererを取得するメソッド
    public SkinnedMeshRenderer GetFaceMeshRenderer()
    {
        return faceMeshRenderer;
    }

    // 会話を開始するメソッド
    public void StartTalking()
    {
        isTalking = true;
        
        if (bodyAnimator != null)
        {
            bodyAnimator.SetBool("Is Talking", true);
            
            // TalkingNeutralモードの場合
            if (baseEmotion == "neutral")
            {
                bodyAnimator.SetInteger("BodyAnimation", 5); // TalkingNeutral = 5
            }
            
            bodyAnimator.Update(0f);
        }
        
        Debug.Log("会話を開始しました");
    }

    // 会話を停止するメソッド
    public void StopTalking()
    {
        isTalking = false;
        
        if (bodyAnimator != null)
        {
            bodyAnimator.SetBool("Is Talking", false);
            bodyAnimator.Update(0f);
        }
        
        Debug.Log("会話を停止しました");
    }

    // 追加：ダイレクト口パク値を保存
    private void AddDirectMouthValue(int index, float value)
    {
        // 既存の値があれば更新、なければ追加
        bool found = false;
        for (int i = 0; i < mouthBlendShapeDirectValues.Count; i++)
        {
            if (mouthBlendShapeDirectValues[i].Key == index)
            {
                mouthBlendShapeDirectValues[i] = new KeyValuePair<int, float>(index, value);
                found = true;
                break;
            }
        }
        
        if (!found)
        {
            mouthBlendShapeDirectValues.Add(new KeyValuePair<int, float>(index, value));
        }
    }
    
    // 問題修正：アニメーション状態を強制設定するメソッド
    public void ForceSetState(string stateName)
    {
        if (bodyAnimator == null) return;

        Debug.Log($"状態を強制設定: {stateName}");
        
        // TalkingNeutralの特殊ケース
        if (stateName.ToLower() == "talkingneutral")
        {
            // TalkingNeutral状態をセット
            bodyAnimator.SetInteger("BodyAnimation", 5); // TalkingNeutral = 5
            bodyAnimator.SetInteger("Expression", 0);    // Neutral
            bodyAnimator.SetBool("Is Talking", true);
            bodyAnimator.Update(0f);
            
            // 現在の感情を更新
            baseEmotion = "neutral";
            currentEmotion = "neutral";
            isTalking = true;
            
            Debug.Log("TalkingNeutral状態を強制設定しました");
        }
        else
        {
            // 感情に対応する値を取得
            int bodyAnimValue = 0;
            int expressionValue = 0;
            
            switch (stateName.ToLower())
            {
                case "happy":
                    bodyAnimValue = 1;
                    expressionValue = 1;
                    break;
                case "sad":
                    bodyAnimValue = 2;
                    expressionValue = 2;
                    break;
                case "angry":
                    bodyAnimValue = 3;
                    expressionValue = 3;
                    break;
                case "surprised":
                    bodyAnimValue = 4;
                    expressionValue = 4;
                    break;
                default: // neutral
                    bodyAnimValue = 0;
                    expressionValue = 0;
                    break;
            }
            
            // アニメーターパラメータを設定
            bodyAnimator.SetInteger("BodyAnimation", bodyAnimValue);
            bodyAnimator.SetInteger("Expression", expressionValue);
            bodyAnimator.Update(0f);
            
            // 現在の感情を更新
            baseEmotion = stateName.ToLower();
            currentEmotion = stateName.ToLower();
            
            Debug.Log($"{stateName}状態を強制設定しました");
        }
    }
    
    // 問題修正：口パク状態を強制更新するメソッド
    private void ForceUpdateMouthMovement()
    {
        if (!mouthMovementActive || !audioSource.isPlaying || faceMeshRenderer == null) return;
        
        // 現在の口パクBlendShapeをチェック
        bool isAnyMouthOpen = false;
        
        // 1. 指定されたBlendShapeをチェック
        foreach (var pair in specificMouthShapes)
        {
            float value = faceMeshRenderer.GetBlendShapeWeight(pair.Value);
            if (value > 10f)
            {
                isAnyMouthOpen = true;
                break;
            }
        }
        
        // 2. 自動検出したBlendShapeをチェック
        if (!isAnyMouthOpen)
        {
            foreach (var pair in autoDetectedMouthShapes)
            {
                float value = faceMeshRenderer.GetBlendShapeWeight(pair.Value);
                if (value > 10f)
                {
                    isAnyMouthOpen = true;
                    break;
                }
            }
        }
        
        // 3. 保存されたダイレクト値を直接適用
        if (!isAnyMouthOpen && mouthBlendShapeDirectValues.Count > 0)
        {
            Debug.Log("保存されたダイレクト口パク値を再適用");
            
            bool applied = false;
            foreach (var pair in mouthBlendShapeDirectValues)
            {
                try
                {
                    faceMeshRenderer.SetBlendShapeWeight(pair.Key, pair.Value);
                    applied = true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"BlendShape再適用エラー: {ex.Message}");
                }
            }
            
            if (applied)
            {
                ForceUpdateRenderer();
                return;
            }
        }
        
        // 4. 口が閉じている場合は強制的に口を開く
        if (!isAnyMouthOpen)
        {
            Debug.Log("口が閉じているため強制的に開きます");
            ApplyVowelChange();
        }
    }
    
    // 問題修正：母音切り替えを適用
    private void ApplyVowelChange()
    {
        if (faceMeshRenderer == null || !mouthMovementActive || !audioSource.isPlaying) return;
        
        // 母音BlendShapeを適用
        if (specificMouthShapes.Count > 0)
        {
            // 現在の母音を取得
            if (specificMouthShapes.TryGetValue(currentVowelBlendShape, out int index))
            {
                try
                {
                    // 他の母音BlendShapeをリセット
                    foreach (var pair in specificMouthShapes)
                    {
                        if (pair.Key != currentVowelBlendShape && pair.Key != "Fcl_MTH_Close")
                        {
                            faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
                        }
                    }
                    
                    // 選択した母音BlendShapeを設定
                    float randomIntensity = UnityEngine.Random.Range(mouthOpenIntensity * 0.7f, mouthOpenIntensity * 1.2f);
                    faceMeshRenderer.SetBlendShapeWeight(index, randomIntensity);
                    
                    // 直接値を更新
                    mouthBlendShapeDirectValues.Clear();
                    AddDirectMouthValue(index, randomIntensity);
                    
                    // レンダラー更新を強制
                    ForceUpdateRenderer();
                    
                    Debug.Log($"母音切り替え: {currentVowelBlendShape} = {randomIntensity}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"母音切り替え適用エラー: {e.Message}");
                }
            }
        }
    }
    
    // 問題修正：TalkingNeutral状態へのスムーズな遷移
    private IEnumerator SmoothTransitionToTalkingNeutral()
    {
        if (bodyAnimator == null) yield break;
        
        Debug.Log("TalkingNeutralへのスムーズな遷移を開始（WebGL最適化版）");
        
        // 遷移中フラグを設定
        isAnimationTransitioning = true;
        
        // WebGL環境では直接パラメータ設定を優先
        #if UNITY_WEBGL && !UNITY_EDITOR
        // 一度にパラメータを設定
        bodyAnimator.SetBool("Is Talking", true);
        bodyAnimator.SetInteger("BodyAnimation", 5); // TalkingNeutral
        bodyAnimator.SetInteger("Expression", 0);    // Neutral
        bodyAnimator.Update(0f); // 即時更新
        
        // 更新を確認するために1フレーム待機
        yield return new WaitForEndOfFrame();
        
        // 確認と修正（必要な場合）
        if (bodyAnimator.GetInteger("BodyAnimation") != 5 || !bodyAnimator.GetBool("Is Talking"))
        {
            Debug.Log("WebGL: パラメータ適用を再試行");
            bodyAnimator.SetBool("Is Talking", true);
            bodyAnimator.SetInteger("BodyAnimation", 5);
            bodyAnimator.Update(0f);
        }
        #else
        // 非WebGL環境では元のロジックを使用
        // 現在の状態を取得
        int currentBodyAnim = bodyAnimator.GetInteger("BodyAnimation");
        
        // 現在がTalkingNeutral(値=5)でない場合のみ遷移を行う
        if (currentBodyAnim != 5)
        {
            // 元のロジック...
            // アニメーターをリセット
            bool rebindSuccess = SafeRebindAnimator();
            
            if (rebindSuccess)
            {
                yield return new WaitForSeconds(0.05f);
            }
            
            bodyAnimator.SetInteger("BodyAnimation", 0);
            bodyAnimator.SetInteger("Expression", 0);
            bodyAnimator.SetBool("Is Talking", false);
            bodyAnimator.SetBool("Is Thinking", false);
            bodyAnimator.Update(0f);
            
            yield return new WaitForSeconds(0.2f);
            
            // その他の既存処理...
        }
        #endif
        
        // TalkingNeutral状態の確認（WebGL環境でも必要）
        if (bodyAnimator.GetInteger("BodyAnimation") != 5)
        {
            Debug.LogWarning("TalkingNeutral状態が正しく設定されていないため再設定");
            bodyAnimator.SetInteger("BodyAnimation", 5);
            bodyAnimator.Update(0f);
        }
        
        // 会話フラグの確認（WebGL環境でも必要）
        if (!bodyAnimator.GetBool("Is Talking"))
        {
            Debug.LogWarning("Is Talkingフラグがfalseになっているため再設定");
            bodyAnimator.SetBool("Is Talking", true);
            bodyAnimator.Update(0f);
        }
        
        // 遷移完了のため、フラグをリセット
        yield return new WaitForSeconds(0.1f);
        isAnimationTransitioning = false;
        
        Debug.Log("TalkingNeutral状態への遷移完了");
    }
    
    // 問題修正：音声再生と口パクアニメーションを連携する強化版メソッド
    public IEnumerator PlayAudioWithMouthAnimation(byte[] audioData)
    {
        Debug.Log("*** 口パクコルーチン開始 ***");
        
        // 口パク状態をアクティブに設定（重要）
        mouthMovementActive = true;
        isTalking = true;
        isInTalkingSession = true;
        isMouthMoving = true; // 新しいフラグを追加

        // 音声再生前に確実に口パク用BlendShapeを初期化
        InitializeMouthBlendShapes();
        
        // 保存されたダイレクト口パク値をクリア
        mouthBlendShapeDirectValues.Clear();
        
        // 母音切り替えタイマーをリセット
        vowelChangeTimer = 0f;
        
        // 最初の母音をランダム選択
        currentVowelBlendShape = vowelBlendShapes[UnityEngine.Random.Range(0, vowelBlendShapes.Length)];
        
        // 確実に会話状態をONにする
        if (bodyAnimator != null)
        {
            bodyAnimator.SetBool("Is Talking", true);
            // Neutral感情の場合はTalkingNeutralを設定
            if (currentEmotion.ToLower() == "neutral")
            {
                bodyAnimator.SetInteger("BodyAnimation", 5); // TalkingNeutral
            }
            bodyAnimator.Update(0f);
            Debug.Log("会話フラグを強制的にONに設定");
        }

        // 音声再生前に直接口パクBlendShapeを強制適用（即効性を高める）
        ForceApplyMouthMovement();
        
        // 口パク用の主要BlendShapeを選択・準備
        PrepareMainMouthBlendShape();

        // AudioClipを作成して再生
        AudioClip clip = null;
        bool audioAvailable = false;
        
        if (audioData != null && audioData.Length > 0)
        {
            try 
            {
                clip = WavUtility.ToAudioClip(audioData);
                
                // 音声変換の失敗をチェック
                if (WavUtility.LastConversionFailed || clip == null)
                {
                    throw new Exception("音声変換に失敗しました");
                }
                
                audioAvailable = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"音声変換エラー: {e.Message}");
                
                // 音声変換に失敗しても口パク動作は継続する
                audioAvailable = false;
                clip = null;
            }
        }
        
        // 追加: 口パクタイマーをリセットと口パク動作フラグを確実にON
        mouthUpdateTimer = 0f;
        forceMouthMovementTimer = 0f;
        mouthMovementActive = true;
        
        // 音声が利用可能な場合は再生
        if (audioAvailable && clip != null)
        {
            Debug.Log($"音声再生開始: 長さ={clip.length}秒, 口パク動作フラグ={mouthMovementActive}");
            audioSource.clip = clip;
            audioSource.Play();
        }
        else
        {
            // 音声が利用できない場合はダミー音声の長さだけループ
            Debug.Log("音声データなし/変換失敗: ダミー時間で口パク動作を継続");
        }
        
        // 口パク動作の実行時間
        float mouthMovementDuration = 3.0f; // デフォルト3秒
        
        if (audioAvailable && clip != null)
        {
            // 音声があれば、その長さに合わせる
            mouthMovementDuration = clip.length;
        }
        
        float startTime = Time.time;
        
        // 音声再生中または指定時間内のループ
        while ((audioAvailable && audioSource.isPlaying) || 
               (!audioAvailable && (Time.time - startTime) < mouthMovementDuration))
        {
            // 母音切り替えタイマー更新
            vowelChangeTimer += Time.deltaTime;
            if (vowelChangeTimer >= vowelChangeDuration)
            {
                vowelChangeTimer = 0f;
                // 次の母音をランダムに選択
                currentVowelBlendShape = vowelBlendShapes[UnityEngine.Random.Range(0, vowelBlendShapes.Length)];
                // 母音切り替えを適用
                ApplyVowelChange();
            }
            
            // 定期的に口パクの状態を強制更新
            forceMouthMovementTimer += Time.deltaTime;
            if (forceMouthMovementTimer >= forceMouthMovementInterval)
            {
                forceMouthMovementTimer = 0f;
                ForceUpdateMouthMovement();
            }
            
            // 会話フラグを定期的に確認
            if (bodyAnimator != null && !bodyAnimator.GetBool("Is Talking"))
            {
                bodyAnimator.SetBool("Is Talking", true);
                bodyAnimator.Update(0f);
                Debug.Log("音声再生中に会話フラグがOFFになったため再設定");
            }
            
            yield return null;
        }
        
        Debug.Log("音声再生または口パク時間終了");
        
        // 口パクを停止し、確実に口を閉じる
        try
        {
            // まずすべての母音BlendShapeをリセット
            foreach (var pair in specificMouthShapes)
            {
                if (pair.Key != "Fcl_MTH_Close")
                {
                    faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
                }
            }
            
            foreach (var pair in autoDetectedMouthShapes)
            {
                if (pair.Key != "Fcl_MTH_Close")
                {
                    faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
                }
            }
            
            // Fcl_MTH_Closeを使って口を閉じる
            int closeIndex = GetBlendShapeIndexDirect("Fcl_MTH_Close");
            if (closeIndex >= 0)
            {
                faceMeshRenderer.SetBlendShapeWeight(closeIndex, 100f);
            }
            else
            {
                // 代替手段
                ApplyMouthClose();
            }
            
            // レンダラーを確実に更新
            ForceUpdateRenderer();
        }
        catch (Exception ex)
        {
            Debug.LogError($"会話終了時の口閉じエラー: {ex.Message}");
            // エラーが発生しても続行
        }

        // 口パク状態をリセット
        ResetMouthState();
        
        // フラグのリセットを遅延させる（即時終了を防ぐ）
        yield return new WaitForSeconds(0.5f);
        
        // 会話終了時のフラグリセット
        isTalking = false;
        isResponding = false;
        mouthMovementActive = false;
        isMouthMoving = false;

        // ダイレクトBlendShape値をクリア
        mouthBlendShapeDirectValues.Clear();
        
        // 少し遅らせてから会話セッションを終了
        yield return new WaitForSeconds(0.2f);
        isInTalkingSession = false;
        
        // 会話終了時には必ず「Is Talking」フラグをfalseに設定
        if (bodyAnimator != null)
        {
            bodyAnimator.SetBool("Is Talking", false);
            bodyAnimator.Update(0f);
            Debug.Log("会話終了フラグをOFFに設定 - Is Talking = false");
        }
        
        // 話し終わったら少し待ってからNeutralに戻す
        yield return new WaitForSeconds(0.3f);
        
        // Neutralに戻す
        SetEmotion("neutral", false);
        Debug.Log("Neutralに戻りました");
        
        Debug.Log("*** 口パクコルーチン終了 ***");
    }

    // AvatarController用 - 非同期処理に対応したレスポンス処理メソッド（新規追加）
    public async Task ProcessResponseAsync(string message, byte[] audioData)
    {
        Debug.Log($"======= 非同期応答処理開始 =======");
        
        // 既存の口パク停止
        if (talkingCoroutine != null)
        {
            StopCoroutine(talkingCoroutine);
            talkingCoroutine = null;
        }
        
        // メッセージから感情を抽出
        string emotion = ExtractEmotionFromMessage(message);
        Debug.Log($"メッセージから抽出した感情: {emotion}");
        
        // 遷移中フラグをオン
        isAnimationTransitioning = true;
        StartCoroutine(ResetTransitioningFlag());
        
        // 重要: どの感情でも必ず会話状態をONに
        isTalking = true;
        isResponding = true;
        isInTalkingSession = true;
        
        // 感情設定
        SetEmotion(emotion, true);
        
        // 口パク機能を有効化
        mouthMovementActive = true;
        
        // 口パク用BlendShapeを初期化
        InitializeMouthBlendShapes();
        
        // 後続の処理を非同期で待機
        await Task.Delay(50); // 非同期処理のための少しの待機

        // 音声再生とアニメーション開始
        if (audioData != null && audioData.Length > 0)
        {
            // AudioClipを作成して再生（非同期用に改変）
            talkingCoroutine = StartCoroutine(PlayAudioWithMouthAnimation(audioData));
            Debug.Log("音声と口パクのコルーチンを開始");
        }
        else
        {
            // 音声がない場合はダミー音声で口パク動作
            Debug.Log("音声なしで口パク動作を開始");
            talkingCoroutine = StartCoroutine(PlayAudioWithMouthAnimation(dummyAudioData));
        }
        
        Debug.Log($"======= 非同期応答処理完了 =======");
    }

    // 特定の口の形状を設定するメソッド（新規追加）
    public void SetMouthShape(string shapeName, float intensity = 100f)
    {
        Debug.Log($"口の形状を設定: {shapeName}, 強度: {intensity}");
        
        if (faceMeshRenderer == null) return;
        
        try
        {
            // まず全ての口形状をリセット
            foreach (var pair in specificMouthShapes)
            {
                faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
            }
            
            foreach (var pair in autoDetectedMouthShapes)
            {
                faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
            }
            
            // 指定された形状を設定
            int shapeIndex = -1;
            
            // 指定形状のインデックスを取得
            if (specificMouthShapes.TryGetValue(shapeName, out int index))
            {
                shapeIndex = index;
            }
            else
            {
                // 指定形状が見つからない場合は直接検索
                shapeIndex = GetBlendShapeIndexDirect(shapeName);
            }
            
            if (shapeIndex >= 0)
            {
                // 形状を設定
                faceMeshRenderer.SetBlendShapeWeight(shapeIndex, intensity);
                
                // ダイレクト値を保存
                mouthBlendShapeDirectValues.Clear();
                AddDirectMouthValue(shapeIndex, intensity);
                
                // レンダラーを更新
                ForceUpdateRenderer();
                
                Debug.Log($"口の形状を設定しました: {shapeName} = {intensity}");
            }
            else
            {
                Debug.LogWarning($"指定された口の形状 {shapeName} が見つかりません");
                
                // 見つからない場合はデフォルトの口形状を使用
                if (specificMouthShapes.Count > 0)
                {
                    var firstShape = specificMouthShapes.First();
                    faceMeshRenderer.SetBlendShapeWeight(firstShape.Value, intensity);
                    
                    // ダイレクト値を保存
                    mouthBlendShapeDirectValues.Clear();
                    AddDirectMouthValue(firstShape.Value, intensity);
                    
                    // レンダラーを更新
                    ForceUpdateRenderer();
                    
                    Debug.Log($"代替の口形状を使用: {firstShape.Key} = {intensity}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"口形状設定エラー: {ex.Message}");
        }
    }
    
    // メッセージを受け取って処理するメソッド
    public void ProcessResponse(string message, byte[] audioData)
    {
        Debug.Log($"======= 応答処理開始 =======");
        
        // 既存の口パク停止
        if (talkingCoroutine != null)
        {
            StopCoroutine(talkingCoroutine);
            talkingCoroutine = null;
        }
        
        // メッセージから感情を抽出
        string emotion = ExtractEmotionFromMessage(message);
        Debug.Log($"メッセージから抽出した感情: {emotion}");
        
        // 遷移中フラグをオン
        isAnimationTransitioning = true;
        StartCoroutine(ResetTransitioningFlag());
        
        // 重要: どの感情でも必ず会話状態をONに
        isTalking = true;
        isResponding = true;
        isInTalkingSession = true;
        
        // 感情設定
        SetEmotion(emotion, true);
        
        // 口パク機能を有効化
        mouthMovementActive = true;
        
        // 口パク用BlendShapeを初期化
        InitializeMouthBlendShapes();
        
        // 音声があれば再生を開始
        if (audioData != null && audioData.Length > 0)
        {
            // 音声と口パクのコルーチンを開始
            talkingCoroutine = StartCoroutine(PlayAudioWithMouthAnimation(audioData));
            Debug.Log("音声と口パクのコルーチンを開始");
        }
        else
        {
            // 音声がない場合はダミー音声で口パク動作
            Debug.Log("音声なしで口パク動作を開始");
            talkingCoroutine = StartCoroutine(PlayAudioWithMouthAnimation(dummyAudioData));
        }
        
        Debug.Log($"======= 応答処理完了 =======");
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
    
    // 問題修正：遷移中フラグをリセットするコルーチン
    private IEnumerator ResetTransitioningFlag(float delay = 0.2f)
    {
        yield return new WaitForSeconds(delay);
        isAnimationTransitioning = false;
        Debug.Log("アニメーション遷移フラグをリセットしました");
    }
    
    // Animatorを安全にリバインドするヘルパーメソッド
    private bool SafeRebindAnimator()
    {
        if (bodyAnimator == null) return false;
        
        try
        {
            bodyAnimator.Rebind();
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"アニメーターリセットエラー: {ex.Message}");
            return false;
        }
    }
    
    // 口パク用の主要BlendShapeを選択・準備
    private void PrepareMainMouthBlendShape()
    {
        activeMouthBlendShapeIndex = -1;
        activeMouthBlendShapeName = "";
        
        // 指定されたBlendShapeを優先使用
        foreach (string shapeName in vowelBlendShapes)
        {
            if (specificMouthShapes.TryGetValue(shapeName, out int index))
            {
                activeMouthBlendShapeIndex = index;
                activeMouthBlendShapeName = shapeName;
                Debug.Log($"指定された口パクBlendShapeとして {shapeName} を選択 (インデックス: {index})");
                currentVowelBlendShape = shapeName;
                return;
            }
        }
        
        // 優先度の高いBlendShapeを検索
        foreach (string shape in priorityMouthShapes)
        {
            if (autoDetectedMouthShapes.ContainsKey(shape))
            {
                activeMouthBlendShapeIndex = autoDetectedMouthShapes[shape];
                activeMouthBlendShapeName = shape;
                Debug.Log($"主要口パクBlendShapeとして {shape} を選択 (インデックス: {activeMouthBlendShapeIndex})");
                
                // 母音BlendShapeなら現在の母音に設定
                if (Array.IndexOf(vowelBlendShapes, shape) >= 0)
                {
                    currentVowelBlendShape = shape;
                }
                
                return;
            }
        }
        
        // それでも見つからない場合は最初のものを使用
        if (autoDetectedMouthShapes.Count > 0)
        {
            var firstPair = autoDetectedMouthShapes.First();
            activeMouthBlendShapeIndex = firstPair.Value;
            activeMouthBlendShapeName = firstPair.Key;
            Debug.Log($"代替口パクBlendShapeとして {activeMouthBlendShapeName} を選択 (インデックス: {activeMouthBlendShapeIndex})");
        }
        else if (faceMeshRenderer != null && faceMeshRenderer.sharedMesh != null)
        {
            // 最後の手段: 最初の数個のBlendShapeを試す
            int count = Math.Min(5, faceMeshRenderer.sharedMesh.blendShapeCount);
            if (count > 0)
            {
                activeMouthBlendShapeIndex = 0; // 最初のBlendShape
                activeMouthBlendShapeName = faceMeshRenderer.sharedMesh.GetBlendShapeName(0);
                Debug.Log($"緊急代替: 最初のBlendShape {activeMouthBlendShapeName} を使用 (インデックス: 0)");
            }
            else
            {
                Debug.LogWarning("口パク用BlendShapeが見つかりません。口パクは無効になります。");
            }
        }
        else
        {
            Debug.LogWarning("口パク用BlendShapeが見つかりません。口パクは無効になります。");
        }
    }
    
    // 口パク用BlendShapeの初期化を強化
    private void InitializeMouthBlendShapes()
    {
        if (faceMeshRenderer == null)
        {
            Debug.LogError("InitializeMouthBlendShapes: faceMeshRendererがnullです");
            return;
        }
        
        Debug.Log("口パク用BlendShape初期化開始");
        
        // すべての口パク関連BlendShapeをリセット
        int resetCount = 0;
        
        // 指定されたBlendShape
        foreach (var pair in specificMouthShapes)
        {
            try {
                faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
                resetCount++;
            } catch (Exception e) {
                Debug.LogError($"指定BlendShape {pair.Key} のリセットでエラー: {e.Message}");
            }
        }
        
        // 設定された口パクBlendShape
        foreach (string shape in mouthOpenBlendShapes)
        {
            if (blendShapeIndexMap.TryGetValue(shape, out int index))
            {
                try {
                    faceMeshRenderer.SetBlendShapeWeight(index, 0f);
                    resetCount++;
                } catch (Exception e) {
                    Debug.LogError($"BlendShape {shape} の設定でエラー: {e.Message}");
                }
            }
        }
        
        foreach (string shape in mouthCloseBlendShapes)
        {
            if (blendShapeIndexMap.TryGetValue(shape, out int index))
            {
                try {
                    faceMeshRenderer.SetBlendShapeWeight(index, 0f);
                    resetCount++;
                } catch (Exception e) {
                    Debug.LogError($"BlendShape {shape} の設定でエラー: {e.Message}");
                }
            }
        }
        
        // 自動検出したBlendShapeもリセット
        foreach (var pair in autoDetectedMouthShapes)
        {
            try {
                faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
                resetCount++;
            } catch (Exception e) {
                Debug.LogError($"自動検出BlendShape {pair.Key} の設定でエラー: {e.Message}");
            }
        }
        
        Debug.Log($"口パク用BlendShape初期化完了: {resetCount}個のBlendShapeをリセット");
        
        // レンダラーの更新を確実に
        ForceUpdateRenderer();
    }
    
    // 口の状態をリセットする関数
    private void ResetMouthState()
    {
        // すべての口パク用BlendShapeをリセット
        if (faceMeshRenderer != null)
        {
            // 指定されたBlendShapeをリセット
            foreach (var pair in specificMouthShapes)
            {
                try {
                    faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
                } catch (Exception) { }
            }
            
            // 自動検出したBlendShapeをリセット
            foreach (var pair in autoDetectedMouthShapes)
            {
                try {
                    faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
                } catch (Exception) { }
            }
            
            // 設定済みのBlendShapeもリセット
            foreach (string shape in mouthOpenBlendShapes)
            {
                if (blendShapeIndexMap.TryGetValue(shape, out int index))
                {
                    try {
                        faceMeshRenderer.SetBlendShapeWeight(index, 0f);
                    } catch (Exception) { }
                }
            }
            
            foreach (string shape in mouthCloseBlendShapes)
            {
                if (blendShapeIndexMap.TryGetValue(shape, out int index))
                {
                    try {
                        faceMeshRenderer.SetBlendShapeWeight(index, 0f);
                    } catch (Exception) { }
                }
            }
            
            // アクティブな口パクBlendShapeも確実にリセット
            if (activeMouthBlendShapeIndex >= 0)
            {
                try {
                    faceMeshRenderer.SetBlendShapeWeight(activeMouthBlendShapeIndex, 0f);
                } catch (Exception) { }
            }
            
            // ダイレクト値リストをクリア
            mouthBlendShapeDirectValues.Clear();
            
            // レンダラーの強制更新
            ForceUpdateRenderer();
            
            Debug.Log("口パク状態をリセットしました");
        }
    }
    
    // 口を強制的に動かす処理
    private void ForceApplyMouthMovement()
    {
        if (faceMeshRenderer == null) return;
        
        try
        {
            // ランダムに母音BlendShapeを選択
            string selectedVowel = vowelBlendShapes[UnityEngine.Random.Range(0, vowelBlendShapes.Length)];
            
            // 指定されたBlendShape優先
            if (specificMouthShapes.ContainsKey(selectedVowel))
            {
                int index = specificMouthShapes[selectedVowel];
                
                // 他の母音BlendShapeをリセット
                foreach (var pair in specificMouthShapes)
                {
                    if (pair.Key != selectedVowel)
                    {
                        faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
                    }
                }
                
                // 選択した母音BlendShapeを設定
                float intensity = UnityEngine.Random.Range(mouthOpenIntensity * 0.8f, mouthOpenIntensity * 1.2f);
                faceMeshRenderer.SetBlendShapeWeight(index, intensity);
                
                // ダイレクト値を保存
                mouthBlendShapeDirectValues.Clear();
                AddDirectMouthValue(index, intensity);
                
                // 現在の母音を更新
                currentVowelBlendShape = selectedVowel;
                
                // レンダラーを強制更新
                ForceUpdateRenderer();
                
                Debug.Log($"強制口パク: {selectedVowel} = {intensity}");
                return;
            }
            
            // 指定されたBlendShapeがない場合は自動検出したものを使用
            if (autoDetectedMouthShapes.Count > 0)
            {
                // 優先順位の高いBlendShapeを使用
                foreach (string shape in priorityMouthShapes)
                {
                    if (autoDetectedMouthShapes.ContainsKey(shape))
                    {
                        int index = autoDetectedMouthShapes[shape];
                        
                        // 他のBlendShapeをリセット
                        foreach (var pair in autoDetectedMouthShapes)
                        {
                            if (pair.Value != index)
                            {
                                faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
                            }
                        }
                        
                        float intensity = UnityEngine.Random.Range(mouthOpenIntensity * 0.8f, mouthOpenIntensity * 1.2f);
                        faceMeshRenderer.SetBlendShapeWeight(index, intensity);
                        
                        // ダイレクト値を保存
                        mouthBlendShapeDirectValues.Clear();
                        AddDirectMouthValue(index, intensity);
                        
                        // レンダラーを強制更新
                        ForceUpdateRenderer();
                        
                        Debug.Log($"強制口パク: {shape} = {intensity}");
                        return;
                    }
                }
                
                // 優先順位のBlendShapeが見つからない場合は最初のものを使用
                var firstPair = autoDetectedMouthShapes.First();
                faceMeshRenderer.SetBlendShapeWeight(firstPair.Value, mouthOpenIntensity);
                
                // ダイレクト値を保存
                mouthBlendShapeDirectValues.Clear();
                AddDirectMouthValue(firstPair.Value, mouthOpenIntensity);
                
                // レンダラーを強制更新
                ForceUpdateRenderer();
                
                Debug.Log($"強制口パク: {firstPair.Key} = {mouthOpenIntensity}");
            }
            else
            {
                Debug.LogWarning("口パク用BlendShapeが見つかりません");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"強制口パクエラー: {ex.Message}");
        }
    }

    // 口を閉じる処理
    private void ApplyMouthClose()
    {
        if (faceMeshRenderer == null) return;
        
        try
        {
            // すべての母音BlendShapeをリセット
            foreach (var pair in specificMouthShapes)
            {
                if (pair.Key != "Fcl_MTH_Close")
                {
                    faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
                }
            }
            
            // 自動検出したBlendShapeもリセット
            foreach (var pair in autoDetectedMouthShapes)
            {
                faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
            }
            
            // Fcl_MTH_Closeがあれば使用
            if (specificMouthShapes.ContainsKey("Fcl_MTH_Close"))
            {
                int closeIndex = specificMouthShapes["Fcl_MTH_Close"];
                faceMeshRenderer.SetBlendShapeWeight(closeIndex, 100f);
                
                // ダイレクト値を保存
                mouthBlendShapeDirectValues.Clear();
                AddDirectMouthValue(closeIndex, 100f);
            }
            else
            {
                // mouthCloseBlendShapesの使用を試みる
                foreach (string closeName in mouthCloseBlendShapes)
                {
                    if (blendShapeIndexMap.TryGetValue(closeName, out int index))
                    {
                        faceMeshRenderer.SetBlendShapeWeight(index, 100f);
                        break;
                    }
                }
            }
            
            // レンダラーを強制更新
            ForceUpdateRenderer();
            
            Debug.Log("口を閉じました");
        }
        catch (Exception ex)
        {
            Debug.LogError($"口を閉じる処理でエラー: {ex.Message}");
        }
    }

    // 問題修正：口パク用BlendShapeの自動検出強化
    private void AutoDetectMouthShapes()
    {
        if (faceMeshRenderer == null || faceMeshRenderer.sharedMesh == null) return;
        
        Debug.Log("口パク用BlendShapeを自動検出中...");
        
        // 初期化前の状態をログ出力
        Debug.Log($"初期化前のBlendShape数: {autoDetectedMouthShapes.Count}");
        Debug.Log($"利用可能なすべてのBlendShape:");
        if (faceMeshRenderer != null && faceMeshRenderer.sharedMesh != null) {
            for (int i = 0; i < faceMeshRenderer.sharedMesh.blendShapeCount; i++) {
                string name = faceMeshRenderer.sharedMesh.GetBlendShapeName(i);
                Debug.Log($"BlendShape {i}: {name}");
            }
        }
        
        // 指定された口パクBlendShape名をまず検索
        string[] specificMouthShapeNames = {
            "Fcl_MTH_A", "Fcl_MTH_I", "Fcl_MTH_U", "Fcl_MTH_E", "Fcl_MTH_O", "Fcl_MTH_Close"
        };
        
        // 特定のBlendShape名を検索してマッピング
        specificMouthShapes.Clear();
        foreach (string shapeName in specificMouthShapeNames)
        {
            if (blendShapeIndexMap.TryGetValue(shapeName, out int index))
            {
                specificMouthShapes[shapeName] = index;
                autoDetectedMouthShapes[shapeName] = index;
                Debug.Log($"指定された口パクBlendShape検出: {shapeName} (インデックス: {index})");
            }
        }
        
        // 指定されたBlendShapeが一つも見つからなかった場合のみ、一般的な検出を実行
        if (specificMouthShapes.Count == 0)
        {
            // 共通の口パク関連キーワード（大幅拡張）
            string[] mouthKeywords = { 
                "mouth", "mth", "lip", "oral", "jaw", "teeth", 
                "a", "o", "u", "e", "i", "open", "close", "talk",
                "あ", "い", "う", "え", "お", "口", "開", "閉",
                "口開", "口閉", "smile", "fcl_mth", "fcl_oral", "speak"
            };
            
            // 完全一致するBlendShape名（通常のモデルで使われる一般的な名前）
            string[] exactMatchNames = {
                "Fcl_MTH_A", "Fcl_MTH_I", "Fcl_MTH_U", "Fcl_MTH_E", "Fcl_MTH_O", "Fcl_MTH_Close",
                "Mouth_A", "Mouth_I", "Mouth_U", "Mouth_E", "Mouth_O",
                "A", "I", "U", "E", "O", "Ah", "Oh",
                "MTH_A", "MTH_I", "MTH_U", "MTH_E", "MTH_O",
                "Mouth", "MouthOpen", "MouthMove", "Speak", "Talk", "JawOpen"
            };
            
            // まず完全一致するものを検索
            foreach (var exactName in exactMatchNames)
            {
                if (blendShapeIndexMap.TryGetValue(exactName, out int index))
                {
                    autoDetectedMouthShapes[exactName] = index;
                    Debug.Log($"口パク用BlendShape完全一致検出: {exactName}");
                }
            }
            
            // 一つも見つからなかった場合は部分一致を探す
            if (autoDetectedMouthShapes.Count == 0)
            {
                foreach (var entry in blendShapeIndexMap)
                {
                    string name = entry.Key.ToLower();
                    int index = entry.Value;
                    
                    foreach (string keyword in mouthKeywords)
                    {
                        if (name.Contains(keyword.ToLower()))
                        {
                            autoDetectedMouthShapes[entry.Key] = index;
                            Debug.Log($"口パク用BlendShape部分一致検出: {entry.Key} (キーワード: {keyword})");
                            break;
                        }
                    }
                }
            }
            
            // それでも見つからない場合は最後の手段：インデックス番号で推測
            if (autoDetectedMouthShapes.Count == 0 && faceMeshRenderer != null && faceMeshRenderer.sharedMesh != null)
            {
                // 多くのモデルでは口関連のBlendShapeは先頭の方に定義されていることが多い
                for (int i = 0; i < Math.Min(10, faceMeshRenderer.sharedMesh.blendShapeCount); i++)
                {
                    string shapeName = faceMeshRenderer.sharedMesh.GetBlendShapeName(i);
                    autoDetectedMouthShapes[shapeName] = i;
                    Debug.Log($"口パク用BlendShape推測検出: {shapeName} (インデックス推測)");
                }
            }
        }
        
        Debug.Log($"口パク用BlendShape自動検出結果: {autoDetectedMouthShapes.Count}個検出");
        hasInitializedMouthShapes = true;
        
        // 問題修正：すべてのBlendShapeをログに出力（デバッグ用）
        if (debugMode)
        {
            Debug.Log("利用可能なすべてのBlendShape:");
            foreach (var entry in blendShapeIndexMap)
            {
                Debug.Log($"- {entry.Key} (インデックス: {entry.Value})");
            }
        }
        
        // 検出結果を確認
        if (autoDetectedMouthShapes.Count == 0) {
            Debug.LogError("口パク用BlendShapeが1つも検出できませんでした。手動で設定してください。");
        }
    }
    
    // BlendShapeごとの強度設定を初期化
    private void InitializeBlendShapeIntensities()
    {
        // デフォルト値をまず設定
        foreach (string name in blendShapeIndexMap.Keys)
        {
            blendShapeIntensityMap[name] = defaultBlendShapeIntensity;
        }
        
        // カスタム強度設定を適用
        if (customIntensities != null)
        {
            foreach (var intensitySetting in customIntensities)
            {
                if (!string.IsNullOrEmpty(intensitySetting.blendShapeName) && 
                    blendShapeIndexMap.ContainsKey(intensitySetting.blendShapeName))
                {
                    blendShapeIntensityMap[intensitySetting.blendShapeName] = intensitySetting.intensity;
                    DebugLog($"カスタム強度を設定: {intensitySetting.blendShapeName} = {intensitySetting.intensity}");
                }
            }
        }
        
        // BlendShapeグループごとの強度設定も適用
        foreach (var group in blendShapeGroups)
        {
            if (group.intensities != null)
            {
                foreach (var intensitySetting in group.intensities)
                {
                    if (!string.IsNullOrEmpty(intensitySetting.blendShapeName) && 
                        blendShapeIndexMap.ContainsKey(intensitySetting.blendShapeName))
                    {
                        blendShapeIntensityMap[intensitySetting.blendShapeName] = intensitySetting.intensity;
                        DebugLog($"グループ '{group.groupName}' の強度を設定: {intensitySetting.blendShapeName} = {intensitySetting.intensity}");
                    }
                }
            }
        }
    }
    
    private void Start()
    {
        // Awakeの後に実行され、他のコンポーネントが初期化された後に呼ばれる
#if VRM_IMPORTED
        if (useVRM && vrmProxy != null)
        {
            // VRMモデルのBlendShapeProxyが存在する場合は無効化
            vrmProxy.enabled = false;
            DebugLog("VRMBlendShapeProxyを無効化しました");
        }
#endif

        // 瞬きコルーチンを開始
        if (enableBlinking && blinkCoroutine == null)
        {
            blinkCoroutine = StartCoroutine(BlinkRoutine());
            DebugLog("瞬きコルーチンを開始しました");
        }
    }
    
    private IEnumerator InitializeAnimatorDelayed()
    {
        // アニメーターが準備完了するまで少し待つ
        yield return new WaitForSeconds(0.1f);
        
        // 初期状態をneutralに強制設定
        if (bodyAnimator != null)
        {
            Debug.Log("=== アニメーター初期化 ===");
            // すべてのパラメータをリセット
            bodyAnimator.SetInteger("BodyAnimation", 0);
            bodyAnimator.SetInteger("Expression", 0);
            bodyAnimator.SetBool("Is Talking", false);
            bodyAnimator.SetBool("Is Thinking", false);
            
            // 強制更新
            bodyAnimator.Update(0f);
            
            Debug.Log($"初期化後 - BodyAnimation: {bodyAnimator.GetInteger("BodyAnimation")}");
            Debug.Log($"初期化後 - Expression: {bodyAnimator.GetInteger("Expression")}");
        }
        
        // neutralの感情を明示的に設定
        SetEmotion("neutral", false);
    }
    
    // アプリケーション終了時にVRMBlendShapeProxyを再有効化
    private void OnDestroy()
    {
#if VRM_IMPORTED
        if (useVRM && vrmProxy != null)
        {
            vrmProxy.enabled = true;
            DebugLog("VRMBlendShapeProxyを再有効化しました");
        }
#endif
    }
    
    // 感情BlendShapeマップの初期化
    private void InitializeEmotionBlendShapeMap()
    {
        // 設定されたBlendShapeグループを辞書に変換
        foreach (BlendShapeGroup group in blendShapeGroups)
        {
            if (!string.IsNullOrEmpty(group.groupName) && group.blendShapeNames != null && group.blendShapeNames.Length > 0)
            {
                List<string> validBlendShapes = new List<string>();
                foreach (string name in group.blendShapeNames)
                {
                    if (blendShapeIndexMap.ContainsKey(name))
                    {
                        validBlendShapes.Add(name);
                    }
                    else
                    {
                        Debug.LogWarning($"BlendShape '{name}' はモデルに存在しません。");
                    }
                }
                
                if (validBlendShapes.Count > 0)
                {
                    emotionBlendShapeMap[group.groupName.ToLower()] = validBlendShapes;
                    DebugLog($"感情 '{group.groupName}' に {validBlendShapes.Count} 個の有効なBlendShapeを登録しました");
                }
            }
        }
        
        // デフォルトの感情マップがない場合は、実在するBlendShapeを自動探索して追加
        if (emotionBlendShapeMap.Count == 0)
        {
            DebugLog("感情BlendShapeグループが設定されていないため、自動探索します...");
            AutoDetectEmotionBlendShapes();
        }
    }
    
    // 既存のBlendShape名から自動的に感情マップを構築
    private void AutoDetectEmotionBlendShapes()
    {
        Dictionary<string, List<string>> tempMap = new Dictionary<string, List<string>>();
        
        // 基本感情のキーワード
        string[] emotionKeywords = { "neutral", "happy", "sad", "angry", "surprised", "joy", "fun", "sorrow", "idle" };
        
        // 各感情に関連するBlendShapeを検索
        foreach (string emotion in emotionKeywords)
        {
            List<string> matchingBlendShapes = new List<string>();
            
            foreach (string blendShapeName in blendShapeIndexMap.Keys)
            {
                // 小文字に変換して感情名を含むか確認
                string lowerName = blendShapeName.ToLower();
                if (lowerName.Contains(emotion) || lowerName.Contains("_" + emotion) || lowerName.EndsWith("_" + emotion))
                {
                    matchingBlendShapes.Add(blendShapeName);
                }
            }
            
            if (matchingBlendShapes.Count > 0)
            {
                string emotionKey = emotion;
                // joyとfunはhappyグループに統合
                if (emotion == "joy" || emotion == "fun")
                {
                    emotionKey = "happy";
                }
                // sorrowはsadグループに統合
                else if (emotion == "sorrow")
                {
                    emotionKey = "sad";
                }
                
                // 該当するグループがあれば追加、なければ新規作成
                if (tempMap.ContainsKey(emotionKey))
                {
                    tempMap[emotionKey].AddRange(matchingBlendShapes);
                }
                else
                {
                    tempMap[emotionKey] = matchingBlendShapes;
                }
                
                DebugLog($"感情 '{emotionKey}' に対して {matchingBlendShapes.Count} 個のBlendShapeを自動検出しました");
            }
        }
        
        // 一時マップを実際のマップに反映
        foreach (var entry in tempMap)
        {
            emotionBlendShapeMap[entry.Key] = entry.Value;
        }
    }

    // Animatorパラメータの検証
    private void ValidateAnimatorParameters()
    {
        if (bodyAnimator == null) return;
        
        // パラメータの存在を確認し、なければ作成を促す
        bool hasBodyAnimation = false;
        bool hasExpression = false;
        bool hasIsThinking = false;
        bool hasIsTalking = false;
        
        foreach (AnimatorControllerParameter param in bodyAnimator.parameters)
        {
            switch (param.name)
            {
                case "BodyAnimation":
                    hasBodyAnimation = true;
                    if (param.type != AnimatorControllerParameterType.Int)
                        Debug.LogError("BodyAnimationパラメータはInteger型である必要があります");
                    break;
                case "Expression":
                    hasExpression = true;
                    if (param.type != AnimatorControllerParameterType.Int)
                        Debug.LogError("ExpressionパラメータはInteger型である必要があります");
                    break;
                case "Is Thinking":
                    hasIsThinking = true;
                    if (param.type != AnimatorControllerParameterType.Bool)
                        Debug.LogError("Is ThinkingパラメータはBool型である必要があります");
                    break;
                case "Is Talking":
                    hasIsTalking = true;
                    if (param.type != AnimatorControllerParameterType.Bool)
                        Debug.LogError("Is TalkingパラメータはBool型である必要があります");
                    break;
            }
        }
        
        // 不足しているパラメータを報告
        if (!hasBodyAnimation)
            Debug.LogError("AnimatorにBodyAnimation (Integer)パラメータがありません");
        if (!hasExpression)
            Debug.LogError("AnimatorにExpression (Integer)パラメータがありません");
        if (!hasIsThinking)
            Debug.LogError("AnimatorにIs Thinking (Bool)パラメータがありません");
        if (!hasIsTalking)
            Debug.LogError("AnimatorにIs Talking (Bool)パラメータがありません");
    }

    // Animatorの設定を確認
    private void CheckAnimatorSetup()
    {
        if (bodyAnimator == null) return;
        
        DebugLog($"Animatorコントローラー: {bodyAnimator.runtimeAnimatorController?.name ?? "なし"}");
        
        // 設定された体のアニメーション名を確認
        DebugLog("体のアニメーション名の確認:");
        if (!string.IsNullOrEmpty(idleBodyAnimName))
            DebugLog($"- アイドル: {idleBodyAnimName}");
        if (!string.IsNullOrEmpty(happyBodyAnimName))
            DebugLog($"- 嬉しい: {happyBodyAnimName}");
        if (!string.IsNullOrEmpty(sadBodyAnimName))
            DebugLog($"- 悲しい: {sadBodyAnimName}");
        if (!string.IsNullOrEmpty(angryBodyAnimName))
            DebugLog($"- 怒り: {angryBodyAnimName}");
        if (!string.IsNullOrEmpty(surprisedBodyAnimName))
            DebugLog($"- 驚き: {surprisedBodyAnimName}");
        if (!string.IsNullOrEmpty(talkingNeutralBodyAnimName))
            DebugLog($"- 会話中ニュートラル: {talkingNeutralBodyAnimName}");
    }

    // BlendShapeインデックスのマッピングを初期化
    private void InitializeBlendShapeMap()
    {
        if (faceMeshRenderer == null || faceMeshRenderer.sharedMesh == null) return;

        // 使用可能なすべてのBlendShapeをログに出力（デバッグ用）
        DebugLog($"使用可能なBlendShape数: {faceMeshRenderer.sharedMesh.blendShapeCount}");
        for (int i = 0; i < faceMeshRenderer.sharedMesh.blendShapeCount; i++)
        {
            string name = faceMeshRenderer.sharedMesh.GetBlendShapeName(i);
            DebugLog($"BlendShape {i}: {name}");
            blendShapeIndexMap[name] = i;
        }
    }

    // BlendShape値の初期化
    private void InitializeBlendShapeValues()
    {
        // すべてのBlendShapeの現在値と目標値を初期化
        foreach (string name in blendShapeIndexMap.Keys)
        {
            currentBlendShapeValues[name] = 0f;
            targetBlendShapeValues[name] = 0f;
        }
        
        DebugLog($"{blendShapeIndexMap.Count}個のBlendShapeを初期化しました");
    }

    private void Update()
    {
        // BlendShape値のスムーズな遷移
        UpdateBlendShapeValues();
        
        // 会話状態の監視（新規追加）
        if (isInTalkingSession)
        {
            talkingCheckTimer += Time.deltaTime;
            
            // 定期的にIs Talkingフラグと会話状態を確認・再設定
            if (talkingCheckTimer >= 0.5f)
            {
                talkingCheckTimer = 0f;
                
                if (bodyAnimator != null && !bodyAnimator.GetBool("Is Talking"))
                {
                    Debug.Log("会話セッション中にIs Talkingがfalseになったため再設定");
                    bodyAnimator.SetBool("Is Talking", true);
                    
                    // TalkingNeutralの場合、BodyAnimationも確認・再設定
                    if (currentEmotion.ToLower() == "neutral") 
                    {
                        int currentBodyAnim = bodyAnimator.GetInteger("BodyAnimation");
                        if (currentBodyAnim != 5) // TalkingNeutralは5
                        {
                            Debug.Log("会話セッション中にTalkingNeutralが解除されたため再設定");
                            bodyAnimator.SetInteger("BodyAnimation", 5);
                        }
                    }
                    
                    // 更新を強制
                    bodyAnimator.Update(0f);
                }
            }
        }

        // 追加：口パク強制更新タイマー
        if (mouthMovementActive && audioSource.isPlaying)
        {
            forceMouthMovementTimer += Time.deltaTime;
            if (forceMouthMovementTimer >= forceMouthMovementInterval)
            {
                forceMouthMovementTimer = 0f;
                ForceUpdateMouthMovement();
            }
            
            // 母音切り替えタイマー更新
            vowelChangeTimer += Time.deltaTime;
            if (vowelChangeTimer >= vowelChangeDuration)
            {
                vowelChangeTimer = 0f;
                // 次の母音をランダムに選択
                currentVowelBlendShape = vowelBlendShapes[UnityEngine.Random.Range(0, vowelBlendShapes.Length)];
                // 母音切り替えを適用
                ApplyVowelChange();
            }
        }

        // 問題修正: 口パク中なのに口が動いていない場合は強制的に口を動かす（より頻繁に）
        if (mouthMovementActive && audioSource.isPlaying && faceMeshRenderer != null && !isAnimationTransitioning)
        {
            mouthUpdateTimer += Time.deltaTime;
            if (mouthUpdateTimer >= MOUTH_UPDATE_INTERVAL) // より頻繁にチェック
            {
                mouthUpdateTimer = 0f;
                
                Debug.Log($"口パク状態確認: active={mouthMovementActive}, playing={audioSource.isPlaying}");
                
                bool anyMouthOpen = false;
                
                // 口パク用BlendShapeをチェック
                foreach (var pair in autoDetectedMouthShapes)
                {
                    float currentValue = faceMeshRenderer.GetBlendShapeWeight(pair.Value);
                    if (currentValue > 2.0f) // わずかでも開いていれば検出（閾値を下げる）
                    {
                        anyMouthOpen = true;
                        break;
                    }
                }
                
                // 口パク用BlendShapeが全て閉じている場合は強制的に口を開く
                if (!anyMouthOpen)
                {
                    Debug.Log("口パクが停止したため強制的に口を動かします");
                    ForceApplyMouthMovement();
                }
            }
        }
        
        #if !UNITY_WEBGL || UNITY_EDITOR
        // WebGLビルドではテストモードを実行しない
        if (testMode)
        {
            TestModeUpdate();
        }
        #endif
        
        // マニュアルテスト用キーボード入力
        HandleKeyboardInput();
    }
    
    // テストモードの更新
    private void TestModeUpdate()
    {
        testTimer += Time.deltaTime;
        if (testTimer >= 3f) // 3秒ごとに感情を切り替え
        {
            testTimer = 0f;
            SetEmotion(testEmotions[testEmotionIndex]);
            Debug.Log($"【テストモード】感情切り替え: {testEmotions[testEmotionIndex]}");
            
            testEmotionIndex++;
            if (testEmotionIndex >= testEmotions.Length)
            {
                testEmotionIndex = 0;
            }
        }
    }
    
    // キーボード入力でのテスト（拡張）
    private void HandleKeyboardInput()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.N))
        {
            Debug.Log("【キーボード入力】Neutral感情をセット");
            SetEmotion("neutral");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.H))
        {
            Debug.Log("【キーボード入力】Happy感情をセット");
            SetEmotion("happy");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.S))
        {
            Debug.Log("【キーボード入力】Sad感情をセット");
            SetEmotion("sad");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.A))
        {
            Debug.Log("【キーボード入力】Angry感情をセット");
            SetEmotion("angry");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.U))
        {
            Debug.Log("【キーボード入力】Surprised感情をセット");
            SetEmotion("surprised");
        }
        else if (Input.GetKeyDown(KeyCode.Alpha0) || Input.GetKeyDown(KeyCode.T))
        {
            Debug.Log("【キーボード入力】TalkingNeutral感情をセット");
            SetEmotion("neutral", true);
        }
        else if (Input.GetKeyDown(KeyCode.Space))
        {
            testMode = !testMode;
            Debug.Log($"【テストモード】{(testMode ? "開始" : "終了")}");
            if (testMode)
            {
                testTimer = 0f;
                testEmotionIndex = 0;
            }
        }
        else if (Input.GetKeyDown(KeyCode.B))
        {
            Debug.Log("【キーボード入力】瞬き実行");
            StartCoroutine(SingleBlinkCoroutine());
        }
        // 口パクテスト
        else if (Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.L))
        {
            Debug.Log("【キーボード入力】口パクテスト実行");
            TestMouthMovement();
        }
        // 各感情での会話テスト
        else if (Input.GetKeyDown(KeyCode.F1))
        {
            Debug.Log("【キーボード入力】Neutral会話テスト");
            TestTalk("neutral");
        }
        else if (Input.GetKeyDown(KeyCode.F2))
        {
            Debug.Log("【キーボード入力】Happy会話テスト");
            TestTalk("happy");
        }
        else if (Input.GetKeyDown(KeyCode.F3))
        {
            Debug.Log("【キーボード入力】Sad会話テスト");
            TestTalk("sad");
        }
        else if (Input.GetKeyDown(KeyCode.F4))
        {
            Debug.Log("【キーボード入力】Angry会話テスト");
            TestTalk("angry");
        }
        else if (Input.GetKeyDown(KeyCode.F5))
        {
            Debug.Log("【キーボード入力】Surprised会話テスト");
            TestTalk("surprised");
        }
        // 追加：直接口パクテスト
        else if (Input.GetKeyDown(KeyCode.P))
        {
            Debug.Log("【キーボード入力】直接口パクテスト");
            ForceApplyMouthMovement();
        }
    }
    
    // 口パク動作テスト（強化版）
    private void TestMouthMovement()
    {
        StartCoroutine(TestMouthMovementCoroutine());
    }
    
    private IEnumerator TestMouthMovementCoroutine()
    {
        Debug.Log("口パクテスト開始（3秒間）");
        
        // 口パク用BlendShapeのインデックスを取得
        List<KeyValuePair<string, int>> mouthShapeIndices = new List<KeyValuePair<string, int>>();
        
        // 指定された口パクBlendShapeを優先使用
        string[] targetShapes = { "Fcl_MTH_A", "Fcl_MTH_I", "Fcl_MTH_U", "Fcl_MTH_E", "Fcl_MTH_O" };
        foreach (string shape in targetShapes)
        {
            if (specificMouthShapes.ContainsKey(shape))
            {
                mouthShapeIndices.Add(new KeyValuePair<string, int>(shape, specificMouthShapes[shape]));
                Debug.Log($"テスト用指定BlendShape {shape} のインデックス: {specificMouthShapes[shape]}");
            }
        }
        
        // 指定BlendShapeがなければ設定済みの口パクBlendShapeを使用
        if (mouthShapeIndices.Count == 0)
        {
            foreach (string shape in mouthOpenBlendShapes)
            {
                if (blendShapeIndexMap.TryGetValue(shape, out int index))
                {
                    mouthShapeIndices.Add(new KeyValuePair<string, int>(shape, index));
                    Debug.Log($"テスト用BlendShape {shape} のインデックス: {index}");
                }
            }
        }
        
        // それでもなければ自動検出したものを使用
        if (mouthShapeIndices.Count == 0)
        {
            foreach (var pair in autoDetectedMouthShapes)
            {
                mouthShapeIndices.Add(new KeyValuePair<string, int>(pair.Key, pair.Value));
                Debug.Log($"テスト用自動検出BlendShape {pair.Key} のインデックス: {pair.Value}");
            }
        }
        
        if (mouthShapeIndices.Count == 0)
        {
            Debug.LogError("口パク用BlendShapeが見つかりません");
            yield break;
        }
        
        // BlendShapeの初期値を一度リセット
        foreach (var pair in mouthShapeIndices)
        {
            try {
                faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
            } catch (Exception) { }
        }
        
        // レンダラーの強制更新
        ForceUpdateRenderer();
        
        float endTime = Time.time + 3f; // 3秒間テスト
        
        while (Time.time < endTime)
        {
            // ランダムに母音を選択
            KeyValuePair<string, int> selectedShape = mouthShapeIndices[UnityEngine.Random.Range(0, mouthShapeIndices.Count)];
            
            // 口を開く - 選択した母音BlendShapeを直接100%に設定
            try {
                // 他のBlendShapeをリセット
                foreach (var pair in mouthShapeIndices)
                {
                    if (pair.Key != selectedShape.Key)
                    {
                        faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
                    }
                }
                
                // 選択したBlendShapeを設定
                faceMeshRenderer.SetBlendShapeWeight(selectedShape.Value, 100f);
                Debug.Log($"テスト: 口を開く（直接設定） {selectedShape.Key} = 100");
                
                // ダイレクト値リストに保存
                mouthBlendShapeDirectValues.Clear();
                AddDirectMouthValue(selectedShape.Value, 100f);
            } catch (Exception ex) {
                Debug.LogError($"BlendShape設定エラー: {ex.Message}");
            }
            
            // 値の強制アップデート
            ForceUpdateRenderer();
            
            // 現在値を表示
            LogMouthBlendShapeValues();
            
            // 少し待つ
            yield return new WaitForSeconds(mouthOpenDuration);
            
            // 口を閉じる - すべてゼロに設定
            try {
                foreach (var pair in mouthShapeIndices)
                {
                    faceMeshRenderer.SetBlendShapeWeight(pair.Value, 0f);
                }
                Debug.Log("テスト: 口を閉じる（直接設定）");
                
                // ダイレクト値リストをクリア
                mouthBlendShapeDirectValues.Clear();
            } catch (Exception ex) {
                Debug.LogError($"BlendShapeリセットエラー: {ex.Message}");
            }
            
            // 値の強制アップデート
            ForceUpdateRenderer();
            
            // 少し待つ
            yield return new WaitForSeconds(mouthCloseDuration);
        }
        
        Debug.Log("口パクテスト終了");
    }
    
    // 各感情でのセリフ付き会話テスト
    private void TestTalk(string emotion)
    {
        // テスト用のセリフを用意
        string message = emotion + " これはテスト会話です。このように口を動かして話します。";
        
        // ダミー音声データで応答をシミュレート
        ProcessResponse(message, dummyAudioData);
    }

    // デバッグ用関数：すべての口パク用BlendShapeの現在値をログに出力
    private void LogMouthBlendShapeValues()
    {
        if (faceMeshRenderer == null || !debugMode) return;
        
        Debug.Log("===== 口パクBlendShape現在値 =====");
        
        // 指定されたBlendShape値をログ
        foreach (var pair in specificMouthShapes)
        {
            float value = faceMeshRenderer.GetBlendShapeWeight(pair.Value);
            Debug.Log($"{pair.Key} = {value}");
        }
        
        foreach (string shape in mouthOpenBlendShapes)
        {
            if (blendShapeIndexMap.TryGetValue(shape, out int index))
            {
                float value = faceMeshRenderer.GetBlendShapeWeight(index);
                Debug.Log($"{shape} = {value}");
            }
        }
        
        foreach (string shape in mouthCloseBlendShapes)
        {
            if (blendShapeIndexMap.TryGetValue(shape, out int index))
            {
                float value = faceMeshRenderer.GetBlendShapeWeight(index);
                Debug.Log($"{shape} = {value}");
            }
        }
        
        // 自動検出したBlendShapeも出力
        foreach (var pair in autoDetectedMouthShapes)
        {
            float value = faceMeshRenderer.GetBlendShapeWeight(pair.Value);
            Debug.Log($"{pair.Key} = {value}");
        }
        
        Debug.Log("=================================");
    }
    
    // BlendShape値の更新
    private void UpdateBlendShapeValues()
    {
        if (faceMeshRenderer == null) return;

        bool anyChanges = false;

        foreach (var entry in targetBlendShapeValues)
        {
            string name = entry.Key;
            float target = entry.Value;
            
            if (currentBlendShapeValues.ContainsKey(name))
            {
                float current = currentBlendShapeValues[name];
                
                // 現在値と目標値が異なる場合、スムーズに変化させる
                if (Mathf.Abs(current - target) > 0.01f)
                {
                    current = Mathf.Lerp(current, target, Time.deltaTime * blendShapeTransitionSpeed);
                    currentBlendShapeValues[name] = current;
                    
                    // BlendShapeに適用
                    if (blendShapeIndexMap.TryGetValue(name, out int index))
                    {
                        faceMeshRenderer.SetBlendShapeWeight(index, current);
                    }
                    
                    anyChanges = true;
                }
            }
        }

        // 変更があった場合にのみログ出力
        if (anyChanges && debugMode)
        {
            DebugLog("BlendShape値を更新中...");
        }
    }

    // 感情を設定するパブリックメソッド（アニメーション遷移を最適化）
    public void SetEmotion(string emotion, bool isResponseMode = false)
    {
        Debug.Log($"=== SetEmotion開始 === 感情: {emotion}, 応答モード: {isResponseMode}");
        
        // 問題修正：遷移中フラグをオン
        isAnimationTransitioning = true;
        StartCoroutine(ResetTransitioningFlag());
        
        // 現在の状態をログ出力
        if (bodyAnimator != null)
        {
            Debug.Log("=== 現在のアニメーター状態 ===");
            Debug.Log($"BodyAnimation: {bodyAnimator.GetInteger("BodyAnimation")}");
            Debug.Log($"Expression: {bodyAnimator.GetInteger("Expression")}");
            Debug.Log($"Is Talking: {bodyAnimator.GetBool("Is Talking")}");
            Debug.Log($"Is Thinking: {bodyAnimator.GetBool("Is Thinking")}");
        }
        
        string emotionKey = emotion.ToLower();
        
        // 前の感情遷移中のコルーチンを停止
        if (blendShapeCoroutine != null)
        {
            StopCoroutine(blendShapeCoroutine);
        }
        
        // 感情維持コルーチンを停止
        if (emotionMaintainCoroutine != null)
        {
            StopCoroutine(emotionMaintainCoroutine);
        }
        
        // すべての表情BlendShapeをリセット
        if (faceMeshRenderer != null)
        {
            ResetFacialBlendShapes();
        }
        
        // 基本感情を更新
        baseEmotion = emotionKey;
        currentEmotion = emotionKey;
        isResponding = isResponseMode; // 応答モードかどうかを設定
        
        // 会話セッション状態の更新
        isInTalkingSession = isResponseMode;
        
        // BlendShapeの設定
        SetEmotionBlendShapes(emotionKey);
        
        // 問題修正：TalkingNeutral状態への移行を最適化
        if (emotionKey == "neutral" && isResponseMode)
        {
            // Neutral + 応答モードの場合、先にBodyAnimationを0に設定してからTalkingNeutralにする
            // これにより、遷移がスムーズになる
            if (bodyAnimator != null)
            {
                StartCoroutine(SmoothTransitionToTalkingNeutral());
            }
        }
        else
        {
            // 他の感情の体のアニメーション設定
            SetBodyAnimation(emotionKey, isResponseMode);
        }
        
        // 感情状態を維持
        emotionMaintainCoroutine = StartCoroutine(MaintainEmotionState(emotionKey, isResponseMode));
        
        // 応答モードがオンの場合は必ず会話中フラグも設定
        if (isResponseMode && bodyAnimator != null)
        {
            // TalkingNeutralの場合は特に注意
            if (emotionKey == "neutral")
            {
                Debug.Log("応答モード + Neutralの場合は必ずTalkingNeutralを設定");
                // Neutral+応答モードはSmoothTransitionToTalkingNeutralで処理するため、ここでは特別な処理は不要
            }
            
            // 会話フラグを確実に設定
            bodyAnimator.SetBool("Is Talking", true);
            
            // 強制更新と確認
            bodyAnimator.Update(0f);
            
            // isTalking状態を更新
            isTalking = true;
            
            // 設定後の状態確認ログを追加
            Debug.Log($"会話フラグ設定後 - Is Talking: {bodyAnimator.GetBool("Is Talking")}");
            Debug.Log($"BodyAnimation: {bodyAnimator.GetInteger("BodyAnimation")}");
            
            // 念のための二重チェック - 万が一フラグが設定されていなければ再設定
            if (!bodyAnimator.GetBool("Is Talking"))
            {
                Debug.LogWarning("会話フラグがfalseになっているため強制的に再設定");
                // 別の方法で再設定
                bodyAnimator.Rebind();
                bodyAnimator.SetBool("Is Talking", true);
                bodyAnimator.Update(0f);
            }
        }
        
        Debug.Log($"基本感情設定完了: {baseEmotion}, 応答モード: {isResponding}");
        Debug.Log($"=== SetEmotion完了 ===");
    }
    
    // 感情に応じたBlendShapeを設定
    private void SetEmotionBlendShapes(string emotion)
    {
        // VRMモデルの場合
#if VRM_IMPORTED
        if (useVRM && vrmProxy != null)
        {
            // VRMの感情BlendShapeを設定
            SetVRMEmotion(emotion);
        }
        else
#endif
        {
            // 通常のBlendShapeを使用
            // 感情BlendShapeグループが存在するか確認
            if (emotionBlendShapeMap.ContainsKey(emotion))
            {
                // 該当する感情のBlendShapeグループを設定
                List<string> blendShapes = emotionBlendShapeMap[emotion];
                foreach (string blendShapeName in blendShapes)
                {
                    // 個別の強度設定を使用
                    float intensity = GetBlendShapeIntensity(blendShapeName);
                    SetBlendShapeTarget(blendShapeName, intensity);
                    DebugLog($"BlendShape設定: {blendShapeName} = {intensity}");
                }
                
                DebugLog($"感情 '{emotion}' のBlendShapeグループ ({blendShapes.Count} 個) を設定しました");
            }
            else
            {
                DebugLog($"感情 '{emotion}' のBlendShapeグループは見つかりませんでした。デフォルト表情を使用します。");
                
                // デフォルト（neutral）のBlendShapeを設定
                if (emotionBlendShapeMap.ContainsKey("neutral"))
                {
                    List<string> neutralBlendShapes = emotionBlendShapeMap["neutral"];
                    foreach (string blendShapeName in neutralBlendShapes)
                    {
                        float intensity = GetBlendShapeIntensity(blendShapeName);
                        SetBlendShapeTarget(blendShapeName, intensity);
                    }
                }
            }
        }
    }
    
    // BlendShapeごとの強度設定を取得
    private float GetBlendShapeIntensity(string blendShapeName)
    {
        // マップに保存された強度を返す
        if (blendShapeIntensityMap.TryGetValue(blendShapeName, out float intensity))
        {
            return intensity;
        }
        return defaultBlendShapeIntensity; // デフォルト値を使用
    }
    
#if VRM_IMPORTED
    // VRMモデルの感情BlendShapeを設定
    private void SetVRMEmotion(string emotion)
    {
        if (vrmProxy == null) return;
        
        VRMBlendShapeProxy.BlendShapeKey key = null;
        
        // 感情に対応するBlendShapeKeyを選択
        switch (emotion)
        {
            case "happy":
            case "joy":
                key = BlendShapeKey.CreateFromPreset(BlendShapePreset.Joy);
                break;
            case "angry":
                key = BlendShapeKey.CreateFromPreset(BlendShapePreset.Angry);
                break;
            case "sad":
            case "sorrow":
                key = BlendShapeKey.CreateFromPreset(BlendShapePreset.Sorrow);
                break;
            case "surprised":
                key = BlendShapeKey.CreateFromPreset(BlendShapePreset.Surprised);
                break;
            case "neutral":
            default:
                key = BlendShapeKey.CreateFromPreset(BlendShapePreset.Neutral);
                break;
        }
        
        // すべてのBlendShapeKeyをリセット
        foreach (var preset in System.Enum.GetValues(typeof(BlendShapePreset)))
        {
            var presetKey = BlendShapeKey.CreateFromPreset((BlendShapePreset)preset);
            vrmProxy.SetValue(presetKey, 0);
        }
        
        // 選択されたBlendShapeKeyを設定
        if (key != null)
        {
            vrmProxy.SetValue(key, 1.0f);
            DebugLog($"VRM感情BlendShape設定: {key}");
        }
        
        // 変更を適用
        vrmProxy.Apply();
    }
#endif
    
    // 体のアニメーション設定
    private void SetBodyAnimation(string emotion, bool isResponseMode = false)
    {
        if (bodyAnimator == null) return;
        
        Debug.Log($"SetBodyAnimation: emotion={emotion}, isResponseMode={isResponseMode}");
        
        // 現在のパラメータ値をログ出力
        Debug.Log("=== アニメーターパラメータ設定前 ===");
        Debug.Log($"BodyAnimation: {bodyAnimator.GetInteger("BodyAnimation")}");
        Debug.Log($"Expression: {bodyAnimator.GetInteger("Expression")}");
        
        // 問題修正：アニメーターのリバインド方法を変更
        try
        {
            // 問題修正：一時的にアニメーターを無効化して再有効化
            bool wasEnabled = bodyAnimator.enabled;
            bodyAnimator.enabled = false;
            
            // 少し待機
            System.Threading.Thread.Sleep(1);
            
            // 再有効化
            bodyAnimator.enabled = wasEnabled;
            
            if (wasEnabled)
            {
                bodyAnimator.Rebind();
                bodyAnimator.Update(0f);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"アニメーターリセットエラー: {ex.Message}");
        }
        
        // 重要な変更：neutralかつ応答モードの場合は確実にTalkingNeutralを設定
        if (emotion.ToLower() == "neutral" && isResponseMode) {
            // TalkingNeutralモードは別のロジックで処理
            // SmoothTransitionToTalkingNeutralコルーチンで処理されるため、ここでは早期リターン
            return;
        }
        
        // パラメーターを使用してアニメーションを制御（すべての感情で会話を許可）
        switch (emotion)
        {
            case "happy":
                bodyAnimator.SetInteger("BodyAnimation", 1);
                bodyAnimator.SetInteger("Expression", 1);
                bodyAnimator.SetBool("Is Talking", isResponseMode);
                bodyAnimator.SetBool("Is Thinking", false);
                Debug.Log("Happy感情を設定 - 会話モード: " + (isResponseMode ? "ON" : "OFF"));
                break;
                
            case "sad":
                bodyAnimator.SetInteger("BodyAnimation", 2);
                bodyAnimator.SetInteger("Expression", 2);
                bodyAnimator.SetBool("Is Talking", isResponseMode);
                bodyAnimator.SetBool("Is Thinking", false);
                Debug.Log("Sad感情を設定 - 会話モード: " + (isResponseMode ? "ON" : "OFF"));
                break;
                
            case "angry":
                bodyAnimator.SetInteger("BodyAnimation", 3);
                bodyAnimator.SetInteger("Expression", 3);
                bodyAnimator.SetBool("Is Talking", isResponseMode);
                bodyAnimator.SetBool("Is Thinking", false);
                Debug.Log("Angry感情を設定 - 会話モード: " + (isResponseMode ? "ON" : "OFF"));
                break;
                
            case "surprised":
                bodyAnimator.SetInteger("BodyAnimation", 4);
                bodyAnimator.SetInteger("Expression", 4);
                bodyAnimator.SetBool("Is Talking", isResponseMode);
                bodyAnimator.SetBool("Is Thinking", false);
                Debug.Log("Surprised感情を設定 - 会話モード: " + (isResponseMode ? "ON" : "OFF"));
                break;
                
            case "neutral":
            default:
                // 通常のNeutralかTalkingNeutralかを設定
                int bodyAnimValue = isResponseMode ? 5 : 0; // 応答モードなら5=TalkingNeutral、そうでなければ0=通常Neutral
                bodyAnimator.SetInteger("BodyAnimation", bodyAnimValue);
                bodyAnimator.SetInteger("Expression", 0);
                bodyAnimator.SetBool("Is Talking", isResponseMode);
                bodyAnimator.SetBool("Is Thinking", false);
                Debug.Log($"{(isResponseMode ? "会話中" : "通常")}Neutral感情を設定 - BodyAnimation={bodyAnimValue}");
                break;
        }
        
        // 設定後のパラメータ値をログ出力
        Debug.Log("=== アニメーターパラメータ設定後 ===");
        Debug.Log($"BodyAnimation: {bodyAnimator.GetInteger("BodyAnimation")}");
        Debug.Log($"Expression: {bodyAnimator.GetInteger("Expression")}");
        Debug.Log($"Is Talking: {bodyAnimator.GetBool("Is Talking")}");
        Debug.Log($"Is Thinking: {bodyAnimator.GetBool("Is Thinking")}");
        
        // アニメーターの更新を強制
        bodyAnimator.Update(0f);
    }
    
    // 感情状態を維持するコルーチン
    private IEnumerator MaintainEmotionState(string emotion, bool isTalkingMode)
    {
        Debug.Log($"感情状態の維持を開始: {emotion}, 会話モード: {isTalkingMode}");
        
        // 感情状態を一定時間維持する
        float maintainDuration = 0f;
        
        // 会話中は長めに状態を維持（音声再生が完了するまで）
        float maxMaintainTime = isTalkingMode ? 60f : 5f;
        
        while (currentEmotion == emotion && maintainDuration < maxMaintainTime)
        {
            maintainDuration += 0.5f;
            
            // 0.5秒ごとに状態を確認
            yield return new WaitForSeconds(0.5f);
            
            // Animatorパラメータが変更されていないか確認
            if (bodyAnimator != null)
            {
                // 会話中の場合は特に注意して確認
                if (isTalkingMode)
                {
                    // TalkingNeutralの場合
                    if (emotion == "neutral")
                    {
                        int currentValue = bodyAnimator.GetInteger("BodyAnimation");
                        if (currentValue != 5) // TalkingNeutralは5
                        {
                            Debug.LogWarning($"TalkingNeutral状態が変更されたため再設定 (現在値: {currentValue})");
                            bodyAnimator.SetInteger("BodyAnimation", 5);
                            bodyAnimator.SetInteger("Expression", 0);
                            bodyAnimator.SetBool("Is Talking", true);
                            bodyAnimator.Update(0f);
                        }
                        
                        // 会話フラグが変更されていないか確認
                        if (!bodyAnimator.GetBool("Is Talking"))
                        {
                            Debug.LogWarning("Is Talkingがfalseになったため再設定");
                            bodyAnimator.SetBool("Is Talking", true);
                            bodyAnimator.Update(0f);
                        }
                    }
                    else
                    {
                        // 他の感情でも会話状態を確認
                        int expectedValue = GetBodyAnimationValue(emotion, true);
                        int currentValue = bodyAnimator.GetInteger("BodyAnimation");
                        
                        if (currentValue != expectedValue)
                        {
                            Debug.LogWarning($"感情状態を再設定: {emotion} (現在値: {currentValue}, 期待値: {expectedValue})");
                            bodyAnimator.SetInteger("BodyAnimation", expectedValue);
                            bodyAnimator.SetInteger("Expression", GetExpressionValue(emotion));
                        }
                        
                        // 会話フラグの確認は常に行う
                        if (!bodyAnimator.GetBool("Is Talking"))
                        {
                            Debug.LogWarning("Is Talkingがfalseになったため再設定");
                            bodyAnimator.SetBool("Is Talking", true);
                            bodyAnimator.Update(0f);
                        }
                    }
                }
                else
                {
                    // 非会話状態の確認（必要に応じて）
                }
            }
        }
        
        Debug.Log($"感情状態の維持を終了: {emotion}, 継続時間: {maintainDuration}秒");
    }
    
    // 感情に対応するBodyAnimation値を取得
    private int GetBodyAnimationValue(string emotion, bool isResponseMode)
    {
        // 重要な点：neutralかつ応答モードの場合はTalkingNeutralを返す
        if (emotion == "neutral" && isResponseMode)
        {
            return 5; // TalkingNeutral（新しいアニメーション）
        }
        
        switch (emotion)
        {
            case "happy": return 1;
            case "sad": return 2;
            case "angry": return 3;
            case "surprised": return 4;
            default: return 0; // 通常のNeutral
        }
    }
    
    // 感情に対応するExpressionパラメータ値を取得
    private int GetExpressionValue(string emotion)
    {
        switch (emotion)
        {
            case "happy": return 1;
            case "sad": return 2;
            case "angry": return 3;
            case "surprised": return 4;
            default: return 0;
        }
    }
    
    // BlendShapeの目標値を設定
    private void SetBlendShapeTarget(string name, float value)
    {
        if (string.IsNullOrEmpty(name)) return;
        
        if (blendShapeIndexMap.ContainsKey(name))
        {
            targetBlendShapeValues[name] = value;
            DebugLog($"BlendShape目標値設定: {name} = {value}");
        }
        else
        {
            Debug.LogWarning($"BlendShape '{name}' はモデルに存在しません");
        }
    }
    
    // すべての表情BlendShapeをリセット
    private void ResetFacialBlendShapes()
    {
        // すべてのBlendShapeをリセット
        foreach (string name in blendShapeIndexMap.Keys)
        {
            // 口パクBlendShape以外をリセット
            bool isMouthBlendShape = false;
            bool isBlinkBlendShape = false;
            
            // 口パクBlendShapeかどうかをチェック
            foreach (string mouthShape in mouthOpenBlendShapes)
            {
                if (name == mouthShape)
                {
                    isMouthBlendShape = true;
                    break;
                }
            }
            
            if (!isMouthBlendShape)
            {
                foreach (string mouthShape in mouthCloseBlendShapes)
                {
                    if (name == mouthShape)
                    {
                        isMouthBlendShape = true;
                        break;
                    }
                }
            }
            
            // 自動検出した口パクBlendShapeかどうかをチェック
            if (!isMouthBlendShape && autoDetectedMouthShapes.ContainsKey(name))
            {
                isMouthBlendShape = true;
            }
            
            // 指定されたBlendShapeかどうかをチェック
            if (!isMouthBlendShape && specificMouthShapes.ContainsKey(name))
            {
                isMouthBlendShape = true;
            }
            
            // 瞬きBlendShapeかどうかをチェック
            foreach (string blinkShape in blinkBlendShapes)
            {
                if (name == blinkShape)
                {
                    isBlinkBlendShape = true;
                    break;
                }
            }
            
            // 口パクと瞬き以外のBlendShapeをリセット
            if (!isMouthBlendShape && !isBlinkBlendShape)
            {
                SetBlendShapeTarget(name, 0f);
            }
        }
    }
    
    // 瞬き関連処理 - 瞬きのコルーチン
    private IEnumerator BlinkRoutine()
    {
        if (!enableBlinking || blinkBlendShapes.Length == 0)
            yield break;
        
        DebugLog("瞬きコルーチンを開始");
        
        while (enableBlinking)
        {
            // ランダムな間隔で瞬き
            float waitTime = UnityEngine.Random.Range(minBlinkInterval, maxBlinkInterval);
            yield return new WaitForSeconds(waitTime);
            
            // 瞬き実行
            yield return StartCoroutine(SingleBlinkCoroutine());
        }
    }
    
    // 一回の瞬き処理
    private IEnumerator SingleBlinkCoroutine()
    {
        if (!enableBlinking || blinkBlendShapes.Length == 0)
            yield break;
        
        // 目を閉じる（速い）
        float closeSpeed = 1.5f;
        float openSpeed = 1.0f;
       
        // 目を閉じる過程
        float closeTime = blinkDuration * 0.3f;
        float startTime = Time.time;
        float endTime = startTime + closeTime;
       
        while (Time.time < endTime)
        {
            float t = (Time.time - startTime) / closeTime;
            float value = Mathf.Lerp(0, defaultBlendShapeIntensity, t * closeSpeed);
            
            foreach (string blinkShape in blinkBlendShapes)
            {
                if (blendShapeIndexMap.ContainsKey(blinkShape))
                {
                    faceMeshRenderer.SetBlendShapeWeight(blendShapeIndexMap[blinkShape], value);
                }
            }
            
            yield return null;
        }
       
        // 目を閉じた状態を維持
        float holdTime = blinkDuration * 0.2f;
        yield return new WaitForSeconds(holdTime);
       
        // 目を開く過程（やや遅め）
        float openTime = blinkDuration * 0.5f;
        startTime = Time.time;
        endTime = startTime + openTime;
       
        while (Time.time < endTime)
        {
            float t = (Time.time - startTime) / openTime;
            float value = Mathf.Lerp(defaultBlendShapeIntensity, 0, t * openSpeed);
            
            foreach (string blinkShape in blinkBlendShapes)
            {
                if (blendShapeIndexMap.ContainsKey(blinkShape))
                {
                    faceMeshRenderer.SetBlendShapeWeight(blendShapeIndexMap[blinkShape], value);
                }
            }
            
            yield return null;
        }
       
        // 完全に開いた状態に
        foreach (string blinkShape in blinkBlendShapes)
        {
            if (blendShapeIndexMap.ContainsKey(blinkShape))
            {
                faceMeshRenderer.SetBlendShapeWeight(blendShapeIndexMap[blinkShape], 0f);
            }
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
}