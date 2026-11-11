using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Constellation
{
    /// <summary>
    /// 밤하늘을 코드로 만든다. 카메라를 따라다니는 큰 구의 안쪽에 하늘 셰이더를 그린다.
    /// </summary>
    public class SkyBuilder : MonoBehaviour
    {
        const float k_DomeDiameter = 180f;

        ConstellationConfig m_Config;
        Transform m_Head;
        Transform m_Dome;
        Material m_SkyMaterial;
        ParticleSystem m_Dust;
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

            BuildDome();
            BuildDust();
            BuildPostProcessing(headCamera);
        }

        void BuildDome()
        {
            GameObject dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "Sky Dome";
            Collider collider = dome.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            m_Dome = dome.transform;
            m_Dome.SetParent(transform, false);
            m_Dome.localScale = Vector3.one * k_DomeDiameter;

            m_SkyMaterial = VisualFactory.CreateMaterial("Sky");
            m_SkyMaterial.SetColor("_ZenithColor", m_Config.skyZenithColor);
            m_SkyMaterial.SetColor("_HorizonColor", m_Config.skyHorizonColor);
            m_SkyMaterial.SetColor("_HillColor", m_Config.hillColor);

            MeshRenderer renderer = dome.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = m_SkyMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // 가까이에서 천천히 떠다니는 별빛 먼지. 공간감을 준다.
        void BuildDust()
        {
            if (m_Config.ambientParticleCount <= 0)
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

        // 블룸, 비네트, 색 보정. 프레임이 떨어지면 설정에서 끈다.
        void BuildPostProcessing(Camera headCamera)
        {
            UniversalAdditionalCameraData cameraData = headCamera.GetUniversalAdditionalCameraData();
            if (cameraData != null)
                cameraData.renderPostProcessing = m_Config.usePostProcessing;
            if (!m_Config.usePostProcessing)
                return;

            GameObject go = new GameObject("Post Processing");
            go.transform.SetParent(transform, false);
            m_Volume = go.AddComponent<Volume>();
            m_Volume.isGlobal = true;
            m_Volume.priority = 10f;

            m_Profile = ScriptableObject.CreateInstance<VolumeProfile>();
            m_Profile.name = "Constellation Post (runtime)";

            Bloom bloom = m_Profile.Add<Bloom>(true);
            bloom.intensity.Override(m_Config.bloomIntensity);
            bloom.threshold.Override(m_Config.bloomThreshold);
            bloom.scatter.Override(0.65f);

            Vignette vignette = m_Profile.Add<Vignette>(true);
            vignette.intensity.Override(m_Config.vignetteIntensity);
            vignette.smoothness.Override(0.45f);

            ColorAdjustments color = m_Profile.Add<ColorAdjustments>(true);
            color.contrast.Override(8f);
            color.saturation.Override(6f);

            m_Volume.sharedProfile = m_Profile;
        }

        void LateUpdate()
        {
            // 하늘은 무한히 멀어 보여야 하므로 머리 위치를 따라간다(회전은 따라가지 않는다).
            if (m_Dome != null && m_Head != null)
                m_Dome.position = m_Head.position;
            if (m_Dust != null && m_Head != null)
                m_Dust.transform.position = m_Head.position;
        }

        void OnDestroy()
        {
            VisualFactory.DestroyObject(m_SkyMaterial);
            VisualFactory.DestroyObject(m_Profile);
        }
    }
}
