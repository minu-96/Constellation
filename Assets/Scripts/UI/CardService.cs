using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace Constellation
{
    /// <summary>
    /// 결과 카드(F-12). 이름·이야기·별자리 이미지를 중계 서버에 올리고, 헤드셋 안에는 카드 미리보기와 번호를 보여 준다.
    /// QR은 기본적으로 전시장 모니터 페이지(/wall)에 뜬다. 휴대폰으로 찍으면 서버가 카드 페이지를 보여 준다.
    /// (설정의 showQrInHeadset을 켜면 헤드셋 안에도 QR을 띄운다.)
    /// 업로드에 실패하면 HasFailed가 true가 되고, SessionController가 카드 없이 끝낸다.
    /// </summary>
    public class CardService : MonoBehaviour
    {
        enum UploadState
        {
            None,
            Uploading,
            Ready,
            Failed
        }

        [Serializable]
        class CardRequest
        {
            public string keyword;
            public string name;
            public string story;
            public string image;
        }

        [Serializable]
        class CardResponse
        {
            public string id;
            public string url;
            public int number;
        }

        ConstellationConfig m_Config;
        UploadState m_State = UploadState.None;
        Coroutine m_Upload;
        UnityWebRequest m_Request;
        int m_UploadId;
        string m_CardUrl;
        Texture2D m_QrTexture;

        Transform m_Root;
        MeshRenderer m_Image;
        MeshRenderer m_Qr;
        TextMeshPro m_Name;
        TextMeshPro m_Story;
        TextMeshPro m_Caption;
        TextMeshPro m_Number;
        bool m_Visible;
        float m_Appear;

        const float k_PanelWidth = 1.25f;
        const float k_PanelHeight = 0.72f;

        public bool IsReady { get { return m_State == UploadState.Ready; } }
        public bool HasFailed { get { return m_State == UploadState.Failed; } }
        public string CardUrl { get { return m_CardUrl; } }
        /// <summary>서버가 매긴 "몇 번째 별자리" 번호. 아직 모르거나 받지 못했으면 0.</summary>
        public int CardNumber { get; private set; }

        public void Init(ConstellationConfig config)
        {
            m_Config = config;
            BuildPanel();
            m_Root.gameObject.SetActive(false);
        }

        // QR을 띄울 때: 왼쪽에 별자리 그림·이름·이야기, 오른쪽에 QR과 안내
        // QR을 띄우지 않을 때(기본): 가운데에 그림·이름·이야기, 맨 아래에 안내. QR은 모니터 페이지에서 찍는다.
        void BuildPanel()
        {
            m_Root = new GameObject("Card Panel").transform;
            m_Root.SetParent(transform, false);

            bool withQr = m_Config.showQrInHeadset;
            float width = withQr ? k_PanelWidth : k_PanelWidth * 0.56f;
            float height = withQr ? k_PanelHeight : k_PanelHeight * 1.12f;
            VisualFactory.CreatePanel("Background", m_Root, new Vector2(width, height), new Color(0.03f, 0.04f, 0.11f, 0.86f), null)
                .transform.localPosition = new Vector3(0f, withQr ? 0f : -0.04f, 0.01f);

            float leftX = withQr ? -k_PanelWidth * 0.24f : 0f;
            float rightX = withQr ? k_PanelWidth * 0.26f : 0f;

            m_Image = VisualFactory.CreatePanel("Constellation Image", m_Root, new Vector2(0.34f, 0.34f), Color.white, null);
            m_Image.transform.localPosition = new Vector3(leftX, 0.15f, 0f);

            m_Number = TextFactory.Create("Card Number", m_Root, 0.032f, new Vector2(0.56f, 0.05f), TextAlignmentOptions.Center, m_Config.textColor);
            m_Number.transform.localPosition = new Vector3(leftX, -0.035f, 0f);

            m_Name = TextFactory.Create("Card Name", m_Root, 0.055f, new Vector2(0.56f, 0.08f), TextAlignmentOptions.Center, m_Config.textAccentColor);
            m_Name.textWrappingMode = TextWrappingModes.NoWrap;
            m_Name.transform.localPosition = new Vector3(leftX, -0.08f, 0f);

            m_Story = TextFactory.Create("Card Story", m_Root, 0.03f, new Vector2(0.56f, 0.2f), TextAlignmentOptions.Top, m_Config.textColor);
            TextFactory.EnableAutoSize(m_Story, 0.018f, 0.03f);
            m_Story.transform.localPosition = new Vector3(leftX, -0.225f, 0f);

            m_Qr = VisualFactory.CreatePanel("QR", m_Root, new Vector2(0.42f, 0.42f), Color.white, null);
            m_Qr.transform.localPosition = new Vector3(rightX, 0.06f, 0f);
            m_Qr.enabled = false;

            m_Caption = TextFactory.Create("Card Caption", m_Root, 0.034f, new Vector2(0.5f, 0.1f), TextAlignmentOptions.Center, m_Config.textColor);
            m_Caption.transform.localPosition = new Vector3(rightX, withQr ? -0.24f : -0.385f, 0f);
        }

        /// <summary>카드 데이터를 서버에 올리기 시작한다. 결과 화면에 들어갈 때 미리 부른다.</summary>
        /// <param name="shape">별자리 모양(0~1로 맞춘 점들). 모두의 하늘에 쌓이고 번호를 받는 데 쓴다. 없어도 된다.</param>
        public void Prepare(string keywordId, string constellationName, string story, byte[] jpeg, System.Collections.Generic.IList<Vector2> shape)
        {
            CancelUpload();
            m_CardUrl = null;
            CardNumber = 0;

            AppSecrets secrets = AppSecrets.Load();
            if (!secrets.IsConfigured || jpeg == null || jpeg.Length == 0)
            {
                m_State = UploadState.Failed;
                return;
            }

            m_State = UploadState.Uploading;
            m_UploadId++;
            m_Upload = StartCoroutine(Upload(m_UploadId, secrets, keywordId, constellationName, story, jpeg, shape));
        }

        IEnumerator Upload(int id, AppSecrets secrets, string keywordId, string constellationName, string story, byte[] jpeg, System.Collections.Generic.IList<Vector2> shape)
        {
            CardRequest body = new CardRequest();
            body.keyword = keywordId;
            body.name = constellationName;
            body.story = story;
            body.image = Convert.ToBase64String(jpeg);
            byte[] payload = Encoding.UTF8.GetBytes(AppendShape(JsonUtility.ToJson(body), shape));

            UnityWebRequest request = new UnityWebRequest(secrets.Url("/card"), UnityWebRequest.kHttpVerbPOST);
            m_Request = request;
            request.uploadHandler = new UploadHandlerRaw(payload);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-App-Token", secrets.appToken);
            request.SetRequestHeader("X-Device-Id", AiClient.DeviceId());
            request.timeout = Mathf.CeilToInt(m_Config.cardUploadTimeoutSeconds) + 1;

            float start = Time.realtimeSinceStartup;
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            bool timedOut = false;
            while (!operation.isDone)
            {
                if (Time.realtimeSinceStartup - start >= m_Config.cardUploadTimeoutSeconds)
                {
                    timedOut = true;
                    request.Abort();
                    break;
                }
                yield return null;
            }

            string url = null;
            int number = 0;
            if (!timedOut && request.result == UnityWebRequest.Result.Success && request.responseCode == 200)
            {
                try
                {
                    CardResponse response = JsonUtility.FromJson<CardResponse>(request.downloadHandler.text);
                    if (response != null && !string.IsNullOrEmpty(response.url) && response.url.StartsWith("http"))
                    {
                        url = response.url;
                        number = response.number;
                    }
                }
                catch (Exception)
                {
                    url = null;
                }
            }
            else if (m_Config.logStateChanges)
            {
                Debug.Log("[Constellation] 카드 업로드 실패: " + (timedOut ? "timeout" : request.responseCode + " " + request.result));
            }

            DisposeRequest();
            m_Upload = null;
            if (id != m_UploadId)
                yield break;

            if (url == null)
            {
                m_State = UploadState.Failed;
                yield break;
            }

            // QR은 헤드셋 안에 띄울 때만 만든다. 기본은 모니터 페이지에서 찍는다.
            Texture2D qr = m_Config.showQrInHeadset ? QrEncoder.ToTexture(QrEncoder.Encode(url), 8, 4) : null;
            if (m_Config.showQrInHeadset && qr == null)
            {
                m_State = UploadState.Failed;
                yield break;
            }

            ReleaseQr();
            m_QrTexture = qr;
            m_CardUrl = url;
            CardNumber = number;
            m_Qr.sharedMaterial.SetTexture(VisualFactory.MainTexId, qr);
            m_State = UploadState.Ready;
        }

        /// <summary>카드 미리보기를 체험자 앞에 띄운다. QR은 업로드가 끝나면 나타난다.</summary>
        public void Show(Vector3 anchorPosition, Quaternion anchorRotation, Texture2D image, string constellationName, string story)
        {
            m_Root.position = anchorPosition + anchorRotation * new Vector3(0f, -0.05f, m_Config.cardDistance);
            m_Root.rotation = anchorRotation;
            m_Image.sharedMaterial.SetTexture(VisualFactory.MainTexId, image);
            m_Image.enabled = image != null;
            m_Name.text = constellationName ?? string.Empty;
            m_Story.text = story ?? string.Empty;
            m_Caption.text = string.Empty;
            m_Appear = 0f;
            m_Visible = true;
            m_Root.localScale = Vector3.one * 0.9f;
            m_Root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            m_Visible = false;
            if (m_Root != null)
                m_Root.gameObject.SetActive(false);
        }

        /// <summary>업로드를 끊고 카드와 QR을 모두 지운다. 체험이 끝날 때 부른다.</summary>
        public void Clear()
        {
            CancelUpload();
            Hide();
            ReleaseQr();
            m_CardUrl = null;
            CardNumber = 0;
            m_State = UploadState.None;
            if (m_Qr != null)
                m_Qr.enabled = false;
            if (m_Image != null)
                m_Image.sharedMaterial.SetTexture(VisualFactory.MainTexId, null);
        }

        void Update()
        {
            if (!m_Visible)
                return;

            m_Appear = Mathf.MoveTowards(m_Appear, 1f, Time.deltaTime * 2f);
            m_Root.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, Mathf.SmoothStep(0f, 1f, m_Appear));

            bool ready = m_State == UploadState.Ready;
            m_Qr.enabled = ready && m_Config.showQrInHeadset;
            m_Caption.text = !ready ? m_Config.msgCardWaiting : m_Config.showQrInHeadset ? m_Config.msgCardReadyWithQr : m_Config.msgCardReady;
            m_Number.text = ready && CardNumber > 0 ? string.Format(m_Config.msgCardNumber, CardNumber) : string.Empty;
        }

        // JsonUtility는 겹친 배열([[x, y], …])을 만들지 못해서, 모양은 직접 이어 붙인다.
        static string AppendShape(string json, System.Collections.Generic.IList<Vector2> shape)
        {
            if (shape == null || shape.Count < 2 || string.IsNullOrEmpty(json) || !json.EndsWith("}"))
                return json;
            StringBuilder builder = new StringBuilder(json, 0, json.Length - 1, json.Length + shape.Count * 16 + 16);
            builder.Append(",\"points\":[");
            System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i < shape.Count; i++)
            {
                if (i > 0)
                    builder.Append(',');
                builder.Append('[');
                builder.Append(Mathf.Clamp01(shape[i].x).ToString("0.###", invariant));
                builder.Append(',');
                builder.Append(Mathf.Clamp01(shape[i].y).ToString("0.###", invariant));
                builder.Append(']');
            }
            builder.Append("]}");
            return builder.ToString();
        }

        void CancelUpload()
        {
            m_UploadId++;
            if (m_Upload != null)
            {
                StopCoroutine(m_Upload);
                m_Upload = null;
            }
            DisposeRequest();
        }

        void DisposeRequest()
        {
            if (m_Request != null)
            {
                try
                {
                    m_Request.Abort();
                    m_Request.Dispose();
                }
                catch (Exception)
                {
                    // 이미 끝난 요청이면 무시한다.
                }
                m_Request = null;
            }
        }

        void ReleaseQr()
        {
            if (m_QrTexture != null)
                Destroy(m_QrTexture);
            m_QrTexture = null;
        }

        void OnDestroy()
        {
            ReleaseQr();
        }
    }
}
