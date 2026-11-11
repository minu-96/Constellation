namespace Constellation
{
    /// <summary>
    /// 이름·이야기 규칙(docs/spec.md "AI 요청·응답 규격").
    /// Worker(worker/src/rules.js, validate.js)와 같은 규칙이다. 한쪽을 바꾸면 다른 쪽도 맞춘다.
    /// </summary>
    public static class StoryRules
    {
        public const int NameMinChars = 2;
        public const int NameMaxChars = 8;
        public const int StoryMinChars = 20;
        public const int StoryMaxChars = 200;
        public const int StoryMinSentences = 2;
        public const int StoryMaxSentences = 3;

        // 어린이 전시에 맞지 않는 표현. 뜻이 분명한 낱말만 둔다.
        static readonly string[] k_BannedWords =
        {
            "살인", "살해", "죽이", "죽였", "죽여", "시체", "송장", "귀신", "유령", "저주",
            "피투성이", "핏빛", "지옥", "악마", "괴물", "칼로", "총으로", "자살", "학살"
        };

        /// <summary>규칙을 통과하면 true. 통과한 값은 앞뒤 공백을 다듬어 돌려준다.</summary>
        public static bool TryNormalize(string rawName, string rawStory, out string name, out string story, out string error)
        {
            name = null;
            story = null;
            error = null;

            if (rawName == null || rawStory == null)
            {
                error = "missing_fields";
                return false;
            }

            string n = rawName.Trim();
            string s = CollapseWhitespace(rawStory);

            if (n.Length < NameMinChars || n.Length > NameMaxChars)
            {
                error = "name_length";
                return false;
            }
            if (!IsKoreanName(n))
            {
                error = "name_not_korean";
                return false;
            }
            if (s.Length < StoryMinChars || s.Length > StoryMaxChars)
            {
                error = "story_length";
                return false;
            }

            bool hasHangul = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (IsHangul(c))
                    hasHangul = true;
                if (c < ' ' || c == '<' || c == '>' || c == '{' || c == '}')
                {
                    error = "story_bad_chars";
                    return false;
                }
            }
            if (!hasHangul)
            {
                error = "story_not_korean";
                return false;
            }

            int sentences = CountSentences(s);
            if (sentences < StoryMinSentences || sentences > StoryMaxSentences)
            {
                error = "story_sentences";
                return false;
            }

            string all = n + " " + s;
            for (int i = 0; i < k_BannedWords.Length; i++)
            {
                if (all.IndexOf(k_BannedWords[i], System.StringComparison.Ordinal) >= 0)
                {
                    error = "banned_word";
                    return false;
                }
            }

            name = n;
            story = s;
            return true;
        }

        static bool IsHangul(char c)
        {
            return c >= '가' && c <= '힣';
        }

        // 한글 음절과 낱말 사이 공백 한 칸만 허용한다.
        static bool IsKoreanName(string name)
        {
            bool previousSpace = true;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == ' ')
                {
                    if (previousSpace)
                        return false;
                    previousSpace = true;
                }
                else if (IsHangul(c))
                {
                    previousSpace = false;
                }
                else
                {
                    return false;
                }
            }
            return !previousSpace;
        }

        static string CollapseWhitespace(string text)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder(text.Length);
            bool previousSpace = true;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    if (!previousSpace)
                        builder.Append(' ');
                    previousSpace = true;
                }
                else
                {
                    builder.Append(c);
                    previousSpace = false;
                }
            }
            return builder.ToString().Trim();
        }

        /// <summary>문장 수. 마침표·물음표·느낌표·말줄임표로 끝나는 덩어리를 센다.</summary>
        public static int CountSentences(string story)
        {
            int count = 0;
            bool hasContent = false;
            for (int i = 0; i < story.Length; i++)
            {
                char c = story[i];
                bool terminator = c == '.' || c == '!' || c == '?' || c == '。' || c == '…';
                if (terminator)
                {
                    if (hasContent)
                        count++;
                    hasContent = false;
                }
                else if (!char.IsWhiteSpace(c) && c != '"' && c != '\'' && c != '”' && c != '’' && c != ')' && c != ']')
                {
                    hasContent = true;
                }
            }
            if (hasContent)
                count++;
            return count;
        }
    }
}
