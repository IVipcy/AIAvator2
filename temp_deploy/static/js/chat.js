// chat.js - 3Dアバターチャットシステム（完全修正版）

(function() {
    'use strict';
    
    // ====== 🧠 訪問者管理システム ======
    class VisitorManager {
        constructor() {
            this.visitorId = this.getOrCreateVisitorId();
            this.visitData = this.loadVisitData();
            this.updateVisitData();
        }
        
        getOrCreateVisitorId() {
            let visitorId = localStorage.getItem('visitor_id');
            if (!visitorId) {
                visitorId = 'visitor_' + Math.random().toString(36).substring(2, 9) + '_' + Date.now();
                localStorage.setItem('visitor_id', visitorId);
                console.log('🆕 新規訪問者ID生成:', visitorId);
            } else {
                console.log('🔄 既存訪問者ID:', visitorId);
            }
            return visitorId;
        }
        
        loadVisitData() {
            const savedData = localStorage.getItem('visit_data');
            if (savedData) {
                return JSON.parse(savedData);
            }
            return {
                firstVisit: new Date().toISOString(),
                visitCount: 0,
                lastVisit: null,
                totalConversations: 0,
                topicsDiscussed: [],
                questionCounts: {},
                relationshipLevel: 0,
                selectedSuggestions: []  // 🎯 選択済みサジェスチョンを記録
            };
        }
        
        updateVisitData() {
            this.visitData.visitCount++;
            this.visitData.lastVisit = new Date().toISOString();
            this.saveVisitData();
            console.log('📊 訪問データ更新:', this.visitData);
        }
        
        saveVisitData() {
            localStorage.setItem('visit_data', JSON.stringify(this.visitData));
        }
        
        incrementQuestionCount(question) {
            const normalizedQuestion = this.normalizeQuestion(question);
            if (!this.visitData.questionCounts[normalizedQuestion]) {
                this.visitData.questionCounts[normalizedQuestion] = 0;
            }
            this.visitData.questionCounts[normalizedQuestion]++;
            this.saveVisitData();
            return this.visitData.questionCounts[normalizedQuestion];
        }
        
        normalizeQuestion(question) {
            // 質問を正規化（簡易的な実装）
            return question.toLowerCase()
                .replace(/[？?。、！!]/g, '')
                .replace(/\s+/g, '')
                .trim();
        }
        
        addTopic(topic) {
            if (!this.visitData.topicsDiscussed.includes(topic)) {
                this.visitData.topicsDiscussed.push(topic);
                this.saveVisitData();
            }
        }
        
        updateRelationshipLevel(level) {
            this.visitData.relationshipLevel = level;
            this.saveVisitData();
        }
        
        incrementConversationCount() {
            this.visitData.totalConversations++;
            this.saveVisitData();
            return this.visitData.totalConversations;
        }
        
        // 🎯 選択済みサジェスチョンを記録
        addSelectedSuggestion(suggestion) {
            if (!this.visitData.selectedSuggestions.includes(suggestion)) {
                this.visitData.selectedSuggestions.push(suggestion);
                // 最大100個まで保持
                if (this.visitData.selectedSuggestions.length > 100) {
                    this.visitData.selectedSuggestions.shift();
                }
                this.saveVisitData();
            }
        }
        
        getSelectedSuggestions() {
            return this.visitData.selectedSuggestions || [];
        }
    }
    
    // ====== 🎯 関係性レベル管理システム ======
    class RelationshipManager {
        constructor() {
            this.levels = [
                { level: 0, minConversations: 0, maxConversations: 0, name: "初対面", nameEn: "First Meeting", style: "formal", description: "はじめまして！" },
                { level: 1, minConversations: 1, maxConversations: 2, name: "興味あり", nameEn: "Interested", style: "slightly_casual", description: "また来てくれたんやね" },
                { level: 2, minConversations: 3, maxConversations: 4, name: "顔見知り", nameEn: "Acquaintance", style: "casual", description: "よく来てくれるようになったね" },
                { level: 3, minConversations: 5, maxConversations: 7, name: "常連さん", nameEn: "Regular", style: "friendly", description: "いつもありがとう！" },
                { level: 4, minConversations: 8, maxConversations: 10, name: "友達", nameEn: "Friend", style: "friend", description: "もう友達みたいなもんやね" },
                { level: 5, minConversations: 11, maxConversations: 999, name: "親友", nameEn: "Best Friend", style: "bestfriend", description: "何でも話せる仲やね" }
            ];
            
            this.previousLevel = 0;
            this.isAnimating = false;
        }
        
        calculateLevel(conversationCount) {
            for (let i = this.levels.length - 1; i >= 0; i--) {
                if (conversationCount >= this.levels[i].minConversations) {
                    return this.levels[i];
                }
            }
            return this.levels[0];
        }
        
        calculateProgress(levelInfo, conversationCount) {
            if (levelInfo.level >= this.levels.length - 1) {
                return 100; // 最高レベル
            }
            
            const currentMin = levelInfo.minConversations;
            const nextLevel = this.levels[levelInfo.level + 1];
            const nextMin = nextLevel.minConversations;
            
            const progress = ((conversationCount - currentMin) / (nextMin - currentMin)) * 100;
            return Math.min(Math.max(progress, 0), 100);
        }
        
        updateUI(levelInfo, conversationCount) {
            const currentLanguage = appState.currentLanguage || 'ja';
            
            // レベル表示を更新
            const levelElement = document.querySelector('.relationship-level');
            if (levelElement) {
                const levelName = currentLanguage === 'ja' ? levelInfo.name : levelInfo.nameEn;
                levelElement.textContent = `Lv.${levelInfo.level} ${levelName}`;
            }
            
            // プログレスバーの計算と更新
            const progress = this.calculateProgress(levelInfo, conversationCount);
            const progressBar = document.querySelector('.relationship-progress');
            if (progressBar) {
                progressBar.style.width = `${progress}%`;
            }
            
            // 経験値表示を更新
            const expElement = document.querySelector('.relationship-exp');
            if (expElement) {
                const nextLevel = this.levels[levelInfo.level + 1];
                if (nextLevel) {
                    if (currentLanguage === 'ja') {
                        expElement.textContent = `${conversationCount} / ${nextLevel.minConversations} 会話`;
                    } else {
                        expElement.textContent = `${conversationCount} / ${nextLevel.minConversations} conversations`;
                    }
                } else {
                    if (currentLanguage === 'ja') {
                        expElement.textContent = `${conversationCount} 会話（最高レベル達成！）`;
                    } else {
                        expElement.textContent = `${conversationCount} conversations (Max level!)`;
                    }
                }
            }
            
            // 親密度ラベルも言語に応じて更新
            const labelElement = document.querySelector('.relationship-label');
            if (labelElement) {
                labelElement.textContent = currentLanguage === 'ja' ? '親密度' : 'Intimacy';
            }
            
            // レベルアップ判定
            if (levelInfo.level > this.previousLevel && !this.isAnimating) {
                this.showLevelUpEffect(levelInfo);
                this.previousLevel = levelInfo.level;
            }
        }
        
        showLevelUpEffect(newLevel) {
            if (this.isAnimating) return;
            this.isAnimating = true;
            
            const container = document.querySelector('.relationship-meter-container');
            if (!container) return;
            
            // キラキラエフェクトを生成
            for (let i = 0; i < 30; i++) {
                setTimeout(() => {
                    const sparkle = document.createElement('div');
                    sparkle.className = 'sparkle';
                    sparkle.style.left = Math.random() * 100 + '%';
                    sparkle.style.top = Math.random() * 100 + '%';
                    sparkle.style.animationDelay = Math.random() * 0.5 + 's';
                    container.appendChild(sparkle);
                    
                    setTimeout(() => sparkle.remove(), 2500);
                }, i * 50);
            }
            
            // レベルアップメッセージ
            const message = document.createElement('div');
            message.className = 'levelup-message';
            const levelName = appState.currentLanguage === 'ja' ? newLevel.name : newLevel.nameEn;
            message.innerHTML = `🎉 ${appState.currentLanguage === 'ja' ? 'レベルアップ！' : 'Level Up!'}<br>Lv.${newLevel.level} ${levelName}<br><small>${newLevel.description}</small>`;
            container.appendChild(message);
            
            // 効果音を再生（ミュート状態でなければ）
            if (!audioState.isMuted) {
                playSystemSound('levelup');
            }
            
            setTimeout(() => {
                message.remove();
                this.isAnimating = false;
            }, 3000);
        }
        
        getCurrentLevelStyle(conversationCount) {
            const levelInfo = this.calculateLevel(conversationCount);
            return levelInfo.style;
        }
    }
    
    // ====== 🎯 会話履歴管理システム ======
    class ConversationMemory {
        constructor() {
            this.history = [];
            this.maxHistory = 20; // 最大20ターンまで記憶
            this.currentTopic = null;
            this.previousTopics = [];
        }
        
        addMessage(role, content, emotion = null, timestamp = null) {
            const message = {
                role: role,
                content: content,
                emotion: emotion,
                timestamp: timestamp || Date.now(),
                turn: this.history.length
            };
            
            this.history.push(message);
            
            // 履歴が上限を超えたら古いものから削除
            if (this.history.length > this.maxHistory) {
                this.history.shift();
            }
            
            console.log('💭 会話履歴追加:', message);
        }
        
        getRecentContext(turnCount = 5) {
            // 最近のN個の会話を取得
            return this.history.slice(-turnCount);
        }
        
        getFullHistory() {
            return this.history;
        }
        
        findPreviousMention(keyword) {
            // キーワードが以前に言及されたか検索
            for (let i = this.history.length - 1; i >= 0; i--) {
                if (this.history[i].content.includes(keyword)) {
                    return {
                        found: true,
                        message: this.history[i],
                        turnsAgo: this.history.length - i
                    };
                }
            }
            return { found: false };
        }
        
        updateCurrentTopic(topic) {
            if (this.currentTopic && this.currentTopic !== topic) {
                this.previousTopics.push(this.currentTopic);
                if (this.previousTopics.length > 5) {
                    this.previousTopics.shift();
                }
            }
            this.currentTopic = topic;
        }
        
        getSummary() {
            // 会話の要約を生成（簡易版）
            const topics = [...new Set(this.previousTopics)];
            const userQuestions = this.history
                .filter(m => m.role === 'user')
                .map(m => m.content);
            
            return {
                topics: topics,
                currentTopic: this.currentTopic,
                turnCount: this.history.length,
                userQuestions: userQuestions.slice(-5) // 最近の5つの質問
            };
        }
    }
    
    // ====== 🎯 自己紹介部長システム ======
    class IntroductionManager {
        constructor() {
            this.status = 'pending';
            this.lastExecutionTime = 0;
            this.debugMode = true;
            this.requesterLog = [];
            this.pendingIntroData = null;
        }
        
        canStartIntroduction(requester = 'unknown') {
            const now = Date.now();
            const timeSinceLastExecution = now - this.lastExecutionTime;
            
            this.requesterLog.push({requester, time: now, status: this.status});
            
            if (this.status === 'completed') {
                this.debugLog(`自己紹介スキップ: 既に完了済み (要求者: ${requester})`);
                return false;
            }
            
            if (this.status === 'running' || this.status === 'waiting_unity') {
                this.debugLog(`自己紹介スキップ: 現在実行中/待機中 (要求者: ${requester})`);
                return false;
            }
            
            if (timeSinceLastExecution < 3000 && this.lastExecutionTime > 0) {
                this.debugLog(`自己紹介スキップ: 前回から${timeSinceLastExecution}ms経過 (要求者: ${requester})`);
                return false;
            }
            
            return true;
        }
        
        startIntroduction(requester = 'unknown', data = null) {
            if (!this.canStartIntroduction(requester)) {
                return false;
            }
            
            if (!isUnityFullyReady()) {
                this.status = 'waiting_unity';
                this.pendingIntroData = data;
                this.lastExecutionTime = Date.now();
                this.debugLog(`🎬 自己紹介部長：Unity初期化待ち (要求者: ${requester})`);
                return true;
            }
            
            this.status = 'running';
            this.lastExecutionTime = Date.now();
            this.debugLog(`🎬 自己紹介部長：自己紹介を開始します (承認要求者: ${requester})`);
            
            return true;
        }
        
        completeIntroduction() {
            this.status = 'completed';
            this.pendingIntroData = null;
            this.debugLog('🏁 自己紹介部長：自己紹介完了');
            
            if (this.requesterLog.length > 1) {
                this.debugLog('📊 自己紹介要求者ログ:', this.requesterLog);
            }
        }
        
        onUnityReady() {
            if (this.status === 'waiting_unity' && this.pendingIntroData) {
                this.debugLog('🎮 Unity準備完了 - 保留中の自己紹介を実行');
                this.status = 'running';
                executeIntroduction(this.pendingIntroData);
            }
        }
        
        reset() {
            this.status = 'pending';
            this.lastExecutionTime = 0;
            this.requesterLog = [];
            this.pendingIntroData = null;
            this.debugLog('🔄 自己紹介部長：状態をリセットしました');
        }
        
        debugLog(message, data = null) {
            if (this.debugMode) {
                console.log(`[IntroductionManager] ${message}`, data || '');
            }
        }
    }
    
    // ====== インスタンス作成 ======
    const introductionManager = new IntroductionManager();
    const visitorManager = new VisitorManager();
    const conversationMemory = new ConversationMemory();
    const relationshipManager = new RelationshipManager();
    
    // ====== 初期化時に関係性レベルを復元 ======
    function initializeRelationshipLevel() {
        const conversationCount = visitorManager.visitData.totalConversations;
        const levelInfo = relationshipManager.calculateLevel(conversationCount);
        relationshipManager.previousLevel = levelInfo.level;
        relationshipManager.updateUI(levelInfo, conversationCount);
        console.log(`🎯 関係性レベル初期化: Lv.${levelInfo.level} ${levelInfo.name} (会話数: ${conversationCount})`);
    }
    
    // ====== 🎯 Unity完全準備状態チェック ======
    function isUnityFullyReady() {
        return unityState.isReady && 
               unityState.isFullyInitialized &&
               appState.connectionStatus === 'connected' && 
               audioState.initialized;
    }
    
    // ====== 🎯 自己紹介部長への要求関数 ======
    function requestIntroduction(requester, data = null) {
        if (!isSystemReady()) {
            introductionManager.debugLog(`自己紹介延期: システム準備未完了 (要求者: ${requester})`);
            return false;
        }
        
        if (introductionManager.startIntroduction(requester, data)) {
            if (introductionManager.status === 'running') {
                executeIntroduction(data);
            }
            return true;
        }
        return false;
    }
    
    // ====== 🎯 システム準備状態チェック ======
    function isSystemReady() {
        return unityState.isReady && 
               appState.connectionStatus === 'connected' && 
               audioState.initialized;
    }
    
    // ====== 🎯 実際の自己紹介実行 ======
    function executeIntroduction(data = null) {
        introductionManager.debugLog('🎭 自己紹介実行開始', data);
        
        if (!isUnityFullyReady()) {
            introductionManager.debugLog('⚠️ Unity未初期化のため自己紹介を延期');
            introductionManager.status = 'waiting_unity';
            introductionManager.pendingIntroData = data;
            return;
        }
        
        if (data && data.audio) {
            const emotion = data.emotion || 'happy';
            introductionManager.debugLog(`🎵 音声付き自己紹介: ${emotion}`);
            
            setTimeout(() => {
                startConversation(emotion, data.audio);
            }, 200);
            
            setTimeout(() => {
                introductionManager.completeIntroduction();
            }, 5000);
        } else {
            introductionManager.debugLog('⏳ 音声データなし - 挨拶メッセージ待機中');
            introductionManager.status = 'pending';
        }
    }
    
    // ====== 状態管理システム ======
    let unityState = {
        instance: null,
        isReady: false,
        isFullyInitialized: false,
        retryCount: 0,
        maxRetries: 10,
        lastMessageTime: Date.now(),
        connectionCheckInterval: null,
        messageQueue: [],
        isSending: false,
        sessionId: generateSessionId(),
        activeAudioElement: null,
        
        currentEmotion: 'neutral',
        currentTalkingState: false,
        lastEmotionChangeTime: 0,
        emotionChangeDebounceTime: 50,
        maxEmotionChangesPerSecond: 10,
        currentConversationId: null
    };

    let conversationState = {
        isActive: false,
        startTime: 0,
        audioElement: null,
        currentEmotion: 'neutral',
        conversationId: null
    };
    
    let audioState = {
        recorder: null,
        chunks: [],
        isRecording: false,
        audioContext: null,
        analyser: null,
        gainNode: null,
        initialized: false,
        isMuted: false,  // 🔇 ミュート状態
        originalVolume: 1.0  // 元の音量を保存
    };
    
    let appState = {
        currentLanguage: 'ja',
        isWaitingResponse: false,
        debugMode: false,
        messageHistory: [],
        lastResponseTime: 0,
        connectionStatus: 'disconnected',
        conversationCount: 0,
        interactionCount: 0
    };
    
    // 🔇 システム音声の管理
    const systemSounds = {
        start: null,
        end: null,
        error: null,
        levelup: null
    };
    
    const domElements = {
        chatMessages: null,
        messageInput: null,
        sendButton: null,
        voiceButton: null,
        unityFrame: null,
        languageModal: null,
        changeLanguageBtn: null,
        currentLanguageDisplay: null,
        statusIndicator: null,
        muteButton: null  // 🔇 ミュートボタン要素
    };
    
    let socket;
    
    // ====== 基本システム初期化 ======
    function initialize() {
        console.log('アプリケーションを初期化中...');
        
        initializeDomElements();
        setupEventListeners();
        initializeSocketConnection();
        showLanguageModal();
        initializeUnityConnection();
        initializeAudioSystem();
        initializeSystemSounds();
        initializeRelationshipLevel();
        loadMuteState();  // 🔇 ミュート状態を復元
        
        // 訪問者情報をサーバーに送信
        sendVisitorInfo();
        
        console.log('アプリケーションの初期化が完了しました');
    }
    
    // 訪問者情報をサーバーに送信
    function sendVisitorInfo() {
        setTimeout(() => {
            if (socket && socket.connected) {
                socket.emit('visitor_info', {
                    visitorId: visitorManager.visitorId,
                    visitData: visitorManager.visitData
                });
                console.log('👤 訪問者情報をサーバーに送信');
            }
        }, 1000);
    }
    
    function initializeDomElements() {
        domElements.chatMessages = document.getElementById('chat-messages');
        domElements.messageInput = document.getElementById('message-input');
        domElements.sendButton = document.getElementById('send-button');
        domElements.voiceButton = document.getElementById('voice-button');
        domElements.unityFrame = document.getElementById('unity-frame');
        domElements.languageModal = document.getElementById('language-modal');
        domElements.changeLanguageBtn = document.getElementById('change-language-btn');
        domElements.currentLanguageDisplay = document.getElementById('current-language');
        domElements.statusIndicator = document.getElementById('connection-status') || document.createElement('div');
        
        // 🔇 ミュートボタンは既にHTMLに存在するので、参照を取得するだけ
        domElements.muteButton = document.getElementById('mute-button');
        
        if (!document.getElementById('connection-status')) {
            domElements.statusIndicator.id = 'connection-status';
            domElements.statusIndicator.style.position = 'fixed';
            domElements.statusIndicator.style.bottom = '5px';
            domElements.statusIndicator.style.right = '5px';
            domElements.statusIndicator.style.width = '10px';
            domElements.statusIndicator.style.height = '10px';
            domElements.statusIndicator.style.borderRadius = '50%';
            domElements.statusIndicator.style.backgroundColor = '#999';
            document.body.appendChild(domElements.statusIndicator);
        }
    }
    
    // 🔇 ミュートボタンのアイコンを更新
    function updateMuteButtonIcon() {
        if (!domElements.muteButton) return;
        
        if (audioState.isMuted) {
            domElements.muteButton.innerHTML = '🔇';
            domElements.muteButton.classList.add('muted');
            domElements.muteButton.title = appState.currentLanguage === 'ja' ? '音声をオンにする' : 'Unmute Audio';
        } else {
            domElements.muteButton.innerHTML = '🔊';
            domElements.muteButton.classList.remove('muted');
            domElements.muteButton.title = appState.currentLanguage === 'ja' ? '音声をオフにする' : 'Mute Audio';
        }
    }
    
    // 🔇 ミュート状態の切り替え
    function toggleMute() {
        audioState.isMuted = !audioState.isMuted;
        
        // ミュート状態を保存
        try {
            localStorage.setItem('audio_muted', audioState.isMuted.toString());
        } catch (e) {
            console.warn('ミュート状態の保存に失敗:', e);
        }
        
        // ボタンのアイコンを更新
        updateMuteButtonIcon();
        
        // 現在再生中の音声に適用
        if (conversationState.audioElement) {
            conversationState.audioElement.muted = audioState.isMuted;
        }
        if (unityState.activeAudioElement) {
            unityState.activeAudioElement.muted = audioState.isMuted;
        }
        
        // システム音声のミュート状態を更新
        Object.values(systemSounds).forEach(sound => {
            if (sound) {
                sound.muted = audioState.isMuted;
            }
        });
        
        console.log(`🔇 ミュート状態: ${audioState.isMuted ? 'ON' : 'OFF'}`);
    }
    
    // 🔇 ミュート状態の読み込み
    function loadMuteState() {
        try {
            const savedMuteState = localStorage.getItem('audio_muted');
            if (savedMuteState !== null) {
                audioState.isMuted = savedMuteState === 'true';
                updateMuteButtonIcon();
                console.log(`🔇 保存されたミュート状態を復元: ${audioState.isMuted ? 'ON' : 'OFF'}`);
            }
        } catch (e) {
            console.warn('ミュート状態の読み込みに失敗:', e);
        }
    }
    
    function setupEventListeners() {
        document.getElementById('select-japanese').addEventListener('click', function() {
            selectLanguage('ja');
        });
        
        document.getElementById('select-english').addEventListener('click', function() {
            selectLanguage('en');
        });
        
        domElements.changeLanguageBtn.addEventListener('click', function() {
            showLanguageModal();
        });
        
        domElements.sendButton.addEventListener('click', sendTextMessage);
        
        domElements.messageInput.addEventListener('keydown', function(e) {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                sendTextMessage();
            }
        });
        
        domElements.voiceButton.addEventListener('click', toggleVoiceRecording);
        
        // 🔇 ミュートボタンのイベントリスナー
        if (domElements.muteButton) {
            domElements.muteButton.addEventListener('click', toggleMute);
            
            // ホバーエフェクト
            domElements.muteButton.addEventListener('mouseenter', function() {
                this.style.transform = 'scale(1.1)';
            });
            
            domElements.muteButton.addEventListener('mouseleave', function() {
                this.style.transform = 'scale(1)';
            });
        }
        
        window.addEventListener('message', handleUnityMessage);
        window.addEventListener('beforeunload', cleanupResources);
        
        window.onUnityFullyInitialized = function() {
            console.log('🎮 Unity完全初期化イベントを受信');
            unityState.isFullyInitialized = true;
            introductionManager.onUnityReady();
        };
    }
    
    function initializeSocketConnection() {
        socket = io();
        
        socket.on('connect', handleSocketConnect);
        socket.on('current_language', handleLanguageUpdate);
        socket.on('language_changed', handleLanguageUpdate);
        socket.on('greeting', handleGreetingMessage);
        socket.on('response', handleResponseMessage);
        socket.on('transcription', handleTranscription);
        socket.on('error', handleErrorMessage);
        socket.on('context_aware_response', handleContextAwareResponse);
        
        updateConnectionStatus('connecting');
    }
    
    function initializeUnityConnection() {
        findUnityInstance();
        unityState.connectionCheckInterval = setInterval(checkUnityConnection, 2000);
        console.log('Unity接続の監視を開始しました');
    }
    
    function initializeAudioSystem() {
        try {
            window.AudioContext = window.AudioContext || window.webkitAudioContext;
            audioState.initialized = false;
            console.log('音声システムの初期化準備が完了しました（インタラクション後に完全初期化）');
        } catch (e) {
            console.error('音声システムの初期化に失敗しました:', e);
        }
    }
    
    function lazyInitializeAudioSystem() {
        if (audioState.initialized) return Promise.resolve();
        
        return new Promise((resolve, reject) => {
            try {
                audioState.audioContext = new (window.AudioContext || window.webkitAudioContext)();
                audioState.analyser = audioState.audioContext.createAnalyser();
                audioState.analyser.fftSize = 256;
                audioState.gainNode = audioState.audioContext.createGain();
                audioState.gainNode.gain.value = 1.0;
                
                audioState.gainNode.connect(audioState.analyser);
                audioState.analyser.connect(audioState.audioContext.destination);
                
                audioState.initialized = true;
                console.log('音声システムが完全に初期化されました');
                
                resolve();
            } catch (e) {
                console.error('音声システムの完全初期化に失敗しました:', e);
                reject(e);
            }
        });
    }
    
    // 🔇 システム音声の初期化
    function initializeSystemSounds() {
        const soundFiles = {
            start: '/static/sounds/start.mp3',
            end: '/static/sounds/end.mp3',
            error: '/static/sounds/error.mp3',
            levelup: '/static/sounds/levelup.mp3'
        };
        
        Object.entries(soundFiles).forEach(([soundName, path]) => {
            loadSystemSound(soundName, path);
        });
    }
    
    // 🔇 個別の音声ファイル読み込み
    function loadSystemSound(soundName, path) {
        try {
            const audio = new Audio();
            
            // エラーハンドリング
            audio.addEventListener('error', (e) => {
                console.warn(`⚠️ システム音声 '${soundName}' の読み込みエラー:`, {
                    path: path,
                    error: e,
                    errorCode: audio.error ? audio.error.code : 'unknown',
                    errorMessage: audio.error ? audio.error.message : 'unknown'
                });
                
                // エラーでも続行（音声なしで動作）
                systemSounds[soundName] = null;
            });
            
            // 正常に読み込めた場合
            audio.addEventListener('canplaythrough', () => {
                console.log(`✅ システム音声 '${soundName}' を読み込みました`);
                audio.volume = 0.3;
                audio.muted = audioState.isMuted;  // 現在のミュート状態を適用
                systemSounds[soundName] = audio;
            });
            
            // メタデータ読み込み時
            audio.addEventListener('loadedmetadata', () => {
                console.log(`📊 システム音声 '${soundName}' のメタデータを読み込みました`);
            });
            
            // プリロード設定
            audio.preload = 'auto';
            
            // ソースを設定（これにより読み込み開始）
            audio.src = path;
            
            // 手動で読み込み開始
            audio.load();
            
        } catch (e) {
            console.error(`❌ システム音声 '${soundName}' の初期化エラー:`, e);
            systemSounds[soundName] = null;
        }
    }
    
    // ====== 言語設定とUI ======
    function showLanguageModal() {
        domElements.languageModal.style.display = 'flex';
    }
    
    function selectLanguage(language) {
        appState.currentLanguage = language;
        socket.emit('set_language', { language: language });
        
        updateUILanguage(language);
        updateMuteButtonIcon(); // ミュートボタンのツールチップも更新
        
        // 関係性レベルUIも更新
        const conversationCount = visitorManager.visitData.totalConversations;
        const levelInfo = relationshipManager.calculateLevel(conversationCount);
        relationshipManager.updateUI(levelInfo, conversationCount);
        
        domElements.languageModal.style.display = 'none';
        
        lazyInitializeAudioSystem().catch(e => {
            console.warn('音声システムの遅延初期化に失敗しました:', e);
        });
    }
    
    function updateUILanguage(language) {
        const translations = {
            ja: {
                languageDisplay: '言語: 日本語',
                messagePlaceholder: 'メッセージを入力...',
                sendButton: '送信',
                suggestions: [
                    "京友禅について教えて",
                    "制作過程が知りたい",
                    "伝統的な模様について"
                ]
            },
            en: {
                languageDisplay: 'Language: English',
                messagePlaceholder: 'Type your message...',
                sendButton: 'Send',
                suggestions: [
                    "Tell me about Kyoto Yuzen",
                    "I want to know the production process",
                    "About traditional patterns"
                ]
            }
        };
        
        const langData = translations[language] || translations.ja;
        
        domElements.currentLanguageDisplay.textContent = langData.languageDisplay;
        domElements.messageInput.placeholder = langData.messagePlaceholder;
        domElements.sendButton.textContent = langData.sendButton;
        
        // ミュートボタンのツールチップも更新
        updateMuteButtonIcon();
        
        try {
            localStorage.setItem('preferred_language', language);
        } catch (e) {
            console.warn('言語設定の保存に失敗しました:', e);
        }
    }
    
    // ====== メッセージ送信とUI ======
    function sendTextMessage() {
        const message = domElements.messageInput.value.trim();
        if (!message) return;
        
        appState.isWaitingResponse = true;
        appState.interactionCount++;
        updateConnectionStatus('processing');
        
        // ユーザーメッセージを会話履歴に追加
        conversationMemory.addMessage('user', message, null);
        
        // 質問回数をカウント
        const questionCount = visitorManager.incrementQuestionCount(message);
        console.log(`📊 この質問の回数: ${questionCount}回目`);
        
        addMessage(message, true);
        
        // 会話履歴と訪問者情報を含めて送信
        socket.emit('message', { 
            message: message,
            language: appState.currentLanguage,
            visitorId: visitorManager.visitorId,
            conversationHistory: conversationMemory.getRecentContext(5),
            questionCount: questionCount,
            visitData: visitorManager.visitData,
            interactionCount: appState.interactionCount,
            relationshipLevel: relationshipManager.getCurrentLevelStyle(visitorManager.visitData.totalConversations),
            selectedSuggestions: visitorManager.getSelectedSuggestions()  // 🎯 選択済みサジェスチョンも送信
        });
        
        domElements.messageInput.value = '';
        
        appState.messageHistory.push({
            type: 'user',
            content: message,
            timestamp: Date.now()
        });
    }
    
    function addMessage(message, isUser, options = {}) {
        const messageDiv = document.createElement('div');
        messageDiv.classList.add('message');
        messageDiv.classList.add(isUser ? 'user-message' : 'ai-message');
        
        if (options.isGreeting) {
            messageDiv.classList.add('greeting-message');
        }
        if (options.isError) {
            messageDiv.classList.add('error-message');
        }
        if (options.isThinking) {
            messageDiv.classList.add('thinking');
        }
        
        messageDiv.textContent = message;
        
        const scrollContainer = document.createElement('div');
        scrollContainer.className = 'message-scroll-container';
        scrollContainer.appendChild(messageDiv);
        
        scrollContainer.style.opacity = '0';
        domElements.chatMessages.appendChild(scrollContainer);
        
        smoothScrollToBottom(domElements.chatMessages);
        
        setTimeout(() => {
            scrollContainer.style.transition = 'opacity 0.3s ease-in-out';
            scrollContainer.style.opacity = '1';
        }, 10);
        
        return messageDiv;
    }
    
    function smoothScrollToBottom(element) {
        const scrollHeight = element.scrollHeight;
        const currentPosition = element.scrollTop + element.clientHeight;
        const scrollRemaining = scrollHeight - currentPosition;
        
        if (scrollRemaining <= 0) return;
        
        const duration = Math.min(scrollRemaining * 0.5, 300);
        const startTime = performance.now();
        const startPosition = element.scrollTop;
        const targetPosition = scrollHeight - element.clientHeight;
        
        function scroll(timestamp) {
            const elapsed = timestamp - startTime;
            const progress = Math.min(elapsed / duration, 1);
            const easing = easeOutCubic(progress);
            
            element.scrollTop = startPosition + ((targetPosition - startPosition) * easing);
            
            if (progress < 1) {
                requestAnimationFrame(scroll);
            }
        }
        
        requestAnimationFrame(scroll);
    }
    
    function easeOutCubic(x) {
        return 1 - Math.pow(1 - x, 3);
    }
    
    function showSuggestions(suggestionsData = null) {
        const existingSuggestions = document.querySelector('.suggestions-container');
        if (existingSuggestions) {
            existingSuggestions.remove();
        }
        
        const suggestionsContainer = document.createElement('div');
        suggestionsContainer.classList.add('suggestions-container');
        
        let suggestions;
        if (suggestionsData && Array.isArray(suggestionsData) && suggestionsData.length > 0) {
            suggestions = suggestionsData;
        } else {
            const translations = {
                ja: [
                    "京友禅とは何ですか？",
                    "京友禅の歴史について教えて",
                    "他の染色技法との違いは？"
                ],
                en: [
                    "What is Kyoto Yuzen?",
                    "Tell me about the history",
                    "Differences from other dyeing techniques?"
                ]
            };
            
            suggestions = translations[appState.currentLanguage] || translations.ja;
        }
        
        suggestions.forEach((suggestion, index) => {
            const button = document.createElement('button');
            button.classList.add('suggestion-button');
            button.classList.add('pink-suggestion');
            button.textContent = suggestion;
            
            button.style.animationDelay = `${suggestions.indexOf(suggestion) * 0.1}s`;
            
            button.addEventListener('click', function() {
                domElements.messageInput.value = suggestion;
                sendTextMessage();
                
                suggestionsContainer.classList.add('fade-out');
                setTimeout(() => {
                    suggestionsContainer.remove();
                }, 300);
            });
            
            suggestionsContainer.appendChild(button);
        });
        
        domElements.chatMessages.appendChild(suggestionsContainer);
        smoothScrollToBottom(domElements.chatMessages);
    }
    
    function showError(message) {
        addMessage(`エラー: ${message}`, false, { isError: true });
        playSystemSound('error');
        appState.isWaitingResponse = false;
        updateConnectionStatus('connected');
        sendEmotionToAvatar("neutral", false, 'emergency');
    }
    
    // 🔇 システム音声の再生
    function playSystemSound(soundName) {
        // ミュート状態なら再生しない
        if (audioState.isMuted) {
            console.log(`🔇 ミュート中のため '${soundName}' の再生をスキップ`);
            return;
        }
        
        if (!systemSounds[soundName]) {
            console.warn(`⚠️ システム音声 '${soundName}' が見つかりません`);
            return;
        }
        
        try {
            const sound = systemSounds[soundName];
            
            // 音声が正しく読み込まれているか確認
            if (!sound || !sound.src) {
                console.warn(`⚠️ システム音声 '${soundName}' が正しく初期化されていません`);
                return;
            }
            
            // 音声をリセット
            sound.currentTime = 0;
            
            // 音声を再生
            const playPromise = sound.play();
            
            if (playPromise !== undefined) {
                playPromise
                    .then(() => {
                        console.log(`🔊 システム音声 '${soundName}' を再生しました`);
                    })
                    .catch(error => {
                        console.warn(`⚠️ システム音声 '${soundName}' の再生エラー:`, error);
                        
                        // 自動再生ポリシーによるエラーの場合
                        if (error.name === 'NotAllowedError') {
                            console.log('💡 ユーザーインタラクション後に再生してください');
                        }
                    });
            }
        } catch (e) {
            console.error(`❌ システム音声 '${soundName}' の再生中にエラー:`, e);
        }
    }
    
    // ====== 音声録音機能 ======
    function toggleVoiceRecording() {
        lazyInitializeAudioSystem().then(() => {
            if (audioState.isRecording) {
                stopVoiceRecording();
            } else {
                startVoiceRecording();
            }
        }).catch(e => {
            console.error('音声処理エラー:', e);
            showError('マイクにアクセスできませんでした。');
        });
    }
    
    function startVoiceRecording() {
        appState.isWaitingResponse = true;
        updateConnectionStatus('recording');
        playSystemSound('start');
        
        navigator.mediaDevices.getUserMedia({ audio: true })
            .then(function(stream) {
                audioState.recorder = new MediaRecorder(stream);
                audioState.chunks = [];
                
                audioState.recorder.ondataavailable = function(e) {
                    audioState.chunks.push(e.data);
                };
                
                audioState.recorder.onstop = function() {
                    const audioBlob = new Blob(audioState.chunks, { type: 'audio/webm' });
                    
                    convertBlobToBase64(audioBlob).then(base64data => {
                        // 会話履歴と訪問者情報を含めて送信
                        socket.emit('audio_message', { 
                            audio: base64data,
                            language: appState.currentLanguage,
                            visitorId: visitorManager.visitorId,
                            conversationHistory: conversationMemory.getRecentContext(5),
                            visitData: visitorManager.visitData,
                            interactionCount: appState.interactionCount,
                            relationshipLevel: relationshipManager.getCurrentLevelStyle(visitorManager.visitData.totalConversations),
                            selectedSuggestions: visitorManager.getSelectedSuggestions()
                        });
                    });
                    
                    stream.getTracks().forEach(track => track.stop());
                };
                
                audioState.recorder.start();
                domElements.voiceButton.textContent = '■';
                domElements.voiceButton.classList.add('recording');
                audioState.isRecording = true;
            })
            .catch(function(err) {
                console.error('マイクの使用が許可されていません:', err);
                showError('マイクの使用が許可されていません');
                audioState.isRecording = false;
                updateConnectionStatus('connected');
            });
    }
    
    function stopVoiceRecording() {
        if (!audioState.recorder || audioState.recorder.state === 'inactive') return;
        
        playSystemSound('end');
        
        try {
            audioState.recorder.stop();
        } catch (e) {
            console.error('録音停止エラー:', e);
        }
        
        domElements.voiceButton.textContent = '🎤';
        domElements.voiceButton.classList.remove('recording');
        audioState.isRecording = false;
        updateConnectionStatus('processing');
    }
    
    function convertBlobToBase64(blob) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onloadend = function() {
                resolve(reader.result);
            };
            reader.onerror = function() {
                reject(new Error("ファイル変換エラー"));
            };
            reader.readAsDataURL(blob);
        });
    }
    
    // ====== 感情送信システム ======
    function sendEmotionToAvatar(emotion, isTalking = false, reason = 'manual', conversationId = null) {
        const now = Date.now();
        
        console.log(`感情送信: ${emotion}, 会話=${isTalking}, 理由=${reason}, 会話ID=${conversationId}`);
        
        // 会話開始/終了時は重複チェックをスキップ
        if (reason === 'conversation_start' || reason === 'conversation_end') {
            return executeEmotionChange(emotion, isTalking, reason, now, conversationId);
        }
        
        const newState = `${emotion}_${isTalking}`;
        const currentState = `${unityState.currentEmotion}_${unityState.currentTalkingState}`;
        
        if (newState === currentState && (now - unityState.lastEmotionChangeTime) < unityState.emotionChangeDebounceTime) {
            console.log('同じ状態のためスキップ（短時間重複）');
            return false;
        }
        
        return executeEmotionChange(emotion, isTalking, reason, now, conversationId);
    }

    function executeEmotionChange(emotion, isTalking, reason, timestamp, conversationId = null) {
        try {
            const messageData = {
                type: "emotion",
                emotion: emotion,
                talking: isTalking,
                sequence: unityState.messageQueue.length,
                sessionId: unityState.sessionId,
                timestamp: timestamp,
                reason: reason,
                conversationId: conversationId
            };
            
            const success = sendMessageToUnity(messageData);
            
            if (success) {
                unityState.currentEmotion = emotion;
                unityState.currentTalkingState = isTalking;
                unityState.lastEmotionChangeTime = timestamp;
                unityState.currentConversationId = conversationId;
                
                console.log(`✅ 感情送信成功: ${emotion} (会話=${isTalking}) - ${reason}`);
                return true;
            } else {
                console.error('❌ Unity送信失敗');
                return false;
            }
        } catch (error) {
            console.error('感情送信エラー:', error);
            return false;
        }
    }

    function sendMessageToUnity(messageData) {
        if (!unityState.instance) {
            console.warn('Unity インスタンスが見つかりません');
            
            if (findUnityInstance()) {
                console.log('Unity インスタンスを再検索で発見');
            } else {
                return false;
            }
        }
        
        try {
            unityState.messageQueue.push(messageData);
            
            if (!unityState.isSending) {
                processUnityMessageQueue();
            }
            
            return true;
        } catch (error) {
            console.error('Unity メッセージ送信エラー:', error);
            return false;
        }
    }
    
    function processUnityMessageQueue() {
        if (unityState.isSending || unityState.messageQueue.length === 0) {
            return;
        }
        
        unityState.isSending = true;
        
        if (!unityState.instance) {
            if (!findUnityInstance()) {
                setTimeout(() => {
                    unityState.isSending = false;
                    processUnityMessageQueue();
                }, 500);
                return;
            }
        }
        
        const messageToSend = unityState.messageQueue.shift();
        
        try {
            if (unityState.instance.Module && unityState.instance.Module.SendMessage) {
                unityState.instance.Module.SendMessage(
                    'WebGLBridge',
                    'OnMessage',
                    JSON.stringify(messageToSend)
                );
            } else if (unityState.instance.SendMessage) {
                unityState.instance.SendMessage(
                    'WebGLBridge',
                    'OnMessage',
                    JSON.stringify(messageToSend)
                );
            } else {
                throw new Error('Unity SendMessage関数が見つかりません');
            }
            
            console.log('Unity SendMessage成功:', JSON.stringify(messageToSend));
            unityState.lastMessageTime = Date.now();
            
            setTimeout(() => {
                unityState.isSending = false;
                processUnityMessageQueue();
            }, 30);
        } catch (error) {
            console.error('Unity SendMessageエラー:', error);
            
            unityState.messageQueue.unshift(messageToSend);
            
            setTimeout(() => {
                unityState.isSending = false;
                processUnityMessageQueue();
            }, 1000);
        }
    }
    
    // ====== 会話フロー制御 ======
    function startConversation(emotion, audioData) {
        console.log('🎬 会話開始:', emotion);
        
        stopAllAudio();
        
        // 会話IDを生成
        const conversationId = 'conv_' + Date.now() + '_' + Math.random().toString(36).substring(2, 9);
        
        conversationState.isActive = true;
        conversationState.startTime = Date.now();
        conversationState.currentEmotion = emotion;
        conversationState.conversationId = conversationId;
        
        // 感情を送信
        sendEmotionToAvatar(emotion, true, 'conversation_start', conversationId);
        
        if (audioData && !isAudioPlaying()) {
            playAudioWithLipSync(audioData, emotion);
        } else if (!audioData) {
            setTimeout(() => {
                endConversation();
            }, 2000);
        }
    }

    function isAudioPlaying() {
        return unityState.activeAudioElement && 
               !unityState.activeAudioElement.paused && 
               !unityState.activeAudioElement.ended;
    }

    function stopAllAudio() {
        if (unityState.activeAudioElement) {
            unityState.activeAudioElement.pause();
            unityState.activeAudioElement.currentTime = 0;
            unityState.activeAudioElement = null;
        }
        
        if (conversationState.audioElement) {
            conversationState.audioElement.pause();
            conversationState.audioElement.currentTime = 0;
            conversationState.audioElement = null;
        }
        
        console.log('🔇 すべての音声を停止しました');
    }

    // 🔇 音声再生（ミュート対応版）
    function playAudioWithLipSync(audioData, emotion) {
        const audio = new Audio(audioData);
        audio.muted = audioState.isMuted;  // 現在のミュート状態を適用
        
        unityState.activeAudioElement = audio;
        conversationState.audioElement = audio;
        
        audio.oncanplaythrough = function() {
            console.log('🔊 音声準備完了');
        };
        
        audio.onplay = function() {
            console.log(`🔊 音声再生開始 (ミュート: ${audioState.isMuted})`);
        };
        
        audio.onended = function() {
            console.log('🔊 音声再生完了');
            onAudioEnd();
        };
        
        audio.onerror = function(error) {
            console.error('🔊 音声再生エラー:', error);
            onAudioEnd();
        };
        
        audio.play().catch(error => {
            console.error('音声再生開始エラー:', error);
            
            // 自動再生ポリシーによるエラーの場合
            if (error.name === 'NotAllowedError') {
                console.log('💡 自動再生がブロックされました。ユーザーインタラクション後に再生してください。');
                
                // ミュート状態で再試行
                audio.muted = true;
                audio.play().then(() => {
                    console.log('🔇 ミュート状態で再生を開始しました');
                }).catch(e => {
                    console.error('ミュート状態でも再生できませんでした:', e);
                    onAudioEnd();
                });
            } else {
                onAudioEnd();
            }
        });
    }

    function onAudioEnd() {
        console.log('🎵 音声終了処理開始');
        endConversation();
    }

    function endConversation() {
        console.log('🏁 会話終了処理開始');
        
        // 現在の感情を保持せず、常にneutral/falseに戻す
        sendEmotionToAvatar('neutral', false, 'conversation_end', conversationState.conversationId);
        
        resetConversationState();
        
        console.log('🏁 会話終了処理完了');
    }

    function resetConversationState() {
        conversationState.isActive = false;
        conversationState.startTime = 0;
        conversationState.audioElement = null;
        conversationState.currentEmotion = 'neutral';
        conversationState.conversationId = null;
        
        console.log('🏁 会話状態リセット完了');
    }
    
    // ====== Unity接続管理 ======
    function findUnityInstance() {
        const unityFrame = domElements.unityFrame;
        
        if (!unityFrame || !unityFrame.contentWindow) {
            console.warn('Unity iframeが見つかりません');
            return false;
        }
        
        const frameWindow = unityFrame.contentWindow;
        
        if (frameWindow.unityInstance) {
            unityState.instance = frameWindow.unityInstance;
            unityState.isReady = true;
            console.log('Unity instanceを直接取得しました');
            return true;
        }
        
        if (frameWindow.Module && frameWindow.Module.unityInstance) {
            unityState.instance = frameWindow.Module.unityInstance;
            unityState.isReady = true;
            console.log('Unity instanceをModuleから取得しました');
            return true;
        }
        
        if (frameWindow.gameInstance) {
            unityState.instance = frameWindow.gameInstance;
            unityState.isReady = true;
            console.log('Unity instanceをgameInstanceから取得しました');
            return true;
        }
        
        for (const key in frameWindow) {
            const obj = frameWindow[key];
            if (obj && (typeof obj === 'object') && 
                ((obj.SendMessage && typeof obj.SendMessage === 'function') || 
                 (obj.Module && obj.Module.SendMessage && typeof obj.Module.SendMessage === 'function'))) {
                unityState.instance = obj;
                unityState.isReady = true;
                console.log(`Unity instanceを発見: ${key}`);
                return true;
            }
        }
        
        return false;
    }
    
    function checkUnityConnection() {
        if (!unityState.instance) {
            if (unityState.retryCount < unityState.maxRetries) {
                unityState.retryCount++;
                if (findUnityInstance()) {
                    console.log('Unity instanceの接続に成功しました');
                    sendEmotionToAvatar('neutral', false, 'initialization');
                    unityState.isReady = true;
                    
                    checkUnityFullInitialization();
                    
                    if (appState.connectionStatus === 'unity_disconnected') {
                        updateConnectionStatus('connected');
                    }
                } else {
                    console.warn(`Unity instanceの接続を再試行中... (${unityState.retryCount}/${unityState.maxRetries})`);
                    updateConnectionStatus('unity_disconnected');
                }
            } else if (unityState.retryCount === unityState.maxRetries) {
                console.error('Unity instanceの取得に失敗しました（最大試行回数に到達）');
                unityState.retryCount++;
                updateConnectionStatus('unity_failed');
            }
        } else {
            const now = Date.now();
            const elapsed = now - unityState.lastMessageTime;
            
            if (elapsed > 5 * 60 * 1000) {
                console.log('長時間通信がないため接続状態を確認');
                
                try {
                    sendEmotionToAvatar('neutral', false, 'connection_test');
                } catch (e) {
                    console.warn('Unity接続テストに失敗、接続をリセットします');
                    unityState.instance = null;
                    unityState.isReady = false;
                    unityState.retryCount = 0;
                }
            }
        }
    }
    
    function checkUnityFullInitialization() {
        if (!unityState.isFullyInitialized && unityState.instance) {
            setTimeout(() => {
                if (!unityState.isFullyInitialized) {
                    console.log('🎮 Unity完全初期化タイムアウト - 強制的に初期化完了とみなす');
                    unityState.isFullyInitialized = true;
                    introductionManager.onUnityReady();
                }
            }, 3000);
        }
    }
    
    function handleUnityMessage(event) {
        if (!event.data || typeof event.data !== 'object') return;
        
        if (event.data.type === 'unity-ready') {
            console.log('Unityから準備完了の通知を受信しました');
            
            setTimeout(() => {
                if (findUnityInstance()) {
                    console.log('Unity instanceの準備完了');
                    sendEmotionToAvatar('neutral', false, 'unity_ready');
                    unityState.isReady = true;
                    
                    checkUnityFullInitialization();
                    
                    updateConnectionStatus('connected');
                }
            }, 500);
        }
        
        if (event.data.type === 'unity-fully-initialized') {
            console.log('🎮 Unityから完全初期化通知を受信');
            unityState.isFullyInitialized = true;
            introductionManager.onUnityReady();
        }
        
        if (event.data.type === 'unity-error') {
            console.error('Unityからエラーを受信:', event.data.message);
            updateConnectionStatus('unity_error');
        }
        
        if (event.data.type === 'audio-complete') {
            console.log('Unityから音声再生完了通知を受信');
        }
        
        if (event.data.type === 'state-update') {
            console.log('Unityから状態更新を受信:', event.data);
        }
    }
    
    // ====== Socket.ioイベントハンドラー ======
    function handleSocketConnect() {
        console.log('サーバーに接続しました');
        updateConnectionStatus('connected');
        
        try {
            const savedLanguage = localStorage.getItem('preferred_language');
            if (savedLanguage && (savedLanguage === 'ja' || savedLanguage === 'en')) {
                selectLanguage(savedLanguage);
            }
        } catch (e) {
            console.warn('保存済み言語設定の読み込みに失敗:', e);
        }
        
        // 訪問者情報を送信
        sendVisitorInfo();
    }
    
    function handleLanguageUpdate(data) {
        console.log('言語が設定/変更されました:', data.language);
        appState.currentLanguage = data.language;
        updateUILanguage(data.language);
    }
    
    function handleGreetingMessage(data) {
        domElements.chatMessages.innerHTML = '';
        
        addMessage(data.message, false, { isGreeting: true });
        
        // AIの挨拶を会話履歴に追加
        conversationMemory.addMessage('assistant', data.message, data.emotion);
        appState.conversationCount++;
        
        showSuggestions();
        
        appState.isWaitingResponse = false;
        updateConnectionStatus('connected');
        
        const emotion = data.emotion || 'happy';
        
        if (data.audio) {
            console.log('🎵 音声付き挨拶メッセージを受信 - 自己紹介部長に要求');
            requestIntroduction('greeting_with_audio', { emotion, audio: data.audio });
        } else {
            console.log('📝 テキストのみ挨拶メッセージを受信');
            sendEmotionToAvatar(emotion, false, 'greeting_no_audio');
        }
    }
    
    function handleResponseMessage(data) {
        try {
            appState.isWaitingResponse = false;
            updateConnectionStatus('connected');
            appState.lastResponseTime = Date.now();
            
            addMessage(data.message, false);
            
            // AIの応答を会話履歴に追加
            conversationMemory.addMessage('assistant', data.message, data.emotion);
            appState.conversationCount++;
            
            // 🎯 会話カウントを増やして関係性レベルを更新
            const newConversationCount = visitorManager.incrementConversationCount();
            const levelInfo = relationshipManager.calculateLevel(newConversationCount);
            relationshipManager.updateUI(levelInfo, newConversationCount);
            visitorManager.updateRelationshipLevel(levelInfo.level);
            
            // トピックの更新
            if (data.currentTopic) {
                conversationMemory.updateCurrentTopic(data.currentTopic);
                visitorManager.addTopic(data.currentTopic);
            }
            
            // 感情の処理
            let emotion = data.emotion || 'neutral';
            
            if (data.audio) {
                startConversation(emotion, data.audio);
            } else {
                // 音声データがない場合でもneutral+talkingで会話を開始
                console.log('🔇 音声データなし - シンプル会話モード');
                
                // テキストの長さから推定時間を計算
                const textLength = data.message ? data.message.length : 20;
                const estimatedDuration = Math.max(3, Math.min(15, textLength * 0.12));
                
                // 会話開始
                sendEmotionToAvatar(emotion, true, 'simple_conversation_start');
                
                // 推定時間後に会話終了
                setTimeout(() => {
                    sendEmotionToAvatar(emotion, false, 'simple_conversation_end');
                }, estimatedDuration * 1000);
            }
            
            if (data.suggestions && data.suggestions.length > 0) {
                setTimeout(() => {
                    showSuggestions(data.suggestions);
                }, conversationState.isActive ? 3000 : 500);
            }
            
        } catch (error) {
            console.error('レスポンス処理エラー:', error);
            sendEmotionToAvatar('neutral', false, 'emergency');
        }
    }
    
    // 文脈認識応答ハンドラー
    function handleContextAwareResponse(data) {
        console.log('🧠 文脈認識応答を受信:', data);
        handleResponseMessage(data);
    }
    
    function handleTranscription(data) {
        addMessage(data.text, true);
        
        // 音声認識結果も会話履歴に追加
        conversationMemory.addMessage('user', data.text, null);
        appState.interactionCount++;
        
        // 質問回数をカウント
        const questionCount = visitorManager.incrementQuestionCount(data.text);
        console.log(`📊 音声質問の回数: ${questionCount}回目`);
    }
    
    function handleErrorMessage(data) {
        console.error('エラー:', data.message);
        showError(data.message || '不明なエラーが発生しました');
        updateConnectionStatus('error');
        sendEmotionToAvatar('neutral', false, 'emergency');
    }
    
    // ====== ユーティリティ関数 ======
    function updateConnectionStatus(status) {
        if (appState.connectionStatus === status) return;
        
        appState.connectionStatus = status;
        
        switch (status) {
            case 'disconnected':
                domElements.statusIndicator.style.backgroundColor = '#999';
                domElements.statusIndicator.title = '切断されています';
                break;
                
            case 'connecting':
                domElements.statusIndicator.style.backgroundColor = '#FFA500';
                domElements.statusIndicator.title = '接続中...';
                break;
                
            case 'connected':
                domElements.statusIndicator.style.backgroundColor = '#00FF00';
                domElements.statusIndicator.title = '接続済み';
                break;
                
            case 'unity_disconnected':
                domElements.statusIndicator.style.backgroundColor = '#FF00FF';
                domElements.statusIndicator.title = 'Unityとの接続がありません';
                break;
                
            case 'unity_failed':
                domElements.statusIndicator.style.backgroundColor = '#FF0000';
                domElements.statusIndicator.title = 'Unityとの接続に失敗しました';
                break;
                
            case 'processing':
                domElements.statusIndicator.style.backgroundColor = '#0000FF';
                domElements.statusIndicator.title = '処理中...';
                break;
                
            case 'recording':
                domElements.statusIndicator.style.backgroundColor = '#FF0000';
                domElements.statusIndicator.title = '録音中...';
                break;
                
            case 'error':
                domElements.statusIndicator.style.backgroundColor = '#FF0000';
                domElements.statusIndicator.title = 'エラーが発生しました';
                break;
        }
        
        console.log(`接続状態を更新: ${status}`);
    }
    
    function generateSessionId() {
        return 'session_' + Math.random().toString(36).substring(2, 9) + '_' + 
               new Date().getTime().toString(36);
    }
    
    function cleanupResources() {
        if (audioState.recorder && audioState.recorder.state === 'recording') {
            audioState.recorder.stop();
        }
        
        if (audioState.audioContext && audioState.audioContext.state !== 'closed') {
            audioState.audioContext.close().catch(e => {
                console.warn('AudioContextのクローズに失敗:', e);
            });
        }
        
        if (unityState.connectionCheckInterval) {
            clearInterval(unityState.connectionCheckInterval);
        }
        
        if (conversationState.isActive) {
            resetConversationState();
        }
        
        stopAllAudio();
        
        console.log('リソースをクリーンアップしました');
    }
    
    // ====== デバッグ機能（拡張版） ======
    window.resetIntroduction = function() {
        introductionManager.reset();
        console.log('🔄 自己紹介状態をリセットしました');
    };
    
    window.testIntroduction = function() {
        requestIntroduction('manual_test');
    };
    
    window.getIntroductionStatus = function() {
        return {
            status: introductionManager.status,
            lastExecutionTime: introductionManager.lastExecutionTime,
            requesterLog: introductionManager.requesterLog
        };
    };
    
    // 会話記憶デバッグ関数
    window.getConversationMemory = function() {
        return {
            history: conversationMemory.getFullHistory(),
            currentTopic: conversationMemory.currentTopic,
            previousTopics: conversationMemory.previousTopics,
            summary: conversationMemory.getSummary()
        };
    };
    
    window.getVisitorData = function() {
        return {
            visitorId: visitorManager.visitorId,
            visitData: visitorManager.visitData
        };
    };
    
    window.clearVisitorData = function() {
        localStorage.removeItem('visitor_id');
        localStorage.removeItem('visit_data');
        console.log('🗑️ 訪問者データをクリアしました');
    };
    
    // 🎯 関係性レベルデバッグ関数
    window.getRelationshipLevel = function() {
        const count = visitorManager.visitData.totalConversations;
        const level = relationshipManager.calculateLevel(count);
        return {
            conversationCount: count,
            currentLevel: level,
            progress: relationshipManager.calculateProgress(level, count)
        };
    };
    
    window.setRelationshipLevel = function(conversationCount) {
        visitorManager.visitData.totalConversations = conversationCount;
        visitorManager.saveVisitData();
        const levelInfo = relationshipManager.calculateLevel(conversationCount);
        relationshipManager.updateUI(levelInfo, conversationCount);
        console.log(`🎯 関係性レベルを手動設定: ${conversationCount}会話 → Lv.${levelInfo.level}`);
    };
    
    window.testLevelUp = function() {
        const current = visitorManager.visitData.totalConversations;
        const nextLevel = relationshipManager.calculateLevel(current + 1);
        if (nextLevel.level > relationshipManager.calculateLevel(current).level) {
            setRelationshipLevel(current + 1);
        } else {
            // 次のレベルまでジャンプ
            const next = relationshipManager.levels.find(l => l.level > relationshipManager.calculateLevel(current).level);
            if (next) {
                setRelationshipLevel(next.minConversations);
            }
        }
    };
    
    // 🔇 音声デバッグ関数
    window.testSystemSounds = function() {
        console.log('🔊 システム音声テスト開始');
        const sounds = ['start', 'end', 'error', 'levelup'];
        let index = 0;
        
        const playNext = () => {
            if (index < sounds.length) {
                const soundName = sounds[index];
                console.log(`🎵 テスト: ${soundName}`);
                playSystemSound(soundName);
                index++;
                setTimeout(playNext, 1000);
            } else {
                console.log('✅ システム音声テスト完了');
            }
        };
        
        playNext();
    };
    
    window.getAudioState = function() {
        return {
            initialized: audioState.initialized,
            isMuted: audioState.isMuted,
            isRecording: audioState.isRecording,
            systemSounds: Object.keys(systemSounds).map(name => ({
                name: name,
                loaded: systemSounds[name] !== null,
                src: systemSounds[name] ? systemSounds[name].src : null
            }))
        };
    };
    
    // 🎯 サジェスチョンデバッグ関数
    window.getSelectedSuggestions = function() {
        return visitorManager.getSelectedSuggestions();
    };
    
    window.clearSelectedSuggestions = function() {
        visitorManager.visitData.selectedSuggestions = [];
        visitorManager.saveVisitData();
        console.log('🗑️ 選択済みサジェスチョンをクリアしました');
    };
    
    // ====== 初期化実行 ======
    document.addEventListener('DOMContentLoaded', initialize);
    
    if (window.location.search.includes('debug=1')) {
        appState.debugMode = true;
        console.log('デバッグモードが有効化されました');
    }
    
    console.log('🎬 Chat.js 完全修正版 読み込み完了');
})();