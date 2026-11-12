using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

namespace Constellation
{
    /// <summary>
    /// 씬에 이 컴포넌트 하나만 두면 나머지(카메라, 하늘, 손 입력, UI, 상태 머신)를 모두 코드로 만든다.
    /// Main 씬은 Constellation > Setup 메뉴(또는 첫 실행 시 자동)로 만들어진다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class AppBootstrap : MonoBehaviour
    {
        // 에디터에서 헤드셋 없이 실행할 때의 눈높이(m)
        const float k_EditorEyeHeight = 1.5f;

        void Awake()
        {
            ConstellationConfig config = ConstellationConfig.Load();

            Transform origin;
            Camera headCamera = BuildRig(out origin);

            SkyBuilder sky = Create<SkyBuilder>("Sky");
            sky.Init(config, headCamera);

            HandInput hands = Create<HandInput>("Hand Input");
            hands.Init(config, origin, headCamera);
            sky.SetHands(hands);

            HandVisual handVisual = Create<HandVisual>("Hand Visual");
            handVisual.Init(config, hands);

            GuideUI guide = Create<GuideUI>("Guide UI");
            guide.Init(config, headCamera.transform);

            GestureDetector gestures = Create<GestureDetector>("Gesture Detector");
            gestures.Init(config, hands, headCamera.transform);

            KeywordSelector keywordSelector = Create<KeywordSelector>("Keyword Selector");
            keywordSelector.Init(config, hands);

            StarField starField = Create<StarField>("Star Field");
            starField.Init(config);

            ConstellationDrawer drawer = Create<ConstellationDrawer>("Constellation Drawer");
            drawer.Init(config, hands, starField);

            GestureGuide gestureGuide = Create<GestureGuide>("Gesture Guide");
            gestureGuide.Init(handVisual, headCamera.transform);

            FinishButton finishButton = Create<FinishButton>("Finish Button");
            finishButton.Init(config, hands);

            CompletionEffect completionEffect = Create<CompletionEffect>("Completion Effect");
            completionEffect.Init(config, starField, drawer);
            // 별자리가 새겨지는 순간 배경 별도 함께 밝아진다.
            completionEffect.Engraved += center => sky.PulseStars();

            CaptureService capture = Create<CaptureService>("Capture Service");
            capture.Init(config);

            AiClient aiClient = Create<AiClient>("AI Client");
            aiClient.Init(config);

            FallbackStore fallback = Create<FallbackStore>("Fallback Store");
            fallback.Init(config);

            GatherEffect gatherEffect = Create<GatherEffect>("Gather Effect");
            gatherEffect.Init(config);

            ResultView resultView = Create<ResultView>("Result View");
            resultView.Init(config);

            CardService cardService = Create<CardService>("Card Service");
            cardService.Init(config);

            AudioManager audioManager = Create<AudioManager>("Audio Manager");
            audioManager.Init(config, headCamera.transform);

            KeywordSky keywordSky = Create<KeywordSky>("Keyword Sky");
            keywordSky.Init(config, headCamera.transform);

            SharedSky sharedSky = Create<SharedSky>("Shared Sky");
            sharedSky.Init(config, headCamera.transform);

            SessionController session = Create<SessionController>("Session Controller");
            session.config = config;
            session.head = headCamera.transform;
            session.hands = hands;
            session.guide = guide;
            session.gestures = gestures;
            session.keywordSelector = keywordSelector;
            session.starField = starField;
            session.drawer = drawer;
            session.completionEffect = completionEffect;
            session.gestureGuide = gestureGuide;
            session.finishButton = finishButton;
            session.capture = capture;
            session.aiClient = aiClient;
            session.fallback = fallback;
            session.gatherEffect = gatherEffect;
            session.resultView = resultView;
            session.cardService = cardService;
            session.sharedSky = sharedSky;
            session.keywordSky = keywordSky;
            session.audioManager = audioManager;
            session.Begin();

            if (config.showFpsOverlay && (Application.isEditor || Debug.isDebugBuild))
                Create<FpsOverlay>("FPS Overlay").Init(config, headCamera.transform);

#if UNITY_EDITOR
            // 메뉴 Constellation > 스크린샷 찍기로 시작했을 때만 자동 촬영을 돌린다.
            if (ScreenshotDirector.Requested)
                Create<ScreenshotDirector>("Screenshot Director").Init(config, session, hands, keywordSelector, starField, headCamera.transform);
#endif
        }

        T Create<T>(string objectName) where T : Component
        {
            GameObject go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        // XR Origin > Camera Offset > Main Camera 구조를 코드로 만든다.
        Camera BuildRig(out Transform origin)
        {
            // 씬에 남아 있는 다른 카메라는 끈다(빈 씬이면 아무것도 없다).
            Camera[] existing = FindObjectsByType<Camera>();
            for (int i = 0; i < existing.Length; i++)
                existing[i].gameObject.SetActive(false);

            GameObject originGo = new GameObject("XR Origin");
            originGo.SetActive(false); // 값을 다 넣은 뒤에 켜야 Awake에서 경고가 나지 않는다.
            originGo.transform.SetParent(transform, false);

            GameObject offsetGo = new GameObject("Camera Offset");
            offsetGo.transform.SetParent(originGo.transform, false);

            GameObject cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.SetParent(offsetGo.transform, false);

            Camera headCamera = cameraGo.AddComponent<Camera>();
            headCamera.nearClipPlane = 0.05f;
            headCamera.farClipPlane = 300f;
            headCamera.clearFlags = CameraClearFlags.SolidColor;
            headCamera.backgroundColor = Color.black;
            cameraGo.AddComponent<AudioListener>();

            TrackedPoseDriver driver = cameraGo.AddComponent<TrackedPoseDriver>();
            InputAction position = new InputAction("Head Position", InputActionType.Value, "<XRHMD>/centerEyePosition");
            InputAction rotation = new InputAction("Head Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation");
            driver.positionInput = new InputActionProperty(position);
            driver.rotationInput = new InputActionProperty(rotation);
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

            XROrigin xrOrigin = originGo.AddComponent<XROrigin>();
            xrOrigin.Camera = headCamera;
            xrOrigin.CameraFloorOffsetObject = offsetGo;
            xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            originGo.SetActive(true);

            // 헤드셋이 없으면(에디터) 카메라가 바닥에 놓이므로 리그를 눈높이만큼 올린다.
            if (!XRSettings.isDeviceActive)
                originGo.transform.position = new Vector3(0f, k_EditorEyeHeight, 0f);

            origin = originGo.transform;
            return headCamera;
        }
    }
}
