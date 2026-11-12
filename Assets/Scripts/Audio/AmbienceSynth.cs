using System;

namespace Constellation
{
    /// <summary>
    /// 밤의 환경음을 코드로 합성한다(귀뚜라미, 바람, 먼 물소리). 녹음 파일을 쓰지 않으므로 라이선스 문제가 없다.
    /// Unity API를 쓰지 않아 백그라운드 스레드에서 계산할 수 있다. 결과는 이음매 없이 반복되는 모노 샘플이다.
    /// </summary>
    public static class AmbienceSynth
    {
        public const int SampleRate = 22050;

        const float k_Tau = 6.2831853f;

        // ------------------------------------------------------------------ 귀뚜라미

        /// <summary>
        /// 풀벌레 소리. 높은 음의 짧은 떨림 묶음을 여러 마리가 제각각의 박자로 반복하고, 바탕에 가는 울음이 깔린다.
        /// 모든 박자가 길이를 나누어떨어지게 해서 이음매 없이 반복된다.
        /// </summary>
        public static float[] Crickets(int seed)
        {
            const float seconds = 8f;
            int count = (int)(SampleRate * seconds);
            float[] samples = new float[count];
            Random random = new Random(seed);

            // 마리마다 (울음 주기, 한 번에 떠는 횟수)
            float[] periods = { 0.8f, 1.0f, 1.6f, 2.0f, 1.6f };
            int[] pulses = { 3, 4, 2, 3, 4 };
            const float pulseLength = 0.017f;
            const float pulseGap = 0.012f;

            for (int voice = 0; voice < periods.Length; voice++)
            {
                float frequency = 4100f + 260f * voice + (float)random.NextDouble() * 180f;
                float offset = (float)random.NextDouble() * periods[voice];
                float loudness = 0.07f + (float)random.NextDouble() * 0.07f;
                int chirps = (int)Math.Round(seconds / periods[voice]);
                for (int chirp = 0; chirp < chirps; chirp++)
                {
                    // 가끔 한 번씩 쉬어 기계처럼 들리지 않게 한다.
                    if (random.NextDouble() < 0.12)
                        continue;
                    float start = offset + chirp * periods[voice];
                    float level = loudness * (0.75f + 0.25f * (float)random.NextDouble());
                    for (int p = 0; p < pulses[voice]; p++)
                    {
                        int first = (int)((start + p * (pulseLength + pulseGap)) * SampleRate);
                        int length = (int)(pulseLength * SampleRate);
                        for (int i = 0; i < length; i++)
                        {
                            float window = MathF.Sin(MathF.PI * i / length);
                            float t = i / (float)SampleRate;
                            // 끝으로 갈수록 음이 살짝 내려간다.
                            float value = MathF.Sin(k_Tau * (frequency - 220f * t / pulseLength * 0.2f) * t) * window * window * level;
                            samples[(first + i) % count] += value;
                        }
                    }
                }
            }

            // 바탕에 깔리는 가는 울음(멀리 있는 벌레들)
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float flutter = 0.5f + 0.5f * MathF.Sin(k_Tau * 48f * t);
                float swell = 0.6f + 0.4f * MathF.Sin(k_Tau * t / 4f);
                samples[i] += MathF.Sin(k_Tau * 2900f * t) * flutter * flutter * swell * 0.018f;
                samples[i] += MathF.Sin(k_Tau * 3350f * t) * (1f - flutter) * (1f - swell * 0.5f) * 0.008f;
            }
            return samples;
        }

        // ------------------------------------------------------------------ 바람

        /// <summary>
        /// 낮고 부드러운 바람. 잡음을 걸러 낮은 소리만 남기고, 세기와 음색을 아주 느리게 바꾼다.
        /// </summary>
        public static float[] Wind(int seed)
        {
            const float seconds = 12f;
            const float fadeSeconds = 1.5f;
            int count = (int)(SampleRate * seconds);
            int fade = (int)(SampleRate * fadeSeconds);
            float[] raw = new float[count + fade];
            Random random = new Random(seed);

            float low1 = 0f;
            float low2 = 0f;
            for (int i = 0; i < raw.Length; i++)
            {
                float t = i / (float)SampleRate;
                // 반복 길이를 나누어떨어지는 느린 물결들
                float gust = 0.55f + 0.25f * MathF.Sin(k_Tau * t / 12f + 1f) + 0.15f * MathF.Sin(k_Tau * t / 6f + 2.3f) + 0.05f * MathF.Sin(k_Tau * t / 4f);
                float cutoff = 160f + 380f * gust;
                float k = 1f - MathF.Exp(-k_Tau * cutoff / SampleRate);
                float noise = (float)random.NextDouble() * 2f - 1f;
                low1 += (noise - low1) * k;
                low2 += (low1 - low2) * k;
                raw[i] = low2 * gust * gust;
            }
            return Normalize(CrossfadeLoop(raw, count, fade), 0.5f);
        }

        // ------------------------------------------------------------------ 물

        /// <summary>
        /// 멀리서 들리는 잔잔한 물소리. 걸러 낸 잡음이 물결처럼 밀려왔다 빠지고, 가끔 작은 물방울 소리가 섞인다.
        /// </summary>
        public static float[] Water(int seed)
        {
            const float seconds = 12f;
            const float fadeSeconds = 1.5f;
            int count = (int)(SampleRate * seconds);
            int fade = (int)(SampleRate * fadeSeconds);
            float[] raw = new float[count + fade];
            Random random = new Random(seed);

            float highState = 0f;
            float low = 0f;
            float low2 = 0f;
            float kHigh = 1f - MathF.Exp(-k_Tau * 300f / SampleRate);
            float kLow = 1f - MathF.Exp(-k_Tau * 1300f / SampleRate);
            for (int i = 0; i < raw.Length; i++)
            {
                float t = i / (float)SampleRate;
                float noise = (float)random.NextDouble() * 2f - 1f;
                // 낮은 소리를 빼고(고역 통과) 높은 소리를 줄여(저역 통과) 물 흐르는 대역만 남긴다.
                highState += (noise - highState) * kHigh;
                float band = noise - highState;
                low += (band - low) * kLow;
                low2 += (low - low2) * kLow;

                // 물결이 밀려오는 느낌: 주기가 다른 물결 셋
                float wave = Swell(t, 3f, 0.4f) * 0.5f + Swell(t, 4f, 1.9f) * 0.35f + Swell(t, 6f, 4.1f) * 0.3f;
                raw[i] = low2 * (0.12f + wave);
            }

            // 물방울 소리: 짧게 올라가는 음
            for (int n = 0; n < 34; n++)
            {
                int first = (int)(random.NextDouble() * count);
                float from = 420f + (float)random.NextDouble() * 500f;
                float level = 0.02f + (float)random.NextDouble() * 0.035f;
                int length = (int)((0.025f + (float)random.NextDouble() * 0.03f) * SampleRate);
                float phase = 0f;
                for (int i = 0; i < length; i++)
                {
                    float progress = i / (float)length;
                    phase += k_Tau * (from * (1f + 0.7f * progress)) / SampleRate;
                    float window = MathF.Sin(MathF.PI * progress);
                    raw[(first + i) % count] += MathF.Sin(phase) * window * window * level;
                }
            }
            return Normalize(CrossfadeLoop(raw, count, fade), 0.5f);
        }

        // 0에서 부드럽게 솟았다 꺼지는 물결 하나(주기마다 반복)
        static float Swell(float t, float period, float phase)
        {
            float s = MathF.Sin(k_Tau * t / period + phase);
            return s > 0f ? s * s * s : 0f;
        }

        // ------------------------------------------------------------------ 보조

        // 끝부분(count 뒤의 fade개)을 앞부분에 겹쳐 넣어, 끝에서 처음으로 넘어갈 때 끊기지 않게 한다.
        static float[] CrossfadeLoop(float[] raw, int count, int fade)
        {
            float[] result = new float[count];
            Array.Copy(raw, result, count);
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                result[i] = raw[i] * t + raw[count + i] * (1f - t);
            }
            return result;
        }

        static float[] Normalize(float[] samples, float peak)
        {
            float max = 0.0001f;
            for (int i = 0; i < samples.Length; i++)
                max = Math.Max(max, Math.Abs(samples[i]));
            float gain = peak / max;
            for (int i = 0; i < samples.Length; i++)
                samples[i] *= gain;
            return samples;
        }
    }
}
