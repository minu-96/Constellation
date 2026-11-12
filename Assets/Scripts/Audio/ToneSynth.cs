using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 간단한 소리를 코드로 만든다(사인파를 겹쳐 만든 종소리, 화음, 배경음).
    /// </summary>
    public static class ToneSynth
    {
        const float k_Tau = Mathf.PI * 2f;

        static AudioClip Build(string name, float[] samples, int sampleRate)
        {
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>맑은 종소리. 기본음에 어긋난 배음을 얹고 빠르게 줄어들게 한다.</summary>
        public static AudioClip Bell(int sampleRate, float frequency, float seconds, float brightness)
        {
            int count = Mathf.CeilToInt(sampleRate * seconds);
            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)sampleRate;
                float attack = Mathf.Clamp01(t / 0.004f);
                float value =
                    Mathf.Sin(k_Tau * frequency * t) * Mathf.Exp(-t * 3.2f)
                    + 0.45f * brightness * Mathf.Sin(k_Tau * frequency * 2.76f * t) * Mathf.Exp(-t * 6.5f)
                    + 0.25f * brightness * Mathf.Sin(k_Tau * frequency * 5.4f * t) * Mathf.Exp(-t * 11f)
                    + 0.2f * Mathf.Sin(k_Tau * frequency * 2f * t) * Mathf.Exp(-t * 4.5f);
                samples[i] = value * attack * 0.32f * FadeOut(i, count, sampleRate);
            }
            return Build("Bell", samples, sampleRate);
        }

        /// <summary>아주 짧고 부드러운 틱 소리(핀치).</summary>
        public static AudioClip Tick(int sampleRate, float frequency, float seconds)
        {
            int count = Mathf.CeilToInt(sampleRate * seconds);
            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)sampleRate;
                float attack = Mathf.Clamp01(t / 0.002f);
                float value = Mathf.Sin(k_Tau * frequency * t) * Mathf.Exp(-t * 70f)
                    + 0.4f * Mathf.Sin(k_Tau * frequency * 0.5f * t) * Mathf.Exp(-t * 45f);
                samples[i] = value * attack * 0.3f * FadeOut(i, count, sampleRate);
            }
            return Build("Tick", samples, sampleRate);
        }

        /// <summary>여러 음을 조금씩 간격을 두고 차례로 울리는 화음.</summary>
        public static AudioClip Chord(int sampleRate, float[] frequencies, float stagger, float seconds)
        {
            int count = Mathf.CeilToInt(sampleRate * seconds);
            float[] samples = new float[count];
            for (int n = 0; n < frequencies.Length; n++)
            {
                float start = n * stagger;
                float frequency = frequencies[n];
                for (int i = Mathf.FloorToInt(start * sampleRate); i < count; i++)
                {
                    float t = i / (float)sampleRate - start;
                    float attack = Mathf.Clamp01(t / 0.006f);
                    float value =
                        Mathf.Sin(k_Tau * frequency * t) * Mathf.Exp(-t * 2.2f)
                        + 0.3f * Mathf.Sin(k_Tau * frequency * 2f * t) * Mathf.Exp(-t * 3.5f)
                        + 0.18f * Mathf.Sin(k_Tau * frequency * 3.01f * t) * Mathf.Exp(-t * 6f);
                    samples[i] += value * attack * 0.2f;
                }
            }
            for (int i = 0; i < count; i++)
                samples[i] = Mathf.Clamp(samples[i], -1f, 1f) * FadeOut(i, count, sampleRate);
            return Build("Chord", samples, sampleRate);
        }

        /// <summary>음이 미끄러지듯 내려가거나 올라가는 짧은 소리(되돌리기).</summary>
        public static AudioClip Sweep(int sampleRate, float fromFrequency, float toFrequency, float seconds)
        {
            int count = Mathf.CeilToInt(sampleRate * seconds);
            float[] samples = new float[count];
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float progress = i / (float)count;
                float frequency = Mathf.Lerp(fromFrequency, toFrequency, progress);
                phase += k_Tau * frequency / sampleRate;
                float envelope = Mathf.Sin(progress * Mathf.PI);
                samples[i] = Mathf.Sin(phase) * envelope * 0.25f;
            }
            return Build("Sweep", samples, sampleRate);
        }

        /// <summary>
        /// 잔잔한 배경음. 낮은 화음을 아주 느리게 일렁이게 한다.
        /// 모든 주파수가 길이 안에서 정수 번 진동하도록 맞춰 이음매 없이 반복된다.
        /// </summary>
        public static AudioClip Ambient(int sampleRate)
        {
            const float seconds = 16f;
            int count = Mathf.CeilToInt(sampleRate * seconds);
            float[] samples = new float[count];

            // 레(D) 중심의 열린 화음. 살짝 어긋난 음을 겹쳐 맥놀이를 만든다.
            float[] frequencies = { 73.42f, 110f, 146.83f, 220f, 293.66f, 329.63f, 440f };
            float[] gains = { 0.22f, 0.2f, 0.18f, 0.12f, 0.08f, 0.06f, 0.035f };
            float[] wobble = { 1f, 2f, 3f, 2f, 5f, 3f, 4f };

            for (int n = 0; n < frequencies.Length; n++)
            {
                // 반복 길이 안에서 정수 번 진동하도록 주파수를 맞춘다.
                float cycles = Mathf.Round(frequencies[n] * seconds);
                float detunedCycles = cycles + 3f;
                float lfoCycles = wobble[n];
                for (int i = 0; i < count; i++)
                {
                    float x = i / (float)count;
                    float lfo = 0.6f + 0.4f * Mathf.Sin(k_Tau * lfoCycles * x + n);
                    float value = Mathf.Sin(k_Tau * cycles * x) + 0.6f * Mathf.Sin(k_Tau * detunedCycles * x + 1.3f);
                    samples[i] += value * gains[n] * lfo * 0.5f;
                }
            }

            for (int i = 0; i < count; i++)
                samples[i] = Mathf.Clamp(samples[i], -1f, 1f);
            return Build("Ambient", samples, sampleRate);
        }

        // 끝에서 갑자기 끊겨 '틱' 소리가 나지 않게 마지막 20ms를 줄인다.
        static float FadeOut(int index, int count, int sampleRate)
        {
            int tail = Mathf.Max(1, sampleRate / 50);
            int remaining = count - 1 - index;
            return remaining >= tail ? 1f : remaining / (float)tail;
        }
    }
}
