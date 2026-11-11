using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// AI 응답을 기다리는 동안 별빛이 별자리로 모여드는 연출(F-09).
    /// 구 껍질에서 태어난 입자가 가운데로 빨려 들어간다.
    /// </summary>
    public class GatherEffect : MonoBehaviour
    {
        ConstellationConfig m_Config;
        ParticleSystem m_Particles;
        GlowSprite m_Core;
        bool m_Playing;
        float m_CoreLevel;

        public void Init(ConstellationConfig config)
        {
            m_Config = config;

            m_Particles = gameObject.AddComponent<ParticleSystem>();
            m_Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = m_Particles.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = 1.6f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
            main.startColor = new ParticleSystem.MinMaxGradient(config.starColor, config.starSelectedColor);
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = m_Particles.emission;
            emission.rateOverTime = 70f;

            ParticleSystem.ShapeModule shape = m_Particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radiusThickness = 0.15f;

            ParticleSystem.ColorOverLifetimeModule fade = m_Particles.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystemRenderer renderer = GetComponent<ParticleSystemRenderer>();
            Material material = VisualFactory.CreateMaterial("Glow");
            material.SetFloat(VisualFactory.BillboardId, 0f); // 파티클은 이미 카메라를 본다.
            material.SetFloat(VisualFactory.IntensityId, 1.3f);
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            m_Core = GlowSprite.Create("Gather Core", transform, 0.12f, config.starSelectedColor, 0f);
            m_Core.SetCore(0.1f);
        }

        /// <summary>주어진 위치를 중심으로 별빛을 모은다.</summary>
        public void Play(Vector3 center, float radius)
        {
            transform.position = center;

            ParticleSystem.ShapeModule shape = m_Particles.shape;
            shape.radius = radius;
            // 수명 동안 반지름만큼 안쪽으로 움직이도록 속도를 음수로 준다.
            ParticleSystem.MainModule main = m_Particles.main;
            main.startSpeed = -radius / 1.6f;

            m_Playing = true;
            m_Particles.Play();
        }

        public void Stop()
        {
            if (!m_Playing)
                return;
            m_Playing = false;
            m_Particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void Update()
        {
            if (m_Core == null)
                return;
            // 가운데 빛은 숨 쉬듯 천천히 밝아졌다 어두워진다.
            float target = m_Playing ? 0.55f + 0.25f * Mathf.Sin(Time.time * 2.6f) : 0f;
            m_CoreLevel = Mathf.Lerp(m_CoreLevel, target, 1f - Mathf.Exp(-4f * Time.deltaTime));
            m_Core.SetIntensity(m_CoreLevel);
        }
    }
}
