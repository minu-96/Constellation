using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace Constellation
{
    /// <summary>
    /// 모두의 하늘. 앞선 방문자들이 만든 별자리를 먼 하늘에 작게 그려 둔다. 전시가 이어질수록 하늘이 채워진다.
    /// 목록은 중계 서버의 /sky 에서 받아 온다. 서버가 없거나 실패하면 이번 실행에서 만든 것만 보여 준다.
    /// 선과 별은 각각 메시 하나로 합쳐 그린다(드로우콜 2개). 이름표는 가장 최근 몇 개에만 붙인다.
    /// </summary>
    public class SharedSky : MonoBehaviour
    {
        [Serializable]
        class SkyItem
        {
            public int n;
            public string name;
            public string keyword;
            public float[] xy;
        }

        [Serializable]
        class SkyResponse
        {
            public int count;
            public SkyItem[] items;
        }

        const float k_Radius = 60f;
        const float k_FetchTimeout = 6f;

        ConstellationConfig m_Config;
        Transform m_Head;
        Mesh m_LineMesh;
        Mesh m_DotMesh;
        Material m_LineMaterial;
        Material m_DotMaterial;
        MeshRenderer m_LineRenderer;
        MeshRenderer m_DotRenderer;
        readonly List<TextMeshPro> m_Labels = new List<TextMeshPro>();
        readonly List<SkyItem> m_Server = new List<SkyItem>();
        // 이번 실행에서 만들었지만 서버에 올라가지 못한 별자리. 서버 목록 뒤에 붙여 보여 준다.
        readonly List<SkyItem> m_Local = new List<SkyItem>();
        Coroutine m_Fetch;
        float m_Fade;

        /// <summary>서버가 알려 준 지금까지의 별자리 수. 모르면 0.</summary>
        public int TotalCount { get; private set; }

        public void Init(ConstellationConfig config, Transform head)
        {
            m_Config = config;
            m_Head = head;

            m_LineMesh = new Mesh();
            m_LineMesh.name = "Shared Sky Lines";
            m_DotMesh = new Mesh();
            m_DotMesh.name = "Shared Sky Stars";

            m_LineMaterial = VisualFactory.CreateMaterial("Line");
            m_LineMaterial.SetColor(VisualFactory.ColorId, Color.white);
            m_DotMaterial = VisualFactory.CreateMaterial("Glow");
            m_DotMaterial.SetFloat(VisualFactory.BillboardId, 0f); // 메시가 이미 체험자 쪽을 본다.

            m_LineRenderer = VisualFactory.CreateQuadRenderer("Lines", transform, m_LineMaterial);
            m_LineRenderer.GetComponent<MeshFilter>().sharedMesh = m_LineMesh;
            m_DotRenderer = VisualFactory.CreateQuadRenderer("Stars", transform, m_DotMaterial);
            m_DotRenderer.GetComponent<MeshFilter>().sharedMesh = m_DotMesh;
            m_LineRenderer.enabled = false;
            m_DotRenderer.enabled = false;
        }

        /// <summary>서버에서 목록을 다시 받아 온다. 대기 화면으로 돌아올 때마다 부른다.</summary>
        public void Refresh()
        {
            if (m_Config == null || !m_Config.enableSharedSky)
                return;
            AppSecrets secrets = AppSecrets.Load();
            if (!secrets.IsConfigured)
            {
                Rebuild();
                return;
            }
            if (m_Fetch != null)
                StopCoroutine(m_Fetch);
            m_Fetch = StartCoroutine(Fetch(secrets));
        }

        /// <summary>
        /// 방금 만든 별자리를 바로 하늘에 더한다(서버에 올라가지 못했을 때를 위한 것).
        /// </summary>
        /// <param name="points">0~1로 맞춘 점들(이은 순서)</param>
        public void AddLocal(string constellationName, string keywordId, IList<Vector2> points)
        {
            if (m_Config == null || !m_Config.enableSharedSky || points == null || points.Count < 2)
                return;
            SkyItem item = new SkyItem();
            item.n = 0;
            item.name = constellationName;
            item.keyword = keywordId;
            item.xy = new float[points.Count * 2];
            for (int i = 0; i < points.Count; i++)
            {
                item.xy[i * 2] = points[i].x;
                item.xy[i * 2 + 1] = points[i].y;
            }
            m_Local.Add(item);
            Rebuild();
        }

        /// <summary>
        /// 별자리의 점들을 가로세로 비율을 지키며 0~1 상자 가운데에 맞춘다. 서버에 보내는 모양과 하늘에 그리는 모양이 이것이다.
        /// </summary>
        public static List<Vector2> NormalizePoints(IList<Vector2> points, int maxPoints)
        {
            List<Vector2> result = new List<Vector2>();
            if (points == null || points.Count < 2)
                return result;
            Vector2 min = points[0];
            Vector2 max = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                min = Vector2.Min(min, points[i]);
                max = Vector2.Max(max, points[i]);
            }
            float extent = Mathf.Max(max.x - min.x, max.y - min.y);
            if (extent < 0.0001f)
                extent = 1f;
            Vector2 center = (min + max) * 0.5f;
            int count = Mathf.Min(points.Count, maxPoints);
            for (int i = 0; i < count; i++)
            {
                Vector2 p = (points[i] - center) / extent + new Vector2(0.5f, 0.5f);
                result.Add(new Vector2(Mathf.Clamp01(p.x), Mathf.Clamp01(p.y)));
            }
            return result;
        }

        IEnumerator Fetch(AppSecrets secrets)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(secrets.Url("/sky")))
            {
                request.timeout = Mathf.CeilToInt(k_FetchTimeout);
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success && request.responseCode == 200)
                {
                    SkyResponse response = null;
                    try
                    {
                        response = JsonUtility.FromJson<SkyResponse>(request.downloadHandler.text);
                    }
                    catch (Exception)
                    {
                        response = null;
                    }
                    if (response != null && response.items != null)
                    {
                        m_Server.Clear();
                        for (int i = 0; i < response.items.Length; i++)
                        {
                            SkyItem item = response.items[i];
                            if (item != null && item.xy != null && item.xy.Length >= 4)
                                m_Server.Add(item);
                        }
                        TotalCount = response.count;
                    }
                }
                else if (m_Config.logStateChanges)
                {
                    Debug.Log("[Constellation] 모두의 하늘 목록을 받지 못했습니다: " + request.responseCode + " " + request.result);
                }
            }
            m_Fetch = null;
            Rebuild();
        }

        // 번호에서 하늘의 자리를 정한다. 같은 번호는 언제나 같은 자리에 뜬다.
        // 대부분은 잡는 별보다 높은 하늘(고도 50~74도)에 두르고, 일부는 뒤쪽 낮은 하늘에 둔다.
        static Vector3 SlotDirection(int slot)
        {
            float a = Frac(slot * 0.61803399f);
            float b = Frac(slot * 0.75487767f);
            float c = Frac(slot * 0.38196601f);
            float azimuth;
            float elevation;
            if (c < 0.35f)
            {
                azimuth = 180f + (a - 0.5f) * 140f;
                elevation = 14f + 28f * b;
            }
            else
            {
                azimuth = a * 360f;
                elevation = 50f + 24f * b;
            }
            return Quaternion.Euler(-elevation, azimuth, 0f) * Vector3.forward;
        }

        static float Frac(float value)
        {
            return value - Mathf.Floor(value);
        }

        void Rebuild()
        {
            if (m_LineMesh == null)
                return;

            List<SkyItem> items = new List<SkyItem>();
            int max = Mathf.Max(0, m_Config.sharedSkyMaxCount);
            int skip = Mathf.Max(0, m_Server.Count + m_Local.Count - max);
            for (int i = 0; i < m_Server.Count; i++)
                items.Add(m_Server[i]);
            for (int i = 0; i < m_Local.Count; i++)
                items.Add(m_Local[i]);
            if (skip > 0)
                items.RemoveRange(0, skip);

            List<Vector3> lineVertices = new List<Vector3>();
            List<Vector2> lineUv = new List<Vector2>();
            List<Color> lineColors = new List<Color>();
            List<int> lineTriangles = new List<int>();
            List<Vector3> dotVertices = new List<Vector3>();
            List<Vector2> dotUv = new List<Vector2>();
            List<Color> dotColors = new List<Color>();
            List<int> dotTriangles = new List<int>();

            float span = Mathf.Tan(Mathf.Clamp(m_Config.sharedSkySize, 2f, 30f) * Mathf.Deg2Rad);
            float lineHalfWidth = k_Radius * 0.0011f;
            float dotHalfSize = k_Radius * 0.0055f;
            bool labels = m_Config.qualityTier != QualityTier.Low;
            int labelFrom = items.Count - Mathf.Max(0, m_Config.sharedSkyLabelCount);
            int labelIndex = 0;

            for (int i = 0; i < items.Count; i++)
            {
                SkyItem item = items[i];
                // 서버 번호가 없는 것(이번 실행에서 만든 것)은 목록의 순서로 자리를 정한다.
                int slot = item.n > 0 ? item.n : 100000 + i;
                Vector3 direction = SlotDirection(slot);
                Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
                Vector3 up = Vector3.Cross(direction, right);

                KeywordDefinition keyword = m_Config.FindKeyword(item.keyword);
                Color color = keyword != null ? keyword.color : m_Config.lineColor;
                // 오래된 것은 조금 흐리게, 최근 것은 또렷하게
                float age = items.Count > 1 ? i / (float)(items.Count - 1) : 1f;
                color.a = Mathf.Lerp(0.45f, 1f, age);

                int pointCount = item.xy.Length / 2;
                Vector3 previous = Vector3.zero;
                for (int p = 0; p < pointCount; p++)
                {
                    float x = (Mathf.Clamp01(item.xy[p * 2]) - 0.5f) * span;
                    float y = (Mathf.Clamp01(item.xy[p * 2 + 1]) - 0.5f) * span;
                    Vector3 position = (direction + right * x + up * y).normalized * k_Radius;

                    AddQuad(dotVertices, dotUv, dotColors, dotTriangles, position, right * dotHalfSize, up * dotHalfSize, color);
                    if (p > 0)
                    {
                        // 선은 체험자(가운데)를 향해 눕힌 얇은 띠로 그린다.
                        Vector3 along = (position - previous).normalized;
                        Vector3 side = Vector3.Cross(along, -position.normalized).normalized * lineHalfWidth;
                        int first = lineVertices.Count;
                        lineVertices.Add(previous - side);
                        lineVertices.Add(previous + side);
                        lineVertices.Add(position - side);
                        lineVertices.Add(position + side);
                        lineUv.Add(new Vector2(0f, 0f));
                        lineUv.Add(new Vector2(0f, 1f));
                        lineUv.Add(new Vector2(1f, 0f));
                        lineUv.Add(new Vector2(1f, 1f));
                        for (int k = 0; k < 4; k++)
                            lineColors.Add(color);
                        lineTriangles.Add(first);
                        lineTriangles.Add(first + 1);
                        lineTriangles.Add(first + 2);
                        lineTriangles.Add(first + 2);
                        lineTriangles.Add(first + 1);
                        lineTriangles.Add(first + 3);
                    }
                    previous = position;
                }

                if (labels && i >= labelFrom)
                {
                    TextMeshPro label = GetLabel(labelIndex++);
                    Vector3 below = (direction - up * (span * 0.5f + 0.02f)).normalized * k_Radius;
                    label.transform.localPosition = below;
                    label.transform.localRotation = Quaternion.LookRotation(below.normalized, up);
                    label.text = item.n > 0 ? string.Format(m_Config.msgSharedSkyLabel, item.name, item.n) : item.name;
                    label.color = new Color(m_Config.textColor.r, m_Config.textColor.g, m_Config.textColor.b, 1f);
                    label.gameObject.SetActive(true);
                }
            }
            for (int i = labelIndex; i < m_Labels.Count; i++)
                m_Labels[i].gameObject.SetActive(false);

            Fill(m_LineMesh, lineVertices, lineUv, lineColors, lineTriangles);
            Fill(m_DotMesh, dotVertices, dotUv, dotColors, dotTriangles);
            bool any = items.Count > 0;
            m_LineRenderer.enabled = any;
            m_DotRenderer.enabled = any;
        }

        static void AddQuad(List<Vector3> vertices, List<Vector2> uv, List<Color> colors, List<int> triangles, Vector3 center, Vector3 right, Vector3 up, Color color)
        {
            int first = vertices.Count;
            vertices.Add(center - right - up);
            vertices.Add(center + right - up);
            vertices.Add(center - right + up);
            vertices.Add(center + right + up);
            uv.Add(new Vector2(0f, 0f));
            uv.Add(new Vector2(1f, 0f));
            uv.Add(new Vector2(0f, 1f));
            uv.Add(new Vector2(1f, 1f));
            for (int k = 0; k < 4; k++)
                colors.Add(color);
            triangles.Add(first);
            triangles.Add(first + 2);
            triangles.Add(first + 1);
            triangles.Add(first + 2);
            triangles.Add(first + 3);
            triangles.Add(first + 1);
        }

        static void Fill(Mesh mesh, List<Vector3> vertices, List<Vector2> uv, List<Color> colors, List<int> triangles)
        {
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        TextMeshPro GetLabel(int index)
        {
            while (m_Labels.Count <= index)
            {
                // 하늘 멀리 있으므로 글자도 크다(약 1.3도 높이).
                float size = k_Radius * 0.022f;
                TextMeshPro label = TextFactory.Create("Sky Label", transform, size, new Vector2(size * 16f, size * 2f), TextAlignmentOptions.Center, m_Config.textColor);
                label.textWrappingMode = TextWrappingModes.NoWrap;
                m_Labels.Add(label);
            }
            return m_Labels[index];
        }

        void LateUpdate()
        {
            if (m_Config == null || m_Head == null)
                return;
            // 먼 하늘이므로 머리 위치를 따라간다(회전은 따라가지 않는다).
            transform.position = m_Head.position;

            // 서서히 나타난다. 잡는 별보다 훨씬 어둡게 두어 방해하지 않는다.
            m_Fade = Mathf.MoveTowards(m_Fade, 1f, Time.deltaTime / 4f);
            float brightness = m_Config.sharedSkyBrightness * m_Fade;
            m_LineMaterial.SetFloat(VisualFactory.IntensityId, brightness);
            m_DotMaterial.SetFloat(VisualFactory.IntensityId, brightness * 1.4f);
            for (int i = 0; i < m_Labels.Count; i++)
                m_Labels[i].alpha = 0.75f * m_Fade;
        }

        void OnDestroy()
        {
            VisualFactory.DestroyObject(m_LineMesh);
            VisualFactory.DestroyObject(m_DotMesh);
            VisualFactory.DestroyObject(m_LineMaterial);
            VisualFactory.DestroyObject(m_DotMaterial);
        }
    }
}
