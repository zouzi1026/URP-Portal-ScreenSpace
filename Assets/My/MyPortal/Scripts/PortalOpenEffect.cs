using UnityEngine;

namespace MyPortal
{
    /// <summary>
    /// 开关门的"显现"效果：监听 PortalGate 的状态变化，把进度写进门面材质的 _OpenAmount，
    /// 由 MyPortal/PortalSurface 沿噪声贴图溶解出"关闭面板 -> 门内画面"的过渡，并带一圈烧灼边缘光。
    ///
    /// 它只管进度曲线，不碰穿越逻辑：PortalGate 依旧是"开就能穿、关就不能穿"。
    /// 想在开关瞬间加音效 / 粒子，同样监听 PortalGate.Opened / Closed 即可，不需要改这里。
    /// </summary>
    [RequireComponent(typeof(PortalGate))]
    [DisallowMultipleComponent]
    public class PortalOpenEffect : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("门面 Renderer；留空时优先复用同一个物体上 PortalViewRenderer 的门面")]
        [SerializeField] private Renderer surfaceRenderer;

        [Header("时长")]
        [SerializeField] private float openDuration = 0.6f;
        [SerializeField] private float closeDuration = 0.4f;

        [Header("曲线")]
        [SerializeField] private AnimationCurve openCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private AnimationCurve closeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("材质属性")]
        [SerializeField] private string openAmountProperty = "_OpenAmount";

        private PortalGate gate;

        private float progress = 1f; // 线性进度：0 = 完全关闭，1 = 完全打开
        private float target = 1f;
        private float duration = 1f;
        private AnimationCurve curve;

        /// <summary>当前线性进度，供其它表现层（音效、粒子）读取。</summary>
        public float Progress
        {
            get { return progress; }
        }

        private void Awake()
        {
            gate = GetComponent<PortalGate>();
            curve = openCurve;

            if (surfaceRenderer == null)
            {
                var viewRenderer = GetComponent<PortalViewRenderer>();
                surfaceRenderer = viewRenderer != null && viewRenderer.SurfaceRenderer != null
                    ? viewRenderer.SurfaceRenderer
                    : GetComponentInChildren<Renderer>();
            }

            if (surfaceRenderer == null)
                Debug.LogWarningFormat("[PortalOpenEffect] {0} 找不到门面 Renderer，开关门效果不会显示", name);
        }

        private void OnEnable()
        {
            gate.Opened += OnGateOpened;
            gate.Closed += OnGateClosed;
        }

        private void OnDisable()
        {
            gate.Opened -= OnGateOpened;
            gate.Closed -= OnGateClosed;
        }

        private void Start()
        {
            // 初始状态直接到位：如果场景一开始门就是开的，不该再播一遍开门动画
            progress = gate.IsOpen ? 1f : 0f;
            target = progress;
            ApplyOpenAmount(progress);
        }

        private void Update()
        {
            if (Mathf.Approximately(progress, target))
                return;

            float step = duration > 0f ? Time.deltaTime / duration : 1f;
            progress = Mathf.MoveTowards(progress, target, step);
            ApplyOpenAmount(curve.Evaluate(progress));
        }

        private void OnGateOpened(PortalGate _)
        {
            target = 1f;
            duration = openDuration;
            curve = openCurve;
        }

        private void OnGateClosed(PortalGate _)
        {
            target = 0f;
            duration = closeDuration;
            curve = closeCurve;
        }

        private void ApplyOpenAmount(float value)
        {
            if (surfaceRenderer == null)
                return;

            // material 是材质实例，不会改脏工程里的材质资源
            surfaceRenderer.material.SetFloat(openAmountProperty, value);
        }
    }
}
