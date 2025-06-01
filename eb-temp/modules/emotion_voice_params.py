# emotion_voice_params.py - 感情別の音声パラメータを提供するモジュール

def get_emotion_voice_params(emotion=None):
    """
    感情に応じた音声パラメータを取得
    
    Args:
        emotion: 感情名 (happy, sad, angry, surprised, neutral など)
    
    Returns:
        dict: 音声パラメータの辞書
    """
    # デフォルトパラメータ（標準的な話し方）
    default_params = {
        'voice': 'nova',    # OpenAI TTS音声
        'speed': 1.0,       # 話速（1.0が標準）
        'pitch': 0,         # ピッチ調整（0が標準）
        'volume': 1.0,      # 音量
        'stability': 0.5,   # 安定性
        'similarity': 0.75  # 類似性
    }
    
    # 感情が指定されていない場合はデフォルト値を返す
    if not emotion:
        return default_params
    
    # 感情別パラメータ
    emotion_params = {
        'happy': {
            'voice': 'nova',
            'speed': 1.15,      # 少し早め
            'pitch': 10,        # 高め
            'volume': 1.2,      # やや大きめ
            'stability': 0.4,   # やや不安定（より表情豊か）
            'similarity': 0.8
        },
        'sad': {
            'voice': 'nova',
            'speed': 0.9,       # ゆっくり
            'pitch': -5,        # 低め
            'volume': 0.8,      # 小さめ
            'stability': 0.6,   # やや安定（抑えた表現）
            'similarity': 0.7
        },
        'angry': {
            'voice': 'nova',
            'speed': 1.1,       # やや早め
            'pitch': -3,        # やや低め
            'volume': 1.3,      # 大きめ
            'stability': 0.4,   # 不安定（感情的）
            'similarity': 0.7
        },
        'surprised': {
            'voice': 'nova',
            'speed': 1.2,       # 早め
            'pitch': 15,        # かなり高め
            'volume': 1.2,      # やや大きめ
            'stability': 0.3,   # 不安定（驚き）
            'similarity': 0.8
        },
        'neutral': default_params,
        
        # 追加の感情
        'excited': {
            'voice': 'nova',
            'speed': 1.2,       # 早め
            'pitch': 12,        # かなり高め
            'volume': 1.3,      # 大きめ
            'stability': 0.3,   # 不安定（興奮）
            'similarity': 0.8
        },
        'calm': {
            'voice': 'nova',
            'speed': 0.95,      # やや遅め
            'pitch': 0,         # 標準
            'volume': 0.9,      # やや小さめ
            'stability': 0.7,   # 安定
            'similarity': 0.8
        }
    }
    
    # 指定された感情が定義されていない場合はデフォルト値を返す
    return emotion_params.get(emotion.lower(), default_params) 