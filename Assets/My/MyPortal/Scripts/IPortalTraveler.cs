using UnityEngine;

namespace MyPortal
{
    /// <summary>
    /// 可穿门对象的契约。
    ///
    /// 传送门每帧对登记过的对象做一次"是否越过门面"的数学判定，
    /// 判定命中后调用 <see cref="OnTeleported"/> 把对象搬到出口。
    ///
    /// 约定：
    ///   - <see cref="SamplePosition"/> 与 <see cref="Rotation"/> 必须指向对象当前的真实状态；
    ///   - 采样点可以与 Transform 轴心不同（例如刚体用质心），搬运时会自动补回偏移；
    ///   - 实现之后要在 OnEnable / OnDisable 里调用 PortalTravelerRegistry 注册与注销。
    /// </summary>
    public interface IPortalTraveler
    {
        /// <summary>用于判定"是否越过门面"的世界坐标采样点。</summary>
        Vector3 SamplePosition { get; }

        /// <summary>对象当前的世界朝向。</summary>
        Quaternion Rotation { get; }

        /// <summary>
        /// 被传送的瞬间由传送门调用。
        /// position 是"采样点"应该到达的世界坐标（不是 Transform 轴心）；
        /// rotation 是对象应该到达的世界朝向；
        /// entryGate / exitGate 是这次穿越经过的两个门，可用于换算速度方向。
        /// </summary>
        void OnTeleported(Vector3 position, Quaternion rotation, Transform entryGate, Transform exitGate);
    }
}
