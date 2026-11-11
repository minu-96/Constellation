using UnityEngine;

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

        void LateUpdate()
        {
            // 하늘은 무한히 멀어 보여야 하므로 머리 위치를 따라간다(회전은 따라가지 않는다).
            if (m_Dome != null && m_Head != null)
                m_Dome.position = m_Head.position;
        }

        void OnDestroy()
        {
            VisualFactory.DestroyObject(m_SkyMaterial);
        }
    }
}
