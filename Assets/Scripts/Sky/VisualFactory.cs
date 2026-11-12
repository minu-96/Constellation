using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 셰이더, 머티리얼, 메시를 코드로 만든다. 에디터에서 손으로 만들 에셋을 없애기 위한 것.
    /// 셰이더는 빌드에 꼭 들어가도록 Resources/Shaders에 둔다.
    /// </summary>
    public static class VisualFactory
    {
        public static readonly int ColorId = Shader.PropertyToID("_Color");
        public static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        public static readonly int CoreId = Shader.PropertyToID("_Core");
        public static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        public static readonly int UseTextureId = Shader.PropertyToID("_UseTexture");
        public static readonly int BillboardId = Shader.PropertyToID("_Billboard");
        public static readonly int RingId = Shader.PropertyToID("_Ring");
        public static readonly int FlowStrengthId = Shader.PropertyToID("_FlowStrength");
        public static readonly int FlowDensityId = Shader.PropertyToID("_FlowDensity");
        public static readonly int PulsePositionId = Shader.PropertyToID("_PulsePosition");
        public static readonly int PulseStrengthId = Shader.PropertyToID("_PulseStrength");
        public static readonly int PulseWidthId = Shader.PropertyToID("_PulseWidth");

        static Mesh s_Quad;

        public static Shader LoadShader(string name)
        {
            Shader shader = Resources.Load<Shader>("Shaders/Constellation" + name);
            if (shader == null)
                shader = Shader.Find("Constellation/" + name);
            if (shader == null)
            {
                Debug.LogError("[Constellation] 셰이더를 찾지 못했습니다: " + name);
                shader = Shader.Find("Sprites/Default");
            }
            return shader;
        }

        public static Material CreateMaterial(string shaderName)
        {
            Material material = new Material(LoadShader(shaderName));
            material.name = "Constellation " + shaderName + " (runtime)";
            return material;
        }

        /// <summary>가운데가 원점인 1×1 사각형. 여러 곳에서 함께 쓴다.</summary>
        public static Mesh Quad
        {
            get
            {
                if (s_Quad == null)
                {
                    s_Quad = new Mesh();
                    s_Quad.name = "Constellation Quad";
                    s_Quad.vertices = new[]
                    {
                        new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                        new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
                    };
                    s_Quad.uv = new[]
                    {
                        new Vector2(0f, 0f), new Vector2(1f, 0f),
                        new Vector2(0f, 1f), new Vector2(1f, 1f)
                    };
                    s_Quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                    s_Quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                    // 빌보드는 셰이더에서 펴므로, 컬링에서 잘리지 않게 경계를 넉넉히 둔다.
                    s_Quad.bounds = new Bounds(Vector3.zero, Vector3.one * 1.5f);
                }
                return s_Quad;
            }
        }

        /// <summary>사각형 메시를 가진 빈 렌더러 오브젝트를 만든다.</summary>
        public static MeshRenderer CreateQuadRenderer(string name, Transform parent, Material material)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = Quad;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return renderer;
        }

        /// <summary>텍스처를 붙인 무광 패널(카드, QR).</summary>
        public static MeshRenderer CreatePanel(string name, Transform parent, Vector2 size, Color color, Texture texture)
        {
            Material material = CreateMaterial("Panel");
            material.SetColor(ColorId, color);
            if (texture != null)
                material.SetTexture(MainTexId, texture);
            MeshRenderer renderer = CreateQuadRenderer(name, parent, material);
            renderer.transform.localScale = new Vector3(size.x, size.y, 1f);
            return renderer;
        }

        /// <summary>LineRenderer 공통 설정.</summary>
        public static LineRenderer CreateLine(string name, Transform parent, float width, Color color, bool worldSpace)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            LineRenderer line = go.AddComponent<LineRenderer>();
            Material material = CreateMaterial("Line");
            material.SetColor(ColorId, color);
            line.sharedMaterial = material;
            line.useWorldSpace = worldSpace;
            line.widthMultiplier = width;
            line.positionCount = 0;
            line.numCapVertices = 4;
            line.numCornerVertices = 3;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startColor = Color.white;
            line.endColor = Color.white;
            return line;
        }

        public static void DestroyObject(Object target)
        {
            if (target == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }

    /// <summary>
    /// 빛나는 점 하나. 머티리얼 인스턴스를 가지고 있다가 함께 지운다.
    /// </summary>
    public class GlowSprite : MonoBehaviour
    {
        MeshRenderer m_Renderer;
        Material m_Material;

        public static GlowSprite Create(string name, Transform parent, float size, Color color, float intensity)
        {
            Material material = VisualFactory.CreateMaterial("Glow");
            MeshRenderer renderer = VisualFactory.CreateQuadRenderer(name, parent, material);
            GlowSprite sprite = renderer.gameObject.AddComponent<GlowSprite>();
            sprite.m_Renderer = renderer;
            sprite.m_Material = material;
            sprite.SetSize(size);
            sprite.SetColor(color);
            sprite.SetIntensity(intensity);
            return sprite;
        }

        public void SetSize(float size)
        {
            transform.localScale = new Vector3(size, size, size);
        }

        public void SetColor(Color color)
        {
            if (m_Material != null)
                m_Material.SetColor(VisualFactory.ColorId, color);
        }

        public void SetIntensity(float intensity)
        {
            if (m_Material != null)
                m_Material.SetFloat(VisualFactory.IntensityId, intensity);
        }

        public void SetCore(float core)
        {
            if (m_Material != null)
                m_Material.SetFloat(VisualFactory.CoreId, core);
        }

        /// <summary>가운데 빛 대신 얇은 고리를 그린다(0~1).</summary>
        public void SetRing(float amount)
        {
            if (m_Material != null)
                m_Material.SetFloat(VisualFactory.RingId, amount);
        }

        /// <summary>텍스처 알파를 모양으로 쓴다(키워드 아이콘).</summary>
        public void SetShapeTexture(Texture texture)
        {
            if (m_Material == null)
                return;
            m_Material.SetTexture(VisualFactory.MainTexId, texture);
            m_Material.SetFloat(VisualFactory.UseTextureId, texture != null ? 1f : 0f);
        }

        public void SetVisible(bool visible)
        {
            if (m_Renderer != null)
                m_Renderer.enabled = visible;
        }

        void OnDestroy()
        {
            VisualFactory.DestroyObject(m_Material);
        }
    }
}
