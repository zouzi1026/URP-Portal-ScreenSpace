using UnityEngine;

namespace MyPortal
{
    /// <summary>
    /// 门框常驻粒子：沿门框四条边发射的能量粒子，缓缓被吸向门内。
    ///
    /// 浓度跟随开门进度 —— 开门有粒子、关门无粒子：
    /// 关门过程中发射速率线性降到 0，已有粒子自然熄灭；重新开门时再渐显回来。
    /// 进度取自 PortalOpenEffect.Progress；该门没挂 PortalOpenEffect 时退化为"开 = 满、关 = 无"两态。
    ///
    /// 粒子系统在运行时按面板参数生成，不需要任何美术资源，也不用手工配 ParticleSystem。
    /// </summary>
    [RequireComponent(typeof(PortalGate))]
    [DisallowMultipleComponent]
    public class PortalParticles : MonoBehaviour
    {
        [Header("配色")]
        [SerializeField] private Color flowColor = new Color(0.35f, 0.8f, 1f, 1f);

        [Header("能量流动")]
        [SerializeField] private float flowRate = 24f;
        [SerializeField] private float flowSpeed = 0.5f;
        [SerializeField] private float flowSize = 0.14f;
        [SerializeField] private float flowLifetime = 1.4f;

        [Header("渲染")]
        [Tooltip("粒子材质：用 MyPortal/Materials/PortalParticle.mat；留空会临时用 Shader.Find 创建一份")]
        [SerializeField] private Material particleMaterial;
        [Tooltip("粒子层相对门面往前（-Z，玩家站的那一侧）偏移多少，避免与门面共面而打架")]
        [SerializeField] private float surfaceOffset = 0.05f;

        private PortalGate gate;
        private PortalOpenEffect openEffect;

        private ParticleSystem flow;
        private ParticleSystem.EmissionModule flowEmission;

        private void Awake()
        {
            gate = GetComponent<PortalGate>();
            openEffect = GetComponent<PortalOpenEffect>();

            flow = BuildFlow(ResolveMaterial());
        }

        private void Start()
        {
            flow.Play();
            SetFlowProgress(gate.IsOpen ? 1f : 0f);
        }

        private void Update()
        {
            // 每帧同步浓度：开门过程中渐显，关门过程中渐隐，关到底就彻底没有粒子
            float progress = openEffect != null
                ? Mathf.Clamp01(openEffect.Progress)
                : (gate.IsOpen ? 1f : 0f);

            SetFlowProgress(progress);
        }

        private void SetFlowProgress(float progress)
        {
            flowEmission.rateOverTime = flowRate * progress;
        }

        private ParticleSystem BuildFlow(Material material)
        {
            ParticleSystem system = CreateSystem("Portal Flow", material);

            var main = system.main;
            main.loop = true;
            main.prewarm = true; // 一开场就是"已经在流"的状态，而不是从零慢慢冒出来
            main.duration = flowLifetime;
            main.startLifetime = new ParticleSystem.MinMaxCurve(flowLifetime * 0.6f, flowLifetime);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(flowSize * 0.5f, flowSize);
            main.startColor = flowColor;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

            var emission = system.emission;
            emission.rateOverTime = 0f; // 由开门进度驱动
            flowEmission = emission;

            ApplyRimShape(system.shape);

            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.radial = new ParticleSystem.MinMaxCurve(-flowSpeed); // 负值 = 被吸向门内

            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 0.95f));

            // RGB 保持白色、只控制透明度，这样粒子自身的 Start Color 不会被染色
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.35f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);

            return system;
        }

        private ParticleSystem CreateSystem(string systemName, Material material)
        {
            var systemObject = new GameObject(systemName);
            systemObject.transform.SetParent(transform, false);
            systemObject.transform.localPosition = new Vector3(0f, 0f, -surfaceOffset);
            systemObject.transform.localRotation = Quaternion.identity;
            systemObject.transform.localScale = Vector3.one;

            var system = systemObject.AddComponent<ParticleSystem>();

            // AddComponent 会让粒子系统立刻开始播放（playOnAwake 默认 true），
            // 而"正在播放"时 Unity 不允许修改 duration 等参数，所以先彻底停掉再配置。
            // StopEmittingAndClear 会清掉已产生的粒子，让系统回到完全停止状态。
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; // 跟着门一起移动 / 旋转
            main.gravityModifier = 0f;
            main.maxParticles = 256;

            var systemRenderer = system.GetComponent<ParticleSystemRenderer>();
            systemRenderer.renderMode = ParticleSystemRenderMode.Billboard;

            if (material != null)
                systemRenderer.sharedMaterial = material;

            return system;
        }

        // 门框 = 矩形四条边：用 BoxEdge 形状沿边发射，尺寸取门的开口大小
        private void ApplyRimShape(ParticleSystem.ShapeModule shape)
        {
            Vector2 aperture = gate.ApertureSize;

            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.BoxEdge;
            shape.scale = new Vector3(aperture.x, aperture.y, 0.02f);
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
        }

        private Material ResolveMaterial()
        {
            if (particleMaterial != null)
                return particleMaterial;

            Shader shader = Shader.Find("MyPortal/PortalParticle");
            if (shader == null)
            {
                Debug.LogWarningFormat("[PortalParticles] {0} 找不到 MyPortal/PortalParticle 这个 Shader，粒子不会显示", name);
                return null;
            }

            Debug.LogWarningFormat("[PortalParticles] {0} 没有指定粒子材质，已临时创建一份；建议把 MyPortal/Materials/PortalParticle.mat 拖到 Particle Material 上（打包时 Shader.Find 不一定拿得到）", name);
            return new Material(shader);
        }
    }
}
