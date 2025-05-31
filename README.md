# AI Voice Conversion System

## 概要
このシステムは、AIを活用した音声変換と対話システムを組み合わせたアプリケーションです。

## 機能
- リアルタイム音声変換
- 感情分析に基づく応答生成
- データ管理インターフェース
- Supabaseを使用したストレージ管理
- RAGシステムによる知識ベース

## セットアップ
1. 必要なパッケージのインストール:
```bash
pip install -r requirements.txt
```

2. 環境変数の設定:
- `.env`ファイルを作成し、必要な環境変数を設定

3. データベースの初期化:
```bash
flask db init
flask db migrate
flask db upgrade
```

4. アプリケーションの起動:
```bash
flask run
```

## 必要な環境変数
- SECRET_KEY: Flaskセッション用の秘密鍵
- SUPABASE_URL: SupabaseのURL
- SUPABASE_KEY: Supabaseのアクセスキー
- OPENAI_API_KEY: OpenAIのAPIキー
- COE_FONT_API_KEY: CoeFontのAPIキー
- COE_FONT_SPEAKER_ID: CoeFontのスピーカーID

## ディレクトリ構造
```
.
├── app.py
├── config.py
├── modules/
│   └── rag_system.py
├── static/
│   └── css/
│       └── data_management.css
├── templates/
│   └── data_management.html
├── uploads/
├── data/
│   └── chroma_db/
├── .env
├── requirements.txt
└── README.md
``` 