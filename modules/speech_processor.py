# speech_processor.py - 音声認識処理モジュール（Python 3.13対応版）
import os
import base64
import tempfile
import wave
import io
import subprocess
from openai import OpenAI

# FFmpegのパスを確認
def find_ffmpeg():
    try:
        # ffmpegコマンドの存在を確認
        subprocess.run(['ffmpeg', '-version'], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        return True
    except (subprocess.SubprocessError, FileNotFoundError):
        print("⚠️ FFmpegが見つかりません。PATH環境変数にFFmpegのbinディレクトリが含まれているか確認してください。")
        return False

FFMPEG_AVAILABLE = find_ffmpeg()

class SpeechProcessor:
    def __init__(self):
        self.client = OpenAI()
        self.supported_formats = ['webm', 'mp3', 'mp4', 'mpeg', 'mpga', 'm4a', 'wav', 'ogg']
        self.ffmpeg_available = FFMPEG_AVAILABLE
        print(f"🎤 SpeechProcessor初期化完了 (FFmpeg利用可能: {self.ffmpeg_available})")
    
    def transcribe_audio(self, audio_base64, language='ja'):
        """Base64エンコードされた音声データをテキストに変換"""
        # FFmpegが利用できない場合
        if not self.ffmpeg_available:
            print("⚠️ FFmpegが利用できないため、音声処理ができません。")
            return "音声認識機能は現在利用できません。FFmpegをインストールしてください。テキストで入力してください。"
            
        try:
            print(f"🎤 音声認識開始 (言語: {language})")
            
            # Base64データの検証
            if not audio_base64:
                print("❌ 音声データが空です")
                return None
            
            # データURLスキームの処理
            if audio_base64.startswith('data:'):
                # data:audio/webm;base64,xxxxx の形式から実際のデータを抽出
                try:
                    header, data = audio_base64.split(',', 1)
                    audio_base64 = data
                    print(f"📊 データURLヘッダー: {header}")
                except Exception as e:
                    print(f"❌ データURL解析エラー: {e}")
                    return None
            
            # Base64デコード
            try:
                audio_data = base64.b64decode(audio_base64)
                print(f"✅ Base64デコード成功: {len(audio_data)} バイト")
            except Exception as e:
                print(f"❌ Base64デコードエラー: {e}")
                return None
            
            # 一時ファイルを作成して音声データを保存
            with tempfile.NamedTemporaryFile(delete=False, suffix='.webm') as temp_webm:
                temp_webm.write(audio_data)
                temp_webm_path = temp_webm.name
                print(f"📁 一時ファイル作成: {temp_webm_path}")
            
            try:
                # WAVファイルとして一時保存するパス
                temp_wav_path = tempfile.mktemp(suffix='.wav')
                
                # FFmpegを使用してWebMからWAVに変換
                print(f"🔄 FFmpegでWAVに変換中...")
                subprocess.run([
                    'ffmpeg', 
                    '-i', temp_webm_path, 
                    '-ar', '16000',  # Whisper APIの推奨サンプルレート
                    '-ac', '1',      # モノラル
                    '-y',            # 既存ファイルを上書き
                    temp_wav_path
                ], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
                
                print(f"✅ WAV変換成功: {temp_wav_path}")
                
                # OpenAI Whisper APIで音声認識
                with open(temp_wav_path, 'rb') as audio_file:
                    print("🔄 Whisper APIに送信中...")
                    
                    transcript = self.client.audio.transcriptions.create(
                        model="whisper-1",
                        file=audio_file,
                        language=language,
                        response_format="text",
                        prompt="京友禅、のりおき、職人、染色、着物"  # ドメイン特有の単語をヒントとして提供
                    )
                    
                    # Whisper APIはテキストを直接返す
                    text = transcript.strip() if isinstance(transcript, str) else str(transcript).strip()
                    
                    print(f"✅ 音声認識成功: '{text}'")
                    
                    # 空の結果チェック
                    if not text or text == "":
                        print("⚠️ 音声認識結果が空です")
                        return None
                    
                    return text
                    
            except subprocess.SubprocessError as e:
                print(f"❌ FFmpeg実行エラー: {e}")
                return "音声の変換に失敗しました。FFmpegの設定を確認してください。"
            except Exception as e:
                print(f"❌ 音声処理エラー: {type(e).__name__}: {e}")
                import traceback
                traceback.print_exc()
                
                # エラーの詳細情報
                if hasattr(e, 'response'):
                    print(f"API応答: {e.response}")
                
                return None
                
            finally:
                # 一時ファイルのクリーンアップ
                try:
                    if os.path.exists(temp_webm_path):
                        os.unlink(temp_webm_path)
                        print(f"🗑️ 一時ファイル削除: {temp_webm_path}")
                    if 'temp_wav_path' in locals() and os.path.exists(temp_wav_path):
                        os.unlink(temp_wav_path)
                        print(f"🗑️ 一時ファイル削除: {temp_wav_path}")
                except Exception as e:
                    print(f"⚠️ 一時ファイル削除エラー: {e}")
                    
        except Exception as e:
            print(f"❌ 音声認識エラー: {type(e).__name__}: {e}")
            import traceback
            traceback.print_exc()
            return None
    
    def validate_audio_data(self, audio_base64):
        """音声データの妥当性を検証"""
        # FFmpegが利用できない場合
        if not self.ffmpeg_available:
            return False
            
        try:
            # データURLスキームの確認
            if audio_base64.startswith('data:'):
                header, data = audio_base64.split(',', 1)
                # サポートされている形式かチェック
                if 'audio/' in header:
                    return True
                else:
                    print(f"❌ サポートされていない形式: {header}")
                    return False
            
            # Base64データの基本的な検証
            try:
                decoded = base64.b64decode(audio_base64)
                if len(decoded) < 100:  # 最小サイズチェック
                    print(f"❌ 音声データが小さすぎます: {len(decoded)} バイト")
                    return False
                return True
            except:
                return False
                
        except Exception as e:
            print(f"❌ 音声データ検証エラー: {e}")
            return False
    
    def get_audio_duration(self, audio_base64):
        """音声の長さを取得"""
        # FFmpegが利用できない場合
        if not self.ffmpeg_available:
            return 0
            
        try:
            if audio_base64.startswith('data:'):
                _, data = audio_base64.split(',', 1)
                audio_base64 = data
            
            audio_data = base64.b64decode(audio_base64)
            
            with tempfile.NamedTemporaryFile(delete=False, suffix='.webm') as temp_file:
                temp_file.write(audio_data)
                temp_path = temp_file.name
            
            try:
                # FFmpegを使って長さを取得
                result = subprocess.run([
                    'ffprobe', 
                    '-v', 'error', 
                    '-show_entries', 'format=duration', 
                    '-of', 'default=noprint_wrappers=1:nokey=1', 
                    temp_path
                ], capture_output=True, text=True, check=True)
                
                duration = float(result.stdout.strip())
                return duration
            finally:
                if os.path.exists(temp_path):
                    os.unlink(temp_path)
                    
        except Exception as e:
            print(f"❌ 音声長さ取得エラー: {e}")
            return 0
