# openai_tts_client.py
import os
import base64
from openai import OpenAI

class OpenAITTSClient:
    def __init__(self):
        self.client = OpenAI()
        
        # かわいい女性の声を固定で使用
        self.voice = "nova"  # 明るく元気な女性の声
        self.speed = 1.15   # 少し速めで若々しい印象
    
    def generate_audio(self, text, voice=None, emotion_params=None):
        """テキストから音声を生成"""
        try:
            # 常に同じ声を使用（感情による変化なし）
            response = self.client.audio.speech.create(
                model="tts-1-hd",  # 高品質モデル
                voice=self.voice,
                input=text,
                speed=self.speed
            )
            
            # 音声データをBase64エンコード
            audio_data = base64.b64encode(response.content).decode('utf-8')
            return f"data:audio/mp3;base64,{audio_data}"
            
        except Exception as e:
            print(f"音声生成中にエラーが発生しました: {e}")
            return None