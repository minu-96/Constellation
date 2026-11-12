using System.Threading.Tasks;
using UnityEngine;

namespace Constellation
{
    /// <summary>체험 단계에 따른 소리 장면. 장면마다 배경음과 환경음의 크기가 달라진다.</summary>
    public enum AudioScene
    {
        /// <summary>대기: 밤의 환경음이 주인공이다.</summary>
        Idle,
        /// <summary>키워드 선택과 별 잇기: 환경음을 조금 줄이고 화음 배경을 올린다.</summary>
        Drawing,
        /// <summary>AI를 기다리는 동안: 환경음을 더 줄여 긴장을 모은다.</summary>
        Waiting,
        /// <summary>결과와 카드: 글을 읽을 수 있게 모두 낮춘다.</summary>
        Result
    }

    /// <summary>
    /// 배경음, 환경음, 효과음(F-14). 소리 파일 없이 코드로 만든 소리를 쓴다.
    ///  - 화음 배경(2D)과 바람(2D)
    ///  - 풀벌레와 물소리(3D): 체험 공간 주변에 놓아 고개를 돌리면 방향이 느껴진다.
    ///  - 효과음(2D): 별 잡기, 선 잇기, 되돌리기, 핀치, 완성, 결과
    /// 나중에 파일로 바꾸려면 ConstellationConfig의 AudioClip 칸에 넣으면 된다(비어 있으면 만든 소리를 쓴다).
    /// 긴 반복음은 백그라운드 스레드에서 계산해 시작할 때 화면이 멈추지 않게 한다.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        const int k_SampleRate = 44100;
        const int k_EffectVoices = 6;
        const float k_FadePerSecond = 0.35f;

        // 별을 이을 때마다 한 음씩 올라가는 5음 음계(도 레 미 솔 라 …), 가온 다(C5) 기준 반음 수
        static readonly int[] k_Scale = { 0, 2, 4, 7, 9, 12, 14, 16, 19 };
        const float k_BaseFrequency = 523.25f;

        // 장면별 크기 비율: (화음 배경, 풀벌레, 바람, 물)
        static readonly float[,] k_SceneMix =
        {
            { 0.55f, 1.00f, 1.00f, 1.00f }, // Idle
            { 1.00f, 0.60f, 0.70f, 0.65f }, // Drawing
            { 1.00f, 0.30f, 0.55f, 0.40f }, // Waiting
            { 0.00f, 0.25f, 0.40f, 0.30f }, // Result (화음 배경은 ambientDuckedVolume을 쓴다)
        };

        // 풀벌레를 놓는 방향(도)과 거리(m). 발밑 풀숲에서 들리도록 낮게 둔다.
        static readonly Vector3[] k_CricketSpots =
        {
            new Vector3(-65f, 5f, -1.2f),  // (방위각, 거리, 높이)
            new Vector3(40f, 7f, -1.3f),
            new Vector3(165f, 6f, -1.2f),
        };
        static readonly float[] k_CricketPitch = { 0.94f, 1.0f, 1.07f };
        // 물소리는 호수 쪽 두 곳에서 멀리 들린다.
        static readonly Vector3[] k_WaterSpots =
        {
            new Vector3(-20f, 14f, -1.6f),
            new Vector3(130f, 16f, -1.6f),
        };

        class Loops
        {
            public float[] pad;
            public float[] crickets;
            public float[] wind;
            public float[] water;
        }

        ConstellationConfig m_Config;
        Transform m_Listener;
        Transform m_World;
        AudioSource m_Ambient;
        AudioSource m_Wind;
        AudioSource[] m_Crickets;
        AudioSource[] m_Water;
        AudioSource[] m_Effects;
        int m_NextVoice;
        AudioScene m_Scene = AudioScene.Idle;
        Task<Loops> m_LoopTask;
        bool m_LoopsReady;

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
            m_Listener = listener;
            // 2D 소리는 머리를 따라다니게 해서 어디를 보든 고르게 들리게 한다.
            transform.SetParent(listener, false);

            // 짧은 효과음은 바로 만든다.
            m_BellClip = config.starGrabClip != null ? config.starGrabClip : ToneSynth.Bell(k_SampleRate, k_BaseFrequency, 1.6f, 1f);
            m_SoftBellClip = config.lineConnectClip != null ? config.lineConnectClip : ToneSynth.Bell(k_SampleRate, k_BaseFrequency, 1.2f, 0.6f);
            m_UndoClip = config.undoClip != null ? config.undoClip : ToneSynth.Sweep(k_SampleRate, 620f, 300f, 0.28f);
            m_PinchClip = config.pinchClip != null ? config.pinchClip : ToneSynth.Tick(k_SampleRate, 1900f, 0.07f);
            m_SelectClip = config.keywordSelectClip != null ? config.keywordSelectClip : ToneSynth.Chord(k_SampleRate, new[] { 523.25f, 783.99f }, 0.09f, 1.4f);
            m_CompleteClip = config.completeClip != null ? config.completeClip : ToneSynth.Chord(k_SampleRate, new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.11f, 2.2f);
            m_ResultClip = config.resultClip != null ? config.resultClip : ToneSynth.Chord(k_SampleRate, new[] { 392f, 523.25f, 659.25f, 987.77f }, 0.22f, 3.2f);

            // 효과음이 겹쳐도 끊기지 않게 소스를 여러 개 돌려 쓴다.
            m_Effects = new AudioSource[k_EffectVoices];
            for (int i = 0; i < k_EffectVoices; i++)
                m_Effects[i] = CreateSource("Effect " + i, transform, false);

            m_Ambient = CreateSource("Ambient", transform, false);
            m_Wind = CreateSource("Wind", transform, false);

            // 3D 소리는 세상에 고정한다(머리를 따라다니지 않는다).
            m_World = new GameObject("Nature Sounds").transform;
            m_Crickets = new AudioSource[k_CricketSpots.Length];
            for (int i = 0; i < m_Crickets.Length; i++)
            {
                m_Crickets[i] = CreateSource("Crickets " + i, m_World, true);
                m_Crickets[i].pitch = k_CricketPitch[i];
            }
            m_Water = new AudioSource[k_WaterSpots.Length];
            for (int i = 0; i < m_Water.Length; i++)
                m_Water[i] = CreateSource("Water " + i, m_World, true);

            // 긴 반복음은 계산이 오래 걸려 백그라운드에서 만든다. 다 되면 Update에서 틀기 시작한다.
            bool needPad = config.ambientClip == null;
            bool nature = config.enableNatureAmbience;
            bool needCrickets = nature && config.cricketsClip == null;
            bool needWind = nature && config.windClip == null;
            bool needWater = nature && config.waterClip == null;
            m_LoopTask = Task.Run(() =>
            {
                Loops loops = new Loops();
                if (needPad) loops.pad = ToneSynth.AmbientSamples(k_SampleRate);
                if (needCrickets) loops.crickets = AmbienceSynth.Crickets(11);
                if (needWind) loops.wind = AmbienceSynth.Wind(5);
                if (needWater) loops.water = AmbienceSynth.Water(23);
                return loops;
            });
        }

        AudioSource CreateSource(string sourceName, Transform parent, bool spatial)
        {
            GameObject go = new GameObject(sourceName);
            go.transform.SetParent(parent, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.volume = 0f;
            source.dopplerLevel = 0f;
            if (spatial)
            {
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 2f;
                source.maxDistance = 45f;
                source.spread = 40f; // 한 점에서 나는 소리처럼 들리지 않게 조금 퍼뜨린다.
            }
            else
            {
                source.spatialBlend = 0f;
            }
            return source;
        }

        // 백그라운드 계산이 끝나면 클립을 만들고 반복 재생을 시작한다. 크기는 0에서 서서히 올라온다.
        void StartLoops(Loops loops)
        {
            AudioClip pad = m_Config.ambientClip != null ? m_Config.ambientClip
                : loops.pad != null ? ToneSynth.CreateClip("Ambient", loops.pad, k_SampleRate) : null;
            StartLoop(m_Ambient, pad, 0f);

            if (!m_Config.enableNatureAmbience)
                return;

            AudioClip crickets = m_Config.cricketsClip != null ? m_Config.cricketsClip
                : loops.crickets != null ? ToneSynth.CreateClip("Crickets", loops.crickets, AmbienceSynth.SampleRate) : null;
            AudioClip wind = m_Config.windClip != null ? m_Config.windClip
                : loops.wind != null ? ToneSynth.CreateClip("Wind", loops.wind, AmbienceSynth.SampleRate) : null;
            AudioClip water = m_Config.waterClip != null ? m_Config.waterClip
                : loops.water != null ? ToneSynth.CreateClip("Water", loops.water, AmbienceSynth.SampleRate) : null;

            // 지금 머리가 있는 곳을 중심으로 소리를 놓는다. 높이는 머리 기준이다.
            Vector3 center = m_Listener != null ? m_Listener.position : Vector3.zero;
            m_World.position = center;

            StartLoop(m_Wind, wind, 0f);
            for (int i = 0; i < m_Crickets.Length; i++)
            {
                m_Crickets[i].transform.localPosition = SpotPosition(k_CricketSpots[i]);
                // 같은 소리를 여러 곳에서 틀므로 시작 위치를 어긋나게 한다.
                StartLoop(m_Crickets[i], crickets, i / (float)m_Crickets.Length);
            }
            for (int i = 0; i < m_Water.Length; i++)
            {
                m_Water[i].transform.localPosition = SpotPosition(k_WaterSpots[i]);
                StartLoop(m_Water[i], water, i / (float)m_Water.Length);
            }
        }

        static Vector3 SpotPosition(Vector3 spot)
        {
            Vector3 flat = Quaternion.Euler(0f, spot.x, 0f) * Vector3.forward * spot.y;
            return new Vector3(flat.x, spot.z, flat.z);
        }

        static void StartLoop(AudioSource source, AudioClip clip, float offset01)
        {
            if (source == null || clip == null)
                return;
            source.clip = clip;
            source.loop = true;
            source.volume = 0f;
            source.time = Mathf.Clamp01(offset01) * clip.length * 0.99f;
            source.Play();
        }

        /// <summary>체험 단계가 바뀌면 부른다. 배경음과 환경음의 크기가 부드럽게 바뀐다.</summary>
        public void SetScene(AudioScene scene)
        {
            m_Scene = scene;
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

        /// <summary>완성 연출에서 빛이 별에 닿을 때마다 나는 여린 종소리. 별마다 음이 올라간다.</summary>
        public void PlayEngraveNote(int starNumber)
        {
            Play(m_SoftBellClip, PitchFor(starNumber, m_Config.lineConnectClip != null) * 2f, 0.4f);
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
            if (m_Config == null)
                return;

            if (m_LoopTask != null && m_LoopTask.IsCompleted)
            {
                if (m_LoopTask.Status == TaskStatus.RanToCompletion && m_LoopTask.Result != null)
                    StartLoops(m_LoopTask.Result);
                else
                    Debug.LogWarning("[Constellation] 배경음을 만들지 못했습니다. 효과음만 재생합니다.");
                m_LoopTask = null;
                m_LoopsReady = true;
            }
            if (!m_LoopsReady)
                return;

            int scene = (int)m_Scene;
            float master = m_Config.masterVolume;
            float step = Time.unscaledDeltaTime * k_FadePerSecond;

            // 결과 화면에서는 화음 배경을 따로 정한 작은 크기로 줄인다.
            float pad = m_Scene == AudioScene.Result ? m_Config.ambientDuckedVolume : m_Config.ambientVolume * k_SceneMix[scene, 0];
            Fade(m_Ambient, pad * master, step);

            float nature = m_Config.enableNatureAmbience ? m_Config.natureVolume * master : 0f;
            Fade(m_Wind, nature * m_Config.windVolume * k_SceneMix[scene, 2], step);
            for (int i = 0; i < m_Crickets.Length; i++)
                Fade(m_Crickets[i], nature * m_Config.cricketsVolume * k_SceneMix[scene, 1], step);
            for (int i = 0; i < m_Water.Length; i++)
                Fade(m_Water[i], nature * m_Config.waterVolume * k_SceneMix[scene, 3], step);
        }

        static void Fade(AudioSource source, float target, float step)
        {
            if (source != null && source.clip != null)
                source.volume = Mathf.MoveTowards(source.volume, target, step);
        }

        void OnDestroy()
        {
            if (m_World != null)
                Destroy(m_World.gameObject);
        }
    }
}
