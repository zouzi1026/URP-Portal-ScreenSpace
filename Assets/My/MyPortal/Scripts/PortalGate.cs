using System;
using System.Collections.Generic;
using UnityEngine;

namespace MyPortal
{
    /// <summary>
    /// 传送门的一侧：每帧检查登记过的对象有没有"越过门面"，命中就把它搬到另一侧。
    ///
    /// 摆放约定（两侧门必须一致）：
    ///   门放在门洞中心，+Z 轴（蓝轴）指向门的背面，也就是不可走的那一侧；
    ///   玩家 / 物体站在 −Z 一侧，朝 +Z 方向走过门面。
    ///   于是"从 A 的 −Z 侧走进去"就等价于"从 B 的 −Z 侧走出来"。
    ///
    /// 判定规则（不依赖物理触发器）：
    ///   1. 采样点在门局部空间的 Z 由负变正（也就是从正面穿到背面）= 越过了门面；
    ///      只认这一个方向，所以门是单面的，背面不可穿越；
    ///   2. 越面这一刻，采样点要落在 Aperture Size 定义的开口矩形内；
    ///   3. 不在冷却时间内（避免刚出现在出口侧就被对面门又判一次）。
    ///
    /// 本类只负责穿越判定与搬运；门后景象、开关门、粒子分别由
    /// PortalViewRenderer / PortalOpenEffect / PortalParticles 负责。
    /// </summary>
    [DefaultExecutionOrder(100)] // 排在移动逻辑之后，保证读到的是本帧最新的位置
    public class PortalGate : MonoBehaviour
    {
        [Header("关联")]
        [SerializeField] private PortalGate linkedGate;

        [Header("开口")]
        [Tooltip("门的开口尺寸：局部 X = 宽、局部 Y = 高，原点在门洞中心")]
        [SerializeField] private Vector2 apertureSize = new Vector2(1.6f, 2.6f);

        [Header("判定")]
        [Tooltip("穿越后的冷却时间，防止刚被送到出口侧就被另一侧的门再判一次")]
        [SerializeField] private float teleportCooldown = 0.2f;
        [SerializeField] private bool isOpen = true;

        /// <summary>某个可穿门对象在这一侧门上的跟踪状态。</summary>
        private class TrackingState
        {
            public bool HasSample;
            public float PreviousLocalZ;
            public float CooldownUntil;
        }

        private readonly Dictionary<IPortalTraveler, TrackingState> tracking = new Dictionary<IPortalTraveler, TrackingState>();
        private readonly List<IPortalTraveler> staleBuffer = new List<IPortalTraveler>();

        /// <summary>门被打开时触发（参数是这一侧的门）。开关门的表现（溶解、音效、粒子）都挂在这里。</summary>
        public event Action<PortalGate> Opened;

        /// <summary>门被关闭时触发。</summary>
        public event Action<PortalGate> Closed;

        /// <summary>门当前是否处于"可以穿越"的状态。只读，改状态请用 Open / Close。</summary>
        public bool IsOpen
        {
            get { return isOpen; }
        }

        public PortalGate LinkedGate
        {
            get { return linkedGate; }
            set { linkedGate = value; }
        }

        /// <summary>门的开口尺寸（局部 X = 宽、Y = 高）。表现层（粒子、边框）会用到。</summary>
        public Vector2 ApertureSize
        {
            get { return apertureSize; }
        }

        /// <summary>
        /// 开门：可穿越状态立刻生效（判定数学不变），表现层自己监听 Opened 去播动画。
        /// </summary>
        public void Open()
        {
            if (isOpen)
                return;

            isOpen = true;

            if (Opened != null)
                Opened(this);
        }

        /// <summary>关门：立刻禁止穿越，表现层监听 Closed 去播动画。</summary>
        public void Close()
        {
            if (!isOpen)
                return;

            isOpen = false;

            if (Closed != null)
                Closed(this);
        }

        private void Start()
        {
            if (linkedGate == null)
                Debug.LogWarningFormat("[PortalGate] {0} 没有设置 Linked Gate，这一侧不会传送任何对象", name);
        }

        private void Update()
        {
            if (!isOpen || linkedGate == null || !linkedGate.isOpen)
                return;

            PruneTracking();

            IReadOnlyList<IPortalTraveler> travelers = PortalTravelerRegistry.Travelers;
            for (int i = 0; i < travelers.Count; i++)
            {
                IPortalTraveler traveler = travelers[i];
                if (PortalTravelerRegistry.IsAlive(traveler))
                    Evaluate(traveler);
            }
        }

        private void Evaluate(IPortalTraveler traveler)
        {
            Vector3 localPosition = transform.InverseTransformPoint(traveler.SamplePosition);

            TrackingState state = GetState(traveler);

            // 只认"正面 -> 背面"这一个方向：局部 Z 由负变正才算越面。
            // 反方向（背面 -> 正面）不触发，所以门是单面的。
            bool crossed = state.HasSample && state.PreviousLocalZ < 0f && localPosition.z >= 0f;

            state.PreviousLocalZ = localPosition.z;
            state.HasSample = true;

            if (!crossed)
                return;

            if (!IsInsideAperture(localPosition))
                return;

            if (Time.time < state.CooldownUntil)
                return;

            Teleport(traveler);
        }

        private void Teleport(IPortalTraveler traveler)
        {
            Transform exit = linkedGate.transform;
            Matrix4x4 mapping = PortalMapping.Build(transform, exit);

            Vector3 position = PortalMapping.MapPoint(mapping, traveler.SamplePosition);
            Quaternion rotation = PortalMapping.MapRotation(mapping, traveler.Rotation);

            traveler.OnTeleported(position, rotation, transform, exit);

            // 两侧都登记冷却：物体落点可能正好落在对面门的判定范围内
            float until = Time.time + teleportCooldown;
            GetState(traveler).CooldownUntil = until;
            linkedGate.GetState(traveler).CooldownUntil = until;
        }

        private bool IsInsideAperture(Vector3 localPosition)
        {
            Vector2 half = apertureSize * 0.5f;

            return Mathf.Abs(localPosition.x) <= half.x
                && Mathf.Abs(localPosition.y) <= half.y;
        }

        private TrackingState GetState(IPortalTraveler traveler)
        {
            TrackingState state;
            if (!tracking.TryGetValue(traveler, out state))
            {
                state = new TrackingState();
                tracking.Add(traveler, state);
            }

            return state;
        }

        private void PruneTracking()
        {
            if (tracking.Count == 0)
                return;

            staleBuffer.Clear();

            foreach (KeyValuePair<IPortalTraveler, TrackingState> pair in tracking)
            {
                // 已经注销（禁用 / 销毁）的对象不再跟踪，
                // 否则它下次启用时可能拿旧的采样值误判成一次穿越
                if (!PortalTravelerRegistry.IsAlive(pair.Key) || !PortalTravelerRegistry.Contains(pair.Key))
                    staleBuffer.Add(pair.Key);
            }

            for (int i = 0; i < staleBuffer.Count; i++)
                tracking.Remove(staleBuffer[i]);
        }

        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;

            // 青色框 = 门的开口；黄色箭头 = 穿门时物体的前进方向（+Z）
            Gizmos.color = isOpen ? Color.cyan : new Color(0.4f, 0.4f, 0.4f, 1f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(apertureSize.x, apertureSize.y, 0f));

            Gizmos.color = Color.yellow;
            Vector3 tip = Vector3.forward;
            Gizmos.DrawLine(Vector3.zero, tip);
            Gizmos.DrawLine(tip, new Vector3(0.15f, 0.15f, 0.8f));
            Gizmos.DrawLine(tip, new Vector3(-0.15f, 0.15f, 0.8f));
            Gizmos.DrawLine(tip, new Vector3(0.15f, -0.15f, 0.8f));
            Gizmos.DrawLine(tip, new Vector3(-0.15f, -0.15f, 0.8f));
        }
    }
}
