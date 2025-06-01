using UnityEngine;
using System;
using System.IO;

// WavUtilityクラスをスタティックとして定義
public static class WavUtility
{
    // WAVデータの変換エラーを表すフラグ
    public static bool LastConversionFailed { get; private set; } = false;

    // WAVデータをAudioClipに変換するメソッド（より堅牢な処理を追加）
    public static AudioClip ToAudioClip(byte[] wavData, string name = "audio")
    {
        // エラーフラグをリセット
        LastConversionFailed = false;

        if (wavData == null || wavData.Length < 44) // WAVヘッダは最低44バイト
        {
            Debug.LogError("WavUtility: 無効なWAVデータです（データが小さすぎます）");
            LastConversionFailed = true;
            return CreateFallbackAudioClip(name);
        }

        try
        {
            // WAVデータを変換
            using (MemoryStream stream = new MemoryStream(wavData))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                // WAVフォーマットをチェック
                string riff = new string(reader.ReadChars(4));
                if (riff != "RIFF")
                {
                    Debug.LogWarning("WavUtility: RIFFヘッダがありません - フォーマットチェックをスキップ");
                    // フォーマットチェックをスキップして直接データとして扱う試行
                    return CreateAudioClipFromRawData(wavData, name);
                }

                // ファイルサイズを読み取り
                int fileSize = reader.ReadInt32();
                string format = new string(reader.ReadChars(4));
                if (format != "WAVE")
                {
                    Debug.LogWarning("WavUtility: WAVEフォーマットではありません - 代替処理を試行");
                    return CreateAudioClipFromRawData(wavData, name);
                }

                // データチャンクを探す
                bool foundData = false;
                int channels = 1;        // デフォルト値
                int sampleRate = 44100;  // デフォルト値
                int bitsPerSample = 16;  // デフォルト値
                byte[] audioData = null;

                while (stream.Position < stream.Length)
                {
                    try
                    {
                        string chunkID = new string(reader.ReadChars(4));
                        int chunkSize = reader.ReadInt32();

                        if (chunkID == "fmt ")
                        {
                            // フォーマットチャンクを処理
                            int formatType = reader.ReadInt16(); // 1 = PCM
                            channels = reader.ReadInt16();
                            sampleRate = reader.ReadInt32();
                            int byteRate = reader.ReadInt32();
                            int blockAlign = reader.ReadInt16();
                            bitsPerSample = reader.ReadInt16();

                            // 追加パラメータがある場合はスキップ
                            if (chunkSize > 16)
                            {
                                reader.ReadBytes(chunkSize - 16);
                            }
                        }
                        else if (chunkID == "data")
                        {
                            // データチャンクを処理
                            foundData = true;
                            
                            // データ長が異常に大きい場合のガード
                            if (chunkSize < 0 || chunkSize > 100 * 1024 * 1024) // 100MB制限
                            {
                                Debug.LogWarning($"WavUtility: データチャンクサイズが異常です: {chunkSize} バイト");
                                chunkSize = Math.Min(chunkSize, (int)(stream.Length - stream.Position));
                            }

                            audioData = reader.ReadBytes(chunkSize);
                            break; // データを見つけたら終了
                        }
                        else
                        {
                            // その他のチャンクはスキップ
                            if (chunkSize < 0 || chunkSize > 100 * 1024 * 1024) // 異常なサイズのガード
                            {
                                Debug.LogWarning($"WavUtility: 異常なチャンクサイズ: {chunkID} = {chunkSize} バイト");
                                chunkSize = 0;
                            }
                            reader.ReadBytes(chunkSize);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"WavUtility: チャンク処理中のエラー: {ex.Message}");
                        LastConversionFailed = true;
                        return CreateFallbackAudioClip(name);
                    }
                }

                if (!foundData || audioData == null || audioData.Length == 0)
                {
                    Debug.LogError("WavUtility: 有効なオーディオデータが見つかりませんでした");
                    LastConversionFailed = true;
                    return CreateFallbackAudioClip(name);
                }

                // オーディオデータをfloat配列に変換
                float[] samples = ConvertByteArrayToFloat(audioData, bitsPerSample);
                if (samples == null || samples.Length == 0)
                {
                    Debug.LogError("WavUtility: オーディオデータの変換に失敗しました");
                    LastConversionFailed = true;
                    return CreateFallbackAudioClip(name);
                }

                // AudioClipを作成
                AudioClip audioClip = AudioClip.Create(name, samples.Length / channels, channels, sampleRate, false);
                audioClip.SetData(samples, 0);
                
                Debug.Log($"WavUtility: AudioClip作成成功 - 長さ: {audioClip.length}秒, チャンネル: {channels}, サンプルレート: {sampleRate}Hz");
                return audioClip;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"WavUtility: WAVデータの変換に失敗しました: {ex.Message}");
            LastConversionFailed = true;
            return CreateFallbackAudioClip(name);
        }
    }

    // バイト配列をfloat配列に変換
    private static float[] ConvertByteArrayToFloat(byte[] audioData, int bitsPerSample)
    {
        if (audioData == null || audioData.Length == 0)
            return null;

        try
        {
            float[] samples;
            
            if (bitsPerSample == 8)
            {
                // 8ビットデータ (0-255) から float (-1.0 - 1.0) に変換
                samples = new float[audioData.Length];
                for (int i = 0; i < audioData.Length; i++)
                {
                    samples[i] = (audioData[i] - 128) / 128f;
                }
            }
            else if (bitsPerSample == 16)
            {
                // 16ビットデータを float に変換
                samples = new float[audioData.Length / 2];
                for (int i = 0; i < samples.Length; i++)
                {
                    short sample = (short)((audioData[i * 2 + 1] << 8) | audioData[i * 2]);
                    samples[i] = sample / 32768f;
                }
            }
            else if (bitsPerSample == 24)
            {
                // 24ビットデータを float に変換
                samples = new float[audioData.Length / 3];
                for (int i = 0; i < samples.Length; i++)
                {
                    int sample = (audioData[i * 3 + 2] << 16) | (audioData[i * 3 + 1] << 8) | audioData[i * 3];
                    // 24ビット整数を符号付きに変換
                    if ((sample & 0x800000) != 0)
                        sample = sample | ~0xFFFFFF; // 符号拡張
                    samples[i] = sample / 8388608f;
                }
            }
            else if (bitsPerSample == 32)
            {
                // 32ビットデータを float に変換 (整数形式と仮定)
                samples = new float[audioData.Length / 4];
                for (int i = 0; i < samples.Length; i++)
                {
                    int sample = (audioData[i * 4 + 3] << 24) | (audioData[i * 4 + 2] << 16) | 
                                (audioData[i * 4 + 1] << 8) | audioData[i * 4];
                    samples[i] = sample / 2147483648f;
                }
            }
            else
            {
                Debug.LogError($"WavUtility: サポートされていないビット深度: {bitsPerSample}");
                return null;
            }
            
            return samples;
        }
        catch (Exception ex)
        {
            Debug.LogError($"WavUtility: バイト配列の変換エラー: {ex.Message}");
            return null;
        }
    }

    // 生データから直接AudioClipを作成する試み
    private static AudioClip CreateAudioClipFromRawData(byte[] rawData, string name)
    {
        try
        {
            // 単純に16bit PCM、44.1kHz、モノラルと仮定
            int sampleRate = 44100;
            int channels = 1;
            int bitsPerSample = 16;
            
            // ヘッダーをスキップする（一般的なヘッダーサイズ）
            byte[] audioData = new byte[rawData.Length - 44];
            Array.Copy(rawData, 44, audioData, 0, audioData.Length);
            
            // オーディオデータをfloat配列に変換
            float[] samples = ConvertByteArrayToFloat(audioData, bitsPerSample);
            if (samples == null || samples.Length == 0)
            {
                Debug.LogError("WavUtility: 生データの変換に失敗しました");
                return CreateFallbackAudioClip(name);
            }
            
            // AudioClipを作成
            AudioClip audioClip = AudioClip.Create(name, samples.Length / channels, channels, sampleRate, false);
            audioClip.SetData(samples, 0);
            
            Debug.Log($"WavUtility: 生データからAudioClip作成 - 長さ: {audioClip.length}秒");
            return audioClip;
        }
        catch (Exception ex)
        {
            Debug.LogError($"WavUtility: 生データからのAudioClip作成に失敗: {ex.Message}");
            return CreateFallbackAudioClip(name);
        }
    }

    // 代替のダミーAudioClipを作成
    private static AudioClip CreateFallbackAudioClip(string name)
    {
        try
        {
            // 1秒間のサイレントクリップを作成
            int sampleRate = 44100;
            int channels = 1;
            int samples = sampleRate;
            AudioClip audioClip = AudioClip.Create(name + "_fallback", samples, channels, sampleRate, false);
            
            // 無音データを設定
            float[] silentData = new float[samples];
            audioClip.SetData(silentData, 0);
            
            Debug.Log("WavUtility: フォールバックの無音AudioClipを作成しました");
            return audioClip;
        }
        catch (Exception ex)
        {
            Debug.LogError($"WavUtility: フォールバックAudioClip作成に失敗: {ex.Message}");
            return null;
        }
    }
}