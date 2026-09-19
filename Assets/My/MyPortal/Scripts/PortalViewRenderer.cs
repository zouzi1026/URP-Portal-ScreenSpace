using UnityEngine;

namespace MyPortal
{
    /// <summary>
    /// 门后景象：镜像相机 + RenderTexture + 门面材质。
    ///
    /// 每帧做三件事：
    ///   1. 把镜像相机的位姿设成"玩家相机经过 PortalMapping 映射后的结果"（和人物穿门用的是同一个矩阵）；
    ///   2. 把镜像相机渲染进 RenderTexture（投影参数与玩家相机严格一致）；
    ///   3. 把 RT 交给门面材质，由 MyPortal/PortalSurface 按屏幕空间采样显示。
    ///
    /// 近裁剪面顶到门面上：玩家到入口门面的距离 = 镜像相机到出口门面的距离，
    /// 于是门后面的墙壁不会从门里漏进来。这个做法只在正对门时完全精确；
    /// 斜视角下仍可能漏一点，要彻底解决需要把门面当作近裁剪面做斜投影（后续步骤）。
    ///
    /// 穿越逻辑完全不参与：PortalGate 一行都不用改。
    /// </summary>
    [RequireComponent(typeof(PortalGate))]
    [DisallowMultipleComponent]
    public class PortalViewRenderer : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("观察者相机（玩家相机）。留空时取 Camera.main")]
        [SerializeField] private Camera viewerCamera;
        [Tooltip("门面 Renderer。留空时取子物体上的第一个 Renderer")]
        [SerializeField] private Renderer surfaceRenderer;

        [Header("Render Settings")]
        [Tooltip("RenderTexture 相对屏幕分辨率的倍率：1 = 与屏幕 1:1，最清晰也最费显存")]
        [SerializeField, Range(0.1f, 1f)] private float resolutionScale = 1f;
        [Tooltip("镜像相机要剔除的层：把门面放到独立 Layer（例如 Portal）后填进来，避免门面互相看到自己")]
        [SerializeField] private LayerMask hiddenLayers;
        [SerializeField] private float nearClipMargin = 0.05f;
        [Tooltip("门面材质中接收画面的贴图属性名")]
        [SerializeField] private string textureProperty = "_PortalTexture";

        private PortalGate gate;
        private Camera viewCamera;
        private RenderTexture renderTexture;

        private int cachedScreenWidth;
        private int cachedScreenHeight;
        private float cachedScale;

        public RenderTexture RenderTexture
        {
            get { return renderTexture; }
        }

        public Camera ViewCamera
        {
            get { return viewCamera; }
        }

        /// <summary>门面所用的 Renderer（PortalOpenEffect 等表现层组件可以直接复用，不必重复查找）。</summary>
        public Renderer SurfaceRenderer
        {
            get { return surfaceRenderer; }
        }

        private void Awake()
        {
            gate = GetComponent<PortalGate>();

            if (viewerCamera == null)
                viewerCamera = Camera.main;

            if (surfaceRenderer == null)
                surfaceRenderer = GetComponentInChildren<Renderer>();

            CreateViewCamera();
            RebuildRenderTexture();
        }

        private void Start()
        {
            if (viewerCamera == null)
            {
                Debug.LogWarningFormat("[PortalViewRenderer] {0} 找不到观察者相机（viewerCamera 为空，场景里也没有 MainCamera 标签的相机），门后景象不会更新", name);
                enabled = false;
                return;
            }

            if (gate.LinkedGate == null)
                Debug.LogWarningFormat("[PortalViewRenderer] {0} 上的 PortalGate 没有设置 Linked Gate，门后景象无法渲染", name);
        }

        private void OnEnable()
        {
            if (gate != null)
            {
                gate.Opened += OnGateStateChanged;
                gate.Closed += OnGateStateChanged;
            }

            RefreshViewCamera();
        }

        private void OnDisable()
        {
            if (gate != null)
            {
                gate.Opened -= OnGateStateChanged;
                gate.Closed -= OnGateStateChanged;
            }

            if (viewCamera != null)
                viewCamera.enabled = false;
        }

        private void OnGateStateChanged(PortalGate _)
        {
            RefreshViewCamera();
        }

        // 门关着时没必要再渲染一遍场景：镜像相机停掉即可，
        // 门面这时显示的是"关闭面板"（由溶解效果负责），不依赖 RT。
        private void RefreshViewCamera()
        {
            if (viewCamera != null)
                viewCamera.enabled = isActiveAndEnabled && gate != null && gate.IsOpen;
        }

        private void OnDestroy()
        {
            ReleaseRenderTexture();

            if (viewCamera != null)
                Destroy(viewCamera.gameObject);
        }

        private void LateUpdate()
        {
            if (viewerCamera == null || viewCamera == null || gate.LinkedGate == null)
                return;

            // Game 视图分辨率或倍率变化时重建 RT，保证与屏幕 1:1 采样
            if (renderTexture == null
                || cachedScreenWidth != Screen.width
                || cachedScreenHeight != Screen.height
                || !Mathf.Approximately(cachedScale, resolutionScale))
            {
                RebuildRenderTexture();
            }

            SyncViewCamera();
        }

        private void CreateViewCamera()
        {
            var cameraObject = new GameObject(name + " View Camera");
            cameraObject.transform.SetParent(null);

            viewCamera = cameraObject.AddComponent<Camera>();
            viewCamera.enabled = false;
        }

        private void SyncViewCamera()
        {
            Matrix4x4 mapping = PortalMapping.Build(transform, gate.LinkedGate.transform);

            viewCamera.transform.SetPositionAndRotation(
                PortalMapping.MapPoint(mapping, viewerCamera.transform.position),
                PortalMapping.MapRotation(mapping, viewerCamera.transform.rotation));

            // 投影参数必须与玩家相机严格一致，屏幕空间采样才成立
            viewCamera.orthographic = viewerCamera.orthographic;
            viewCamera.fieldOfView = viewerCamera.fieldOfView;
            viewCamera.aspect = viewerCamera.aspect;
            viewCamera.orthographicSize = viewerCamera.orthographicSize;
            viewCamera.farClipPlane = viewerCamera.farClipPlane;

            // 近裁剪面顶到出口门面上
            float distanceToGatePlane = Mathf.Abs(transform.InverseTransformPoint(viewerCamera.transform.position).z);
            viewCamera.nearClipPlane = Mathf.Max(distanceToGatePlane - nearClipMargin, 0.01f);

            viewCamera.cullingMask = viewerCamera.cullingMask & ~hiddenLayers.value;
            viewCamera.clearFlags = viewerCamera.clearFlags;
            viewCamera.backgroundColor = viewerCamera.backgroundColor;
            viewCamera.allowHDR = viewerCamera.allowHDR;
            viewCamera.allowMSAA = viewerCamera.allowMSAA;
            viewCamera.useOcclusionCulling = viewerCamera.useOcclusionCulling;

            // 必须先于玩家相机渲染，否则门面这一帧采样到的是上一帧的画面
            viewCamera.depth = viewerCamera.depth - 1f;
        }

        private void RebuildRenderTexture()
        {
            ReleaseRenderTexture();

            cachedScreenWidth = Screen.width;
            cachedScreenHeight = Screen.height;
            cachedScale = resolutionScale;

            int width = Mathf.Max(1, Mathf.RoundToInt(cachedScreenWidth * resolutionScale));
            int height = Mathf.Max(1, Mathf.RoundToInt(cachedScreenHeight * resolutionScale));

            // 用默认读写模式：工程为 Linear 时 RT 会自动按 sRGB 处理，画面不会偏暗或过亮
            RenderTextureFormat format = viewerCamera != null && viewerCamera.allowHDR
                ? RenderTextureFormat.DefaultHDR
                : RenderTextureFormat.Default;

            renderTexture = new RenderTexture(width, height, 24, format, RenderTextureReadWrite.Default);
            renderTexture.name = name + " PortalView";
            renderTexture.filterMode = FilterMode.Bilinear;
            renderTexture.wrapMode = TextureWrapMode.Clamp;
            renderTexture.useMipMap = false;
            renderTexture.autoGenerateMips = false;
            renderTexture.Create();

            if (viewCamera != null)
                viewCamera.targetTexture = renderTexture;

            ApplyRenderTextureToSurface();
        }

        private void ApplyRenderTextureToSurface()
        {
            if (surfaceRenderer == null || renderTexture == null)
                return;

            // 用 material（材质实例）而不是 sharedMaterial，避免把工程里的材质资源改脏
            surfaceRenderer.material.SetTexture(textureProperty, renderTexture);
        }

        private void ReleaseRenderTexture()
        {
            if (viewCamera != null)
                viewCamera.targetTexture = null;

            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }
        }
    }
}
