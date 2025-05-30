# test_rag.py
from modules.rag_system import RAGSystem
import time

def test_rag():
    print("RAGシステムのテストを開始します...")
    
    # RAGシステムの初期化
    rag = RAGSystem()
    
    print("\nドキュメントの処理を開始します...")
    success = rag.process_documents()
    
    if success:
        print("ドキュメントの処理が完了しました！\n")
        
        # テスト質問のリスト
        test_questions = [
            "あなたはどんな性格ですか？",
            "プログラミングについて教えてください",
            "悲しい気持ちです",
            "このアプリケーションの特徴を教えてください",
            "機密情報の取り扱いについてどう考えていますか？"
        ]
        
        # 各質問でテスト
        for question in test_questions:
            print("-" * 50)
            print(f"質問: {question}")
            print("-" * 50)
            
            # 回答を取得
            response = rag.answer_question(question)
            print(f"回答: {response}\n")
            
            # 少し待機（API制限を考慮）
            time.sleep(1)
            
        print("テストが完了しました！")
    else:
        print("ドキュメントの処理中にエラーが発生しました。")

if __name__ == "__main__":
    test_rag()