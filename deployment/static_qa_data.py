# static_qa_data.py - 静的Q&Aキャッシュシステム（サジェスチョン優先順位対応版）

import re
from typing import Dict, List, Optional, Tuple

# サジェスチョンの優先順位カテゴリ
SUGGESTION_CATEGORIES = {
    "overview": {  # 概要（最優先）
        "priority": 1,
        "suggestions": [
            "京友禅とは何ですか？",
            "京友禅の歴史について教えて",
            "他の染色技法との違いは？",
            "京友禅の特徴を教えて",
            "なぜ京都で発展したの？"
        ]
    },
    "process": {  # 制作工程（中優先）
        "priority": 2,
        "suggestions": [
            "制作工程の概要を教えて",
            "のりおき工程について詳しく",
            "一番難しい工程は？",
            "制作期間はどれくらい？",
            "使う道具について教えて"
        ]
    },
    "personal": {  # 職人個人の話（低優先）
        "priority": 3,
        "suggestions": [
            "職人になったきっかけは？",
            "15年間で大変だったことは？",
            "1日のスケジュールは？",
            "やりがいを感じる瞬間は？",
            "休日は何してる？"
        ]
    }
}

# 京友禅職人版の静的Q&Aペア（文脈認識対応）
STATIC_QA_PAIRS = [
    # 基本的な挨拶
    {
        "patterns": ["こんにちは", "はじめまして", "初めまして", "はろー", "ハロー", "hello"],
        "answer": "こんにちは〜！京友禅職人の吉田麗です。友禅染のことなら何でも聞いてくださいね。",
        "emotion": "happy",
        "suggestions": ["京友禅とは何ですか？", "京友禅の歴史について教えて", "他の染色技法との違いは？"],
        "category": "overview"
    },
    {
        "patterns": ["おはよう", "おはようございます", "good morning"],
        "answer": "おはようございます〜！今日も朝から制作頑張ってるで。",
        "emotion": "happy",
        "suggestions": ["京友禅の特徴を教えて", "なぜ京都で発展したの？", "京友禅の魅力は？"],
        "category": "overview"
    },
    {
        "patterns": ["元気", "元気？", "調子どう", "調子は"],
        "answer": "めっちゃ元気やで！朝から晩まで糊置きしてても、好きな仕事やから苦にならへんねん。",
        "emotion": "happy",
        "suggestions": ["京友禅とは何ですか？", "のりおきって何？", "職人の仕事について"],
        "category": "overview"
    },
    
    # 京友禅の基本（概要）
    {
        "patterns": ["京友禅とは", "京友禅って何", "京友禅について", "きょうゆうぜん"],
        "answer": "京友禅はね、糸目糊っていう特別な糊を使って模様を描く染色技法やねん。まるで絵筆で紙に絵を描くように、布に美しい模様を染めていくんよ。防染技術の一つで、美術品みたいな着物を作れるのが特徴やで。",
        "emotion": "happy",
        "suggestions": ["糸目糊について詳しく", "他の友禅との違いは？", "制作工程を教えて"],
        "category": "process"
    },
    {
        "patterns": ["歴史", "いつから", "起源", "由来"],
        "answer": "京友禅の歴史は17世紀まで遡るねん。京都の扇絵師・宮崎友禅斎さんが始めた技法で、その名前から『友禅』って呼ばれるようになったんよ。まるで料理のレシピみたいに、代々受け継がれてきた技術やねん。",
        "emotion": "neutral",
        "suggestions": ["宮崎友禅斎について", "なぜ京都で発展したの？", "現代の京友禅は？"],
        "category": "overview"
    },
    
    # 技術的な質問（プロセス）
    {
        "patterns": ["のりおき", "糊置き", "ノリオキ"],
        "answer": "のりおきは友禅染の中で一番重要な工程やねん。ケーキのデコレーションで生クリーム絞るみたいに、糊で模様の輪郭を描くんよ。これがうまくいかんと全部台無しになるから、めっちゃ緊張するで〜。",
        "emotion": "neutral",
        "suggestions": ["のりおきの難しさは？", "使う道具について", "失敗したらどうなる？"],
        "category": "process"
    },
    {
        "patterns": ["工程", "制作過程", "作り方", "手順"],
        "answer": "まずデザインから始まって、全部で10工程あるねん。料理で言うたら、レシピ考えて、下ごしらえして、調理して、盛り付けるまでの流れみたいなもんやね。デザイン→下絵→のりおき→マスキング→地染め...って続くんやけど、一つ一つが大事やで。",
        "emotion": "neutral",
        "suggestions": ["各工程の詳細", "一番難しい工程は？", "制作期間はどれくらい？"],
        "category": "process"
    },
    {
        "patterns": ["プリントとの違い", "手描きの良さ", "機械との違い"],
        "answer": "手描友禅は裏までちゃんと色が通るねん。プリンターで印刷した紙と、絵の具で描いた絵の違いみたいなもんやね。あと、グラデーションの長さとか、その場で調整できるのが大きな違いやで。機械では出せへん深みがあるんよ。",
        "emotion": "happy",
        "suggestions": ["グラデーション技法について", "色の深みとは？", "手描きの価値は？"],
        "category": "process"
    },
    
    # 職人について（個人）
    {
        "patterns": ["職人になった", "きっかけ", "なぜ職人", "転職"],
        "answer": "実は元々会社員やってん。でも自宅で仕事したくてね。大学で染色勉強してたから、縁があって始めたんよ。もう15年になるわ〜。",
        "emotion": "happy",
        "suggestions": ["会社員時代の話", "15年間で大変だったこと", "職人の良さは？"],
        "category": "personal"
    },
    {
        "patterns": ["何年", "経験", "キャリア", "ベテラン"],
        "answer": "友禅職人になって15年やで。最初は全然うまくいかんかったけど、今では賞もらったりするようになったわ。継続は力なりやね〜。",
        "emotion": "happy",
        "suggestions": ["賞をもらった作品について", "15年間の成長", "これからの目標は？"],
        "category": "personal"
    },
    
    # 感情的な反応
    {
        "patterns": ["ありがとう", "ありがと", "感謝", "thanks", "thank you"],
        "answer": "こちらこそ、友禅に興味持ってくれてありがとう！",
        "emotion": "happy",
        "suggestions": ["京友禅の魅力は？", "体験教室について", "作品を見たい"],
        "category": "overview"
    },
    {
        "patterns": ["すごい", "素晴らしい", "かっこいい", "尊敬"],
        "answer": "そんなん言われたら照れるわ〜。でも嬉しいで、おおきに！職人冥利に尽きるってやつやね。",
        "emotion": "happy",
        "suggestions": ["職人のやりがいは？", "嬉しかった瞬間", "お客様の反応は？"],
        "category": "personal"
    },
    {
        "patterns": ["大変", "つらい", "しんどい", "難しい"],
        "answer": "確かに大変な時もあるけどな、好きでやってることやから。お客さんの「きれい」って言葉聞いたら、全部吹っ飛ぶで〜。",
        "emotion": "neutral",
        "suggestions": ["一番大変な時期", "どうやって乗り越える？", "支えになるものは？"],
        "category": "personal"
    },
    
    # 現代的な話題
    {
        "patterns": ["ゲーム", "コラボ", "現代", "新しい"],
        "answer": "最近はゲームとコラボしたり、新しいことやってるで。伝統守りながら、若い人にも興味持ってもらえるように工夫してるねん。",
        "emotion": "happy",
        "suggestions": ["どんなゲームとコラボ？", "若い人の反応は？", "今後の展開は？"],
        "category": "overview"
    },
    {
        "patterns": ["SNS", "インスタ", "発信", "ネット"],
        "answer": "SNSでも発信してる職人さん増えてきたわ。私もたまに作品の写真上げたりしてるで。伝統工芸も時代に合わせて変わらなあかんね。",
        "emotion": "neutral",
        "suggestions": ["SNSの反応は？", "どんな写真を投稿？", "ネットの影響は？"],
        "category": "overview"
    },
    
    # 着物文化
    {
        "patterns": ["着物の手入れ", "管理", "保管", "洗濯"],
        "answer": "確かに洗濯とか保管とか、ちょっと手間かかるね。でも大切に扱えば、代々受け継げるもんやで。最近は手入れしやすい着物も増えてきてるし。",
        "emotion": "neutral",
        "suggestions": ["正しい保管方法", "クリーニングについて", "普段着として着るには？"],
        "category": "overview"
    },
    {
        "patterns": ["着付け", "着る", "普段着", "日常"],
        "answer": "着付けも慣れたら楽しいもんやで。最初は難しいけど、YouTubeとかで勉強できるし。普段着として着物着る人も増えてきて嬉しいわ〜。",
        "emotion": "happy",
        "suggestions": ["初心者向けの着物", "簡単な着付け方法", "普段着におすすめは？"],
        "category": "overview"
    },
    
    # 将来について
    {
        "patterns": ["後継者", "継承", "若い職人", "未来"],
        "answer": "後継者不足は深刻な課題やけど、最近は若い職人さんも増えてきてるで。私も技術継承のために、できることやっていきたいと思ってるねん。",
        "emotion": "neutral",
        "suggestions": ["どうやって技術を伝える？", "若い職人への期待", "業界の未来は？"],
        "category": "overview"
    },
    {
        "patterns": ["目標", "夢", "これから", "将来"],
        "answer": "京刺繍も勉強中でね、12年後には伝統工芸士の資格取りたいねん。友禅染を知らない人にも、この魅力を伝えていきたいわ。",
        "emotion": "happy",
        "suggestions": ["京刺繍について", "なぜ刺繍も？", "伝えたい魅力とは？"],
        "category": "personal"
    },
    
    # 日常生活
    {
        "patterns": ["1日", "スケジュール", "日課", "ルーティン"],
        "answer": "朝8時に起きて、10時から作業開始。お昼挟んで夕方まで作業して、調子良い時は夜中まで続けることもあるで。マイペースでできるのが良いねん。",
        "emotion": "neutral",
        "suggestions": ["休憩時間は？", "夜中まで作業する理由", "休日はある？"],
        "category": "personal"
    },
    {
        "patterns": ["趣味", "プライベート", "休日", "オフ"],
        "answer": "仕事が趣味みたいなもんやけどな〜。でも時々美術館行ったり、他の工芸品見に行ったりするで。インプットも大事やからね。",
        "emotion": "happy",
        "suggestions": ["好きな美術館", "影響を受けた作品", "リフレッシュ方法"],
        "category": "personal"
    }
]

# 複数回質問された場合の応答バリエーション
REPEAT_RESPONSES = {
    2: {
        "prefix": ["あ、さっきも聞かれたね。", "これさっきも説明したけど、", "また同じ質問やね。"],
        "suffix": ["もう一回説明するね。", "大事なことやもんね。", "気になるんやね〜。"]
    },
    3: {
        "prefix": ["また同じ質問？", "よっぽど気になるんやね〜。", "3回目やで〜。"],
        "suffix": ["何回でも説明するで。", "覚えにくいかな？", "大事なことやもんね〜。"]
    },
    4: {
        "prefix": ["もう覚えてや〜（笑）", "何回目やったっけ？", "ほんまに気になるんやね！"],
        "suffix": ["でも、もう一回説明するね。", "メモ取った方がええかも？", "最後にするで〜（笑）"]
    }
}

def normalize_query(query: str) -> str:
    """クエリを正規化（大文字小文字、記号を統一）"""
    # 小文字化
    query = query.lower()
    # 全角を半角に
    query = query.replace('？', '?').replace('！', '!').replace('。', '.').replace('、', ',')
    # 余分なスペースを削除
    query = re.sub(r'\s+', ' ', query).strip()
    return query

def find_matching_qa(query: str) -> Optional[Dict]:
    """クエリにマッチするQ&Aを検索"""
    normalized_query = normalize_query(query)
    
    for qa in STATIC_QA_PAIRS:
        for pattern in qa["patterns"]:
            normalized_pattern = normalize_query(pattern)
            # 完全一致
            if normalized_query == normalized_pattern:
                return qa
            # 部分一致（パターンがクエリに含まれる）
            if normalized_pattern in normalized_query:
                return qa
            # クエリがパターンに含まれる（短い質問への対応）
            if normalized_query in normalized_pattern:
                return qa
    
    return None

def adjust_response_for_repeat(response: str, question_count: int) -> str:
    """質問回数に応じて応答を調整"""
    if question_count <= 1:
        return response
    
    if question_count in REPEAT_RESPONSES:
        import random
        prefix = random.choice(REPEAT_RESPONSES[question_count]["prefix"])
        suffix = random.choice(REPEAT_RESPONSES[question_count]["suffix"])
        
        # 応答を調整
        if question_count == 2:
            return f"{prefix}{response}{suffix}"
        elif question_count == 3:
            # 少し短めに
            short_response = response.split('。')[0] + '。'
            return f"{prefix}{short_response}{suffix}"
        elif question_count >= 4:
            # さらに短く
            very_short = response.split('、')[0] + 'やで。'
            return f"{prefix}{very_short}{suffix}"
    
    return response

def get_prioritized_suggestions(category: str = "overview", selected_suggestions: List[str] = None) -> List[str]:
    """優先順位に基づいてサジェスチョンを取得（重複排除）"""
    if selected_suggestions is None:
        selected_suggestions = []
    
    # カテゴリごとのサジェスチョンを取得
    suggestions = []
    
    # 指定されたカテゴリから開始
    categories = ["overview", "process", "personal"]
    start_idx = categories.index(category) if category in categories else 0
    
    # 優先順位に従ってサジェスチョンを収集
    for i in range(start_idx, len(categories)):
        cat = categories[i]
        if cat in SUGGESTION_CATEGORIES:
            available_suggestions = [s for s in SUGGESTION_CATEGORIES[cat]["suggestions"] 
                                   if s not in selected_suggestions]
            if available_suggestions:
                suggestions.extend(available_suggestions[:3])
                if len(suggestions) >= 3:
                    break
    
    return suggestions[:3]

def get_static_response(query: str, question_count: int = 1, selected_suggestions: List[str] = None) -> Optional[Dict]:
    """静的キャッシュから応答を取得（会話記憶・優先順位対応）"""
    qa = find_matching_qa(query)
    
    if qa:
        # 基本応答を取得
        response = qa["answer"]
        
        # 質問回数に応じて応答を調整
        adjusted_response = adjust_response_for_repeat(response, question_count)
        
        # カテゴリに基づいて優先順位付きサジェスチョンを取得
        category = qa.get("category", "overview")
        suggestions = get_prioritized_suggestions(category, selected_suggestions)
        
        return {
            "answer": adjusted_response,
            "emotion": qa["emotion"],
            "suggestions": suggestions,
            "cached": True,
            "question_count": question_count
        }
    
    return None

def get_contextual_suggestions(current_topic: str, conversation_history: List[Dict], selected_suggestions: List[str] = None) -> List[str]:
    """会話履歴に基づいて文脈に応じたサジェスチョンを生成（重複排除）"""
    if selected_suggestions is None:
        selected_suggestions = []
    
    # 最近の話題を分析
    recent_topics = []
    for msg in conversation_history[-5:]:
        content = msg.get('content', '').lower()
        if '友禅' in content:
            recent_topics.append('yuzen')
        if 'のりおき' in content or '糊' in content:
            recent_topics.append('norioki')
        if '職人' in content:
            recent_topics.append('craftsman')
    
    # 話題に応じたサジェスチョン
    topic_suggestions = {
        'yuzen': ["制作期間はどれくらい？", "他の染物との違いは？", "値段はどれくらい？"],
        'norioki': ["失敗したらどうなる？", "道具は特別なもの？", "一日何本くらい作業する？"],
        'craftsman': ["休日はある？", "収入は安定してる？", "弟子は取ってる？"]
    }
    
    suggestions = []
    # 最近の話題に基づいてサジェスチョンを選択
    for topic in recent_topics:
        if topic in topic_suggestions:
            available_suggestions = [s for s in topic_suggestions[topic] 
                                   if s not in selected_suggestions]
            suggestions.extend(available_suggestions)
    
    # 重複を削除して最初の3つを返す
    unique_suggestions = list(dict.fromkeys(suggestions))
    return unique_suggestions[:3]

# デバッグ用関数
def test_static_qa():
    """静的Q&Aシステムのテスト"""
    test_queries = [
        ("こんにちは", 1, []),
        ("京友禅について教えて", 1, []),
        ("京友禅について教えて", 2, ["京友禅とは何ですか？"]),
        ("のりおきって何？", 1, ["京友禅とは何ですか？", "京友禅の歴史について教えて"]),
        ("ありがとう！", 1, []),
    ]
    
    print("=== 静的Q&Aシステムテスト ===")
    for query, count, selected in test_queries:
        print(f"\nQuery: '{query}' (回数: {count}, 選択済み: {selected})")
        response = get_static_response(query, count, selected)
        if response:
            print(f"Answer: {response['answer']}")
            print(f"Emotion: {response['emotion']}")
            print(f"Suggestions: {response['suggestions']}")
        else:
            print("No match found")
    print("\n=== テスト完了 ===")

# メイン実行
if __name__ == "__main__":
    test_static_qa()