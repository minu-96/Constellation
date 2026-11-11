using System;
using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 중계 서버 주소와 공유 토큰. 저장소에 올리지 않는 파일에서 읽는다.
    /// Assets/Data/constellation_secrets.example.json 을
    /// Assets/Resources/constellation_secrets.json 으로 복사해 값을 채운다(.gitignore에 들어 있다).
    /// 파일이 없으면 AI 요청을 건너뛰고 폴백 이야기로 진행한다.
    /// </summary>
    [Serializable]
    public class AppSecrets
    {
        public const string ResourcePath = "constellation_secrets";

        public string workerUrl = string.Empty;
        public string appToken = string.Empty;

        static AppSecrets s_Instance;

        public bool IsConfigured
        {
            get { return !string.IsNullOrEmpty(workerUrl) && !string.IsNullOrEmpty(appToken) && workerUrl.StartsWith("http"); }
        }

        /// <summary>주소 끝의 '/'를 떼고 경로를 붙인다.</summary>
        public string Url(string path)
        {
            return workerUrl.TrimEnd('/') + path;
        }

        public static AppSecrets Load()
        {
            if (s_Instance != null)
                return s_Instance;

            s_Instance = new AppSecrets();
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("[Constellation] Resources/constellation_secrets.json 이 없습니다. AI 요청 없이 폴백 이야기로 진행합니다.");
                return s_Instance;
            }

            try
            {
                AppSecrets parsed = JsonUtility.FromJson<AppSecrets>(asset.text);
                if (parsed != null)
                {
                    parsed.workerUrl = (parsed.workerUrl ?? string.Empty).Trim();
                    parsed.appToken = (parsed.appToken ?? string.Empty).Trim();
                    s_Instance = parsed;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Constellation] constellation_secrets.json 형식이 잘못되었습니다: " + e.Message);
            }

            if (!s_Instance.IsConfigured)
                Debug.LogWarning("[Constellation] 중계 서버 주소나 토큰이 비어 있습니다. AI 요청 없이 폴백 이야기로 진행합니다.");
            return s_Instance;
        }
    }
}
