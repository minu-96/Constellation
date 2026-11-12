using System.Collections.Generic;
using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 배경 별 수천 개를 메시 하나로 그린다(드로우콜 1개).
    /// 픽셀마다 별을 계산하던 방식과 달리 머리를 움직여도 깜빡이지 않고, 별마다 밝기와 색을 다르게 줄 수 있다.
    /// 별은 아주 먼 하늘 돔에 놓이고, 능선 아래에는 만들지 않는다.
    /// </summary>
    public class StarDome : MonoBehaviour
    {
        const float k_Radius = 80f;

        Mesh m_Mesh;
        Material m_Material;
        Material m_ReflectionMaterial;

        // 별 색(온도). 푸른 흰색, 흰색, 노란색, 주황색
        static readonly Color32[] k_Tints =
        {
            new Color32(184, 209, 255, 255),
            new Color32(255, 255, 255, 255),
            new Color32(255, 237, 194, 255),
            new Color32(255, 194, 143, 255),
        };

        /// <param name="ridges">능선 높이(0~1). 그 아래에는 별을 만들지 않는다.</param>
        /// <param name="ridgeMaxY">능선 최고 높이(방향 벡터의 y)</param>
        /// <param name="reflectionShoreY">호수 반사를 그릴 때 물가의 깊이(y). 0 이하면 반사를 그리지 않는다.</param>
        public void Init(ConstellationConfig config, float[][] ridges, float ridgeMaxY, int count, float reflectionShoreY)
        {
            m_Mesh = BuildMesh(ridges, ridgeMaxY, Mathf.Clamp(count, 100, 12000));

            m_Material = VisualFactory.CreateMaterial("StarDome");
            m_Material.SetFloat("_Brightness", config.backgroundStarBrightness);
            m_Material.SetFloat("_Reflect", 0f);
            CreateRenderer("Stars", m_Material);

            if (reflectionShoreY > 0f)
            {
                m_ReflectionMaterial = VisualFactory.CreateMaterial("StarDome");
                m_ReflectionMaterial.SetFloat("_Brightness", config.backgroundStarBrightness);
                m_ReflectionMaterial.SetFloat("_Reflect", 1f);
                m_ReflectionMaterial.SetFloat("_ShoreY", reflectionShoreY);
                CreateRenderer("Star Reflections", m_ReflectionMaterial);
            }

            transform.localScale = Vector3.one * k_Radius;
        }

        void CreateRenderer(string objectName, Material material)
        {
            GameObject go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = m_Mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        static Mesh BuildMesh(float[][] ridges, float ridgeMaxY, int count)
        {
            // 매번 같은 하늘이 나오도록 씨앗을 고정한다.
            System.Random random = new System.Random(3);

            List<Vector3> vertices = new List<Vector3>(count * 4);
            List<Vector2> corners = new List<Vector2>(count * 4);
            List<Vector4> data = new List<Vector4>(count * 4);
            List<Color32> colors = new List<Color32>(count * 4);
            List<int> triangles = new List<int>(count * 6);

            int made = 0;
            int guard = 0;
            while (made < count && guard < count * 40)
            {
                guard++;
                float y = (float)random.NextDouble();
                float azimuth = ((float)random.NextDouble() - 0.5f) * Mathf.PI * 2f;
                float radius = Mathf.Sqrt(1f - y * y);
                Vector3 direction = new Vector3(Mathf.Sin(azimuth) * radius, y, Mathf.Cos(azimuth) * radius);

                // 능선 아래와 능선에 걸치는 별은 만들지 않는다.
                if (y < SkyData.HighestRidgeAt(ridges, azimuth) * ridgeMaxY + 0.009f)
                    continue;
                // 은하수 쪽에 별이 더 많다.
                float band = SkyData.MilkyWayBand(direction.x, direction.y, direction.z);
                if (random.NextDouble() > 0.3f + 0.7f * band)
                    continue;

                // 대부분은 희미하고 몇 개만 아주 밝다.
                float m = (float)random.NextDouble();
                float brightness = 0.16f + 0.5f * m * m + 2.6f * Mathf.Pow(m, 9f);
                float size = 0.0019f + 0.0012f * m + 0.0042f * Mathf.Pow(m, 7f); // 라디안(반지름)
                float speed = 0.8f + (float)random.NextDouble() * 2.4f;
                float phase = (float)random.NextDouble();

                double t = random.NextDouble();
                Color32 tint = k_Tints[t < 0.3 ? 0 : t < 0.75 ? 1 : t < 0.93 ? 2 : 3];

                int first = vertices.Count;
                for (int corner = 0; corner < 4; corner++)
                {
                    vertices.Add(direction);
                    corners.Add(new Vector2(corner == 0 || corner == 2 ? -1f : 1f, corner < 2 ? -1f : 1f));
                    data.Add(new Vector4(size, speed, brightness, phase));
                    colors.Add(tint);
                }
                triangles.Add(first);
                triangles.Add(first + 2);
                triangles.Add(first + 1);
                triangles.Add(first + 2);
                triangles.Add(first + 3);
                triangles.Add(first + 1);
                made++;
            }

            Mesh mesh = new Mesh();
            mesh.name = "Star Dome";
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, corners);
            mesh.SetUVs(1, data);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            // 셰이더에서 별을 펴고 뒤집으므로, 잘려 사라지지 않게 경계를 넉넉히 둔다.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2.5f);
            mesh.UploadMeshData(true);
            return mesh;
        }

        void OnDestroy()
        {
            VisualFactory.DestroyObject(m_Mesh);
            VisualFactory.DestroyObject(m_Material);
            VisualFactory.DestroyObject(m_ReflectionMaterial);
        }
    }
}
