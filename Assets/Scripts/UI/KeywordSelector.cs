using System;
using TMPro;
using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 키워드 아이콘 3개를 보여 주고 핀치로 하나를 고르게 한다(F-03).
    /// 고른 키워드는 Selected 이벤트로 알린다. 저장과 상태 전환은 SessionController가 한다.
    /// </summary>
    public class KeywordSelector : MonoBehaviour
    {
        public event Action<KeywordDefinition> Selected;

        class Orb
        {
            public KeywordDefinition keyword;
            public Transform transform;
            public GlowSprite halo;
            public GlowSprite icon;
            public TextMeshPro label;
            public float scale = 1f;
            public float brightness;
        }

        ConstellationConfig m_Config;
        HandInput m_Hands;
        Transform m_Root;
        Orb[] m_Orbs;
        bool m_Visible;
        int m_Chosen = -1;
        float m_ChosenTime;

        public void Init(ConstellationConfig config, HandInput hands)
        {
            m_Config = config;
            m_Hands = hands;
            m_Hands.PinchStarted += OnPinchStarted;
            Build();
            m_Root.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (m_Hands != null)
                m_Hands.PinchStarted -= OnPinchStarted;
        }

        void Build()
        {
            m_Root = new GameObject("Keyword Orbs").transform;
            m_Root.SetParent(transform, false);

            KeywordDefinition[] keywords = m_Config.keywords ?? new KeywordDefinition[0];
            m_Orbs = new Orb[keywords.Length];
            float iconSize = m_Config.keywordIconSize;
            for (int i = 0; i < keywords.Length; i++)
            {
                Orb orb = new Orb();
                orb.keyword = keywords[i];
                orb.transform = new GameObject("Orb " + keywords[i].id).transform;
                orb.transform.SetParent(m_Root, false);

                // 체험자를 둥글게 감싸도록 호 위에 놓는다.
                float offset = (i - (keywords.Length - 1) * 0.5f) * m_Config.keywordSpacing;
                float angle = offset / Mathf.Max(0.1f, m_Config.keywordDistance);
                Vector3 local = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * m_Config.keywordDistance;
                orb.transform.localPosition = local;
                orb.transform.localRotation = Quaternion.LookRotation(local.normalized, Vector3.up);

                orb.halo = GlowSprite.Create("Halo", orb.transform, iconSize * 2.1f, keywords[i].color, 0f);
                orb.halo.SetCore(0.12f);
                orb.icon = GlowSprite.Create("Icon", orb.transform, iconSize, keywords[i].color, 0f);
                orb.icon.SetShapeTexture(IconPainter.Get(keywords[i].id));

                float labelSize = iconSize * 0.3f;
                orb.label = TextFactory.Create("Label", orb.transform, labelSize, new Vector2(m_Config.keywordSpacing * 1.4f, labelSize * 2f),
                    TextAlignmentOptions.Center, m_Config.textColor);
                orb.label.transform.localPosition = new Vector3(0f, -iconSize * 0.95f, 0f);
                orb.label.text = keywords[i].label;
                orb.label.alpha = 0f;

                m_Orbs[i] = orb;
            }
        }

        /// <summary>아이콘을 체험자 앞에 띄운다.</summary>
        public void Show(Vector3 anchorPosition, Quaternion anchorRotation)
        {
            m_Root.position = anchorPosition + Vector3.down * m_Config.keywordDrop;
            m_Root.rotation = anchorRotation;
            m_Chosen = -1;
            m_Visible = true;
            for (int i = 0; i < m_Orbs.Length; i++)
            {
                m_Orbs[i].scale = 1f;
                m_Orbs[i].brightness = 0f;
            }
            m_Root.gameObject.SetActive(true);
        }

        /// <summary>아이콘을 서서히 지운다.</summary>
        public void Hide()
        {
            m_Visible = false;
        }

        void OnPinchStarted(HandSide side, Vector3 position)
        {
            if (!m_Visible || m_Chosen >= 0)
                return;

            int best = FindNearest(position, m_Config.keywordGrabRadius);
            if (best < 0)
                return;

            m_Chosen = best;
            m_ChosenTime = Time.time;
            if (Selected != null)
                Selected(m_Orbs[best].keyword);
        }

        int FindNearest(Vector3 position, float radius)
        {
            int best = -1;
            float bestDistance = radius;
            for (int i = 0; i < m_Orbs.Length; i++)
            {
                float distance = Vector3.Distance(position, m_Orbs[i].transform.position);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }
            return best;
        }

        void Update()
        {
            if (m_Config == null || !m_Root.gameObject.activeSelf)
                return;

            int hoverLeft = -1;
            int hoverRight = -1;
            if (m_Visible && m_Chosen < 0)
            {
                float hoverRadius = m_Config.keywordGrabRadius * 1.3f;
                if (m_Hands.Left.isTracked)
                    hoverLeft = FindNearest(m_Hands.Left.pinchPoint, hoverRadius);
                if (m_Hands.Right.isTracked)
                    hoverRight = FindNearest(m_Hands.Right.pinchPoint, hoverRadius);
            }

            float k = 1f - Mathf.Exp(-10f * Time.deltaTime);
            float maxBrightness = 0f;
            for (int i = 0; i < m_Orbs.Length; i++)
            {
                Orb orb = m_Orbs[i];
                bool hovered = i == hoverLeft || i == hoverRight;
                bool chosen = i == m_Chosen;

                float targetBrightness = m_Visible ? 1f : 0f;
                float targetScale = 1f;
                if (hovered)
                {
                    targetBrightness = 1.6f;
                    targetScale = 1.25f;
                }
                if (m_Chosen >= 0)
                {
                    // 선택 효과: 고른 아이콘은 커지며 번쩍이고, 나머지는 사라진다.
                    if (chosen)
                    {
                        float pulse = Mathf.Clamp01((Time.time - m_ChosenTime) / 0.35f);
                        targetBrightness = m_Visible ? Mathf.Lerp(3.2f, 1.8f, pulse) : 0f;
                        targetScale = 1.5f;
                    }
                    else
                    {
                        targetBrightness = 0f;
                        targetScale = 0.8f;
                    }
                }

                orb.brightness = Mathf.Lerp(orb.brightness, targetBrightness, k);
                orb.scale = Mathf.Lerp(orb.scale, targetScale, k);
                orb.transform.localScale = Vector3.one * orb.scale;
                orb.icon.SetIntensity(orb.brightness * 1.4f);
                orb.halo.SetIntensity(orb.brightness * 0.5f);
                orb.label.alpha = Mathf.Clamp01(orb.brightness);
                maxBrightness = Mathf.Max(maxBrightness, orb.brightness);
            }

            // 다 사라지면 오브젝트를 꺼 둔다.
            if (!m_Visible && maxBrightness < 0.02f)
                m_Root.gameObject.SetActive(false);
        }
    }
}
