# 실행 준비와 기기 테스트 안내

코드로 할 수 없는 일(Unity 에디터, Cloudflare, API 키, Quest 빌드)을 순서대로 정리했다. 구현 범위와 규격은 [기능 명세서](spec.md)를 따른다.

## 지금 상태

- 구현 범위: P0 전체와 P1(F-06, F-12, F-14). F-13은 Quest 캐스트 기능을 쓰므로 코드가 없다. F-16, F-17은 하지 않았다.
- 중계 서버(worker/)는 단위 테스트를 통과했다. 실제 배포와 실제 Claude 호출은 아직 하지 않았다.
- Unity 코드는 엔진 API 기준으로 컴파일만 확인했다. 에디터와 Quest에서 실행해 본 적은 없다. 셰이더 4개와 에디터 자동 설정 스크립트는 컴파일도 확인하지 못했다.

## 할 일 (순서대로)

### 1. Unity에서 열기

- [ ] `feature/polish` 브랜치를 체크아웃하고 Unity 6000.6으로 연다.
- [ ] `Assets/Scenes/Main.unity`가 자동으로 생겼는지 확인한다. 없으면 메뉴 **Constellation > Setup (씬과 설정 만들기)** 를 누른다.
- [ ] Console에 컴파일 오류나 셰이더 오류가 없는지 본다. 오류가 있으면 메시지를 그대로 남겨 둔다.
- [ ] **File > Build Profiles**에서 Main 씬이 목록 첫 줄에 체크되어 있는지 확인한다.

### 2. 에디터에서 흐름 확인

Main 씬을 열고 Play 한다. 헤드셋 없이 아래 키로 손을 흉내 낼 수 있다(에디터 전용).

| 입력 | 동작 |
| --- | --- |
| H | 손 숨기기 / 보이기 (기본은 보임) |
| 화살표 키, 마우스 가운데 버튼 드래그 | 둘러보기 (헤드셋이 없을 때만) |
| 마우스 이동 | 오른손 위치 |
| 마우스 휠 | 손까지의 거리 |
| 마우스 왼쪽 버튼 | 핀치 |
| Space (누르고 있기), 마우스 오른쪽 클릭 | 양손 모으기 (오른쪽 클릭은 꺼짐 → 떨어짐 → 모음 순서로 바뀜) |
| Z | 되돌리기 |

- [ ] H를 누르면 대기 화면에서 키워드 선택으로 넘어간다.
- [ ] 아이콘 하나를 클릭하면 별이 나타난다.
- [ ] 별을 클릭해 3개 이상 잇고 Space를 누르고 있으면 결과 화면까지 간다(서버 연결 전에는 폴백 이야기가 나온다).
- [ ] 한글이 네모로 보이지 않는다.

### 3. Unity가 만든 파일 커밋

- [ ] `Assets/Scenes/Main.unity`와 `.meta`
- [ ] Unity가 고친 `.meta` 파일들과 `Assets/Resources/ConstellationConfig.asset`
- [ ] `ProjectSettings/GraphicsSettings.asset`, `ProjectSettings/EditorBuildSettings.asset`

### 4. 계정과 키

- [ ] Cloudflare 계정을 만든다(무료 플랜).
- [ ] [Anthropic 콘솔](https://console.anthropic.com/)에서 API 키를 발급하고 월 지출 한도를 건다.

### 5. 중계 서버 배포

[worker/README.md](../worker/README.md)를 따른다.

- [ ] `npm install`, `npx wrangler login`
- [ ] 시크릿 등록: `ANTHROPIC_API_KEY`, `APP_SHARED_TOKEN`
- [ ] (QR 카드를 쓸 때만) `npx wrangler kv namespace create CARDS`를 실행하고, 나온 id를 `wrangler.toml`의 주석 블록에 넣어 주석을 푼다.
- [ ] `npm run deploy`로 배포하고 주소를 적어 둔다.
- [ ] README의 curl 예시로 실제 이야기가 오는지 확인한다.

### 6. 앱에 주소와 토큰 넣기

- [ ] `Assets/Data/constellation_secrets.example.json`을 `Assets/Resources/constellation_secrets.json`으로 복사한다.
- [ ] `workerUrl`에 Worker 주소, `appToken`에 `APP_SHARED_TOKEN`과 같은 값을 넣는다.
- [ ] 이 파일은 `.gitignore`에 들어 있다. 커밋 목록에 보이지 않는지 확인한다.

### 7. Quest 3 빌드

- [ ] Build Profiles에서 Android로 전환한다.
- [ ] **Project Settings > XR Plug-in Management > Android**에서 OpenXR이 켜져 있고, OpenXR 기능 중 Hand Tracking Subsystem과 Meta Quest Support가 켜져 있는지 확인한다(저장소 설정상 켜져 있다).
- [ ] Quest를 개발자 모드로 연결하고 Build And Run 한다.
- [ ] Quest 설정에서 손 추적이 켜져 있는지 확인한다.

### 8. 기기 테스트

[기능 명세서의 테스트 체크리스트](spec.md#테스트-체크리스트)를 돌린다. 아래 값을 조정하며 맞춘다.

## 기기에서 조정할 설정값

Unity 쪽 값은 모두 `Assets/Resources/ConstellationConfig.asset`에 있다(인스펙터에서 수정).

| 구분 | 항목 | 초깃값 |
| --- | --- | --- |
| 핀치 | `pinchStartDistance` / `pinchEndDistance` | 0.022 / 0.04 m |
| 별 잡기 | `starGrabRadius`, `starHintRange` | 0.05 m, 0.1 m |
| 별 잡기 | `selectWhileDragging`, `dragSnapAngle`, `dragSnapDepthTolerance` | 켬, 5도, 0.3m |
| 키워드 | `keywordGrabRadius` | 0.09 m |
| 손 표시 | `handFingerWidth`, `handPalmOpacity` | 0.013 m, 0.14 |
| 손 표시 | `pinchHintRange` (빛이 모이기 시작하는 거리) | 0.06 m |
| 손 표시 | `handColor`, `thumbTipColor`, `indexTipColor` | 민트, 분홍, 연두 |
| 소리 | `pinchVolume` | 0.3 |
| 양손 모으기 | `handsTogetherDistance` / `handsTogetherReleaseDistance` | 0.11 / 0.2 m |
| 양손 모으기 | `handsTogetherHoldSeconds` | 0.7초 |
| 되돌리기 | `undoGesture` | PalmSwipe (다른 선택지: MiddlePinch) |
| 되돌리기 | `undoSwipeSpeed` / `undoSwipeDistance` | 1.0 m/s / 0.22 m |
| 손 인식 | `handCheckHoldSeconds` / `handLostGuideDelay` | 0.4 / 1.0초 |
| 시간 | `keywordIdleTimeoutSeconds` / `drawingIdleTimeoutSeconds` | 45 / 90초 |
| 시간 | `resultSeconds` / `cardSeconds` | 14 / 20초 |
| 시간 | `endCooldownSeconds` / `aiMinWaitSeconds` | 5 / 3초 |
| 별 구역 | `posture` | Standing (다른 선택지: Seated, Custom) |
| 별 구역 | `standingZone` (고도, 좌우, 거리) | +10°~+35°, ±55°, 0.45 m |
| 별 구역 | `seatedZone` (고도, 좌우, 거리) | +10°~+32°, ±55°, 0.40 m |
| 별 구역 | `ridgeMaxElevation`, `starRidgeMargin` | 8°, 2° |
| 별 구역 | `starRows`, `starJitter` | 3줄, 0.35 |
| 배치 | `guideElevation`, `guideTextSize`, `resultStorySize` | -27°, 0.075 m, 0.095 m |
| 성능 | `qualityTier` | Medium (Low / Medium / High) |
| 성능 | `usePostProcessing`, `ambientParticleCount` | 끔(High에서만 적용), 120 |
| 캡처 | `captureJpegQuality` | 80 |

중계 서버 쪽 값은 `worker/wrangler.toml`의 `[vars]`에 있다: `RATE_LIMIT_PER_MINUTE`(6), `CLAUDE_MODEL`, `CLAUDE_EFFORT`, `CARD_TTL_DAYS`.

프레임이 떨어지면 `qualityTier`를 Low로 낮춘다.

### 별 구역

잡는 별은 체험을 시작할 때(손 인식 직후)의 눈 위치와 정면을 기준으로 놓는다. 각도는 눈높이가 0°, 위가 +다.

- 상한은 45°에서 잘리고, 하한은 `ridgeMaxElevation + starRidgeMargin`(기본 10°) 아래로 내려가지 않는다. 그래서 별은 항상 능선보다 위에 보인다.
- 별이 눈높이보다 위에 있으므로 팔은 어깨보다 올라간다. 계산상 평균 45°, 가장 높은 별에서 60° 정도 올린다. 힘들어 보이면 `elevationMax`를 낮추거나 `distance`를 줄인다.
- 어린이는 팔이 짧다. `distance`를 0.35~0.40으로 줄여 확인한다.
- 구역을 좁히면 별 사이가 가까워져 옆 별이 잡히기 쉽다. 그럴 때는 `candidateStarCount`를 줄이거나 `starGrabRadius`를 줄인다.
- 머리를 많이 움직이면(숙이거나 앉으면) 가까운 별이 능선 쪽으로 내려와 보일 수 있다. 기준은 시작 순간의 눈 위치다.

## 구현하면서 정한 것

명세에 없거나 미정이던 부분을 이렇게 정했다. 바꾸고 싶으면 해당 파일을 고친다.

| 항목 | 정한 내용 | 위치 |
| --- | --- | --- |
| F-12 저장 방식 | Worker의 `/card`가 Cloudflare KV에 30일 보관. 카드는 휴대폰에서 열리는 웹 페이지 | `worker/src/card.js` |
| 캡처 방식 | 카메라 촬영 대신 별 좌표로 512px 그림을 직접 그림 | `CaptureService.cs` |
| 되돌리기 | 명세대로 선이 없으면(별 1개) 아무 일도 없음. 첫 별은 바꿀 수 없다 | `ConstellationDrawer.cs` |
| 무입력 복귀 | KeywordSelect뿐 아니라 Drawing에도 적용 | `SessionController.cs` |
| 다음 체험 시작 | 끝난 뒤 5초 쉬고, 손을 한 번 내렸다가 들어야 시작 | `SessionController.cs` |
| 손 표시 | 손 모델 대신 손끝·손바닥에 빛나는 점 | `HandVisual.cs` |
| 한글 폰트 | Gowun Dodum(OFL)을 `Assets/Resources/Fonts`에 포함 | `TextFactory.cs` |
| 금칙어 | 프롬프트 금지에 더해 뜻이 분명한 낱말 목록으로 한 번 더 거름 | `worker/src/rules.js`, `StoryRules.cs` |
| 호출 제한 | Worker 인스턴스 메모리 기준이라 엄밀하지 않음. 지출 상한은 Anthropic 콘솔 한도로 건다 | `worker/src/rateLimit.js` |

이름·이야기 규칙은 Worker와 앱 두 곳에 있다. 한쪽을 바꾸면 다른 쪽도 맞춘다.

## 전시 당일 확인

- [ ] 전시장 와이파이로 AI 결과가 10초 안에 오는지 확인한다. 안 되면 휴대폰 핫스팟으로 바꾼다.
- [ ] 와이파이를 끄고도 폴백 이야기로 끝까지 가는지 확인한다.
- [ ] Quest 캐스트로 모니터에 화면을 띄운다(F-13). QR은 이 모니터에서 찍는다.
- [ ] 전시장 조명에서 손 추적이 되는지 확인한다.
