using System;
using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 세션 상태 머신(F-01). 상태 전환은 이 스크립트에서만 한다.
    /// 다른 스크립트는 이벤트로 알리기만 하고, 여기서 다음 상태를 정한다.
    /// 체험이 끝나면 모든 값을 비우고 Idle로 돌아간다.
    /// </summary>
    public class SessionController : MonoBehaviour
    {
        // AppBootstrap이 넣어 준다.
        [NonSerialized] public ConstellationConfig config;
        [NonSerialized] public Transform head;
        [NonSerialized] public HandInput hands;
        [NonSerialized] public GuideUI guide;
        [NonSerialized] public GestureDetector gestures;
        [NonSerialized] public KeywordSelector keywordSelector;
        [NonSerialized] public StarField starField;
        [NonSerialized] public ConstellationDrawer drawer;

        /// <summary>상태가 바뀐 뒤에 알린다(이전 상태, 새 상태).</summary>
        public event Action<SessionState, SessionState> StateChanged;

        public SessionState State { get; private set; }

        // ---- 체험 한 번의 데이터. ResetSession에서 모두 비운다.
        KeywordDefinition m_Keyword;
        Vector3 m_AnchorPosition;
        Quaternion m_AnchorRotation = Quaternion.identity;
        int m_StarCount;
        string m_ResultName;
        string m_ResultStory;

        // ---- 타이머
        float m_StateEnterTime;
        float m_LastActivityTime;
        float m_NoHandSeconds;
        float m_HandHoldSeconds;
        float m_KeywordChosenTime = -1f;
        float m_IdleReadyTime;
        bool m_SawNoHandsSinceEnd = true;
        bool m_Started;

        // 아직 구현되지 않은 상태를 자동으로 넘기는 시간(초). 뼈대 확인용.
        const float k_PlaceholderSeconds = 2f;

        float Now { get { return Time.unscaledTime; } }
        float StateSeconds { get { return Now - m_StateEnterTime; } }

        /// <summary>AppBootstrap이 참조를 다 넣은 뒤 한 번 부른다.</summary>
        public void Begin()
        {
            hands.PinchStarted += OnPinchStarted;
            keywordSelector.Selected += OnKeywordSelected;
            drawer.StarAdded += OnStarAdded;
            drawer.MaxReached += OnMaxStarsReached;
            drawer.Undone += OnUndone;
            gestures.HandsTogether += OnHandsTogether;
            gestures.UndoRequested += OnUndoRequested;

            m_Started = true;
            State = SessionState.Idle;
            m_StateEnterTime = Now;
            Enter(SessionState.Idle);
        }

        void OnDestroy()
        {
            if (hands != null)
                hands.PinchStarted -= OnPinchStarted;
            if (keywordSelector != null)
                keywordSelector.Selected -= OnKeywordSelected;
            if (drawer != null)
            {
                drawer.StarAdded -= OnStarAdded;
                drawer.MaxReached -= OnMaxStarsReached;
                drawer.Undone -= OnUndone;
            }
            if (gestures != null)
            {
                gestures.HandsTogether -= OnHandsTogether;
                gestures.UndoRequested -= OnUndoRequested;
            }
        }

        // ================================================================ 상태 전환

        void Go(SessionState next)
        {
            SessionState previous = State;
            Exit(previous);
            State = next;
            m_StateEnterTime = Now;
            m_LastActivityTime = Now;
            if (config.logStateChanges)
                Debug.Log("[Constellation] " + previous + " → " + next);
            Enter(next);
            if (StateChanged != null)
                StateChanged(previous, next);
        }

        void Enter(SessionState state)
        {
            switch (state)
            {
                case SessionState.Idle:
                    ResetSession();
                    guide.SetElevation(config.guideElevationIdle);
                    guide.SetTitle(config.msgTitle);
                    guide.SetHint(Now < m_IdleReadyTime ? config.msgGoodbye : config.msgIdle);
                    break;

                case SessionState.HandCheck:
                    m_HandHoldSeconds = 0f;
                    m_NoHandSeconds = 0f;
                    guide.SetHint(config.msgHandCheck);
                    break;

                case SessionState.KeywordSelect:
                    PlaceAnchor();
                    m_KeywordChosenTime = -1f;
                    guide.SetElevation(config.guideElevation);
                    guide.SetTitle(config.msgKeywordQuestion);
                    guide.SetHint(config.msgKeywordHint);
                    keywordSelector.Show(m_AnchorPosition, m_AnchorRotation);
                    break;

                case SessionState.Drawing:
                    guide.SetTitle(string.Empty);
                    guide.SetHint(config.msgDrawingHint);
                    drawer.Begin();
                    gestures.ResetGestures();
                    gestures.Detecting = true;
                    break;

                case SessionState.Capture:
                    guide.SetHint(string.Empty);
                    guide.ClearAlert();
                    break;

                case SessionState.AiRequest:
                    guide.SetHint(config.msgAiWaiting);
                    break;

                case SessionState.Result:
                    guide.ClearAlert();
                    // 결과 화면이 생기기 전까지는 안내 글자로 이름과 이야기를 보여 준다.
                    guide.SetElevation(config.guideElevationIdle);
                    guide.SetTitle(m_ResultName ?? string.Empty);
                    guide.SetHint(m_ResultStory ?? string.Empty);
                    break;

                case SessionState.Card:
                    break;
            }
        }

        void Exit(SessionState state)
        {
            switch (state)
            {
                case SessionState.KeywordSelect:
                    keywordSelector.Hide();
                    break;

                case SessionState.Drawing:
                    drawer.End();
                    gestures.Detecting = false;
                    break;

                case SessionState.Card:
                    // 체험이 끝났다. 같은 사람이 손을 든 채로 곧바로 다시 시작되지 않게 잠시 쉰다.
                    m_IdleReadyTime = Now + config.endCooldownSeconds;
                    m_SawNoHandsSinceEnd = false;
                    break;
            }
        }

        // ================================================================ 상태별 갱신

        void Update()
        {
            if (!m_Started)
                return;

            m_NoHandSeconds = hands.AnyHandTracked ? 0f : m_NoHandSeconds + Time.unscaledDeltaTime;

            switch (State)
            {
                case SessionState.Idle: UpdateIdle(); break;
                case SessionState.HandCheck: UpdateHandCheck(); break;
                case SessionState.KeywordSelect: UpdateKeywordSelect(); break;
                case SessionState.Drawing: UpdateDrawing(); break;
                case SessionState.Capture: UpdateCapture(); break;
                case SessionState.AiRequest: UpdateAiRequest(); break;
                case SessionState.Result: UpdateResult(); break;
                case SessionState.Card: UpdateCard(); break;
            }
        }

        void UpdateIdle()
        {
            if (!hands.AnyHandTracked)
                m_SawNoHandsSinceEnd = true;

            if (Now < m_IdleReadyTime)
                return;
            guide.SetHint(config.msgIdle);

            // 직전 체험자가 손을 내린 적이 있거나, 충분히 시간이 지났을 때만 새 체험을 받는다.
            bool ready = m_SawNoHandsSinceEnd || Now > m_IdleReadyTime + config.endCooldownSeconds * 2f;
            if (ready && hands.AnyHandTracked)
                Go(SessionState.HandCheck);
        }

        void UpdateHandCheck()
        {
            if (hands.AnyHandTracked)
            {
                guide.ClearAlert();
                m_HandHoldSeconds += Time.unscaledDeltaTime;
                if (m_HandHoldSeconds >= config.handCheckHoldSeconds)
                    Go(SessionState.KeywordSelect);
                return;
            }

            m_HandHoldSeconds = 0f;
            if (m_NoHandSeconds >= config.handLostGuideDelay)
                guide.SetAlert(config.msgHandLost);
            if (m_NoHandSeconds >= config.handCheckTimeoutSeconds)
                Go(SessionState.Idle);
        }

        void UpdateKeywordSelect()
        {
            if (m_Keyword != null)
            {
                // 선택 효과를 잠깐 보여 준 뒤 넘어간다.
                if (Now - m_KeywordChosenTime >= config.keywordSelectEffectSeconds)
                    Go(SessionState.Drawing);
                return;
            }

            UpdateStickyAlert(config.keywordIdleTimeoutSeconds);
            if (Now - m_LastActivityTime >= config.keywordIdleTimeoutSeconds)
                Go(SessionState.Idle);
        }

        void UpdateDrawing()
        {
            UpdateStickyAlert(config.drawingIdleTimeoutSeconds);
            if (Now - m_LastActivityTime >= config.drawingIdleTimeoutSeconds)
                Go(SessionState.Idle);
        }

        void UpdateCapture()
        {
            if (StateSeconds >= k_PlaceholderSeconds)
                Go(SessionState.AiRequest);
        }

        void UpdateAiRequest()
        {
            if (StateSeconds >= k_PlaceholderSeconds)
            {
                m_ResultName = "별자리";
                m_ResultStory = "아직 이야기가 연결되지 않았어요.";
                Go(SessionState.Result);
            }
        }

        void UpdateResult()
        {
            if (StateSeconds >= config.resultSeconds)
                Go(SessionState.Card);
        }

        void UpdateCard()
        {
            // 결과 카드가 생기기 전까지는 곧바로 대기 화면으로 돌아간다.
            Go(SessionState.Idle);
        }

        // 손 인식 실패와 무입력 안내. 상황이 풀리면 사라진다(F-15).
        void UpdateStickyAlert(float idleTimeout)
        {
            if (m_NoHandSeconds >= config.handLostGuideDelay)
                guide.SetAlert(config.msgHandLost);
            else if (Now - m_LastActivityTime >= idleTimeout - config.idleWarningLeadSeconds)
                guide.SetAlert(config.msgIdleWarning);
            else
                guide.ClearAlert();
        }

        // ================================================================ 이벤트 처리

        void OnPinchStarted(HandSide side, Vector3 position)
        {
            m_LastActivityTime = Now;
        }

        void OnKeywordSelected(KeywordDefinition keyword)
        {
            if (State != SessionState.KeywordSelect || m_Keyword != null || keyword == null)
                return;
            m_Keyword = keyword;
            m_KeywordChosenTime = Now;
            m_LastActivityTime = Now;
            guide.SetTitle(keyword.description);
            guide.SetHint(string.Empty);
            guide.ClearAlert();
            // 선택 직후 후보 별을 뿌린다(F-04).
            starField.Spawn(m_AnchorPosition, m_AnchorRotation);
        }

        void OnStarAdded(int count)
        {
            if (State != SessionState.Drawing)
                return;
            m_StarCount = count;
            m_LastActivityTime = Now;
            guide.SetHint(count >= config.minStars ? config.msgDrawingHintCanFinish : config.msgDrawingHint);
            if (count == 2)
                guide.FlashAlert(UndoHint(), config.alertSeconds + 1f);
        }

        void OnMaxStarsReached()
        {
            if (State != SessionState.Drawing)
                return;
            guide.FlashAlert(string.Format(config.msgTooManyStars, config.maxStars), config.alertSeconds);
        }

        void OnUndoRequested()
        {
            if (State != SessionState.Drawing)
                return;
            m_LastActivityTime = Now;
            drawer.Undo();
        }

        void OnUndone(int count)
        {
            m_StarCount = count;
            guide.SetHint(count >= config.minStars ? config.msgDrawingHintCanFinish : config.msgDrawingHint);
        }

        void OnHandsTogether()
        {
            if (State != SessionState.Drawing)
                return;
            m_LastActivityTime = Now;
            m_StarCount = drawer.Count;
            if (m_StarCount < config.minStars)
            {
                guide.FlashAlert(config.msgNeedMoreStars, config.alertSeconds);
                return;
            }
            Go(SessionState.Capture);
        }

        string UndoHint()
        {
            return config.undoGesture == UndoGestureType.MiddlePinch ? config.msgUndoHintMiddlePinch : config.msgUndoHintSwipe;
        }

        // ================================================================ 보조

        // 체험의 기준점. 이 순간의 머리 위치와 바라보는 방향(수평)으로 정한다.
        void PlaceAnchor()
        {
            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            m_AnchorPosition = head.position;
            m_AnchorRotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        // 이전 체험의 값이 다음 체험에 남지 않게 모두 비운다(F-01).
        void ResetSession()
        {
            m_Keyword = null;
            m_KeywordChosenTime = -1f;
            m_ResultName = null;
            m_ResultStory = null;
            m_HandHoldSeconds = 0f;
            m_StarCount = 0;
            gestures.Detecting = false;
            keywordSelector.Hide();
            drawer.Clear();
            starField.Clear();
            guide.ClearAll();
        }
    }
}
