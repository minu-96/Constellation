using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 배경음과 효과음(F-14). 소리 파일 없이 코드로 만든 톤을 쓴다.
    /// 나중에 파일로 바꾸려면 ConstellationConfig의 AudioClip 칸에 넣으면 된다(비어 있으면 만든 소리를 쓴다).
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        const int k_SampleRate = 44100;
        const int k_EffectVoices = 6;

        // 별을 이을 때마다 한 음씩 올라가는 5음 음계(도 레 미 솔 라 …), 가온 다(C5) 기준 반음 수
        static readonly int[] k_Scale = { 0, 2, 4, 7, 9, 12, 14, 16, 19 };
        const float k_BaseFrequency = 523.25f;

        ConstellationConfig m_Config;
        AudioSource m_Ambient;
        AudioSource[] m_Effects;
        int m_NextVoice;
        float m_AmbientTarget;

        AudioClip m_AmbientClip;
        AudioClip m_BellClip;
        AudioClip m_SoftBellClip;
        AudioClip m_UndoClip;
        AudioClip m_PinchClip;
        AudioClip m_SelectClip;
        AudioClip m_CompleteClip;
        AudioClip m_ResultClip;

        public void Init(ConstellationConfig config, Transform listener)
        {
            m_Config = config;
            // 소리는 머리를 따라다니게 해서 어디를 보든 고르게 들리게 한다.
            transform.SetParent(listener, false);

            m_AmbientClip = config.ambientClip != null ? config.ambientClip : ToneSynth.Ambient(k_SampleRate);
            m_BellClip = config.starGrabClip != null ? config.starGrabClip : ToneSynth.Bell(k_SampleRate, k_BaseFrequency, 1.6f, 1f);
            m_SoftBellClip = config.lineConnectClip != null ? config.lineConnectClip : ToneSynth.Bell(k_SampleRate, k_BaseFrequency, 1.2f, 0.6f);
            m_UndoClip = config.undoClip != null ? config.undoClip : ToneSynth.Sweep(k_SampleRate, 620f, 300f, 0.28f);
            m_PinchClip = config.pinchClip != null ? config.pinchClip : ToneSynth.Tick(k_SampleRate, 1900f, 0.07f);
            m_SelectClip = config.keywordSelectClip != null ? config.keywordSelectClip : ToneSynth.Chord(k_SampleRate, new[] { 523.25f, 783.99f }, 0.09f, 1.4f);
            m_CompleteClip = config.completeClip != null ? config.completeClip : ToneSynth.Chord(k_SampleRate, new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.11f, 2.2f);
            m_ResultClip = config.resultClip != null ? config.resultClip : ToneSynth.Chord(k_SampleRate, new[] { 392f, 523.25f, 659.25f, 987.77f }, 0.22f, 3.2f);

            m_Ambient = CreateSource("Ambient");
            m_Ambient.clip = m_AmbientClip;
            m_Ambient.loop = true;
            m_Ambient.volume = 0f;
            m_AmbientTarget = config.ambientVolume;
            m_Ambient.Play();

            // 효과음이 겹쳐도 끊기지 않게 소스를 여러 개 돌려 쓴다.
            m_Effects = new AudioSource[k_EffectVoices];
            for (int i = 0; i < k_EffectVoices; i++)
                m_Effects[i] = CreateSource("Effect " + i);
        }

        AudioSource CreateSource(string sourceName)
        {
            GameObject go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            return source;
        }

        /// <summary>별을 잡을 때의 맑은 종소리.</summary>
        public void PlayStarGrab(int starNumber)
        {
            Play(m_BellClip, PitchFor(starNumber, m_Config.starGrabClip != null), 1f);
        }

        /// <summary>별을 이을 때. 이을수록 음이 조금씩 올라간다.</summary>
        public void PlayLineConnect(int starNumber)
        {
            bool custom = m_Config.starGrabClip != null || m_Config.lineConnectClip != null;
            Play(m_BellClip, PitchFor(starNumber, custom), 0.9f);
            Play(m_SoftBellClip, PitchFor(starNumber, custom) * 1.5f, 0.35f);
        }

        /// <summary>핀치하는 순간의 아주 작은 틱 소리.</summary>
        public void PlayPinch()
        {
            if (m_Config.pinchVolume > 0.001f)
                Play(m_PinchClip, 1f, m_Config.pinchVolume);
        }

        public void PlayUndo()
        {
            Play(m_UndoClip, 1f, 0.7f);
        }

        public void PlayKeywordSelect()
        {
            Play(m_SelectClip, 1f, 0.9f);
        }

        public void PlayComplete()
        {
            Play(m_CompleteClip, 1f, 1f);
        }

        public void PlayResult()
        {
            Play(m_ResultClip, 1f, 0.9f);
        }

        /// <summary>결과 화면에서는 배경음을 줄인다.</summary>
        public void SetAmbientDucked(bool ducked)
        {
            if (m_Config != null)
                m_AmbientTarget = ducked ? m_Config.ambientDuckedVolume : m_Config.ambientVolume;
        }

        // 파일로 바꾼 소리는 음높이를 덜 바꾼다(녹음된 음색이 어색해지지 않게).
        static float PitchFor(int starNumber, bool customClip)
        {
            int step = Mathf.Clamp(starNumber - 1, 0, k_Scale.Length - 1);
            float pitch = Mathf.Pow(2f, k_Scale[step] / 12f);
            return customClip ? Mathf.Lerp(1f, pitch, 0.5f) : pitch;
        }

        void Play(AudioClip clip, float pitch, float volume)
        {
            if (clip == null || m_Effects == null)
                return;
            AudioSource source = m_Effects[m_NextVoice];
            m_NextVoice = (m_NextVoice + 1) % m_Effects.Length;
            source.Stop();
            source.clip = clip;
            source.pitch = pitch;
            source.volume = volume * m_Config.effectVolume * m_Config.masterVolume;
            source.Play();
        }

        void Update()
        {
            if (m_Ambient == null)
                return;
            float target = m_AmbientTarget * m_Config.masterVolume;
            m_Ambient.volume = Mathf.MoveTowards(m_Ambient.volume, target, Time.unscaledDeltaTime * 0.25f);
        }
    }
}
