using UnityEngine;
using UnityEngine.UIElements;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine.Networking;
using System.Text.RegularExpressions;

namespace Chat
{
    public class ChatController : MonoBehaviour
    {
        // シングルトンパターン
        public static ChatController Instance { get; private set; }

        private string chatId;
        private const string BASE_URL = "http://localhost:8000";
        private static readonly HttpClient httpClient = new();
        [SerializeField]
        private UIDocument uiDocument;
        private VisualElement rootVisualElement;
        [SerializeField]
        private VoicePlayController voicePlayController;
        [SerializeField]
        private EmotionController emotionController;
        private bool hasLoggedMissingEmotionController;
        private bool hasLoggedMissingVoicePlayController;

        private async UniTask<AudioClip> DownloadWav(string wavId)
        {
            var wavUrl = $"{BASE_URL}/voice_wav/{wavId}.wav";
            using UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(wavUrl, AudioType.WAV);
            await www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                AudioClip downloadedClip = DownloadHandlerAudioClip.GetContent(www);
                return downloadedClip;
            }
            else
            {
                Debug.LogError($"Failed to download WAV file: {www.error}");
                throw new Exception($"Failed to download WAV file: {www.error}");
            }
        }


        // Start is called once before the first execution of Update after the MonoBehaviour is created
        async void Start()
        {
            Instance = this;
            ResolveControllersIfNeeded();

            if (uiDocument == null)
            {
                Debug.LogError("UIDocument is not assigned on ChatController.");
                return;
            }

            rootVisualElement = uiDocument.rootVisualElement;

            Debug.Log("Starting chat initialization...");
            try
            {
                chatId = await StartChat();
                Debug.Log($"Chat initialization completed. Chat ID: {chatId}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize chat: {ex.Message}");
            }

            var messageInput = rootVisualElement.Q<TextField>("MessageInput");
            if (messageInput != null)
            {
                messageInput.RegisterCallback<KeyDownEvent>(OnMessageInputKeyDown);
                Debug.Log("Message input event registered successfully");
            }
            else
            {
                Debug.LogError("MessageInput TextField not found in UI");
            }
        }

        // Update is called once per frame
        private async UniTask<string> StartChat()
        {
            string url = $"{BASE_URL}/start_chat";
            var content = new StringContent(JsonSerializer.Serialize(new { character_id = "hoge" }), Encoding.UTF8, "application/json");

            try
            {
                HttpResponseMessage response = await httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();
                string responseBody = await response.Content.ReadAsStringAsync();
                var startchatResponse = JsonSerializer.Deserialize<StartChatResponse>(responseBody);
                string chatId = startchatResponse.ChatId;
                Debug.Log($"Chat started with ID: {chatId}");
                return chatId;
            }
            catch (HttpRequestException e)
            {
                Debug.LogError($"Error starting chat: {e.Message}");
                return null;
            }

        }

        public async UniTask SendChatMessage(string message)
        {
            ResolveControllersIfNeeded();

            if (string.IsNullOrEmpty(chatId))
            {
                Debug.LogError("Chat ID is not set. Cannot send message.");
                return;
            }

            await UniTask.SwitchToMainThread();
            var _ = new ChatMessageView(
                "ユーザ",
                message,
                rootVisualElement);

            ChatMessageView aiMessage = new("EMA", "", rootVisualElement);
            await UniTask.SwitchToThreadPool();

            string url = $"{BASE_URL}/chat/{chatId}";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(JsonSerializer.Serialize(new { content = message }), Encoding.UTF8, "application/json");
            string responseContent = "";
            string latestEmotionKey = null;
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead
            );
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync();
            var reader = new StreamReader(stream);

            await foreach (var line in ReadLinesAsync(reader))
            {
                if (string.IsNullOrEmpty(line)) continue;
                ChatResponse chatResponse;
                try
                {
                    chatResponse = JsonSerializer.Deserialize<ChatResponse>(line);
                }
                catch (JsonException ex)
                {
                    Debug.LogWarning($"Failed to parse chat response line: {line}. Error: {ex.Message}");
                    continue;
                }

                if (chatResponse == null)
                {
                    Debug.LogWarning($"Received null chat response. line: {line}");
                    continue;
                }
                if (chatResponse.Status == "finished")
                {
                    break;
                }

                if (!string.IsNullOrEmpty(chatResponse.Content))
                {
                    responseContent += chatResponse.Content;

                    await UniTask.SwitchToMainThread();

                    Debug.Log($"Full response content: '{responseContent}'");
                    Debug.Log($"ChatResponse.Content: '{chatResponse.Content}'");

                    // emotionタグを抽出
                    var emotionKey = ExtractEmotionKey(chatResponse.Content);
                    if (string.IsNullOrEmpty(emotionKey))
                    {
                        emotionKey = ExtractEmotionKey(responseContent);
                    }
                    if (!string.IsNullOrEmpty(emotionKey))
                    {
                        latestEmotionKey = emotionKey;
                        Debug.Log($"emotion: {emotionKey}");
                    }
                    else
                    {
                        Debug.Log("emotionタグが見つかりません");
                    }

                    aiMessage.Content = RemoveEmotionTags(responseContent);

                    if (!string.IsNullOrEmpty(chatResponse.Wav))
                    {
                        var clip = await DownloadWav(chatResponse.Wav);
                        if (voicePlayController != null)
                        {
                            voicePlayController.AddAudioClipToWaitList(clip, latestEmotionKey);
                        }
                        else if (!hasLoggedMissingVoicePlayController)
                        {
                            hasLoggedMissingVoicePlayController = true;
                            Debug.LogWarning("VoicePlayController is not assigned on ChatController.");
                        }
                    }

                    await UniTask.SwitchToThreadPool();
                }
            }
        }

        private static string ExtractEmotionKey(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            var bracketMatches = Regex.Matches(text, @"\[(normal|neutral|happy|angry|sad)\]", RegexOptions.IgnoreCase);
            if (bracketMatches.Count > 0)
            {
                var last = bracketMatches[bracketMatches.Count - 1];
                return last.Groups[1].Value;
            }

            var keyValueMatches = Regex.Matches(text, @"emotion\s*[:=]\s*(normal|neutral|happy|angry|sad)", RegexOptions.IgnoreCase);
            if (keyValueMatches.Count > 0)
            {
                var last = keyValueMatches[keyValueMatches.Count - 1];
                return last.Groups[1].Value;
            }

            return null;
        }

        private static string RemoveEmotionTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            // Remove bracket tags like [happy], [sad], etc. from UI text only.
            return Regex.Replace(text, @"\[(normal|neutral|happy|angry|sad)\]", "", RegexOptions.IgnoreCase).TrimStart();
        }

        private void ResolveControllersIfNeeded()
        {
            if (emotionController == null)
            {
                emotionController = GetComponent<EmotionController>();
                if (emotionController == null)
                {
                    emotionController = FindAnyObjectByType<EmotionController>();
                }
            }

            if (voicePlayController == null)
            {
                voicePlayController = GetComponent<VoicePlayController>();
                if (voicePlayController == null)
                {
                    voicePlayController = FindAnyObjectByType<VoicePlayController>();
                }
            }
        }

        private static async IAsyncEnumerable<string> ReadLinesAsync(StreamReader reader)
        {
            while (!reader.EndOfStream)
            {
                yield return await reader.ReadLineAsync();
            }


        }

        private void OnMessageInputKeyDown(KeyDownEvent evt)
        {
            bool isWindows =
            Application.platform == RuntimePlatform.WindowsPlayer ||
            Application.platform == RuntimePlatform.WindowsEditor;

            bool isMac =
            Application.platform == RuntimePlatform.OSXPlayer ||
            Application.platform == RuntimePlatform.OSXEditor;

            bool isEnterKeyPressed =
            evt.keyCode == KeyCode.Return ||
            evt.keyCode == KeyCode.KeypadEnter;

            bool isModifierKeyPressed =
            ((isWindows && evt.modifiers.HasFlag(EventModifiers.Control)) ||
            (isMac && evt.modifiers.HasFlag(EventModifiers.Command)));

            if (isEnterKeyPressed && !isModifierKeyPressed)
            {
                var messageInput = rootVisualElement.Q<TextField>("MessageInput");
                string message = messageInput.value;
                if (!string.IsNullOrEmpty(message))
                {
                    SendChatMessage(message).Forget();
                    messageInput.value = string.Empty;
                }
            }
            evt.StopPropagation();
        }

        private class StartChatResponse
        {
            [JsonPropertyName("chat_id")]
            public string ChatId { get; set; }
        }
        // /chat/{chatId} のJSONレスポンスのモデル定義
        private class ChatResponse
        {
            [JsonPropertyName("content")]
            public string Content { get; set; }
            [JsonPropertyName("status")]
            public string Status { get; set; }
            [JsonPropertyName("wav")]
            public string Wav { get; set; }
        }

        // 音声データをサーバーに送信して文字起こしを行うメソッド
        public async UniTask SendChatMessageWithVoice(byte[] wavData)
        {
            string url = $"{BASE_URL}/voice2text";

            // MultipartFormDataContentを作成
            using var formData = new MultipartFormDataContent();
            var audioContent = new ByteArrayContent(wavData);
            formData.Add(audioContent, "file", "audio.wav");

            try
            {
                var response = await httpClient.PostAsync(url, formData);
                response.EnsureSuccessStatusCode();
                var responseBody = await response.Content.ReadAsStringAsync();
                var transcriptionResponse = JsonSerializer.Deserialize<TranscriptionResponse>(responseBody);
                await SendChatMessage(transcriptionResponse.Text);
            }
            catch (HttpRequestException e)
            {
                Debug.LogError($"Error sending voice data: {e.Message}");
            }
        }

        // レスポンスのモデル定義
        private class TranscriptionResponse
        {
            [JsonPropertyName("text")]
            public string Text { get; set; }
        }
    }
}
