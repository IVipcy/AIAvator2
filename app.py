# app.py - 会話記憶システム + 関係性レベル + より人間らしい会話実装版（京友禅職人版）
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
from supabase import create_client, Client
from modules.rag_system import RAGSystem
from modules.speech_processor import SpeechProcessor
from modules.openai_tts_client import OpenAITTSClient
from modules.coe_font_client import CoeFontClient
from modules.emotion_voice_params import get_emotion_voice_params
from openai import OpenAI

# 静的Q&Aシステム
from static_qa_data import get_static_response, STATIC_QA_PAIRS

# 設定ファイルの読み込み
from config import Config

# Flaskアプリケーションの初期化
app = Flask(__name__)
app.config.from_object(Config)

# Supabaseクライアントの初期化
supabase: Client = create_client(Config.SUPABASE_URL, Config.SUPABASE_KEY)

# Socket.IOの設定
socketio = SocketIO(
    app,
    cors_allowed_origins="*",
    async_mode='threading',
    ping_timeout=25,
    ping_interval=10,
    logger=True,
    engineio_logger=True,
    path='socket.io',
    max_http_buffer_size=5e7  # 50MB
)

# 一時アップロードディレクトリの作成
os.makedirs(Config.UPLOAD_FOLDER, exist_ok=True)

# インスタンスの初期化
rag_system = RAGSystem()
speech_processor = SpeechProcessor()
tts_client = OpenAITTSClient()
coe_font_client = CoeFontClient()

# キャッシュ統計情報
cache_stats = {
    'total_requests': 0,
    'cache_hits': 0,
    'cache_misses': 0,
    'total_time_saved': 0.0,
    'coe_font_requests': 0,
    'openai_tts_requests': 0
}

# セッションデータの一時保存（メモリキャッシュ）
session_data = {}

def generate_ai_response(message: str, conversation_history: List[Dict]) -> str:
    """AIの応答を生成"""
    try:
        client = OpenAI()
        messages = []
        
        # システムメッセージを追加
        messages.append({
            "role": "system",
            "content": "あなたは京友禅の職人で、手描友禅を15年やっているREIです。"
        })
        
        # 会話履歴を追加
        for conv in conversation_history:
            messages.append({
                "role": conv["role"],
                "content": conv["content"]
            })
        
        # ユーザーの新しいメッセージを追加
        messages.append({
            "role": "user",
            "content": message
        })
        
        # GPT-4で応答を生成
        response = client.chat.completions.create(
            model="gpt-4",
            messages=messages,
            temperature=0.7,
            max_tokens=500
        )
        
        return response.choices[0].message.content
        
    except Exception as e:
        print(f"AI応答生成エラー: {e}")
        return "申し訳ありません。応答の生成中にエラーが発生しました。"

# ファイルアップロード処理の関数
def save_uploaded_file(file):
    """ファイルをSupabaseストレージにアップロード"""
    if not file:
        return None
        
    filename = secure_filename(file.filename)
    temp_path = os.path.join(Config.UPLOAD_FOLDER, filename)
    
    try:
        # 一時ファイルとして保存
        file.save(temp_path)
        
        # Supabaseストレージにアップロード
        storage_path = f'uploads/{filename}'
        with open(temp_path, 'rb') as f:
            supabase.storage.from_('uploads').upload(storage_path, f)
            
        # Supabaseにメタデータを保存
        file_data = {
            'filename': filename,
            'storage_path': storage_path,
            'file_type': file.content_type,
            'size': os.path.getsize(temp_path),
            'uploaded_at': datetime.utcnow().isoformat()
        }
        result = supabase.table('uploaded_files').insert(file_data).execute()
        
        # 一時ファイルを削除
        os.remove(temp_path)
        
        return result.data[0] if result.data else None
        
    except Exception as e:
        print(f"ファイルアップロードエラー: {e}")
        if os.path.exists(temp_path):
            os.remove(temp_path)
        return None

@app.route('/')
def index():
    """メインページ"""
    session_id = str(uuid.uuid4())
    session['session_id'] = session_id
    
    # Supabaseにセッション情報を保存
    session_data = {
        'id': session_id,
        'created_at': datetime.utcnow().isoformat(),
        'last_activity': datetime.utcnow().isoformat()
    }
    supabase.table('sessions').insert(session_data).execute()
    
    return render_template('index.html')

@app.route('/api/chat', methods=['POST'])
def chat():
    """チャットAPIエンドポイント"""
    data = request.get_json()
    message = data.get('message', '')
    session_id = session.get('session_id')
    
    if not session_id:
        return jsonify({'error': 'Invalid session'}), 400
    
    # 会話履歴を取得
    result = supabase.table('conversations').select('*').eq('session_id', session_id).order('created_at').execute()
    conversation_history = result.data if result.data else []
    
    # AIの応答を生成
    response = generate_ai_response(message, conversation_history)
    
    # 会話履歴を保存
    conversation_data = {
        'session_id': session_id,
        'role': 'user',
        'content': message,
        'created_at': datetime.utcnow().isoformat()
    }
    supabase.table('conversations').insert(conversation_data).execute()
    
    conversation_data = {
        'session_id': session_id,
        'role': 'assistant',
        'content': response,
        'created_at': datetime.utcnow().isoformat()
    }
    supabase.table('conversations').insert(conversation_data).execute()
    
    return jsonify({'response': response})

@app.route('/api/upload', methods=['POST'])
def upload_file():
    """ファイルアップロードエンドポイント"""
    if 'file' not in request.files:
        return jsonify({'error': 'No file part'}), 400
        
    file = request.files['file']
    if file.filename == '':
        return jsonify({'error': 'No selected file'}), 400
        
    uploaded_file = save_uploaded_file(file)
    if not uploaded_file:
        return jsonify({'error': 'Failed to upload file'}), 500
        
    return jsonify({
        'message': 'File uploaded successfully',
        'filename': uploaded_file.get('filename'),
        'storage_path': uploaded_file.get('storage_path')
    })

if __name__ == '__main__':
    print(f"\n🚀 サーバーレス対応版サーバー起動")
    socketio.run(
        app,
        host='0.0.0.0',
        port=int(os.getenv('PORT', 5000)),
        debug=True,
        allow_unsafe_werkzeug=True
    )