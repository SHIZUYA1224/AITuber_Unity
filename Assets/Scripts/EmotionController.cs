using System.Collections.Generic;
using UnityEngine;
using UniVRM10;

public class EmotionController : MonoBehaviour
{
    [SerializeField]
    private Vrm10Instance vrmInstance;
    [SerializeField]
    private float emotionHoldSeconds = 2.0f;
    private static readonly Dictionary<string, ExpressionPreset> EmotionMap = new()
    {
        {"normal", ExpressionPreset.neutral},
        {"neutral", ExpressionPreset.neutral},
        {"happy", ExpressionPreset.happy},
        {"angry", ExpressionPreset.angry},
        {"sad", ExpressionPreset.sad},
    };
    private float emotionTimer = 0f;
    private bool hasActiveEmotion = false;

    private void Update()
    {
        if (!hasActiveEmotion) return;
        emotionTimer -= Time.deltaTime;
        if (emotionTimer > 0f) return;

        ResetEmotionToNeutral();
        hasActiveEmotion = false;
    }

    public void SetEmotion(string emotion)
    {
        Debug.Log($"SetEmotion called with: '{emotion}'");

        if (vrmInstance == null)
        {
            vrmInstance = GetComponent<Vrm10Instance>();
            if (vrmInstance == null)
            {
                vrmInstance = FindAnyObjectByType<Vrm10Instance>();
            }
        }

        if (vrmInstance == null)
        {
            Debug.LogError("VRM10Instance is null! Please assign it in Inspector.");
            return;
        }

        var normalizedEmotion = NormalizeEmotionKey(emotion);
        if (string.IsNullOrEmpty(normalizedEmotion))
        {
            Debug.LogWarning($"Emotion key is empty. raw: '{emotion}'");
            return;
        }

        // 現在の表情をリセット
        Debug.Log("Resetting all expressions to 0");
        foreach (var item in EmotionMap)
        {
            vrmInstance.Runtime.Expression.SetWeight(ExpressionKey.CreateFromPreset(item.Value), 0.0f);
        }

        if (EmotionMap.TryGetValue(normalizedEmotion, out var preset))
        {
            Debug.Log($"Setting emotion: {normalizedEmotion} -> {preset} to 1.0");
            var key = ExpressionKey.CreateFromPreset(preset);
            vrmInstance.Runtime.Expression.SetWeight(key, 1.0f);
            emotionTimer = Mathf.Max(0f, emotionHoldSeconds);
            hasActiveEmotion = preset != ExpressionPreset.neutral;

            // 設定後の確認
            var currentValue = vrmInstance.Runtime.Expression.GetWeight(key);
            Debug.Log($"Confirmed {preset} weight: {currentValue}");
        }
        else
        {
            Debug.LogWarning($"Emotion '{normalizedEmotion}' not found. Available: {string.Join(", ", EmotionMap.Keys)}");
        }
    }

    private static string NormalizeEmotionKey(string rawEmotion)
    {
        if (string.IsNullOrWhiteSpace(rawEmotion)) return string.Empty;
        return rawEmotion.Trim().Trim('[', ']').ToLowerInvariant();
    }

    private void ResetEmotionToNeutral()
    {
        if (vrmInstance == null) return;

        foreach (var item in EmotionMap)
        {
            vrmInstance.Runtime.Expression.SetWeight(ExpressionKey.CreateFromPreset(item.Value), 0.0f);
        }
        vrmInstance.Runtime.Expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.neutral), 1.0f);
    }

    public void ForceNeutral()
    {
        hasActiveEmotion = false;
        emotionTimer = 0f;
        ResetEmotionToNeutral();
    }
}
