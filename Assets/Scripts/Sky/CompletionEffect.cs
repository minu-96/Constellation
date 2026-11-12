using System;
using System.Collections.Generic;
using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 별자리를 완성한 순간의 연출. 이은 순서대로 별이 번쩍이고 빛이 선을 훑은 뒤,
    /// 별가루가 흩어지며 선이 하늘에 새겨진 듯 밝아진다.
    /// 보여 주기만 한다. 상태 전환은 SessionController가 하고, 연출은 AI 대기 시간 안에 끝난다.
    /// </summary>
    public class CompletionEffect : MonoBehaviour
    {
        /// <summary>빛이 n번째 별에 닿았을 때(0부터). 소리를 맞춰 내는 데 쓴다.</summary>
        public event Action<int> StarReached;

        ConstellationConfig m_Config;
        StarField m_StarField;
        ConstellationDrawer m_Drawer;
        ParticleSystem m_Dust;
        readonly List<int> m_Order = new List<int>();
        bool m_Playing;
        float m_Time;
        int m_NextStar;

        public bool IsPlaying { get { return m_Playing; } }

        public void Init(ConstellationConfig config, StarField starField, ConstellationDrawer drawer)
        {
            m_Config = config;
            m_StarField = starField;
            m_Drawer = drawer;
            BuildDust();
        }

        // 별에서 흩어지는 별가루. 평소에는 아무것도 내보내지 않고, Emit으로 필요한 만큼만 뿌린다.
        void BuildDust()
        {
            m_Dust = gameObject.AddComponent<ParticleSystem>();
            m_Dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = m_Dust.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.04f, 0.22f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.016f);
            main.startColor = new ParticleSystem.MinMaxGradient(m_Config.starSelectedColor, Color.white);
            main.maxParticles = 160;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.02f;

            ParticleSystem.EmissionModule emission = m_Dust.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = m_Dust.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.004f;

            ParticleSystem.ColorOverLifetimeModule fade = m_Dust.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystemRenderer renderer = GetComponent<ParticleSystemRenderer>();
            Material material = VisualFactory.CreateMaterial("Glow");
            material.SetFloat(VisualFactory.BillboardId, 0f); // 파티클은 이미 카메라를 본다.
            material.SetFloat(VisualFactory.IntensityId, 1.6f);
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>완성 연출을 시작한다. 인자는 이은 순서대로의 별 번호.</summary>
        public void Play(IList<int> selectedIndices)
        {
            m_Order.Clear();
            if (selectedIndices != null)
            {
                for (int i = 0; i < selectedIndices.Count; i++)
                    m_Order.Add(selectedIndices[i]);
            }
            if (m_Order.Count == 0)
                return;

            if (!m_Config.enableCompletionEffect)
            {
                m_Drawer.SetEngraved(true);
                return;
            }

            m_Time = 0f;
            m_NextStar = 0;
            m_Playing = true;
            m_Dust.Play();
        }

        /// <summary>연출을 멈추고 흔적을 지운다. 체험이 끝날 때 부른다.</summary>
        public void Stop()
        {
            m_Playing = false;
            m_Order.Clear();
            if (m_Drawer != null)
                m_Drawer.SetPulse(0f, 0f);
            if (m_Dust != null)
                m_Dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void Update()
        {
            if (!m_Playing)
                return;

            // 별이 사라졌으면(초기화) 그만둔다.
            // (같은 별을 다시 지나는 선도 있으므로 별 개수가 아니라 선의 점 개수로 비교한다.)
            if (m_StarField.Count == 0 || m_Drawer.SelectedIndices.Count != m_Order.Count)
            {
                Stop();
                return;
            }

            float step = Mathf.Max(0.05f, m_Config.completionStepSeconds);
            m_Time += Time.deltaTime;
            float progress = m_Time / step; // 0이면 첫 별, 1이면 둘째 별 …
            int last = m_Order.Count - 1;

            // 빛이 닿은 별을 차례로 번쩍이고 별가루를 뿌린다.
            while (m_NextStar <= last && progress >= m_NextStar)
            {
                int star = m_Order[m_NextStar];
                m_StarField.Flash(star);
                ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams();
                emit.position = m_StarField.GetWorldPosition(star);
                emit.applyShapeToPosition = true;
                m_Dust.Emit(emit, 14);
                if (StarReached != null)
                    StarReached(m_NextStar);
                m_NextStar++;
            }

            if (progress < last)
            {
                // 빛 덩어리가 별에서 다음 별로 선을 따라 옮겨 간다.
                int from = Mathf.FloorToInt(progress);
                float t = progress - from;
                float position = Mathf.Lerp(m_Drawer.GetStarUv(from), m_Drawer.GetStarUv(from + 1), t);
                m_Drawer.SetPulse(position, 5f);
                return;
            }

            // 끝까지 훑었으면 선을 새긴다.
            m_Drawer.SetPulse(0f, 0f);
            m_Drawer.SetEngraved(true);
            m_Playing = false;
        }
    }
}
