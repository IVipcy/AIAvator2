import os
from datetime import timedelta
from dotenv import load_dotenv

load_dotenv()

class Config:
    # Flask設定
    SECRET_KEY = os.getenv('SECRET_KEY', 'your-secret-key')
    PERMANENT_SESSION_LIFETIME = timedelta(days=1)
    DEBUG = os.getenv('DEBUG', 'False').lower() == 'true'
    
    # Supabase設定
    SUPABASE_URL = os.getenv('SUPABASE_URL')
    SUPABASE_KEY = os.getenv('SUPABASE_KEY')
    
    # データベース設定
    SQLALCHEMY_DATABASE_URI = os.getenv('DATABASE_URL', 'sqlite:///database.db')
    SQLALCHEMY_TRACK_MODIFICATIONS = False
    
    # アップロード設定
    UPLOAD_FOLDER = os.getenv('UPLOAD_FOLDER', 'uploads')
    MAX_CONTENT_LENGTH = 16 * 1024 * 1024  # 16MB in bytes
    ALLOWED_EXTENSIONS = {'txt', 'pdf'}
    
    # RAG設定
    CHROMA_DB_PATH = os.getenv('CHROMA_DB_PATH', 'data/chroma_db')
    
    # OpenAIの設定
    OPENAI_API_KEY = os.getenv('OPENAI_API_KEY')
    
    # CoeFontの設定
    COE_FONT_API_KEY = os.getenv('COE_FONT_API_KEY')
    COE_FONT_SPEAKER_ID = os.getenv('COE_FONT_SPEAKER_ID') 