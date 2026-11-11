using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Constellation
{
    /// <summary>AI 요청 결과. ok가 false면 SessionController가 폴백으로 넘긴다.</summary>
    public class AiResult
    {
        public bool ok;
        public string name;
        public string story;
        public string error;
    }

    /// <summary>
    /// 중계 서버(Cloudflare Worker)에 이미지·키워드·별 개수를 보내고 최대 10초 기다린다(F-09).
    /// 200이 아니거나, 시간 안에 오지 않거나, 형식이 어긋나면 모두 실패로 알린다. 재시도는 하지 않는다.
    /// </summary>
    public class AiClient : MonoBehaviour
    {
        [Serializable]
        class StoryRequest
        {
            public string keyword;
            public int starCount;
            public string image;
        }

        [Serializable]
        class StoryResponse
        {
            public string name;
            public string story;
        }

        ConstellationConfig m_Config;
        Coroutine m_Running;
        UnityWebRequest m_Request;
        int m_RequestId;

        public void Init(ConstellationConfig config)
        {
            m_Config = config;
        }

        /// <summary>
        /// 요청을 보낸다. onDone은 성공이든 실패든 정확히 한 번 불린다(Cancel한 경우는 제외).
        /// </summary>
        public void Request(string keywordId, int starCount, byte[] jpeg, Action<AiResult> onDone)
        {
            Cancel();
            m_RequestId++;
            m_Running = StartCoroutine(Run(m_RequestId, keywordId, starCount, jpeg, onDone));
        }

        /// <summary>진행 중인 요청을 끊는다. 콜백은 불리지 않는다.</summary>
        public void Cancel()
        {
            m_RequestId++;
            if (m_Running != null)
            {
                StopCoroutine(m_Running);
                m_Running = null;
            }
            DisposeRequest();
        }

        void DisposeRequest()
        {
            if (m_Request != null)
            {
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
        }

        IEnumerator Run(int id, string keywordId, int starCount, byte[] jpeg, Action<AiResult> onDone)
        {
            AiResult result = new AiResult();
            AppSecrets secrets = AppSecrets.Load();

            if (!secrets.IsConfigured)
            {
                result.error = "not_configured";
            }
            else if (jpeg == null || jpeg.Length == 0)
            {
                result.error = "no_image";
            }
            else
            {
                StoryRequest body = new StoryRequest();
                body.keyword = keywordId;
                body.starCount = starCount;
                body.image = Convert.ToBase64String(jpeg);
                byte[] payload = Encoding.UTF8.GetBytes(JsonUtility.ToJson(body));

                UnityWebRequest request = new UnityWebRequest(secrets.Url("/story"), UnityWebRequest.kHttpVerbPOST);
                m_Request = request;
                request.uploadHandler = new UploadHandlerRaw(payload);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("X-App-Token", secrets.appToken);
                request.SetRequestHeader("X-Device-Id", DeviceId());
                // 아래에서 직접 시간을 재지만, 만약을 위해 엔진 타임아웃도 건다.
                request.timeout = Mathf.CeilToInt(m_Config.aiTimeoutSeconds) + 1;

                float start = Time.realtimeSinceStartup;
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                bool timedOut = false;
                while (!operation.isDone)
                {
                    if (Time.realtimeSinceStartup - start >= m_Config.aiTimeoutSeconds)
                    {
                        timedOut = true;
                        request.Abort();
                        break;
                    }
                    yield return null;
                }

                if (timedOut)
                {
                    result.error = "timeout";
                }
                else if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
                {
                    result.error = "http_" + request.responseCode + "_" + request.result;
                }
                else
                {
                    Parse(request.downloadHandler.text, result);
                }

                DisposeRequest();
            }

            m_Running = null;
            // Cancel 뒤에 도착한 응답은 버린다.
            if (id == m_RequestId && onDone != null)
                onDone(result);
        }

        // 응답 JSON을 읽고 이름·이야기 규칙을 확인한다. 어긋나면 실패로 둔다.
        static void Parse(string json, AiResult result)
        {
            StoryResponse response = null;
            try
            {
                response = JsonUtility.FromJson<StoryResponse>(json);
            }
            catch (Exception)
            {
                response = null;
            }

            if (response == null)
            {
                result.error = "invalid_json";
                return;
            }

            string name, story, error;
            if (!StoryRules.TryNormalize(response.name, response.story, out name, out story, out error))
            {
                result.error = "invalid_story_" + error;
                return;
            }

            result.ok = true;
            result.name = name;
            result.story = story;
        }

        /// <summary>호출 제한에 쓰는 기기 식별자. 서버는 영문·숫자·-·_ 만 받는다.</summary>
        public static string DeviceId()
        {
            string raw = SystemInfo.deviceUniqueIdentifier ?? string.Empty;
            StringBuilder builder = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length && builder.Length < 64; i++)
            {
                char c = raw[i];
                bool allowed = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (allowed)
                    builder.Append(c);
            }
            return builder.Length >= 4 ? builder.ToString() : "quest-unknown";
        }
    }
}
