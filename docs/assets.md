# 에셋 출처와 라이선스

이 프로젝트가 쓰는 에셋과 그 출처다. 외부 에셋을 새로 넣으면 이 표에 한 줄을 더한다. 라이선스가 확실하지 않은 것은 넣지 않는다.

## 외부 에셋

| 에셋 | 위치 | 출처 | 라이선스 | 비고 |
| --- | --- | --- | --- | --- |
| Gowun Dodum (한글 폰트) | `Assets/Resources/Fonts/ConstellationKorean.ttf` | [Google Fonts 저장소](https://github.com/google/fonts/tree/main/ofl/gowundodum), 원작 [yangheeryu/Gowun-Dodum](https://github.com/yangheeryu/Gowun-Dodum) | SIL Open Font License 1.1 | 원본 그대로 사용(파일 이름만 바꿈). 라이선스 전문은 같은 폴더의 `ConstellationKorean - OFL.txt` |

Unity 패키지(XR Hands, XR Interaction Toolkit, OpenXR, TextMesh Pro, URP)와 VR 템플릿에 들어 있던 샘플은 Unity Companion License를 따른다. 이 프로젝트의 체험 화면은 그 샘플 에셋을 쓰지 않는다.

## 코드로 만든 것 (외부 에셋 아님)

아래는 모두 저장소의 코드가 실행 중에 만든다. 따로 받아 온 이미지나 소리 파일이 없다.

| 항목 | 만드는 코드 |
| --- | --- |
| 하늘 그러데이션, 대기광, 호수 반사 | `Assets/Resources/Shaders/ConstellationSky.shader` |
| 은하수 그림, 능선 높이 | `Assets/Scripts/Sky/SkyData.cs` |
| 배경 별 | `Assets/Scripts/Sky/StarDome.cs`, `ConstellationStarDome.shader` |
| 잡는 별, 손, 선, 별가루 | `ConstellationGlow.shader`, `ConstellationLine.shader`, `StarField.cs`, `HandVisual.cs`, `CompletionEffect.cs` |
| 키워드 아이콘 | `Assets/Scripts/UI/IconPainter.cs` |
| QR 코드 | `Assets/Scripts/UI/QrEncoder.cs` |
| 효과음, 화음 배경 | `Assets/Scripts/Audio/ToneSynth.cs` |
| 풀벌레, 바람, 물소리 | `Assets/Scripts/Audio/AmbienceSynth.cs` |

## 소리를 녹음 파일로 바꿀 때

`ConstellationConfig`의 오디오 칸(`cricketsClip`, `windClip`, `waterClip`, `ambientClip`, 효과음 칸들)에 파일을 넣으면 합성음 대신 그 파일을 쓴다.

- CC0처럼 조건이 분명한 것만 쓴다. 출처 표기가 필요한 라이선스(CC BY 등)는 전시 안내물에 표기할 수 있을 때만 쓴다.
- 위 "외부 에셋" 표에 파일 위치, 받은 주소, 라이선스를 적는다.
- 반복음은 끝과 처음이 이어지는 파일이어야 한다.
- 이 저장소는 `.wav`, `.ogg`, `.mp3`를 Git LFS로 올린다.
