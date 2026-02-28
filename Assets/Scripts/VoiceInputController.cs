using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Chat
{
    public class VoiceInputController : MonoBehaviour
    {
        private int frequency = 48000;
        [SerializeField]
        private int maxRecordTime = 10;
        [SerializeField]
        private string micDeviceName;
        private AudioClip audioClip;
        private DateTime? recordStartTime = null;
        private bool _enabledRecord = false;
        [SerializeField]
        private float volumeThreshold = 0.1f;
        [SerializeField]
        private float silenceDuration = 1.0f;
        private float[] samples = new float[1024];
        private float lastActiveTime;
        private bool isDetectingVoice = false;

        public bool EnabledRecord
        {
            get => _enabledRecord;
            set
            {
                if (_enabledRecord != value)
                {
                    if (value)
                    {
                        StartRecord();
                    }
                    else
                    {
                        StopRecord();
                    }
                    _enabledRecord = value;
                }
            }
        }

        void Start()
        {
            foreach (var device in Microphone.devices)
            {
                Debug.Log(device);
            }

            if (string.IsNullOrEmpty(micDeviceName) || !Microphone.devices.Contains(micDeviceName))
            {
                if (Microphone.devices.Length > 0)
                {
                    micDeviceName = Microphone.devices[0];
                }
                else
                {
                    Debug.LogError("マイクが見つかりません。");
                }
            }
            EnabledRecord = true;
        }

        void Update()
        {
            if (!_enabledRecord || audioClip == null) return;

            float currentVolume = GetCurrentVolume();
            if (currentVolume >= volumeThreshold)
            {
                if (!isDetectingVoice)
                {
                    isDetectingVoice = true;
                    Debug.Log("発話を検出");
                }
                lastActiveTime = Time.time;
            }
            else if (isDetectingVoice && (Time.time - lastActiveTime > silenceDuration || DateTime.Now - recordStartTime.Value > TimeSpan.FromSeconds(maxRecordTime)))
            {
                StopCurrentRecording();
                StartRecord();
                Debug.Log("無音のため録音終了");
            }

            if (recordStartTime.HasValue && DateTime.Now - recordStartTime.Value > TimeSpan.FromSeconds(maxRecordTime))
            {
                StopRecord();
                StartRecord();
                Debug.Log("録音時間が最大値を超えたため録音終了");
            }
        }

        private float GetCurrentVolume()
        {
            if (audioClip == null) return 0f;
            int micPosition = Microphone.GetPosition(micDeviceName);
            if (micPosition < samples.Length) return 0f;

            int startPosition = Mathf.Max(0, micPosition - samples.Length);
            audioClip.GetData(samples, startPosition);

            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }
            return Mathf.Sqrt(sum / samples.Length);
        }

        private void StopCurrentRecording()
        {
            if (audioClip == null) return;
            Microphone.End(micDeviceName);
            Debug.Log("音声あり");
            byte[] wavData = WavUtility.FromAudioClip(audioClip);
            ChatController.Instance.SendChatMessageWithVoice(wavData);
        }

        private void StartRecord()
        {
            audioClip = Microphone.Start(micDeviceName, true, maxRecordTime, frequency);
            recordStartTime = DateTime.Now;
            isDetectingVoice = false;
            lastActiveTime = 0f;
        }

        private void StopRecord()
        {
            Microphone.End(micDeviceName);
            audioClip = null;
            recordStartTime = null;
            isDetectingVoice = false;
        }
    }

    public static class WavUtility
    {
        public static byte[] FromAudioClip(AudioClip clip)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            // Write WAV header
            writer.Write(0x46464952); // "RIFF"
            writer.Write(0); // ChunkSize
            writer.Write(0x45564157); // "WAVE"
            writer.Write(0x20746d66); // "fmt "
            writer.Write(16); // Subchunk1Size
            writer.Write((ushort)1); // AudioFormat
            writer.Write((ushort)clip.channels); // NumChannels
            writer.Write(clip.frequency); // SampleRate
            writer.Write(clip.frequency * clip.channels * 2); // ByteRate
            writer.Write((ushort)(clip.channels * 2)); // BlockAlign
            writer.Write((ushort)16); // BitsPerSample
            writer.Write(0x61746164); // "data"
            writer.Write(0); // Subchunk2Size

            // Write audio data
            float[] samples = new float[clip.samples];
            clip.GetData(samples, 0);
            short[] intData = new short[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                intData[i] = (short)(samples[i] * 32767f);
            }

            byte[] data = new byte[intData.Length * 2];
            Buffer.BlockCopy(intData, 0, data, 0, data.Length);
            writer.Write(data);

            // Update ChunkSize and Subchunk2Size fields
            writer.Seek(4, SeekOrigin.Begin);
            writer.Write((int)(stream.Length - 8));
            writer.Seek(40, SeekOrigin.Begin);
            writer.Write((int)(stream.Length - 44));

            writer.Close();
            stream.Close();
            return stream.ToArray();
        }
    }
}