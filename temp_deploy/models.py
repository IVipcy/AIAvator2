from datetime import datetime
from flask_sqlalchemy import SQLAlchemy
from sqlalchemy.dialects.postgresql import JSON

db = SQLAlchemy()

class Session(db.Model):
    __tablename__ = 'sessions'
    
    id = db.Column(db.String(36), primary_key=True)  # UUID
    visitor_id = db.Column(db.String(36), db.ForeignKey('visitors.id'))
    created_at = db.Column(db.DateTime, default=datetime.utcnow)
    last_activity = db.Column(db.DateTime, default=datetime.utcnow)
    conversation_history = db.Column(JSON)
    language = db.Column(db.String(10), default='ja')
    relationship_style = db.Column(db.String(20), default='formal')
    
    emotion_history = db.relationship('EmotionHistory', backref='session', lazy=True)

class Visitor(db.Model):
    __tablename__ = 'visitors'
    
    id = db.Column(db.String(36), primary_key=True)  # UUID
    first_visit = db.Column(db.DateTime, default=datetime.utcnow)
    last_visit = db.Column(db.DateTime, default=datetime.utcnow)
    visit_count = db.Column(db.Integer, default=1)
    conversation_count = db.Column(db.Integer, default=0)
    preferences = db.Column(JSON)
    
    sessions = db.relationship('Session', backref='visitor', lazy=True)

class EmotionHistory(db.Model):
    __tablename__ = 'emotion_history'
    
    id = db.Column(db.Integer, primary_key=True)
    session_id = db.Column(db.String(36), db.ForeignKey('sessions.id'))
    emotion = db.Column(db.String(20))
    confidence = db.Column(db.Float)
    timestamp = db.Column(db.DateTime, default=datetime.utcnow)
    mental_state = db.Column(JSON)

class QuestionCount(db.Model):
    __tablename__ = 'question_counts'
    
    id = db.Column(db.Integer, primary_key=True)
    session_id = db.Column(db.String(36), db.ForeignKey('sessions.id'))
    visitor_id = db.Column(db.String(36), db.ForeignKey('visitors.id'))
    question = db.Column(db.String(500))
    count = db.Column(db.Integer, default=1)
    last_asked = db.Column(db.DateTime, default=datetime.utcnow)

class UploadedFile(db.Model):
    __tablename__ = 'uploaded_files'
    
    id = db.Column(db.Integer, primary_key=True)
    filename = db.Column(db.String(255))
    storage_path = db.Column(db.String(500))  # Supabaseのストレージパス
    uploaded_at = db.Column(db.DateTime, default=datetime.utcnow)
    file_type = db.Column(db.String(50))
    size = db.Column(db.Integer) 