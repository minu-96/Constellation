using System;
using System.Collections.Generic;
using UnityEngine;

namespace Constellation
{
    [Serializable]
    public class FallbackStory
    {
        public string keyword;
        public string name;
        public string story;
    }

    [Serializable]
    public class FallbackStoryFile
    {
        public FallbackStory[] stories;
    }

    /// <summary>
    /// 폴백 이야기 15세트를 보관하고 키워드별로 무작위로 하나를 고른다(F-10).
    /// 문구는 Assets/Data/fallback_stories.json 에 있다. 문구를 고칠 때 이 스크립트는 건드리지 않는다.
    /// </summary>
    public class FallbackStore : MonoBehaviour
    {
        readonly Dictionary<string, List<FallbackStory>> m_ByKeyword = new Dictionary<string, List<FallbackStory>>();
        readonly List<FallbackStory> m_All = new List<FallbackStory>();
        string m_LastName;

        public int Count { get { return m_All.Count; } }

        public void Init(ConstellationConfig config)
        {
            Load(config.fallbackStoriesJson != null ? config.fallbackStoriesJson.text : null);
        }

        void Load(string json)
        {
            m_ByKeyword.Clear();
            m_All.Clear();

            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError("[Constellation] 폴백 이야기 파일이 설정에 연결되지 않았습니다. ConstellationConfig의 Fallback Stories Json을 확인하세요.");
                return;
            }

            FallbackStoryFile file = null;
            try
            {
                file = JsonUtility.FromJson<FallbackStoryFile>(json);
            }
            catch (Exception e)
            {
                Debug.LogError("[Constellation] 폴백 이야기 파일을 읽지 못했습니다: " + e.Message);
            }
            if (file == null || file.stories == null)
                return;

            for (int i = 0; i < file.stories.Length; i++)
            {
                FallbackStory entry = file.stories[i];
                if (entry == null || string.IsNullOrEmpty(entry.keyword))
                    continue;

                string name, story, error;
                if (!StoryRules.TryNormalize(entry.name, entry.story, out name, out story, out error))
                {
                    Debug.LogWarning("[Constellation] 규칙에 맞지 않는 폴백 이야기를 건너뜁니다: " + entry.name + " (" + error + ")");
                    continue;
                }
                entry.name = name;
                entry.story = story;

                List<FallbackStory> list;
                if (!m_ByKeyword.TryGetValue(entry.keyword, out list))
                {
                    list = new List<FallbackStory>();
                    m_ByKeyword[entry.keyword] = list;
                }
                list.Add(entry);
                m_All.Add(entry);
            }
        }

        /// <summary>
        /// 키워드에 맞는 이야기를 무작위로 하나 고른다. 어떤 경우에도 null을 돌려주지 않는다.
        /// </summary>
        public FallbackStory GetRandom(string keywordId)
        {
            List<FallbackStory> list = null;
            if (keywordId != null)
                m_ByKeyword.TryGetValue(keywordId, out list);
            if (list == null || list.Count == 0)
                list = m_All;

            if (list.Count == 0)
            {
                // 데이터 파일이 빠졌을 때의 마지막 안전망. 결과 화면까지는 반드시 가야 한다.
                FallbackStory safety = new FallbackStory();
                safety.keyword = keywordId;
                safety.name = "이름 없는 별";
                safety.story = "아직 아무도 이름을 지어 주지 않은 별자리예요. 오늘 밤, 당신이 처음으로 이 별들을 이어 주었대요.";
                return safety;
            }

            // 바로 앞 체험과 같은 이야기가 연달아 나오지 않게 한다.
            FallbackStory pick = list[UnityEngine.Random.Range(0, list.Count)];
            if (list.Count > 1 && pick.name == m_LastName)
                pick = list[(list.IndexOf(pick) + 1) % list.Count];
            m_LastName = pick.name;
            return pick;
        }
    }
}
