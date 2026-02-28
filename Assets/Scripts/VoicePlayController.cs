using UnityEngine;
using System.Collections.Generic;
using UniVRM10;

namespace Chat
{
    public class VoicePlayController : MonoBehaviour
    {
        private struct QueuedVoice
        {
            public AudioClip Clip;
            public string EmotionKey;
        }

        private readonly List<QueuedVoice> _playWaitList = new();

        [SerializeField]
        private AudioSource _characterAudioSource;
        [SerializeField]
        private Vrm10Instance vrmInstance;
        [SerializeField]
        private EmotionController emotionController;
        [SerializeField]
        private float trimStart = 0f;
        [SerializeField]
        private float trimEnd = 0f;

        private float _audioStopTime;
        private bool _alreadyStopped = false;
        private bool _hasLoggedMissingAudioSource = false;
        [SerializeField]
        private bool useSimpleLipSyncFallback = true;
        [SerializeField]
        private float lipSyncSensitivity = 8f;
        [SerializeField]
        private float lipSyncMaxWeight = 1f;
        [SerializeField]
        private float lipSyncNoiseGate = 0.005f;
        private readonly float[] _outputSamples = new float[256];
        private readonly float[] _spectrum = new float[512];

        private void Awake()
        {
            ResolveAudioSourceIfNeeded();
        }

        public void AddAudioClipToWaitList(AudioClip clip)
        {
            AddAudioClipToWaitList(clip, null);
        }

        public void AddAudioClipToWaitList(AudioClip clip, string emotionKey)
        {
            if (clip == null) return;
            _playWaitList.Add(new QueuedVoice
            {
                Clip = clip,
                EmotionKey = emotionKey
            });
        }

        private void Update()
        {
            ResolveAudioSourceIfNeeded();
            if (_characterAudioSource == null) return;

            if (_playWaitList.Count <= 0 && _alreadyStopped)
                return;

            if (_characterAudioSource.isPlaying)
            {
                if (_characterAudioSource.time >= _audioStopTime)
                {
                    _characterAudioSource.Stop();
                    ForceNeutralIfIdle();
                    if (_playWaitList.Count <= 0)
                        _alreadyStopped = true;
                }
            }
            else
            {
                if (_playWaitList.Count <= 0)
                    return;

                var queued = _playWaitList[0];
                _alreadyStopped = false;
                _characterAudioSource.clip = queued.Clip;
                _playWaitList.RemoveAt(0);
                _audioStopTime = _characterAudioSource.clip.length - trimStart - trimEnd;
                if (_audioStopTime < trimStart) _audioStopTime = _characterAudioSource.clip.length;
                _characterAudioSource.time = trimStart;
                ApplyEmotionIfNeeded(queued.EmotionKey);
                _characterAudioSource.Play();
            } 
        }

        private void LateUpdate()
        {
            ApplySimpleLipSyncFallback();
        }

        private void ResolveAudioSourceIfNeeded()
        {
            if (_characterAudioSource != null) return;

            _characterAudioSource = GetComponent<AudioSource>();
            if (_characterAudioSource == null)
            {
                _characterAudioSource = FindAnyObjectByType<AudioSource>();
            }

            if (_characterAudioSource == null && !_hasLoggedMissingAudioSource)
            {
                _hasLoggedMissingAudioSource = true;
                Debug.LogWarning("AudioSource is not assigned on VoicePlayController.");
            }
        }

        private void ApplySimpleLipSyncFallback()
        {
            if (!useSimpleLipSyncFallback) return;
            if (vrmInstance == null)
            {
                vrmInstance = GetComponent<Vrm10Instance>();
                if (vrmInstance == null)
                {
                    vrmInstance = FindAnyObjectByType<Vrm10Instance>();
                }
            }
            if (vrmInstance == null || _characterAudioSource == null) return;
            if (!_characterAudioSource.isPlaying)
            {
                ResetVowelWeights(vrmInstance.Runtime.Expression);
                return;
            }

            _characterAudioSource.GetOutputData(_outputSamples, 0);
            float sum = 0f;
            for (int i = 0; i < _outputSamples.Length; i++)
            {
                float s = _outputSamples[i];
                sum += s * s;
            }
            float rms = Mathf.Sqrt(sum / _outputSamples.Length);
            float openAmount = Mathf.Clamp01((rms - lipSyncNoiseGate) * lipSyncSensitivity) * lipSyncMaxWeight;

            var expression = vrmInstance.Runtime.Expression;
            ResetVowelWeights(expression);

            if (openAmount <= 0f) return;

            // Rough vowel estimation from spectral bands.
            _characterAudioSource.GetSpectrumData(_spectrum, 0, FFTWindow.BlackmanHarris);
            float sampleRate = AudioSettings.outputSampleRate;
            float aa = GetBandEnergy(sampleRate, 500f, 900f);
            float ih = GetBandEnergy(sampleRate, 1800f, 2800f);
            float ou = GetBandEnergy(sampleRate, 300f, 700f);
            float ee = GetBandEnergy(sampleRate, 2200f, 3600f);
            float oh = GetBandEnergy(sampleRate, 700f, 1300f);

            float total = aa + ih + ou + ee + oh;
            if (total <= 1e-6f)
            {
                expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.aa), openAmount);
                return;
            }

            expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.aa), openAmount * (aa / total));
            expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.ih), openAmount * (ih / total));
            expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.ou), openAmount * (ou / total));
            expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.ee), openAmount * (ee / total));
            expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.oh), openAmount * (oh / total));
        }

        private static void ResetVowelWeights(Vrm10RuntimeExpression expression)
        {
            expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.aa), 0f);
            expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.ih), 0f);
            expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.ou), 0f);
            expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.ee), 0f);
            expression.SetWeight(ExpressionKey.CreateFromPreset(ExpressionPreset.oh), 0f);
        }

        private float GetBandEnergy(float sampleRate, float hzMin, float hzMax)
        {
            int n = _spectrum.Length;
            float nyquist = sampleRate * 0.5f;
            int min = Mathf.Clamp(Mathf.FloorToInt((hzMin / nyquist) * n), 0, n - 1);
            int max = Mathf.Clamp(Mathf.CeilToInt((hzMax / nyquist) * n), min, n - 1);

            float sum = 0f;
            for (int i = min; i <= max; i++)
            {
                sum += _spectrum[i];
            }
            return sum;
        }

        private void ApplyEmotionIfNeeded(string emotionKey)
        {
            if (string.IsNullOrWhiteSpace(emotionKey)) return;

            if (emotionController == null)
            {
                emotionController = GetComponent<EmotionController>();
                if (emotionController == null)
                {
                    emotionController = FindAnyObjectByType<EmotionController>();
                }
            }
            if (emotionController == null) return;

            emotionController.SetEmotion(emotionKey);
        }

        private void ForceNeutralIfIdle()
        {
            if (_playWaitList.Count > 0) return;
            if (emotionController == null) return;
            emotionController.ForceNeutral();
        }
    }
}
