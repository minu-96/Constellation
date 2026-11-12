using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 고른 키워드에 따라 하늘이 달라진다. 키워드를 고르는 순간 서서히 나타나고 체험이 끝나면 사라진다.
    ///  - wish(소원): 유성우. 정면 높은 곳에서 별똥별이 사방으로 떨어진다.
    ///  - adventure(모험): 오로라. 정면 먼 하늘에 초록과 보라의 장막이 일렁인다.
    ///  - loved_one(소중한 사람): 반딧불. 주변에 따뜻한 빛이 천천히 떠다니며 깜빡인다.
    /// 그 밖의 키워드에는 아무것도 하지 않는다. 보여 주기만 하고 상태에는 관여하지 않는다.
    /// </summary>
    public class KeywordSky : MonoBehaviour
    {
        enum Effect
        {
            None,
            Meteors,
            Aurora,
            Fireflies
        }

        const float k_SkyRadius = 55f;
        const int k_MeteorCount = 4;
        const float k_MeteorSeconds = 0.65f;

        ConstellationConfig m_Config;
        Transform m_Head;
        Effect m_Effect = Effect.None;
        Vector3 m_AnchorPosition;
        Quaternion m_AnchorRotation = Quaternion.identity;

        // 유성우
        LineRenderer[] m_Meteors;
        float[] m_MeteorAge;
        Vector3[] m_MeteorStart;
        Vector3[] m_MeteorTravel;
        float m_NextMeteor;
        float m_MeteorLevel;

        // 오로라
        Transform m_Aurora;
        Material m_AuroraMaterial;
        Mesh m_AuroraMesh;
        float m_AuroraLevel;

        // 반딧불
        ParticleSystem m_Fireflies;

        public void Init(ConstellationConfig config, Transform head)
        {
            m_Config = config;
            m_Head = head;
            BuildMeteors();
            BuildAurora();
            BuildFireflies();
        }

        /// <summary>키워드에 맞는 하늘을 띄운다. 기준은 체험을 시작할 때의 머리 위치와 정면이다.</summary>
        public void Show(string keywordId, Vector3 anchorPosition, Quaternion anchorRotation)
        {
            if (m_Config == null || !m_Config.enableKeywordSky)
                return;
            m_AnchorPosition = anchorPosition;
            m_AnchorRotation = anchorRotation;

            switch (keywordId)
            {
                case "wish": m_Effect = Effect.Meteors; break;
                case "adventure": m_Effect = Effect.Aurora; break;
                case "loved_one": m_Effect = Effect.Fireflies; break;
                default: m_Effect = Effect.None; break;
            }

            if (m_Effect == Effect.Meteors)
                m_NextMeteor = Time.time + 0.4f;
            if (m_Effect == Effect.Aurora)
            {
                m_Aurora.rotation = anchorRotation;
                m_Aurora.gameObject.SetActive(true);
            }
            if (m_Effect == Effect.Fireflies)
            {
                // 발밑보다 조금 위, 체험자 둘레에 띄운다.
                m_Fireflies.transform.position = anchorPosition + Vector3.down * 0.7f;
                m_Fireflies.Play();
            }
        }

        /// <summary>서서히 지운다. 체험이 끝날 때 부른다.</summary>
        public void Hide()
        {
            m_Effect = Effect.None;
            if (m_Fireflies != null)
                m_Fireflies.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        // ------------------------------------------------------------------ 유성우

        void BuildMeteors()
        {
            m_Meteors = new LineRenderer[k_MeteorCount];
            m_MeteorAge = new float[k_MeteorCount];
            m_MeteorStart = new Vector3[k_MeteorCount];
            m_MeteorTravel = new Vector3[k_MeteorCount];
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 1f) });
            for (int i = 0; i < k_MeteorCount; i++)
            {
                LineRenderer line = VisualFactory.CreateLine("Meteor " + i, transform, 0.13f, new Color(1f, 0.95f, 0.8f), true);
                line.numCapVertices = 0;
                line.positionCount = 2;
                line.colorGradient = gradient;
                line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(1f, 1f));
                line.enabled = false;
                m_Meteors[i] = line;
                m_MeteorAge[i] = k_MeteorSeconds;
            }
        }

        void UpdateMeteors()
        {
            m_MeteorLevel = Mathf.MoveTowards(m_MeteorLevel, m_Effect == Effect.Meteors ? 1f : 0f, Time.deltaTime / 1.5f);

            if (m_Effect == Effect.Meteors && Time.time >= m_NextMeteor)
            {
                m_NextMeteor = Time.time + Random.Range(0.45f, 1.5f);
                LaunchMeteor();
            }

            Vector3 origin = m_Head != null ? m_Head.position : m_AnchorPosition;
            for (int i = 0; i < k_MeteorCount; i++)
            {
                LineRenderer line = m_Meteors[i];
                if (m_MeteorAge[i] >= k_MeteorSeconds)
                {
                    line.enabled = false;
                    continue;
                }
                m_MeteorAge[i] += Time.deltaTime;
                float t = Mathf.Clamp01(m_MeteorAge[i] / k_MeteorSeconds);
                line.enabled = true;
                line.SetPosition(0, origin + m_MeteorStart[i] + m_MeteorTravel[i] * Mathf.Max(0f, t - 0.35f));
                line.SetPosition(1, origin + m_MeteorStart[i] + m_MeteorTravel[i] * t);
                line.sharedMaterial.SetFloat(VisualFactory.IntensityId, Mathf.Sin(t * Mathf.PI) * 1.7f * m_MeteorLevel * m_Config.keywordSkyIntensity);
            }
        }

        // 유성우는 하늘의 한 점(복사점)에서 사방으로 퍼져 나가는 것처럼 보인다.
        void LaunchMeteor()
        {
            int slot = -1;
            for (int i = 0; i < k_MeteorCount; i++)
            {
                if (m_MeteorAge[i] >= k_MeteorSeconds)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0)
                return;

            Vector3 radiant = m_AnchorRotation * (Quaternion.Euler(-58f, 0f, 0f) * Vector3.forward);
            // 복사점에서 12~50도 떨어진 곳에서 시작해 복사점 반대쪽으로 떨어진다.
            Vector3 axis = Vector3.Cross(radiant, Random.onUnitSphere).normalized;
            Vector3 start = Quaternion.AngleAxis(Random.Range(12f, 50f), axis) * radiant;
            if (start.y < 0.12f)
                return; // 지평선 아래로는 떨어뜨리지 않는다.
            Vector3 away = (start - radiant * Vector3.Dot(start, radiant)).normalized;

            m_MeteorStart[slot] = start * k_SkyRadius;
            m_MeteorTravel[slot] = away * k_SkyRadius * Random.Range(0.14f, 0.26f);
            m_MeteorAge[slot] = 0f;
        }

        // ------------------------------------------------------------------ 오로라

        // 정면 먼 하늘에 세우는 굽은 띠. 가로는 방위각, 세로는 고도다.
        void BuildAurora()
        {
            const int segments = 64;
            const float azimuthHalf = 80f;
            const float low = 13f;
            const float high = 52f;

            Vector3[] vertices = new Vector3[(segments + 1) * 2];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float azimuth = Mathf.Lerp(-azimuthHalf, azimuthHalf, t);
                // 아래쪽 가장자리가 물결치게 해서 반듯한 띠로 보이지 않게 한다.
                float wobble = 3f * Mathf.Sin(t * 9f) + 1.5f * Mathf.Sin(t * 23f + 1f);
                vertices[i * 2] = Quaternion.Euler(-(low + wobble), azimuth, 0f) * Vector3.forward * k_SkyRadius;
                vertices[i * 2 + 1] = Quaternion.Euler(-(high + wobble * 0.5f), azimuth, 0f) * Vector3.forward * k_SkyRadius;
                uv[i * 2] = new Vector2(t, 0f);
                uv[i * 2 + 1] = new Vector2(t, 1f);
                if (i < segments)
                {
                    int k = i * 6;
                    int v = i * 2;
                    triangles[k] = v;
                    triangles[k + 1] = v + 1;
                    triangles[k + 2] = v + 2;
                    triangles[k + 3] = v + 2;
                    triangles[k + 4] = v + 1;
                    triangles[k + 5] = v + 3;
                }
            }

            m_AuroraMesh = new Mesh();
            m_AuroraMesh.name = "Aurora Curtain";
            m_AuroraMesh.vertices = vertices;
            m_AuroraMesh.uv = uv;
            m_AuroraMesh.triangles = triangles;
            m_AuroraMesh.RecalculateBounds();

            m_AuroraMaterial = VisualFactory.CreateMaterial("Aurora");
            m_AuroraMaterial.SetFloat(VisualFactory.IntensityId, 0f);
            MeshRenderer renderer = VisualFactory.CreateQuadRenderer("Aurora", transform, m_AuroraMaterial);
            renderer.GetComponent<MeshFilter>().sharedMesh = m_AuroraMesh;
            m_Aurora = renderer.transform;
            m_Aurora.gameObject.SetActive(false);
        }

        void UpdateAurora()
        {
            m_AuroraLevel = Mathf.MoveTowards(m_AuroraLevel, m_Effect == Effect.Aurora ? 1f : 0f, Time.deltaTime / 3f);
            if (!m_Aurora.gameObject.activeSelf)
                return;
            if (m_AuroraLevel <= 0f && m_Effect != Effect.Aurora)
            {
                m_Aurora.gameObject.SetActive(false);
                return;
            }
            // 먼 하늘이므로 머리 위치를 따라간다.
            if (m_Head != null)
                m_Aurora.position = m_Head.position;
            m_AuroraMaterial.SetFloat(VisualFactory.IntensityId, 0.5f * m_AuroraLevel * m_Config.keywordSkyIntensity);
        }

        // ------------------------------------------------------------------ 반딧불

        void BuildFireflies()
        {
            GameObject go = new GameObject("Fireflies");
            go.transform.SetParent(transform, false);
            m_Fireflies = go.AddComponent<ParticleSystem>();
            m_Fireflies.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = m_Fireflies.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.9f, 0.45f), new Color(0.8f, 1f, 0.5f));
            main.maxParticles = 48;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = m_Fireflies.emission;
            emission.rateOverTime = 7f;

            // 체험자 둘레 1.4~3.5m. 잡는 별(0.45m)보다 훨씬 멀어 손을 방해하지 않는다.
            ParticleSystem.ShapeModule shape = m_Fireflies.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 3.5f;
            shape.radiusThickness = 0.6f;
            shape.scale = new Vector3(1f, 0.45f, 1f);

            // 제멋대로 떠다니게 한다.
            ParticleSystem.NoiseModule noise = m_Fireflies.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.2f;

            // 수명 동안 몇 번 깜빡인다.
            ParticleSystem.ColorOverLifetimeModule blink = m_Fireflies.colorOverLifetime;
            blink.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.15f, 0.3f),
                    new GradientAlphaKey(1f, 0.45f), new GradientAlphaKey(0.1f, 0.62f), new GradientAlphaKey(1f, 0.8f),
                    new GradientAlphaKey(0f, 1f)
                });
            blink.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            Material material = VisualFactory.CreateMaterial("Glow");
            material.SetFloat(VisualFactory.BillboardId, 0f); // 파티클은 이미 카메라를 본다.
            material.SetFloat(VisualFactory.IntensityId, 1.5f * m_Config.keywordSkyIntensity);
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        void Update()
        {
            if (m_Config == null)
                return;
            UpdateMeteors();
            UpdateAurora();
        }

        void OnDestroy()
        {
            VisualFactory.DestroyObject(m_AuroraMesh);
            VisualFactory.DestroyObject(m_AuroraMaterial);
        }
    }
}
