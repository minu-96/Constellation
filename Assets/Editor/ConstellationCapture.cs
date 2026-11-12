using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Constellation.EditorTools
{
    /// <summary>
    /// 단계별 Game 뷰 스크린샷을 자동으로 찍는다(전후 비교, 브로셔 이미지용).
    /// 메뉴: Constellation > 스크린샷 찍기. Main 씬을 열고 플레이를 시작하면 ScreenshotDirector가
    /// 가짜 손으로 체험을 한 바퀴 돌리며 Screenshots/ 폴더에 PNG를 저장하고 플레이를 끝낸다.
    /// 해상도는 Game 뷰 크기를 따른다. 브로셔용은 Game 뷰를 3840×2160으로 맞춘 뒤 실행한다.
    /// </summary>
    public static class ConstellationCapture
    {
        const string k_ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Constellation/스크린샷 찍기 (자동 플레이)")]
        static void CaptureFromMenu()
        {
            Start(false);
        }

        /// <summary>
        /// 명령줄용: Unity -projectPath . -executeMethod Constellation.EditorTools.ConstellationCapture.RunBatch
        /// (-batchmode에서는 Game 뷰가 없어 저장되지 않을 수 있다. 창이 있는 에디터에서 쓰는 것을 권한다.)
        /// </summary>
        public static void RunBatch()
        {
            Start(true);
        }

        static void Start(bool quitWhenDone)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Constellation] 플레이를 멈춘 뒤 다시 실행하세요.");
                return;
            }
            if (!File.Exists(k_ScenePath))
            {
                Debug.LogError("[Constellation] " + k_ScenePath + " 가 없습니다. Constellation > Setup 을 먼저 실행하세요.");
                return;
            }
            if (!quitWhenDone && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorSceneManager.OpenScene(k_ScenePath);
            SessionState.SetBool(ScreenshotDirector.RequestKey, true);
            SessionState.SetBool(ScreenshotDirector.QuitKey, quitWhenDone);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Constellation/스크린샷 폴더 열기")]
        static void OpenFolder()
        {
            Directory.CreateDirectory(ScreenshotDirector.OutputFolder);
            EditorUtility.RevealInFinder(Path.GetFullPath(ScreenshotDirector.OutputFolder));
        }
    }
}
