#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 에디터 전용. 가짜 손을 대본대로 움직여 체험을 처음부터 끝까지 돌리면서 단계마다 Game 뷰를 PNG로 저장한다.
    /// 메뉴 Constellation > 스크린샷 찍기로 시작한다. 빌드에는 들어가지 않는다.
    /// 저장 위치: 프로젝트 폴더의 Screenshots/ (해상도는 Game 뷰 크기를 따른다)
    /// </summary>
    public class ScreenshotDirector : MonoBehaviour
    {
        public const string RequestKey = "Constellation.ScreenshotRequested";
        public const string QuitKey = "Constellation.ScreenshotQuit";
        public const string OutputFolder = "Screenshots";

        /// <summary>메뉴에서 촬영을 요청했는지. 플레이 모드에 들어온 뒤 한 번 읽고 지운다.</summary>
        public static bool Requested
        {
            get { return UnityEditor.SessionState.GetBool(RequestKey, false); }
        }

        // 이을 별(줄 × 칸 배치 기준). 지그재그로 올라가는 모양이 되게 골랐다.
        static readonly int[] k_StarPath = { 3, 14, 5, 16, 7, 26 };

        ConstellationConfig m_Config;
        SessionController m_Session;
        HandInput m_Hands;
        KeywordSelector m_Keywords;
        StarField m_Stars;
        Transform m_Head;
        HandInput.SimScript m_Script;

        public void Init(ConstellationConfig config, SessionController session, HandInput hands, KeywordSelector keywords, StarField stars, Transform head)
        {
            m_Config = config;
            m_Session = session;
            m_Hands = hands;
            m_Keywords = keywords;
            m_Stars = stars;
            m_Head = head;

            UnityEditor.SessionState.SetBool(RequestKey, false);
            m_Script = new HandInput.SimScript();
            m_Hands.Script = m_Script;
            Directory.CreateDirectory(OutputFolder);
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            Debug.Log("[Constellation] 스크린샷 촬영을 시작합니다. 끝날 때까지 Game 뷰를 그대로 두세요.");

            // 1. 대기 화면
            m_Script.pitch = 8f;
            yield return new WaitForSeconds(4f); // 은하수와 소리가 준비되기를 기다린다.
            yield return Shot("01_idle");

            // 2. 손을 들어 키워드 선택으로
            m_Script.handVisible = true;
            m_Script.point = InFront(0.4f, -0.1f);
            yield return WaitForState(SessionState.KeywordSelect, 5f);
            yield return new WaitForSeconds(1.2f);
            m_Script.point = m_Keywords.GetOrbPosition(0) + Vector3.up * 0.06f;
            yield return new WaitForSeconds(0.6f);
            yield return Shot("02_keyword");

            // 3. 키워드 하나를 집는다 → 별이 나타난다.
            yield return PinchAt(m_Keywords.GetOrbPosition(0));
            yield return WaitForState(SessionState.Drawing, 5f);
            m_Script.pitch = 20f;
            m_Script.point = InFront(0.35f, -0.12f);
            yield return new WaitForSeconds(1.6f);
            yield return Shot("03_stars");

            // 4. 별을 차례로 잇는다.
            int picked = 0;
            for (int i = 0; i < k_StarPath.Length; i++)
            {
                int star = k_StarPath[i];
                if (star >= m_Stars.Count)
                    continue;
                Vector3 target = m_Stars.GetWorldPosition(star);
                // 별 근처로 다가가 힌트(커지는 별, 고리)가 보이게 한다.
                m_Script.point = target + (m_Head.position - target).normalized * 0.03f + Vector3.down * 0.03f;
                yield return new WaitForSeconds(0.35f);
                if (picked == 2)
                    yield return Shot("04_hover");
                yield return PinchAt(target);
                picked++;
                if (picked == 4)
                {
                    yield return new WaitForSeconds(0.1f);
                    yield return Shot("05_drawing");
                }
            }
            yield return new WaitForSeconds(0.5f);

            // 5. 양손을 떨어뜨렸다가 모아 완성한다.
            m_Script.point = InFront(m_Config.ActiveStarZone.distance * 0.8f, -0.05f);
            m_Script.bothMode = 1;
            yield return new WaitForSeconds(0.4f);
            m_Script.bothMode = 2;
            yield return WaitForState(SessionState.AiRequest, 4f);
            m_Script.bothMode = 0;
            m_Script.handVisible = false;
            yield return new WaitForSeconds(0.45f);
            yield return Shot("06_complete");

            // 6. 결과 화면
            yield return WaitForState(SessionState.Result, m_Config.aiTimeoutSeconds + 6f);
            m_Script.pitch = 14f;
            yield return new WaitForSeconds(6f);
            yield return Shot("07_result");

            // 7. 카드(서버가 연결되어 있지 않으면 건너뛴다)
            yield return WaitForState(SessionState.Card, m_Config.resultSeconds + 2f);
            if (m_Session.State == SessionState.Card)
            {
                m_Script.pitch = 0f;
                yield return new WaitForSeconds(2.5f);
                if (m_Session.State == SessionState.Card)
                    yield return Shot("08_card");
            }

            Debug.Log("[Constellation] 스크린샷 촬영 끝. 폴더: " + Path.GetFullPath(OutputFolder));
            m_Hands.Script = null;
            yield return new WaitForSeconds(0.5f);

            if (UnityEditor.SessionState.GetBool(QuitKey, false))
                UnityEditor.EditorApplication.Exit(0);
            else
                UnityEditor.EditorApplication.isPlaying = false;
        }

        // 머리 앞(시선 방향이 아니라 수평 정면) 기준의 한 점
        Vector3 InFront(float distance, float height)
        {
            Vector3 forward = m_Head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            return m_Head.position + forward.normalized * distance + Vector3.up * height;
        }

        IEnumerator PinchAt(Vector3 position)
        {
            m_Script.point = position;
            m_Script.pinch = false;
            yield return new WaitForSeconds(0.15f);
            m_Script.pinch = true;
            yield return new WaitForSeconds(0.25f);
            m_Script.pinch = false;
            yield return new WaitForSeconds(0.2f);
        }

        IEnumerator WaitForState(SessionState state, float timeout)
        {
            float until = Time.unscaledTime + timeout;
            while (m_Session.State != state && Time.unscaledTime < until)
                yield return null;
            if (m_Session.State != state)
                Debug.LogWarning("[Constellation] 스크린샷: " + state + " 상태에 도달하지 못했습니다(현재 " + m_Session.State + "). 계속 진행합니다.");
        }

        IEnumerator Shot(string shotName)
        {
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(OutputFolder, shotName + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[Constellation] 저장: " + path);
            // 파일이 쓰일 때까지 두 프레임 기다린다.
            yield return null;
            yield return null;
        }
    }
}
#endif
