# app.py - 会話記憶システム + 関係性レベル + より人間らしい会話実装版（京友禅職人版）
# 感情分析品質改善版 + サジェスチョン重複防止 + 回答品質向上
from flask import Flask, render_template, request, redirect, url_for, session, jsonify
from flask_socketio import SocketIO, emit
from werkzeug.utils import secure_filename
from dotenv import load_dotenv
import os
import base64
import json
import uuid
import time
import re
from datetime import datetime, timedelta
from collections import defaultdict, deque
from typing import Dict, Tuple, List, Set
from modules.rag_system import RAGSystem
from modules.speech_processor import SpeechProcessor
from modules.openai_tts_client import OpenAITTSClient
from modules.coe_font_client import CoeFontClient
from modules.emotion_voice_params import get_emotion_voice_params
from openai import OpenAI

# 静的Q&Aシステム
from static_qa_data import get_static_response, STATIC_QA_PAIRS

# 環境変数をロード
load_dotenv()

# Flaskアプリケーションの初期化
app = Flask(__name__)
app.config['SECRET_KEY'] = os.getenv('SECRET_KEY', 'your_secret_key')
app.config['UPLOAD_FOLDER'] = 'uploads'
app.config['DEBUG'] = os.getenv('DEBUG', 'False').lower() == 'true'

# Socket.IOの設定
socketio = SocketIO(
    app,
    cors_allowed_origins="*",
    async_mode='threading',
    ping_timeout=60,
    ping_interval=25,
    logger=True,
    engineio_logger=True,
    allow_upgrades=True,
    transports=['websocket', 'polling'],
    max_http_buffer_size=1e8,
    path='socket.io',
    always_connect=True,
    cookie=False,
    allow_headers=['*'],
    methods=['GET', 'POST', 'OPTIONS'],
    async_handlers=True,
    monitor_clients=True,
    ping_interval_grace_period=1000
)

# ====== 🎯 感情分析システム（改善版） ======
class EmotionAnalyzer:
    def __init__(self):
        # 感情キーワード辞書（優先度順・拡張版）
        self.emotion_keywords = {
            'happy': {
                'keywords': [
                    'うれしい', '嬉しい', 'ウレシイ', 'ureshii',
                    '楽しい', 'たのしい', 'tanoshii',
                    'ハッピー', 'happy', 'はっぴー',
                    '喜び', 'よろこび', 'yorokobi',
                    '幸せ', 'しあわせ', 'shiawase',
                    '最高', 'さいこう', 'saikou',
                    'やった', 'yatta',
                    'わーい', 'わあい', 'waai',
                    '笑', 'わら', 'wara',
                    '良い', 'いい', 'よい', 'yoi',
                    '素晴らしい', 'すばらしい', 'subarashii',
                    'ありがとう', 'ありがと', 'おかげ',
                    '感謝', 'かんしゃ', '感動', 'かんどう',
                    '面白い', 'おもしろい', 'たのしみ',
                    'ワクワク', 'わくわく', 'ドキドキ'
                ],
                'emojis': ['😊', '😄', '😃', '😁', '🙂', '☺️', '🥰', '😍', '🎉', '✨', '❤️', '💕'],
                'patterns': [r'！+', r'♪+', r'〜+$', r'www', r'笑$'],
                'weight': 1.3  # 優先度を上げる
            },
            'sad': {
                'keywords': [
                    '悲しい', 'かなしい', 'カナシイ', 'kanashii',
                    '寂しい', 'さびしい', 'さみしい', 'sabishii',
                    '辛い', 'つらい', 'ツライ', 'tsurai',
                    '泣', 'なき', 'naki',
                    '涙', 'なみだ', 'namida',
                    'しょんぼり', 'shonbori',
                    'がっかり', 'gakkari',
                    '憂鬱', 'ゆううつ', 'yuuutsu',
                    '落ち込', 'おちこ', 'ochiko',
                    'だめ', 'ダメ', 'dame',
                    '失敗', 'しっぱい', 'shippai',
                    '無理', 'むり', '諦め', 'あきらめ',
                    '疲れ', 'つかれ', 'しんどい'
                ],
                'emojis': ['😢', '😭', '😔', '😞', '😟', '☹️', '😥', '😰', '💔'],
                'patterns': [r'\.\.\.+$', r'…+$', r'はぁ', r'ため息'],
                'weight': 1.2
            },
            'angry': {
                'keywords': [
                    '怒', 'おこ', 'いか', 'oko', 'ika',
                    'ムカつく', 'むかつく', 'mukatsuku',
                    'イライラ', 'いらいら', 'iraira',
                    '腹立', 'はらだ', 'harada',
                    'キレ', 'きれ', 'kire',
                    '最悪', 'さいあく', 'saiaku',
                    'ふざけ', 'fuzake',
                    'もう', 'mou',
                    'なんで', 'nande',
                    'ひどい', 'hidoi',
                    'うざい', 'ウザイ', '邪魔',
                    '嫌い', 'きらい', '憎'
                ],
                'emojis': ['😠', '😡', '🤬', '😤', '💢', '🔥', '👿'],
                'patterns': [r'！！+', r'っ！+', r'ﾁｯ', r'くそ'],
                'weight': 1.1
            },
            'surprised': {
                'keywords': [
                    '驚', 'おどろ', 'odoro',
                    'びっくり', 'ビックリ', 'bikkuri',
                    'すごい', 'スゴイ', '凄い', 'sugoi',
                    'まじ', 'マジ', 'maji',
                    'えっ', 'え？', 'えー', 'e',
                    'わっ', 'wa',
                    'なに', 'ナニ', 'nani',
                    '本当', 'ほんとう', 'hontou',
                    'うそ', 'ウソ', '嘘', 'uso',
                    'やばい', 'ヤバイ', 'yabai',
                    '信じられない', 'しんじられない',
                    'ありえない', '予想外'
                ],
                'emojis': ['😲', '😮', '😯', '😳', '🤯', '😱', '🙀', '⁉️'],
                'patterns': [r'[!?！？]+', r'。。+', r'ええ[!?！？]'],
                'weight': 1.1
            }
        }
        
        # 文脈による感情判定用のフレーズ
        self.context_phrases = {
            'happy': ['よかった', '楽しみ', '期待', '頑張', 'がんば', '応援'],
            'sad': ['残念', 'ざんねん', '悔しい', 'くやしい', '寂しく'],
            'angry': ['許せない', 'ゆるせない', '納得いかない'],
            'surprised': ['知らなかった', 'しらなかった', '初めて', 'はじめて']
        }
        
    def analyze_emotion(self, text: str) -> Tuple[str, float]:
        """
        テキストから感情を分析（改善版）
        Returns: (emotion, confidence)
        """
        if not text:
            return 'neutral', 0.5
            
        # テキストの前処理
        text_lower = text.lower()
        text_normalized = self._normalize_text(text)
        
        # 各感情のスコアを計算
        scores: Dict[str, float] = {
            'happy': 0.0,
            'sad': 0.0,
            'angry': 0.0,
            'surprised': 0.0,
            'neutral': 0.0
        }
        
        # キーワードマッチング
        for emotion, config in self.emotion_keywords.items():
            # キーワードチェック
            for keyword in config['keywords']:
                if keyword in text_normalized:
                    scores[emotion] += 2.0 * config['weight']
                    
            # 絵文字チェック
            for emoji in config['emojis']:
                if emoji in text:
                    scores[emotion] += 1.5 * config['weight']
                    
            # パターンチェック
            for pattern in config['patterns']:
                if re.search(pattern, text):
                    scores[emotion] += 1.0 * config['weight']
        
        # 文脈フレーズのチェック
        for emotion, phrases in self.context_phrases.items():
            for phrase in phrases:
                if phrase in text_normalized:
                    scores[emotion] += 0.5
        
        # 文の長さによる調整（短い文は感情が強い傾向）
        if len(text) < 10 and max(scores.values()) > 0:
            max_emotion = max(scores, key=scores.get)
            scores[max_emotion] *= 1.2
        
        # 感情強度の判定
        max_score = max(scores.values())
        
        if max_score < 1.0:
            return 'neutral', 0.5
            
        # 最高スコアの感情を選択
        detected_emotion = max(scores, key=scores.get)
        confidence = min(scores[detected_emotion] / 10.0, 1.0)
        
        # 複数の感情が競合する場合の処理
        sorted_emotions = sorted(scores.items(), key=lambda x: x[1], reverse=True)
        if len(sorted_emotions) > 1:
            # 2番目に高いスコアとの差が小さい場合は信頼度を下げる
            if sorted_emotions[0][1] - sorted_emotions[1][1] < 1.0:
                confidence *= 0.8
        
        return detected_emotion, confidence
        
    def _normalize_text(self, text: str) -> str:
        """テキストの正規化"""
        # 記号やスペースを除去
        text = re.sub(r'[^\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FAF\w\s]', '', text)
        # 全角英数字を半角に変換
        text = text.translate(str.maketrans('０１２３４５６７８９ＡＢＣＤＥＦＧＨＩＪＫＬＭＮＯＰＱＲＳＴＵＶＷＸＹＺａｂｃｄｅｆｇｈｉｊｋｌｍｎｏｐｑｒｓｔｕｖｗｘｙｚ',
                                           '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz'))
        return text.lower()

# EmotionAnalyzerのインスタンスを作成
emotion_analyzer = EmotionAnalyzer()

# RAGシステムと音声処理のインスタンス化
rag_system = RAGSystem()
speech_processor = SpeechProcessor()
tts_client = OpenAITTSClient()

# CoeFont統合
coe_font_client = CoeFontClient()
use_coe_font = coe_font_client.is_available()
print(f"🎵 CoeFont利用可能: {use_coe_font}")

# キャッシュ統計情報
cache_stats = {
    'total_requests': 0,
    'cache_hits': 0,
    'cache_misses': 0,
    'total_time_saved': 0.0,
    'coe_font_requests': 0,
    'openai_tts_requests': 0
}

# ====== 🧠 会話記憶システム用のデータ構造（強化版） ======
session_data = {}
visitor_data = {}  # 訪問者ごとの永続的なデータ
conversation_histories = {}  # 会話履歴の保存
emotion_histories = {}  # 🎯 セッションごとの感情履歴
mental_state_histories = {}  # 🎯 精神状態の履歴
emotion_transition_stats = defaultdict(lambda: defaultdict(int))  # 🎯 感情遷移の統計

# ====== 🎯 関係性レベル定義 ======
RELATIONSHIP_LEVELS = [
    {'level': 0, 'min_conversations': 0, 'max_conversations': 0, 'name': '初対面', 'style': 'formal'},
    {'level': 1, 'min_conversations': 1, 'max_conversations': 2, 'name': '興味あり', 'style': 'slightly_casual'},
    {'level': 2, 'min_conversations': 3, 'max_conversations': 4, 'name': '知り合い', 'style': 'casual'},
    {'level': 3, 'min_conversations': 5, 'max_conversations': 7, 'name': 'お友達', 'style': 'friendly'},
    {'level': 4, 'min_conversations': 8, 'max_conversations': 10, 'name': '友禅マスター', 'style': 'friend'},
    {'level': 5, 'min_conversations': 11, 'max_conversations': float('inf'), 'name': '親友', 'style': 'bestfriend'}
]

def calculate_relationship_level(conversation_count):
    """会話回数から関係性レベルを計算"""
    for level_info in reversed(RELATIONSHIP_LEVELS):
        if conversation_count >= level_info['min_conversations']:
            return {
                'level': level_info['level'],
                'name': level_info['name'],
                'style': level_info['style'],
                'conversation_count': conversation_count
            }
    
    return RELATIONSHIP_LEVELS[0]

def get_relationship_adjusted_greeting(language, relationship_style):
    """関係性レベルに応じた挨拶メッセージを生成"""
    greetings = {
        'ja': {
            'formal': "こんにちは〜！私は京友禅の職人で、手描友禅を15年やっているREIといいます。友禅染のことなら何でも聞いてくださいね。着物や染色について、何か知りたいことはありますか？",
            'slightly_casual': "あ、また来てくれたんやね！嬉しいわ〜。今日は何について話そうか？",
            'casual': "おっ、来たね〜！最近どう？今日も友禅の話でもする？",
            'friendly': "やっほー！いつもありがとうね。今日は何が聞きたい？なんでも答えるで〜",
            'friend': "お〜！来たか〜！もう友達みたいなもんやね。今日は何の話する？",
            'bestfriend': "きたきた〜！待ってたで！もう何でも話せる仲やもんね。調子どう？"
        },
        'en': {
            'formal': "Hello! I am Rei Yoshida, a Kyoto Yuzen artisan with 15 years of experience in hand-painted Yuzen. Please feel free to ask me anything about Yuzen dyeing, kimono, or traditional textile arts. Is there anything you'd like to know?",
            'slightly_casual': "Oh, you're back! I'm happy to see you again. What shall we talk about today?",
            'casual': "Hey there! How have you been? Want to chat about Yuzen again?",
            'friendly': "Hi hi! Thanks for coming as always. What would you like to know today?",
            'friend': "Hey friend! We're like buddies now. What's on your mind today?",
            'bestfriend': "There you are! I've been waiting! We can talk about anything. How are you doing?"
        }
    }
    
    return greetings.get(language, greetings['ja']).get(relationship_style, greetings[language]['formal'])

def get_session_data(session_id):
    """セッションデータを取得（感情履歴対応版）"""
    if session_id not in session_data:
        session_data[session_id] = {
            'language': 'ja',
            'user_id': str(uuid.uuid4()),
            'visitor_id': None,
            'conversation_history': [],
            'interaction_count': 0,
            'relationship_level': 0,
            'relationship_style': 'formal',
            'last_topics': [],
            'session_start': datetime.now().isoformat(),
            'current_topic': None,
            'question_counts': defaultdict(int),
            'current_emotion': 'neutral',  # 🎯 現在の感情
            'emotion_history': deque(maxlen=20),  # 🎯 感情履歴（最新20個）
            'mental_state': {  # 🎯 現在の精神状態
                'energy_level': 80,
                'stress_level': 20,
                'openness': 70,
                'patience': 90,
                'creativity': 85,
                'loneliness': 30,
                'work_satisfaction': 90,
                'physical_fatigue': 20
            },
            'selected_suggestions': [],  # 🎯 選択されたサジェスチョンの履歴
            'fatigue_mentioned': False  # 🎯 疲労について言及したか
        }
        # 感情履歴の初期化
        emotion_histories[session_id] = deque(maxlen=50)
        mental_state_histories[session_id] = deque(maxlen=50)
    return session_data[session_id]

def get_visitor_data(visitor_id):
    """訪問者データを取得または作成"""
    if visitor_id not in visitor_data:
        visitor_data[visitor_id] = {
            'first_seen': datetime.now().isoformat(),
            'visit_count': 1,
            'total_conversations': 0,
            'topics_discussed': [],
            'relationship_level': 0,
            'relationship_style': 'formal',
            'favorite_topics': [],
            'last_visit': datetime.now().isoformat(),
            'question_history': defaultdict(int),
            'personality_traits': {
                'interests': [],
                'communication_style': 'neutral',
                'knowledge_level': 'beginner'
            },
            'selected_suggestions': set()  # 🎯 選択されたサジェスチョンの記録
        }
    return visitor_data[visitor_id]

def update_visitor_data(visitor_id, session_info):
    """訪問者データを更新"""
    if visitor_id:
        v_data = get_visitor_data(visitor_id)
        v_data['last_visit'] = datetime.now().isoformat()
        v_data['total_conversations'] += session_info.get('interaction_count', 0)
        
        # トピックの更新
        for topic in session_info.get('last_topics', []):
            if topic not in v_data['topics_discussed']:
                v_data['topics_discussed'].append(topic)
        
        # 関係性レベルの更新
        current_level = session_info.get('relationship_level', 0)
        if current_level > v_data['relationship_level']:
            v_data['relationship_level'] = current_level
        
        # 関係性スタイルの更新
        v_data['relationship_style'] = session_info.get('relationship_style', 'formal')
        
        # 選択されたサジェスチョンの更新
        for suggestion in session_info.get('selected_suggestions', []):
            v_data['selected_suggestions'].add(suggestion)

def update_emotion_history(session_id, emotion, mental_state=None):
    """🎯 感情履歴を更新"""
    session_info = get_session_data(session_id)
    
    # 現在の感情を更新
    previous_emotion = session_info.get('current_emotion', 'neutral')
    session_info['current_emotion'] = emotion
    session_info['emotion_history'].append({
        'emotion': emotion,
        'timestamp': datetime.now().isoformat(),
        'interaction_count': session_info['interaction_count']
    })
    
    # 感情遷移の統計を更新
    emotion_transition_stats[previous_emotion][emotion] += 1
    
    # 全体の感情履歴に追加
    if session_id in emotion_histories:
        emotion_histories[session_id].append({
            'emotion': emotion,
            'timestamp': datetime.now().isoformat()
        })
    
    # 精神状態も記録
    if mental_state:
        session_info['mental_state'] = mental_state
        if session_id in mental_state_histories:
            mental_state_histories[session_id].append({
                'state': mental_state,
                'timestamp': datetime.now().isoformat()
            })

def normalize_question(question):
    """質問を正規化（重複判定用）"""
    return question.lower().replace('？', '').replace('?', '').replace('。', '').replace('、', '').replace('！', '').replace('!', '').strip()

def get_question_count(session_id, visitor_id, question):
    """質問の回数を取得"""
    normalized = normalize_question(question)
    session_info = get_session_data(session_id)
    
    # セッション内での回数
    session_count = session_info['question_counts'][normalized]
    
    # 訪問者全体での回数
    visitor_count = 0
    if visitor_id:
        v_data = get_visitor_data(visitor_id)
        visitor_count = v_data['question_history'][normalized]
    
    return max(session_count, visitor_count)

def increment_question_count(session_id, visitor_id, question):
    """質問回数をインクリメント"""
    normalized = normalize_question(question)
    session_info = get_session_data(session_id)
    
    # セッションでカウント
    session_info['question_counts'][normalized] += 1
    
    # 訪問者データでもカウント
    if visitor_id:
        v_data = get_visitor_data(visitor_id)
        v_data['question_history'][normalized] += 1
    
    return session_info['question_counts'][normalized]

def extract_topic_from_question(question):
    """質問からトピックを抽出（簡易版）"""
    keywords = {
        '京友禅': 'kyoto_yuzen',
        'のりおき': 'norioki',
        '職人': 'craftsman',
        '伝統': 'tradition',
        '着物': 'kimono',
        '染色': 'dyeing',
        '模様': 'pattern',
        '工程': 'process',
        '道具': 'tools',
        'コラボ': 'collaboration'
    }
    
    for keyword, topic in keywords.items():
        if keyword in question:
            return topic
    
    return 'general'

def get_context_prompt(conversation_history, question_count=1, relationship_style='formal', fatigue_mentioned=False):
    """会話履歴から文脈プロンプトを生成（関係性レベル対応）"""
    if not conversation_history:
        return ""
    
    context_parts = []
    
    # 最近の会話を要約
    recent_messages = conversation_history[-5:]  # 最近5つのメッセージ
    if recent_messages:
        context_parts.append("【最近の会話】")
        for msg in recent_messages:
            role = "ユーザー" if msg['role'] == 'user' else "あなた"
            context_parts.append(f"{role}: {msg['content']}")
    
    # 関係性レベルに基づく指示
    relationship_prompts = {
        'formal': "【関係性】初対面の相手なので、丁寧で礼儀正しく、敬語を使って話してください。",
        'slightly_casual': "【関係性】少し親しくなってきた相手なので、まだ丁寧だけど少し親しみを込めて話してください。",
        'casual': "【関係性】顔見知りになった相手なので、親しみやすい口調で、でも失礼にならない程度に話してください。",
        'friendly': "【関係性】常連さんなので、タメ口も混じる親しい感じで話してください。",
        'friend': "【関係性】友達として、冗談も言える関係で話してください。もうタメ口でOKです。",
        'bestfriend': "【関係性】親友として、何でも話せる関係で話してください。昔からの友達みたいに。"
    }
    
    context_parts.append(relationship_prompts.get(relationship_style, relationship_prompts['formal']))
    
    # 疲労表現の制限
    if fatigue_mentioned:
        context_parts.append("\n【重要】既に疲れについて言及したので、疲労に関する発言は控えてください。")
    
    # 質問回数に基づく注意事項
    if question_count > 1:
        context_parts.append(f"\n【注意】この質問は{question_count}回目です。")
        if question_count == 2:
            context_parts.append("「あ、さっきも聞かれたね」という反応を含めてください。")
        elif question_count == 3:
            context_parts.append("「また同じ質問？よっぽど気になるんやね〜」という反応を含めてください。")
        elif question_count >= 4:
            context_parts.append("「もう覚えてや〜（笑）」という反応を含めてください。")
    
    return "\n".join(context_parts)

# 音声生成関数（CoeFontを優先）
def generate_audio_by_language(text, language, emotion_params=None):
    """言語に応じて適切な音声エンジンを使用（CoeFont優先）"""
    try:
        # 日本語の場合は常にCoeFontを試す
        if language == 'ja':
            print(f"🎵 CoeFont音声生成開始: {text[:30]}... (感情: {emotion_params})")
            audio_data = coe_font_client.generate_audio(text, emotion=emotion_params)
            
            if audio_data:
                cache_stats['coe_font_requests'] += 1
                print(f"✅ CoeFont音声生成成功: {len(audio_data)} バイト")
                return audio_data
            else:
                print("❌ CoeFont音声生成失敗 → OpenAI TTSにフォールバック")
        
        print(f"🎵 OpenAI TTS音声生成開始: {text[:30]}... (言語: {language})")
        
        if language == 'ja':
            voice = "nova"
        else:
            voice = "echo"
        
        audio_data = tts_client.generate_audio(text, voice=voice, emotion_params=emotion_params)
        
        if audio_data:
            cache_stats['openai_tts_requests'] += 1
            print(f"✅ OpenAI TTS音声生成成功: {len(audio_data)} 文字")
            return audio_data
        else:
            print("❌ OpenAI TTS音声生成も失敗")
            return None
            
    except Exception as e:
        print(f"❌ 音声生成エラー: {e}")
        import traceback
        traceback.print_exc()
        return None

def adjust_response_for_language(response, language):
    """言語に応じて回答を調整"""
    if language == 'en':
        client = OpenAI()
        try:
            translation = client.chat.completions.create(
                model="gpt-4",
                messages=[
                    {
                        "role": "system", 
                        "content": "Translate the following Japanese text to natural, conversational English. Maintain the casual, friendly tone."
                    },
                    {
                        "role": "user", 
                        "content": response
                    }
                ],
                temperature=0.7,
                max_tokens=100
            )
            return translation.choices[0].message.content
        except Exception as e:
            print(f"翻訳エラー: {e}")
            response = response.replace("だよね", ", right?")
            response = response.replace("だよ", "")
            response = response.replace("じゃん", ", you know")
            response = response.replace("だし", ", and")
    return response

def analyze_emotion(text):
    """★★★ 修正手順対応: 改善された感情分析 ★★★"""
    # 新しいEmotionAnalyzerを使用
    emotion, confidence = emotion_analyzer.analyze_emotion(text)
    
    print(f"🎭 EmotionAnalyzer結果: {emotion} (信頼度: {confidence:.2f})")
    
    # 信頼度が低い場合はGPTにも確認
    if confidence < 0.7:
        print(f"📊 信頼度が低いため({confidence:.2f})、GPTでも確認します")
        
        client = OpenAI()
        try:
            response = client.chat.completions.create(
                model="gpt-4",
                messages=[
                    {"role": "system", "content": "入力されたテキストの感情を分析し、happy, sad, angry, surprised, neutralのいずれか1つだけを返してください。"},
                    {"role": "user", "content": text}
                ],
                max_tokens=10,
                temperature=0.1
            )
            gpt_emotion = response.choices[0].message.content.strip().lower()
            
            valid_emotions = ['happy', 'sad', 'angry', 'surprised', 'neutral']
            if gpt_emotion in valid_emotions:
                # 両方の結果を考慮
                if gpt_emotion != 'neutral' and gpt_emotion != emotion:
                    print(f"🧠 GPT-4感情分析結果: {gpt_emotion} (採用)")
                    emotion = gpt_emotion
                else:
                    print(f"🧠 GPT-4感情分析結果: {gpt_emotion} (EmotionAnalyzer結果を維持)")
            else:
                print(f"⚠️ GPT-4から無効な感情値: {gpt_emotion}")
                
        except Exception as e:
            print(f"❌ GPT-4感情分析エラー: {e}")
    
    print(f"🔍 最終感情判定: {emotion}")
    return emotion

def generate_prioritized_suggestions(session_info, visitor_info, relationship_style, language='ja'):
    """優先順位付きサジェスチョン生成（重複防止対応）"""
    # 選択済みサジェスチョンを取得
    selected_suggestions = set()
    if session_info:
        selected_suggestions.update(session_info.get('selected_suggestions', []))
    if visitor_info:
        selected_suggestions.update(visitor_info.get('selected_suggestions', set()))
    
    # 会話回数を取得
    conversation_count = session_info.get('interaction_count', 0) if session_info else 0
    
    # サジェスチョンカテゴリと優先順位
    suggestion_categories = {
        'overview': {  # 概要
            'priority': 1,
            'ja': [
                "京友禅について教えて",
                "京友禅の歴史を知りたい",
                "京友禅の特徴は何？",
                "友禅染って何がすごいの？",
                "なぜ京都で友禅が発展したの？"
            ],
            'en': [
                "Tell me about Kyoto Yuzen",
                "I want to know the history of Kyoto Yuzen",
                "What are the characteristics of Kyoto Yuzen?",
                "What's amazing about Yuzen dyeing?",
                "Why did Yuzen develop in Kyoto?"
            ]
        },
        'process': {  # 工程
            'priority': 2,
            'ja': [
                "制作工程を教えて",
                "のりおき工程について詳しく",
                "一番難しい工程は？",
                "どんな道具を使うの？",
                "制作期間はどれくらい？"
            ],
            'en': [
                "Tell me about the production process",
                "Details about the paste resist process",
                "What's the most difficult process?",
                "What tools do you use?",
                "How long does production take?"
            ]
        },
        'personal': {  # 個人的な話
            'priority': 3,
            'ja': [
                "職人になったきっかけは？",
                "15年間で印象に残っていることは？",
                "仕事のやりがいは？",
                "休日は何してる？",
                "将来の夢は？"
            ],
            'en': [
                "Why did you become an artisan?",
                "What impressed you in 15 years?",
                "What's rewarding about your work?",
                "What do you do on holidays?",
                "What are your future dreams?"
            ]
        },
        'advanced': {  # 詳細な話題
            'priority': 4,
            'ja': [
                "手描きとプリントの違いは？",
                "グラデーション技法について",
                "伝統工芸の定義って？",
                "後継者問題について",
                "現代のコラボレーションは？"
            ],
            'en': [
                "Difference between hand-painted and printed?",
                "About gradation techniques",
                "Definition of traditional crafts?",
                "About successor issues",
                "Modern collaborations?"
            ]
        }
    }
    
    # 初回訪問の場合は概要を優先
    if conversation_count < 3:
        priority_order = ['overview', 'process', 'personal', 'advanced']
    elif conversation_count < 6:
        priority_order = ['process', 'overview', 'advanced', 'personal']
    else:
        priority_order = ['personal', 'advanced', 'process', 'overview']
    
    # サジェスチョンを生成
    suggestions = []
    for category in priority_order:
        category_suggestions = suggestion_categories[category][language]
        
        # 選択されていないサジェスチョンをフィルタリング
        available_suggestions = [s for s in category_suggestions if s not in selected_suggestions]
        
        if available_suggestions:
            # カテゴリから1-2個選択
            count = min(2, len(available_suggestions))
            selected = available_suggestions[:count]
            suggestions.extend(selected)
            
            if len(suggestions) >= 3:
                break
    
    # 3個になるまで追加
    if len(suggestions) < 3:
        # すべてのカテゴリから未選択のものを追加
        all_suggestions = []
        for category in suggestion_categories.values():
            all_suggestions.extend(category[language])
        
        available = [s for s in all_suggestions if s not in selected_suggestions and s not in suggestions]
        if available:
            remaining = 3 - len(suggestions)
            suggestions.extend(available[:remaining])
    
    return suggestions[:3]  # 最大3個

def print_cache_stats():
    """キャッシュ統計を出力"""
    if cache_stats['total_requests'] > 0:
        hit_rate = (cache_stats['cache_hits'] / cache_stats['total_requests']) * 100
        avg_time_saved = cache_stats['total_time_saved'] / max(cache_stats['cache_hits'], 1)
        
        print(f"\n=== CoeFont統合キャッシュ統計 ===")
        print(f"📊 総リクエスト数: {cache_stats['total_requests']}")
        print(f"🎯 キャッシュヒット数: {cache_stats['cache_hits']}")
        print(f"⚡ キャッシュヒット率: {hit_rate:.1f}%")
        print(f"⏱️  平均時間短縮: {avg_time_saved:.2f}秒")
        print(f"💨 総時間短縮: {cache_stats['total_time_saved']:.2f}秒")
        print(f"🎵 CoeFont使用回数: {cache_stats['coe_font_requests']}")
        print(f"🗣️ OpenAI TTS使用回数: {cache_stats['openai_tts_requests']}")
        print(f"================================\n")

# ============== ルート定義 ==============

@app.route('/')
def index():
    return render_template('index.html', title='感情的AIアバター')

@app.route('/data-management')
def data_management():
    files = []
    if os.path.exists(app.config['UPLOAD_FOLDER']):
        files = os.listdir(app.config['UPLOAD_FOLDER'])
    return render_template('data_management.html', title='感情的AIアバター', files=files)

@app.route('/upload-files', methods=['POST'])
def upload_files():
    if 'files' not in request.files:
        return redirect(url_for('data_management'))
    
    files = request.files.getlist('files')
    
    if not os.path.exists(app.config['UPLOAD_FOLDER']):
        os.makedirs(app.config['UPLOAD_FOLDER'])
    
    for file in files:
        if file.filename:
            filename = secure_filename(file.filename)
            file.save(os.path.join(app.config['UPLOAD_FOLDER'], filename))
    
    return render_template('data_management.html', 
                          title='感情的AIアバター', 
                          files=os.listdir(app.config['UPLOAD_FOLDER']),
                          message='ファイルが正常にアップロードされました')

@app.route('/process-documents', methods=['POST'])
def process_documents():
    success = rag_system.process_documents(app.config['UPLOAD_FOLDER'])
    
    files = []
    if os.path.exists(app.config['UPLOAD_FOLDER']):
        files = os.listdir(app.config['UPLOAD_FOLDER'])
    
    if success:
        return render_template('data_management.html', 
                              title='感情的AIアバター', 
                              files=files,
                              message='ドキュメントが正常に処理されました')
    else:
        return render_template('data_management.html', 
                              title='感情的AIアバター', 
                              files=files,
                              error='ドキュメントの処理中にエラーが発生しました')

@app.route('/cache-stats')
def show_cache_stats():
    """キャッシュ統計を表示"""
    return jsonify({
        'total_requests': cache_stats['total_requests'],
        'cache_hits': cache_stats['cache_hits'],
        'cache_misses': cache_stats['cache_misses'],
        'hit_rate': (cache_stats['cache_hits'] / max(cache_stats['total_requests'], 1)) * 100,
        'total_time_saved': cache_stats['total_time_saved'],
        'available_static_qa': len(STATIC_QA_PAIRS),
        'coe_font_requests': cache_stats['coe_font_requests'],
        'openai_tts_requests': cache_stats['openai_tts_requests'],
        'coe_font_available': use_coe_font,
        'system_status': {
            'coe_font': 'available' if use_coe_font else 'unavailable',
            'openai_tts': 'available',
            'rag_system': 'available'
        }
    })

# ====== 🧠 会話記憶システムのデバッグエンドポイント ======
@app.route('/visitor-stats')
def show_visitor_stats():
    """訪問者統計を表示"""
    return jsonify({
        'total_visitors': len(visitor_data),
        'active_sessions': len(session_data),
        'visitor_summary': [
            {
                'visitor_id': vid,
                'visit_count': vdata.get('visit_count', 0),
                'total_conversations': vdata.get('total_conversations', 0),
                'relationship_level': vdata.get('relationship_level', 0),
                'topics_discussed': vdata.get('topics_discussed', [])
            }
            for vid, vdata in visitor_data.items()
        ]
    })

# 🎯 新しいエンドポイント：感情統計
@app.route('/emotion-stats')
def show_emotion_stats():
    """感情統計を表示"""
    # セッションごとの感情分布
    session_emotions = {}
    for sid, sdata in session_data.items():
        if 'emotion_history' in sdata:
            emotions = [e['emotion'] for e in sdata['emotion_history']]
            session_emotions[sid] = {
                'total': len(emotions),
                'distribution': dict(defaultdict(int, {e: emotions.count(e) for e in set(emotions)})),
                'current': sdata.get('current_emotion', 'neutral')
            }
    
    # 感情遷移の統計
    transition_matrix = {}
    for from_emotion, to_emotions in emotion_transition_stats.items():
        transition_matrix[from_emotion] = dict(to_emotions)
    
    return jsonify({
        'session_emotions': session_emotions,
        'emotion_transitions': transition_matrix,
        'total_sessions': len(session_data),
        'active_emotions': {
            sid: sdata.get('current_emotion', 'neutral') 
            for sid, sdata in session_data.items()
        }
    })

# 🎯 新しいエンドポイント：精神状態
@app.route('/mental-state/<session_id>')
def show_mental_state(session_id):
    """特定セッションの精神状態を表示"""
    if session_id not in session_data:
        return jsonify({'error': 'Session not found'}), 404
    
    session_info = session_data[session_id]
    mental_state = session_info.get('mental_state', {})
    
    # 精神状態の履歴
    history = []
    if session_id in mental_state_histories:
        history = list(mental_state_histories[session_id])[-10:]  # 最新10件
    
    return jsonify({
        'session_id': session_id,
        'current_mental_state': mental_state,
        'emotion': session_info.get('current_emotion', 'neutral'),
        'relationship_level': session_info.get('relationship_style', 'formal'),
        'interaction_count': session_info.get('interaction_count', 0),
        'history': history
    })

# ============== WebSocketイベントハンドラー ==============

# 訪問者情報の受信
@socketio.on('visitor_info')
def handle_visitor_info(data):
    session_id = request.sid
    visitor_id = data.get('visitorId')
    visit_data = data.get('visitData', {})
    
    session_info = get_session_data(session_id)
    session_info['visitor_id'] = visitor_id
    
    # 訪問者データの更新
    if visitor_id:
        v_data = get_visitor_data(visitor_id)
        v_data['visit_count'] = visit_data.get('visitCount', 1)
        v_data['last_visit'] = datetime.now().isoformat()
        
        print(f'👤 訪問者情報更新: {visitor_id} (訪問回数: {v_data["visit_count"]})')

@socketio.on('connect')
def handle_connect():
    session_id = request.sid
    data = get_session_data(session_id)
    language = data["language"]
    
    # 訪問者の関係性レベルを確認
    visitor_id = data.get('visitor_id')
    visitor_info = None
    relationship_style = 'formal'
    if visitor_id and visitor_id in visitor_data:
        visitor_info = visitor_data[visitor_id]
        conversation_count = visitor_info.get('total_conversations', 0)
        rel_info = calculate_relationship_level(conversation_count)
        relationship_style = rel_info['style']
        data['relationship_style'] = relationship_style
    
    print(f'🔌 クライアント接続: {session_id}, 言語: {language}, 関係性: {relationship_style}')
    emit('status', {'message': '接続成功'})
    
    emit('current_language', {'language': language})
    
    # 関係性レベルに応じた初期挨拶
    greeting_message = get_relationship_adjusted_greeting(language, relationship_style)
    greeting_emotion = "happy"
    
    # 🎯 初回の感情を記録
    update_emotion_history(session_id, greeting_emotion)
    
    try:
        audio_data = generate_audio_by_language(
            greeting_message, 
            language, 
            emotion_params=greeting_emotion
        )
    except Exception as e:
        print(f"❌ 挨拶音声生成エラー: {e}")
        audio_data = None
    
    greeting_data = {
        'message': greeting_message,
        'emotion': greeting_emotion,
        'audio': audio_data,
        'isGreeting': True,
        'language': language,
        'voice_engine': 'coe_font' if use_coe_font and language == 'ja' else 'openai_tts',
        'relationshipLevel': relationship_style,
        'mentalState': data['mental_state']  # 🎯 精神状態も送信
    }
    
    # 優先順位付きサジェスチョンを生成
    greeting_data['suggestions'] = generate_prioritized_suggestions(
        data, visitor_info, relationship_style, language
    )
    
    emit('greeting', greeting_data)

@socketio.on('set_language')
def handle_set_language(data):
    session_id = request.sid
    language = data.get('language', 'ja')
    
    session_info = get_session_data(session_id)
    session_info['language'] = language
    
    # 関係性レベルを確認
    visitor_id = session_info.get('visitor_id')
    visitor_info = None
    relationship_style = 'formal'
    if visitor_id and visitor_id in visitor_data:
        visitor_info = visitor_data[visitor_id]
        conversation_count = visitor_info.get('total_conversations', 0)
        rel_info = calculate_relationship_level(conversation_count)
        relationship_style = rel_info['style']
    
    print(f"🌐 言語設定変更: {session_id} -> {language}")
    
    emit('language_changed', {'language': language})
    
    # 関係性レベルに応じた挨拶
    greeting_message = get_relationship_adjusted_greeting(language, relationship_style)
    greeting_emotion = "happy"
    
    try:
        audio_data = generate_audio_by_language(
            greeting_message, 
            language, 
            emotion_params=greeting_emotion
        )
    except Exception as e:
        print(f"❌ 挨拶音声生成エラー: {e}")
        audio_data = None
    
    greeting_data = {
        'message': greeting_message,
        'emotion': greeting_emotion,
        'audio': audio_data,
        'isGreeting': True,
        'language': language,
        'voice_engine': 'coe_font' if use_coe_font and language == 'ja' else 'openai_tts',
        'relationshipLevel': relationship_style,
        'mentalState': session_info['mental_state']
    }
    
    # 言語に応じたサジェスチョンを生成
    greeting_data['suggestions'] = generate_prioritized_suggestions(
        session_info, visitor_info, relationship_style, language
    )
    
    emit('greeting', greeting_data)

@socketio.on('disconnect')
def handle_disconnect():
    session_id = request.sid
    
    # セッション終了時に訪問者データを更新
    if session_id in session_data:
        session_info = session_data[session_id]
        visitor_id = session_info.get('visitor_id')
        
        if visitor_id:
            update_visitor_data(visitor_id, session_info)
        
        del session_data[session_id]
    
    print(f'🔌 クライアント切断: {session_id}')
    print_cache_stats()

# ====== 🧠 会話記憶対応メッセージハンドラー（感情履歴管理強化版） ======
@socketio.on('message')
def handle_message(data):
    start_time = time.time()
    
    try:
        session_id = request.sid
        session_info = get_session_data(session_id)
        language = session_info['language']
        
        message = data.get('message', '')
        visitor_id = data.get('visitorId')
        conversation_history = data.get('conversationHistory', [])
        interaction_count = data.get('interactionCount', 0)
        relationship_level_style = data.get('relationshipLevel', 'formal')
        
        print(f'📨 受信メッセージ: {message} (言語: {language}, 訪問者: {visitor_id}, 関係性: {relationship_level_style})')
        
        # サジェスチョンが選択された場合、記録する
        if message in session_info.get('selected_suggestions', []):
            pass  # 既に記録済み
        else:
            session_info['selected_suggestions'].append(message)
            if visitor_id:
                v_data = get_visitor_data(visitor_id)
                v_data['selected_suggestions'].add(message)
        
        # 訪問者IDを更新
        if visitor_id:
            session_info['visitor_id'] = visitor_id
            session_info['relationship_style'] = relationship_level_style
        
        # 会話履歴を更新
        session_info['conversation_history'] = conversation_history
        session_info['interaction_count'] = interaction_count
        
        # 質問回数を取得・更新
        question_count = increment_question_count(session_id, visitor_id, message)
        print(f"📊 質問回数: {question_count}回目")
        
        # トピック抽出
        current_topic = extract_topic_from_question(message)
        session_info['current_topic'] = current_topic
        
        if current_topic not in session_info['last_topics']:
            session_info['last_topics'].append(current_topic)
            if len(session_info['last_topics']) > 10:
                session_info['last_topics'].pop(0)
        
        # 統計更新
        cache_stats['total_requests'] += 1
        
        if not message:
            emit('error', {'message': 'メッセージが空です'})
            return
        
        # 静的キャッシュをチェック
        static_response = get_static_response(message)
        
        if static_response:
            cache_hit_time = time.time()
            processing_time = cache_hit_time - start_time
            
            print(f"🚀 静的キャッシュヒット！ 処理時間: {processing_time:.3f}秒")
            
            cache_stats['cache_hits'] += 1
            estimated_saved_time = 6.0
            cache_stats['total_time_saved'] += estimated_saved_time
            
            emotion = static_response['emotion']
            response = static_response['answer']
            suggestions = static_response.get('suggestions', [])
            
            # 🎯 感情履歴を更新
            update_emotion_history(session_id, emotion, session_info['mental_state'])
            
            # 質問回数に応じて応答を調整
            if question_count > 1:
                if question_count == 2:
                    response = f"あ、さっきも聞かれたね。{response}"
                elif question_count == 3:
                    response = f"また同じ質問？よっぽど気になるんやね〜。{response}"
                elif question_count >= 4:
                    response = f"もう覚えてや〜（笑）でも、もう一回説明するね。{response}"
            
            if language == 'en':
                response = adjust_response_for_language(response, language)
                translated_suggestions = []
                for suggestion in suggestions:
                    translated = adjust_response_for_language(suggestion, language)
                    translated_suggestions.append(translated)
                suggestions = translated_suggestions
            
            try:
                audio_data = generate_audio_by_language(
                    response, 
                    language, 
                    emotion_params=emotion
                )
            except Exception as e:
                print(f"❌ キャッシュ応答の音声合成エラー: {e}")
                audio_data = None
            
            # 優先順位付きサジェスチョンを生成（キャッシュヒット時も）
            visitor_info = get_visitor_data(visitor_id) if visitor_id else None
            suggestions = generate_prioritized_suggestions(
                session_info, visitor_info, relationship_level_style, language
            )
            
            response_data = {
                'message': response,
                'emotion': emotion,
                'audio': audio_data,
                'suggestions': suggestions,
                'language': language,
                'cached': True,
                'processing_time': processing_time,
                'voice_engine': 'coe_font' if use_coe_font and language == 'ja' else 'openai_tts',
                'currentTopic': current_topic,
                'relationshipLevel': relationship_level_style,
                'mentalState': session_info['mental_state']  # 🎯 精神状態も送信
            }
            
            print(f"⚡ キャッシュ応答送信完了 - 感情: {emotion}, 処理時間: {processing_time:.3f}秒")
            emit('response', response_data)
            return
        
        # キャッシュミス → 通常処理（関係性レベル・感情履歴対応）
        print(f"❌ キャッシュミス → 通常のRAG処理を実行")
        cache_stats['cache_misses'] += 1
        
        try:
            user_emotion = analyze_emotion(message)
            print(f"🎭 ユーザーメッセージの感情分析結果: {user_emotion}")
        except Exception as e:
            print(f"❌ 感情分析エラー: {e}")
            user_emotion = "neutral"
        
        # 🎯 前回の感情を取得
        previous_emotion = session_info.get('current_emotion', 'neutral')
        
        # 🎯 文脈プロンプトを生成（関係性レベル付き）
        context_prompt = get_context_prompt(
            conversation_history, 
            question_count, 
            relationship_level_style,
            session_info.get('fatigue_mentioned', False)
        )
        
        # RAGシステムで回答とサジェスションを生成（文脈付き）
        try:
            # RAGシステムに文脈と関係性レベルを渡す
            response_data_rag = rag_system.answer_with_suggestions(
                message, 
                context=context_prompt,
                question_count=question_count,
                relationship_style=relationship_level_style,
                previous_emotion=previous_emotion  # 🎯 前回の感情も渡す
            )
            response = response_data_rag['answer']
            next_suggestions = response_data_rag.get('suggestions', [])
            current_emotion = response_data_rag.get('current_emotion', user_emotion)  # 🎯 現在の感情を取得
            
            # 疲労表現をチェック
            if '疲れ' in response and not session_info.get('fatigue_mentioned', False):
                session_info['fatigue_mentioned'] = True
            
            # 🎯 感情履歴を更新（RAGシステムから取得した精神状態を使用）
            if hasattr(rag_system, 'mental_states'):
                update_emotion_history(session_id, current_emotion, rag_system.mental_states)
            else:
                update_emotion_history(session_id, current_emotion)
            
            response = adjust_response_for_language(response, language)
            
            # 優先順位付きサジェスチョンを生成（RAGの提案を上書き）
            visitor_info = get_visitor_data(visitor_id) if visitor_id else None
            next_suggestions = generate_prioritized_suggestions(
                session_info, visitor_info, relationship_level_style, language
            )
            
            if not response:
                emit('error', {'message': '回答の生成に失敗しました'})
                return
        except Exception as e:
            print(f"❌ RAGシステムエラー: {e}")
            emit('error', {'message': f'回答の生成中にエラーが発生しました: {str(e)}'})
            return
        
        # 🎯 最終的な感情を使用
        final_emotion = current_emotion
        print(f"🎯 最終的に使用する感情: {final_emotion}")
        
        try:
            audio_data = generate_audio_by_language(
                response, 
                language, 
                emotion_params=final_emotion
            )
        except Exception as e:
            print(f"❌ 音声合成エラー: {e}")
            audio_data = None
        
        end_time = time.time()
        processing_time = end_time - start_time
        
        response_data = {
            'message': response,
            'emotion': final_emotion,
            'audio': audio_data,
            'suggestions': next_suggestions,
            'language': language,
            'cached': False,
            'processing_time': processing_time,
            'voice_engine': 'coe_font' if use_coe_font and language == 'ja' else 'openai_tts',
            'currentTopic': current_topic,
            'relationshipLevel': relationship_level_style,
            'mentalState': session_info['mental_state']  # 🎯 精神状態も送信
        }
        
        print(f"📤 通常処理応答送信完了 - 感情: {final_emotion}, 処理時間: {processing_time:.3f}秒")
        emit('response', response_data)
        
    except Exception as e:
        print(f"❌ メッセージ処理エラー: {e}")
        import traceback
        traceback.print_exc()
        emit('error', {'message': f'メッセージの処理中にエラーが発生しました: {str(e)}'})

# 音声メッセージハンドラー（感情履歴対応）
@socketio.on('audio_message')
def handle_audio_message(data):
    start_time = time.time()
    
    try:
        session_id = request.sid
        session_info = get_session_data(session_id)
        language = session_info['language']
        
        audio_data = data.get('audio')
        visitor_id = data.get('visitorId')
        conversation_history = data.get('conversationHistory', [])
        interaction_count = data.get('interactionCount', 0)
        relationship_level_style = data.get('relationshipLevel', 'formal')
        
        if not audio_data:
            emit('error', {'message': '音声データが受信できませんでした'})
            return

        # 音声認識
        text = speech_processor.transcribe_audio(audio_data, language=language[:2])
        if not text:
            emit('error', {'message': '音声認識に失敗しました'})
            return

        emit('transcription', {'text': text})
        
        # 訪問者IDと会話履歴を更新
        if visitor_id:
            session_info['visitor_id'] = visitor_id
            session_info['relationship_style'] = relationship_level_style
        session_info['conversation_history'] = conversation_history
        session_info['interaction_count'] = interaction_count
        
        # 質問回数を更新
        question_count = increment_question_count(session_id, visitor_id, text)
        print(f"📊 音声質問回数: {question_count}回目")
        
        # トピック抽出
        current_topic = extract_topic_from_question(text)
        session_info['current_topic'] = current_topic
        
        # 🎯 前回の感情を取得
        previous_emotion = session_info.get('current_emotion', 'neutral')
        
        print(f'🎤 音声認識結果: {text} → 静的キャッシュをチェック中...')
        
        cache_stats['total_requests'] += 1
        
        static_response = get_static_response(text)
        
        if static_response:
            cache_hit_time = time.time()
            processing_time = cache_hit_time - start_time
            
            print(f"🚀 音声→静的キャッシュヒット！ 処理時間: {processing_time:.3f}秒")
            
            cache_stats['cache_hits'] += 1
            estimated_saved_time = 8.0
            cache_stats['total_time_saved'] += estimated_saved_time
            
            emotion = static_response['emotion']
            response = static_response['answer']
            suggestions = static_response.get('suggestions', [])
            
            # 🎯 感情履歴を更新
            update_emotion_history(session_id, emotion, session_info['mental_state'])
            
            # 質問回数に応じて応答を調整
            if question_count > 1:
                if question_count == 2:
                    response = f"あ、さっきも聞かれたね。{response}"
                elif question_count == 3:
                    response = f"また同じ質問？よっぽど気になるんやね〜。{response}"
                elif question_count >= 4:
                    response = f"もう覚えてや〜（笑）でも、もう一回説明するね。{response}"
            
            if language == 'en':
                response = adjust_response_for_language(response, language)
                translated_suggestions = []
                for suggestion in suggestions:
                    translated = adjust_response_for_language(suggestion, language)
                    translated_suggestions.append(translated)
                suggestions = translated_suggestions
            
            try:
                audio_response = generate_audio_by_language(
                    response, 
                    language, 
                    emotion_params=emotion
                )
            except Exception as e:
                print(f"❌ 音声応答の音声合成エラー: {e}")
                audio_response = None
            
            # 優先順位付きサジェスチョンを生成
            visitor_info = get_visitor_data(visitor_id) if visitor_id else None
            suggestions = generate_prioritized_suggestions(
                session_info, visitor_info, relationship_level_style, language
            )
            
            response_data = {
                'message': response,
                'emotion': emotion,
                'audio': audio_response,
                'suggestions': suggestions,
                'language': language,
                'cached': True,
                'processing_time': processing_time,
                'voice_engine': 'coe_font' if use_coe_font and language == 'ja' else 'openai_tts',
                'currentTopic': current_topic,
                'relationshipLevel': relationship_level_style,
                'mentalState': session_info['mental_state']
            }
            
            print(f"⚡ 音声キャッシュ応答送信完了 - 感情: {emotion}")
            emit('response', response_data)
            return
        
        print(f"❌ 音声キャッシュミス → 通常処理")
        cache_stats['cache_misses'] += 1

        try:
            user_emotion = analyze_emotion(text)
            print(f"🎭 音声認識テキストの感情分析結果: {user_emotion}")
        except Exception as e:
            print(f"❌ 感情分析エラー: {e}")
            user_emotion = "neutral"

        # 🎯 文脈プロンプトを生成（関係性レベル付き）
        context_prompt = get_context_prompt(
            conversation_history, 
            question_count,
            relationship_level_style,
            session_info.get('fatigue_mentioned', False)
        )
        
        # RAGシステムで回答とサジェスションを生成（文脈付き）
        response_data_rag = rag_system.answer_with_suggestions(
            text,
            context=context_prompt,
            question_count=question_count,
            relationship_style=relationship_level_style,
            previous_emotion=previous_emotion  # 🎯 前回の感情も渡す
        )
        response = response_data_rag['answer']
        next_suggestions = response_data_rag.get('suggestions', [])
        current_emotion = response_data_rag.get('current_emotion', user_emotion)
        
        # 疲労表現をチェック
        if '疲れ' in response and not session_info.get('fatigue_mentioned', False):
            session_info['fatigue_mentioned'] = True
        
        # 🎯 感情履歴を更新
        if hasattr(rag_system, 'mental_states'):
            update_emotion_history(session_id, current_emotion, rag_system.mental_states)
        else:
            update_emotion_history(session_id, current_emotion)
        
        response = adjust_response_for_language(response, language)
        
        # 優先順位付きサジェスチョンを生成
        visitor_info = get_visitor_data(visitor_id) if visitor_id else None
        next_suggestions = generate_prioritized_suggestions(
            session_info, visitor_info, relationship_level_style, language
        )
        
        audio_response = generate_audio_by_language(
            response, 
            language, 
            emotion_params=current_emotion
        )
        
        end_time = time.time()
        processing_time = end_time - start_time
        
        response_data = {
            'message': response,
            'emotion': current_emotion,
            'audio': audio_response,
            'suggestions': next_suggestions,
            'language': language,
            'cached': False,
            'processing_time': processing_time,
            'voice_engine': 'coe_font' if use_coe_font and language == 'ja' else 'openai_tts',
            'currentTopic': current_topic,
            'relationshipLevel': relationship_level_style,
            'mentalState': session_info['mental_state']
        }
        
        print(f"📤 音声通常処理応答送信完了 - 感情: {current_emotion}, 処理時間: {processing_time:.3f}秒")
        emit('response', response_data)
        
    except Exception as e:
        print(f"❌ 音声メッセージ処理エラー: {e}")
        import traceback
        traceback.print_exc()
        emit('error', {'message': '音声メッセージの処理中にエラーが発生しました'})

@app.context_processor
def inject_data_management_url():
    return {'data_management_url': url_for('data_management')}

@app.errorhandler(404)
def page_not_found(e):
    return render_template('error.html', error_code=404), 404

@app.errorhandler(500)
def internal_server_error(e):
    return render_template('error.html', error_code=500), 500

@app.after_request
def after_request(response):
    response.headers.add('Access-Control-Allow-Origin', '*')
    response.headers.add('Access-Control-Allow-Headers', 'Content-Type,Authorization')
    response.headers.add('Access-Control-Allow-Methods', 'GET,PUT,POST,DELETE,OPTIONS')
    return response

# ============== メインプログラム ==============

if __name__ == '__main__':
    if not os.path.exists(app.config['UPLOAD_FOLDER']):
        os.makedirs(app.config['UPLOAD_FOLDER'])
    
    print(f"\n🚀 会話記憶システム + 関係性レベル + より人間らしい会話実装版サーバー起動")
    print(f"🧠 会話記憶システム: 有効")
    print(f"📊 質問カウントシステム: 有効")
    print(f"💬 文脈認識: 有効")
    print(f"🎯 関係性レベルシステム: 有効")
    print(f"🎭 感情履歴管理: 有効")
    print(f"💭 深層心理システム: 有効")
    print(f"🎵 CoeFont利用可能: {use_coe_font}")
    print(f"✨ 感情分析品質: 改善版（キーワード＋スコアリング＋GPT）")
    print(f"🔍 サジェスチョン優先順位: 有効")
    print(f"🚫 サジェスチョン重複防止: 有効")
    
    print(f"\n📊 === エンドポイント一覧 ===")
    print(f"🏠 メインページ: http://localhost:5000/")
    print(f"📊 統計確認: http://localhost:5000/cache-stats")
    print(f"👥 訪問者統計: http://localhost:5000/visitor-stats")
    print(f"🎭 感情統計: http://localhost:5000/emotion-stats")
    print(f"💭 精神状態: http://localhost:5000/mental-state/<session_id>")
    print(f"==============================\n")
    
    socketio.run(
        app,
        host='0.0.0.0',
        port=int(os.getenv('PORT', 5000)),
        debug=True,
        allow_unsafe_werkzeug=True
    )