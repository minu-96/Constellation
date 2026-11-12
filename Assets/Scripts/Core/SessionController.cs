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
        [NonSerialized] public CompletionEffect completionEffect;
        [NonSerialized] public CaptureService capture;
        [NonSerialized] public AiClient aiClient;
        [NonSerialized] public FallbackStore fallback;
        [NonSerialized] public GatherEffect gatherEffect;
        [NonSerialized] public ResultView resultView;
        [NonSerialized] public CardService cardService;
        [NonSerialized] public AudioManager audioManager;

        /// <summary>상태가 바뀐 뒤에 알린다(이전 상태, 새 상태).</summary>
        public event Action<SessionState, SessionState> StateChanged;

        public SessionState State { get; private set; }

        // ---- 체험 한 번의 데이터. ResetSession에서 모두 비운다.
        KeywordDefinition m_Keyword;
        Vector3 m_AnchorPosition;
        Quaternion m_AnchorRotation = Quaternion.identity;
        int m_StarCount;
        byte[] m_CaptureJpeg;
        Texture2D m_CaptureTexture;
        string m_ResultName;
        string m_ResultStory;
        bool m_AiDone;
        bool m_CardQrShown;
        float m_CardReadyTime;

        // ---- 타이머
        float m_StateEnterTime;
        float m_LastActivityTime;
        float m_NoHandSeconds;
        float m_HandHoldSeconds;
        float m_KeywordChosenTime = -1f;
        float m_IdleReadyTime;
        bool m_SawNoHandsSinceEnd = true;
        bool m_Started;

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
            completionEffect.StarReached += OnCompletionStarReached;

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
            if (completionEffect != null)
                completionEffect.StarReached -= OnCompletionStarReached;
            if (gestures != null)
            {
                gestures.HandsTogether -= OnHandsTogether;
                gestures.UndoRequested -= OnUndoRequested;
            }
            ReleaseCapture();
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
            audioManager.SetScene(SceneFor(next));
            if (StateChanged != null)
                StateChanged(previous, next);
        }

        // 단계마다 배경음과 환경음의 균형이 달라진다.
        static AudioScene SceneFor(SessionState state)
        {
            switch (state)
            {
                case SessionState.KeywordSelect:
                case SessionState.Drawing:
                    return AudioScene.Drawing;
                case SessionState.Capture:
                case SessionState.AiRequest:
                    return AudioScene.Waiting;
                case SessionState.Result:
                case SessionState.Card:
                    return AudioScene.Result;
                default:
                    return AudioScene.Idle;
            }
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
                    m_AiDone = false;
                    starField.SetCandidatesVisible(false);
                    gatherEffect.Play(starField.GetSelectionCenter(drawer.SelectedIndices), config.ActiveStarZone.distance * 0.9f);
                    aiClient.Request(m_Keyword.id, m_StarCount, m_CaptureJpeg, OnAiResult);
                    break;

                case SessionState.Result:
                    EnsureResult();
                    guide.ClearAlert();
                    guide.SetTitle(string.Empty);
                    guide.SetHint(string.Empty);
                    starField.SetCandidatesVisible(false);
                    starField.RaiseToSky(true);
                    resultView.Show(m_ResultName, m_ResultStory, m_AnchorPosition, m_AnchorRotation);
                    // 카드 화면에서 QR이 바로 보이도록 업로드를 미리 시작한다.
                    cardService.Prepare(m_Keyword.id, m_ResultName, m_ResultStory, m_CaptureJpeg);
                    audioManager.PlayResult();
                    break;

                case SessionState.Card:
                    m_CardQrShown = false;
                    m_CardReadyTime = -1f;
                    if (!cardService.HasFailed)
                    {
                        resultView.Hide();
                        cardService.Show(m_AnchorPosition, m_AnchorRotation, m_CaptureTexture, m_ResultName, m_ResultStory);
                        // 안내 문구는 카드 안에 있으므로 하늘의 안내는 비운다.
                        guide.SetHint(string.Empty);
                    }
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

                case SessionState.AiRequest:
                    gatherEffect.Stop();
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
            // 자동 단계. 캡처에 실패하면 AI를 건너뛰고 폴백 이야기로 결과 화면에 간다(F-10).
            bool ok = false;
            try
            {
                ReleaseCapture();
                ok = capture.TryCapture(starField.GetProjectedPoints(drawer.SelectedIndices), out m_CaptureJpeg, out m_CaptureTexture);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Constellation] 캡처 실패: " + e.Message);
            }

            if (ok)
            {
                Go(SessionState.AiRequest);
            }
            else
            {
                UseFallback();
                Go(SessionState.Result);
            }
        }

        void UpdateAiRequest()
        {
            // AiClient가 10초에 끊지만, 만약을 위해 여기서도 한 번 더 지켜본다.
            if (!m_AiDone && StateSeconds > config.aiTimeoutSeconds + 2f)
            {
                aiClient.Cancel();
                UseFallback();
                m_AiDone = true;
            }

            if (m_AiDone && StateSeconds >= config.aiMinWaitSeconds)
                Go(SessionState.Result);
        }

        void UpdateResult()
        {
            if (StateSeconds >= config.resultSeconds)
                Go(SessionState.Card);
        }

        void UpdateCard()
        {
            // 카드 생성이나 업로드에 실패하면 카드 없이 끝낸다.
            if (cardService.HasFailed)
            {
                Go(SessionState.Idle);
                return;
            }

            if (cardService.IsReady && !m_CardQrShown)
            {
                m_CardQrShown = true;
                m_CardReadyTime = Now;
            }

            if (m_CardQrShown && Now - m_CardReadyTime >= config.cardSeconds)
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
            // 손으로 무언가를 고르는 상태에서만 핀치 소리를 낸다.
            if (State == SessionState.KeywordSelect || State == SessionState.Drawing)
                audioManager.PlayPinch();
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
            audioManager.PlayKeywordSelect();
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
            if (count <= 1)
                audioManager.PlayStarGrab(count);
            else
                audioManager.PlayLineConnect(count);
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
            audioManager.PlayUndo();
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
            audioManager.PlayComplete();
            // 별이 차례로 번쩍이고 선이 새겨지는 연출. AI를 기다리는 동안 이어진다.
            completionEffect.Play(drawer.SelectedIndices);
            Go(SessionState.Capture);
        }

        void OnCompletionStarReached(int order)
        {
            audioManager.PlayEngraveNote(order + 1);
        }

        string UndoHint()
        {
            return config.undoGesture == UndoGestureType.MiddlePinch ? config.msgUndoHintMiddlePinch : config.msgUndoHintSwipe;
        }

        void OnAiResult(AiResult result)
        {
            if (State != SessionState.AiRequest || m_AiDone)
                return;
            if (result != null && result.ok)
            {
                m_ResultName = result.name;
                m_ResultStory = result.story;
            }
            else
            {
                // 타임아웃, 오류, 형식 이상은 모두 같은 방식으로 폴백한다. 사용자에게는 알리지 않는다.
                if (config.logStateChanges)
                    Debug.Log("[Constellation] AI 실패, 폴백 사용: " + (result != null ? result.error : "null"));
                UseFallback();
            }
            m_AiDone = true;
        }

        void UseFallback()
        {
            FallbackStory story = fallback.GetRandom(m_Keyword != null ? m_Keyword.id : null);
            m_ResultName = story.name;
            m_ResultStory = story.story;
        }

        // 어떤 경로로 왔든 결과 화면에는 반드시 이름과 이야기가 있어야 한다(F-10).
        void EnsureResult()
        {
            if (string.IsNullOrEmpty(m_ResultName) || string.IsNullOrEmpty(m_ResultStory))
                UseFallback();
        }

        void ReleaseCapture()
        {
            if (m_CaptureTexture != null)
                Destroy(m_CaptureTexture);
            m_CaptureTexture = null;
            m_CaptureJpeg = null;
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
            completionEffect.Stop();
            drawer.Clear();
            starField.Clear();
            m_AiDone = false;
            aiClient.Cancel();
            gatherEffect.Stop();
            ReleaseCapture();
            m_CardQrShown = false;
            m_CardReadyTime = -1f;
            resultView.Hide();
            cardService.Clear();
            guide.ClearAll();
        }
    }
}
