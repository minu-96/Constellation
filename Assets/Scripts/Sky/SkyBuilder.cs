using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Constellation
{
    /// <summary>
    /// 밤하늘을 코드로 만든다.
    ///  - 하늘 돔: 카메라를 따라다니는 큰 구의 안쪽에 그러데이션, 은하수, 능선, 호수를 그린다.
    ///  - 별 돔: 배경 별을 메시 하나로 그린다.
    ///  - 별빛 먼지, 별똥별, (High 단계) URP 후처리
    /// 모두 아주 먼 배경이라 머리 위치를 따라간다(회전은 따라가지 않는다).
    /// </summary>
    public class SkyBuilder : MonoBehaviour
    {
        const float k_DomeDiameter = 180f;

        ConstellationConfig m_Config;
        Transform m_Head;
        Transform m_Dome;
        Material m_SkyMaterial;
        ParticleSystem m_Dust;
        StarDome m_StarDome;
        Texture2D m_RidgeTexture;
        Texture2D m_MilkyWayTexture;
        System.Threading.Tasks.Task<Color32[]> m_MilkyWayTask;
        float m_MilkyWayFade;
        bool m_MilkyWayReady;
        Volume m_Volume;
        VolumeProfile m_Profile;

        public void Init(ConstellationConfig config, Camera headCamera)
        {
            m_Config = config;
            m_Head = headCamera.transform;

            headCamera.clearFlags = CameraClearFlags.SolidColor;
            headCamera.backgroundColor = Color.black;

            // 환경광은 쓰지 않는다(모두 무광 셰이더). 기본 스카이박스도 끈다.
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.fog = false;

            float[][] ridges = SkyData.BuildRidges(7);
            float ridgeMaxY = Mathf.Sin(Mathf.Clamp(m_Config.ridgeMaxElevation, 0f, 30f) * Mathf.Deg2Rad);
            float shoreY = Mathf.Sin(Mathf.Clamp(-m_Config.lakeShoreElevation, 5f, 80f) * Mathf.Deg2Rad);

            BuildDome(ridges, ridgeMaxY, shoreY);
            BuildStarDome(ridges, ridgeMaxY, shoreY);
            BuildShootingStars();
            BuildDust();
            BuildPostProcessing(headCamera);
        }

        void BuildDome(float[][] ridges, float ridgeMaxY, float shoreY)
        {
            GameObject dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "Sky Dome";
            Collider collider = dome.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            m_Dome = dome.transform;
            m_Dome.SetParent(transform, false);
            m_Dome.localScale = Vector3.one * k_DomeDiameter;

            m_RidgeTexture = SkyData.BuildRidgeTexture(ridges);

            m_SkyMaterial = VisualFactory.CreateMaterial("Sky");
            m_SkyMaterial.SetColor("_ZenithColor", m_Config.skyZenithColor);
            m_SkyMaterial.SetColor("_HorizonColor", m_Config.skyHorizonColor);
            m_SkyMaterial.SetColor("_HazeColor", m_Config.skyHazeColor);
            m_SkyMaterial.SetColor("_HillColor", m_Config.hillColor);
            m_SkyMaterial.SetColor("_GroundColor", m_Config.groundColor);
            m_SkyMaterial.SetColor("_WaterTint", m_Config.waterTint);
            m_SkyMaterial.SetTexture("_RidgeTex", m_RidgeTexture);
            m_SkyMaterial.SetFloat("_RidgeMax", ridgeMaxY);
            m_SkyMaterial.SetFloat("_RidgeLayers", m_Config.enableRidgeLayers ? 1f : 0f);
            m_SkyMaterial.SetFloat("_Lake", m_Config.enableLake ? 1f : 0f);
            m_SkyMaterial.SetFloat("_ShoreY", shoreY);
            m_SkyMaterial.SetFloat("_Vignette", m_Config.skyVignette);
            m_SkyMaterial.SetFloat("_Exposure", m_Config.skyExposure);
            m_SkyMaterial.SetFloat("_MilkyWayStrength", 0f);

            MeshRenderer renderer = dome.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = m_SkyMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // 은하수 그림은 계산이 무거워 백그라운드에서 만들고, 다 되면 서서히 나타나게 한다.
            if (m_Config.enableMilkyWay)
                m_MilkyWayTask = System.Threading.Tasks.Task.Run(() => SkyData.BuildMilkyWayPixels());
        }

        void BuildStarDome(float[][] ridges, float ridgeMaxY, float shoreY)
        {
            if (!m_Config.enableStarDome)
                return;
            bool low = m_Config.qualityTier == QualityTier.Low;
            int count = low ? m_Config.backgroundStarCount / 2 : m_Config.backgroundStarCount;
            // 호수에 비친 별은 메시를 한 번 더 그린다. Low 단계에서는 생략한다.
            bool reflection = m_Config.enableLake && !low;

            GameObject go = new GameObject("Star Dome");
            go.transform.SetParent(transform, false);
            m_StarDome = go.AddComponent<StarDome>();
            m_StarDome.Init(m_Config, ridges, ridgeMaxY, count, reflection ? shoreY : 0f);
        }

        void BuildShootingStars()
        {
            if (!m_Config.enableShootingStars || m_Config.qualityTier == QualityTier.Low)
                return;
            GameObject go = new GameObject("Shooting Stars");
            go.transform.SetParent(transform, false);
            go.AddComponent<ShootingStars>().Init(m_Config, m_Head);
        }

        // 백그라운드 계산이 끝나면 텍스처로 올리고 은하수를 서서히 밝힌다.
        void UpdateMilkyWay()
        {
            if (m_MilkyWayTask != null && m_MilkyWayTask.IsCompleted)
            {
                if (m_MilkyWayTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && m_MilkyWayTask.Result != null)
                {
                    m_MilkyWayTexture = new Texture2D(SkyData.MilkyWayWidth, SkyData.MilkyWayHeight, TextureFormat.RGBA32, false);
                    m_MilkyWayTexture.name = "Milky Way";
                    m_MilkyWayTexture.wrapModeU = TextureWrapMode.Repeat;
                    m_MilkyWayTexture.wrapModeV = TextureWrapMode.Clamp;
                    m_MilkyWayTexture.filterMode = FilterMode.Bilinear;
                    m_MilkyWayTexture.SetPixels32(m_MilkyWayTask.Result);
                    m_MilkyWayTexture.Apply(false, true);
                    m_SkyMaterial.SetTexture("_MilkyWayTex", m_MilkyWayTexture);
                    m_MilkyWayReady = true;
                }
                else
                {
                    Debug.LogWarning("[Constellation] 은하수 그림을 만들지 못했습니다. 은하수 없이 진행합니다.");
                }
                m_MilkyWayTask = null;
            }

            if (m_MilkyWayReady && m_MilkyWayFade < 1f)
            {
                m_MilkyWayFade = Mathf.MoveTowards(m_MilkyWayFade, 1f, Time.deltaTime / 3f);
                m_SkyMaterial.SetFloat("_MilkyWayStrength", m_Config.milkyWayStrength * m_MilkyWayFade);
            }
        }

        // 가까이에서 천천히 떠다니는 별빛 먼지. 공간감을 준다.
        void BuildDust()
        {
            if (m_Config.ambientParticleCount <= 0 || m_Config.qualityTier == QualityTier.Low)
                return;

            GameObject go = new GameObject("Star Dust");
            go.transform.SetParent(transform, false);
            m_Dust = go.AddComponent<ParticleSystem>();
            m_Dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = m_Dust.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.05f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f);
            main.startColor = new Color(m_Config.starColor.r, m_Config.starColor.g, m_Config.starColor.b, 0.5f);
            main.maxParticles = m_Config.ambientParticleCount;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.prewarm = true;

            ParticleSystem.EmissionModule emission = m_Dust.emission;
            emission.rateOverTime = m_Config.ambientParticleCount / 11f;

            ParticleSystem.ShapeModule shape = m_Dust.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 5f;
            shape.radiusThickness = 0.75f;

            // 태어날 때와 사라질 때 서서히 밝아지고 어두워진다.
            ParticleSystem.ColorOverLifetimeModule fade = m_Dust.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            Material material = VisualFactory.CreateMaterial("Glow");
            material.SetFloat(VisualFactory.BillboardId, 0f); // 파티클은 이미 카메라를 본다.
            material.SetFloat(VisualFactory.IntensityId, 0.8f);
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            m_Dust.Play();
        }

        // URP 후처리: 블룸, (선택) 톤매핑, 비네트, 색 보정, (선택) 필름 그레인.
        // 화면 전체를 여러 번 다시 그리므로 Quest에서는 비용이 크다. 품질 단계가 High이고 설정을 켰을 때만 만든다.
        // 기본 화면은 후처리 없이도 완성되도록 별 후광과 하늘 셰이더의 비네트·잡음으로 처리해 두었다.
        void BuildPostProcessing(Camera headCamera)
        {
            // 후처리는 화면 전체를 여러 번 다시 그린다. High 단계에서 설정을 켰을 때만 쓴다.
            bool usePost = m_Config.usePostProcessing && m_Config.qualityTier == QualityTier.High;
            UniversalAdditionalCameraData cameraData = headCamera.GetUniversalAdditionalCameraData();
            if (cameraData != null)
                cameraData.renderPostProcessing = usePost;
            if (!usePost)
                return;

            GameObject go = new GameObject("Post Processing");
            go.transform.SetParent(transform, false);
            m_Volume = go.AddComponent<Volume>();
            m_Volume.isGlobal = true;
            m_Volume.priority = 10f;

            m_Profile = ScriptableObject.CreateInstance<VolumeProfile>();
            m_Profile.name = "Constellation Post (runtime)";

            // 별빛 번짐. 낮은 해상도와 적은 반복으로 비용을 줄인다.
            Bloom bloom = m_Profile.Add<Bloom>(true);
            bloom.intensity.Override(m_Config.bloomIntensity);
            bloom.threshold.Override(m_Config.bloomThreshold);
            bloom.scatter.Override(m_Config.bloomScatter);
            bloom.highQualityFiltering.Override(false);
            bloom.downscale.Override(m_Config.bloomQuarterResolution ? BloomDownscaleMode.Quarter : BloomDownscaleMode.Half);
            bloom.maxIterations.Override(Mathf.Clamp(m_Config.bloomIterations, 2, 6));

            if (m_Config.postTonemapping)
            {
                Tonemapping tonemapping = m_Profile.Add<Tonemapping>(true);
                tonemapping.mode.Override(TonemappingMode.Neutral);
            }

            if (m_Config.vignetteIntensity > 0.001f)
            {
                Vignette vignette = m_Profile.Add<Vignette>(true);
                vignette.intensity.Override(m_Config.vignetteIntensity);
                vignette.smoothness.Override(0.45f);
            }

            ColorAdjustments color = m_Profile.Add<ColorAdjustments>(true);
            color.contrast.Override(m_Config.postContrast);
            color.saturation.Override(m_Config.postSaturation);

            if (m_Config.filmGrainIntensity > 0.001f)
            {
                FilmGrain grain = m_Profile.Add<FilmGrain>(true);
                grain.type.Override(FilmGrainLookup.Thin1);
                grain.intensity.Override(m_Config.filmGrainIntensity);
            }

            m_Volume.sharedProfile = m_Profile;
        }

        void LateUpdate()
        {
            // 하늘은 무한히 멀어 보여야 하므로 머리 위치를 따라간다(회전은 따라가지 않는다).
            UpdateMilkyWay();
            if (m_Dome != null && m_Head != null)
                m_Dome.position = m_Head.position;
            if (m_StarDome != null && m_Head != null)
                m_StarDome.transform.position = m_Head.position;
            if (m_Dust != null && m_Head != null)
                m_Dust.transform.position = m_Head.position;
        }

        void OnDestroy()
        {
            VisualFactory.DestroyObject(m_SkyMaterial);
            VisualFactory.DestroyObject(m_RidgeTexture);
            VisualFactory.DestroyObject(m_MilkyWayTexture);
            VisualFactory.DestroyObject(m_Profile);
        }
    }
}
