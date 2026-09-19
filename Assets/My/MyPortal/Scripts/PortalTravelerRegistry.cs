using System.Collections.Generic;
using UnityEngine;

namespace MyPortal
{
    /// <summary>
    /// 可穿门对象的登记表。
    ///
    /// 传送门不做物理触发检测，而是每帧遍历这张表做一次越面判定。这样做的好处：
    ///   - 穿门对象不需要 Rigidbody / Collider，镜头、纯 Transform 驱动的角色都能用；
    ///   - 不存在"触发体积太薄，快物体一帧穿过去没触发"的漏判。
    /// 代价是每帧固定遍历一次（门数量 × 对象数量），对个位数的规模可以忽略。
    ///
    /// 实现 IPortalTraveler 的对象请在 OnEnable 里 Register、在 OnDisable 里 Unregister。
    /// </summary>
    public static class PortalTravelerRegistry
    {
        private static readonly List<IPortalTraveler> travelers = new List<IPortalTraveler>();

        /// <summary>当前登记的所有可穿门对象。</summary>
        public static IReadOnlyList<IPortalTraveler> Travelers
        {
            get { return travelers; }
        }

        public static int Count
        {
            get { return travelers.Count; }
        }

        public static bool Contains(IPortalTraveler traveler)
        {
            return travelers.Contains(traveler);
        }

        public static void Register(IPortalTraveler traveler)
        {
            // 注意：这里的 traveler 是接口引用，不能直接用 == null 判空，
            // 被销毁的 MonoBehaviour 只有转成 UnityEngine.Object 之后才会等于 null。
            if (!IsAlive(traveler) || travelers.Contains(traveler))
                return;

            travelers.Add(traveler);
        }

        public static void Unregister(IPortalTraveler traveler)
        {
            travelers.Remove(traveler);
        }

        /// <summary>对象是否还活着（被销毁的 MonoBehaviour 返回 false）。</summary>
        public static bool IsAlive(IPortalTraveler traveler)
        {
            var unityObject = traveler as Object;
            return unityObject != null;
        }
    }
}
