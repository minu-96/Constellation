using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Constellation.EditorTools
{
    /// <summary>
    /// 에디터에서 손으로 해야 할 일을 줄이는 자동 설정.
    /// 프로젝트를 열면 Main 씬과 설정 에셋이 없을 때 한 번 만들어 준다.
    /// 다시 하려면 메뉴 Constellation > Setup 을 누른다.
    /// </summary>
    [InitializeOnLoad]
    public static class ConstellationSetup
    {
        const string k_ScenePath = "Assets/Scenes/Main.unity";
        const string k_ConfigPath = "Assets/Resources/ConstellationConfig.asset";
        const string k_FallbackPath = "Assets/Data/fallback_stories.json";
        static readonly string[] k_TextShaders = { "TextMeshPro/Mobile/Distance Field", "TextMeshPro/Distance Field" };

        static ConstellationSetup()
        {
            // 에셋 임포트가 끝난 뒤에 실행한다.
            EditorApplication.delayCall += AutoSetup;
        }

        static void AutoSetup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isBatchMode)
                return;
            if (File.Exists(k_ScenePath) && File.Exists(k_ConfigPath))
            {
                EnsureConfig();
                return;
            }
            RunSetup(false);
        }

        [MenuItem("Constellation/Setup (씬과 설정 만들기)")]
        static void RunSetupFromMenu()
        {
            RunSetup(true);
        }

        static void RunSetup(bool verbose)
        {
            EnsureConfig();
            bool sceneCreated = EnsureScene();
            EnsureTextShaders();
            AssetDatabase.SaveAssets();
            if (verbose || sceneCreated)
                Debug.Log("[Constellation] 설정 완료. 씬: " + k_ScenePath + ", 설정: " + k_ConfigPath);
        }

        // 설정 에셋이 없으면 만들고, 폴백 이야기 파일 연결을 확인한다.
        static void EnsureConfig()
        {
            ConstellationConfig config = AssetDatabase.LoadAssetAtPath<ConstellationConfig>(k_ConfigPath);
            if (config == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(k_ConfigPath));
                config = ScriptableObject.CreateInstance<ConstellationConfig>();
                AssetDatabase.CreateAsset(config, k_ConfigPath);
            }

            if (config.fallbackStoriesJson == null)
            {
                TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(k_FallbackPath);
                if (json != null)
                {
                    config.fallbackStoriesJson = json;
                    EditorUtility.SetDirty(config);
                }
            }
        }

        // 빈 씬에 AppBootstrap 하나만 둔 Main 씬을 만들고 빌드 목록의 첫 씬으로 넣는다.
        static bool EnsureScene()
        {
            bool created = false;
            if (!File.Exists(k_ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(k_ScenePath));
                // 저장하지 않은 새 씬이 열려 있으면 덧붙이기(Additive)를 쓸 수 없으므로 그 씬을 대신한다.
                bool untitled = string.IsNullOrEmpty(SceneManager.GetActiveScene().path);
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, untitled ? NewSceneMode.Single : NewSceneMode.Additive);
                GameObject root = new GameObject("Constellation");
                root.AddComponent<AppBootstrap>();
                SceneManager.MoveGameObjectToScene(root, scene);
                EditorSceneManager.SaveScene(scene, k_ScenePath);
                if (!untitled)
                    EditorSceneManager.CloseScene(scene, true);
                created = true;
            }

            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>();
            scenes.Add(new EditorBuildSettingsScene(k_ScenePath, true));
            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (existing.path != k_ScenePath)
                    scenes.Add(new EditorBuildSettingsScene(existing.path, false));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
            return created;
        }

        // 글자 셰이더는 실행 중에 만든 폰트가 쓰므로, 빌드에서 빠지지 않게 "Always Included Shaders"에 넣는다.
        static void EnsureTextShaders()
        {
            Object[] settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (settings == null || settings.Length == 0)
                return;

            SerializedObject graphics = new SerializedObject(settings[0]);
            SerializedProperty list = graphics.FindProperty("m_AlwaysIncludedShaders");
            if (list == null)
                return;

            bool changed = false;
            foreach (string shaderName in k_TextShaders)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null)
                    continue;

                bool found = false;
                for (int i = 0; i < list.arraySize; i++)
                {
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                    {
                        found = true;
                        break;
                    }
                }
                if (found)
                    continue;

                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                changed = true;
            }

            if (changed)
                graphics.ApplyModifiedProperties();
        }

        [MenuItem("Constellation/Main 씬 열기")]
        static void OpenMainScene()
        {
            if (!File.Exists(k_ScenePath))
                RunSetup(true);
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(k_ScenePath);
        }
    }
}
