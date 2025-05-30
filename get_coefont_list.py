#!/usr/bin/env python3
# -*- coding: utf-8 -*-

import os
from dotenv import load_dotenv
from modules.coe_font_client import CoeFontClient

def main():
    load_dotenv()
    
    print("=== CoeFont利用可能音声一覧 ===")
    
    # CoeFontクライアントの初期化
    client = CoeFontClient()
    
    if not client.is_available():
        print("❌ CoeFont設定が不完全です。.envファイルで以下を確認してください：")
        print("  - COEFONT_ACCESS_KEY")
        print("  - COEFONT_ACCESS_SECRET") 
        print("  - COEFONT_VOICE_ID")
        return
    
    print(f"✅ CoeFont利用可能: True\n")
    
    # 利用可能なCoeFont一覧を取得
    print("📋 利用可能なCoeFont一覧を取得中...")
    coefonts = client.get_available_coefonts()
    
    if not coefonts:
        print("❌ CoeFont一覧の取得に失敗しました")
        return
    
    print(f"\n🎵 取得成功: {len(coefonts)} 個のCoeFont\n")
    print("=" * 80)
    
    # 現在設定されているIDをチェック
    current_id = os.getenv('COEFONT_VOICE_ID')
    current_voice_found = False
    
    for i, coefont in enumerate(coefonts, 1):
        coefont_id = coefont.get('coefont', 'unknown')
        name = coefont.get('name', 'Unknown')
        description = coefont.get('description', 'No description')
        tags = coefont.get('tags', [])
        
        # 現在設定されているIDかチェック
        is_current = coefont_id == current_id
        if is_current:
            current_voice_found = True
            marker = " ⭐ [現在使用中]"
        else:
            marker = ""
        
        print(f"{i:2}. {name}{marker}")
        print(f"    ID: {coefont_id}")
        print(f"    説明: {description[:100]}{'...' if len(description) > 100 else ''}")
        if tags:
            print(f"    タグ: {', '.join(tags[:5])}")
        print("-" * 80)
    
    print(f"\n📊 合計: {len(coefonts)} 個のCoeFont")
    
    # 現在の設定状況を表示
    print(f"\n🔧 現在の設定:")
    print(f"   COEFONT_VOICE_ID: {current_id}")
    
    if current_voice_found:
        print("   ✅ 現在のIDは有効です")
    else:
        print("   ❌ 現在のIDは一覧に見つかりません")
        print("   💡 上記の一覧から有効なIDを選んで.envファイルを更新してください")
    
    print("\n💡 使用方法:")
    print("   1. 上記一覧から使いたいCoeFontのIDをコピー")
    print("   2. .envファイルのCOEFONT_VOICE_IDを更新")
    print("   3. アプリケーションを再起動")

if __name__ == "__main__":
    main() 