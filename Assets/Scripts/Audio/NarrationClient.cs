using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Constellation
{
    /// <summary>
    /// 결과 이야기를 중계 서버(/tts)에서 음성으로 받아 읽어 준다.
    /// 실패하거나 느리면 조용히 건너뛴다(글자만 보인다). 음성은 파일로 저장하지 않는다.
    /// </summary>
    public class NarrationClient : MonoBehaviour
    {
        [Serializable]
        class TtsRequest
        {
            public string text;
        }

        ConstellationConfig m_Config;
        AudioSource m_Source;
        Coroutine m_Fetch;
        UnityWebRequest m_Request;
        int m_Id;

        string m_Text;          // 지금 받았거나 받는 중인 글
        AudioClip m_Clip;       // 받은 음성
        bool m_PlayRequested;
        float m_PlayAt;

        public void Init(ConstellationConfig config)
        {
            m_Config = config;
            m_Source = gameObject.AddComponent<AudioSource>();
            m_Source.playOnAwake = false;
            m_Source.spatialBlend = 0f; // 머리 안쪽에서 들리는 2D 소리
        }

        bool Enabled
        {
            get { return m_Config != null && m_Config.enableNarration; }
        }

        /// <summary>이야기가 정해지는 즉시 부른다. 결과 화면에 닿기 전에 음성을 미리 받아 둔다.</summary>
        public void Prefetch(string text)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(text))
                return;
            if (text == m_Text && (m_Clip != null || m_Fetch != null))
                return;

            Stop();
            AppSecrets secrets = AppSecrets.Load();
            if (!secrets.IsConfigured)
                return;

            m_Text = text;
            m_Id++;
            m_Fetch = StartCoroutine(Fetch(m_Id, text, secrets));
        }

        /// <summary>delaySeconds 뒤부터 읽는다. 음성이 아직 오지 않았으면 오는 대로 시작한다.</summary>
        public void Play(string text, float delaySeconds)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(text))
                return;
            if (text != m_Text)
                Prefetch(text);
            m_PlayRequested = true;
            m_PlayAt = Time.time + Mathf.Max(0f, delaySeconds);
        }

        /// <summary>읽기를 멈추고 받은 음성도 버린다. 다음 체험으로 넘어갈 때 부른다.</summary>
        public void Stop()
        {
            m_Id++;
            if (m_Fetch != null)
            {
                StopCoroutine(m_Fetch);
                m_Fetch = null;
            }
            DisposeRequest();
            if (m_Source != null)
                m_Source.Stop();
            if (m_Clip != null)
            {
                Destroy(m_Clip);
                m_Clip = null;
            }
            m_Text = null;
            m_PlayRequested = false;
        }

        void Update()
        {
            if (!m_PlayRequested || m_Clip == null || Time.time < m_PlayAt)
                return;

            m_PlayRequested = false;
            m_Source.clip = m_Clip;
            m_Source.volume = Mathf.Clamp01(m_Config.narrationVolume);
            m_Source.Play();
        }

        void DisposeRequest()
        {
            if (m_Request == null)
                return;
            try
            {
                m_Request.Abort();
                m_Request.Dispose();
            }
            catch (Exception)
            {
                // 이미 끝난 요청이면 무시한다.
            }
            m_Request = null;
        }

        IEnumerator Fetch(int id, string text, AppSecrets secrets)
        {
            TtsRequest body = new TtsRequest();
            body.text = text;
            byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));

            string url = secrets.Url("/tts");
            UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            m_Request = request;
            request.uploadHandler = new UploadHandlerRaw(payload);
            request.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.WAV);
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-App-Token", secrets.appToken);
            request.SetRequestHeader("X-Device-Id", AiClient.DeviceId());
            request.timeout = Mathf.CeilToInt(m_Config.narrationTimeoutSeconds);

            float start = Time.realtimeSinceStartup;
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            bool timedOut = false;
            while (!operation.isDone)
            {
                if (Time.realtimeSinceStartup - start >= m_Config.narrationTimeoutSeconds)
                {
                    timedOut = true;
                    request.Abort();
                    break;
                }
                yield return null;
            }

            AudioClip clip = null;
            if (!timedOut && request.result == UnityWebRequest.Result.Success && request.responseCode == 200)
                clip = DownloadHandlerAudioClip.GetContent(request);
            else if (m_Config.logStateChanges)
                Debug.Log("[Constellation] 낭독 음성 받기 실패: " + (timedOut ? "timeout" : request.responseCode + "_" + request.result));

            // Stop/새 Prefetch 뒤에 도착한 응답은 버린다.
            bool stale = id != m_Id;
            if (id == m_Id)
                DisposeRequest();
            m_Fetch = stale ? m_Fetch : null;

            if (stale)
            {
                if (clip != null)
                    Destroy(clip);
                yield break;
            }

            if (clip != null && clip.length > 0.1f)
                m_Clip = clip;
        }
    }
}
