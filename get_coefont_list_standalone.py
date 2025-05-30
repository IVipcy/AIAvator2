# get_coefont_list_standalone.py
# 既存のCoeFontClientを使わずに直接実行

import hashlib
import hmac
from datetime import datetime, timezone
import requests
import json
import os
from dotenv import load_dotenv

# .envファイルを読み込む
load_dotenv()

# 環境変数から取得
ACCESS_KEY = os.getenv('COEFONT_ACCESS_KEY')
ACCESS_SECRET = os.getenv('COEFONT_ACCESS_SECRET')

if not ACCESS_KEY or not ACCESS_SECRET:
    print("❌ エラー: .envファイルにCoeFont APIの情報を設定してください")
    print("必要な環境変数:")
    print("  COEFONT_ACCESS_KEY=あなたのアクセスキー")
    print("  COEFONT_ACCESS_SECRET=あなたのアクセスシークレット")
    exit(1)

# 認証情報を生成
date = str(int(datetime.utcnow().replace(tzinfo=timezone.utc).timestamp()))
signature = hmac.new(
    bytes(ACCESS_SECRET, 'utf-8'),
    date.encode('utf-8'),
    hashlib.sha256
).hexdigest()

print("🔍 CoeFont一覧を取得中...")

# API呼び出し
response = requests.get(
    'https://api.coefont.cloud/v2/coefonts/pro',
    headers={
        'Content-Type': 'application/json',
        'Authorization': ACCESS_KEY,
        'X-Coefont-Date': date,
        'X-Coefont-Content': signature
    }
)

# 結果表示
if response.status_code == 200:
    coefonts = response.json()
    print(f"\n✅ {len(coefonts)}個のCoeFontが利用可能です！\n")
    
    # 一覧を表示
    for i, coefont in enumerate(coefonts, 1):
        print(f"{i}. 名前: {coefont['name']}")
        print(f"   ID: {coefont['coefont']}")
        if 'tags' in coefont:
            print(f"   タグ: {', '.join(coefont['tags'])}")
        print(f"   説明: {coefont['description'][:60]}...")
        print("-" * 60)
        
        # 10個表示したら一旦停止
        if i % 10 == 0 and i < len(coefonts):
            input(f"\n[Enter]キーで続きを表示 ({i}/{len(coefonts)})...")
            print()
    
    # JSONファイルに保存
    with open('available_coefonts.json', 'w', encoding='utf-8') as f:
        json.dump(coefonts, f, ensure_ascii=False, indent=2)
    print(f"\n💾 全{len(coefonts)}件のデータを available_coefonts.json に保存しました")
    
    # ID一覧だけを別ファイルに保存
    id_list = {cf['name']: cf['coefont'] for cf in coefonts}
    with open('coefont_id_list.json', 'w', encoding='utf-8') as f:
        json.dump(id_list, f, ensure_ascii=False, indent=2)
    print("💾 ID一覧を coefont_id_list.json に保存しました")
    
else:
    print(f"❌ エラー: {response.status_code}")
    print(response.text)