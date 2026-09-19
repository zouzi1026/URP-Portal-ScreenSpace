using UnityEngine;

namespace MyPortal
{
    /// 传送门的坐标映射：把"入口坐标系里的状态"换算到"出口坐标系"。
    ///
    /// 矩阵的含义：
    ///   worldToLocal(入口) × 绕局部 Y 轴 180° × localToWorld(出口)
    /// 第一步把对象换到入口的局部空间；第二步绕门的"上"方向转 180°，
    /// 把"进门方向"翻成"出门方向"（从 −Z 侧穿到 +Z 侧，在出口就变成从 +Z 侧往 −Z 出）；
    /// 第三步换回世界空间。
    public static class PortalMapping
    {
        private static readonly Matrix4x4 FlipAlongUp = Matrix4x4.Rotate(Quaternion.AngleAxis(180f, Vector3.up));

        /// 构造"入口 → 出口"的映射矩阵。
        public static Matrix4x4 Build(Transform entryGate, Transform exitGate)
        {
            return exitGate.localToWorldMatrix * FlipAlongUp * entryGate.worldToLocalMatrix;
        }

        /// 映射一个世界坐标点。
        public static Vector3 MapPoint(Matrix4x4 mapping, Vector3 worldPoint)
        {
            return mapping.MultiplyPoint3x4(worldPoint);
        }

        /// 映射一个世界朝向。
        public static Quaternion MapRotation(Matrix4x4 mapping, Quaternion worldRotation)
        {
            return mapping.rotation * worldRotation;
        }

        /// 映射一个方向向量（线速度、角速度都用它）。
        public static Vector3 MapDirection(Matrix4x4 mapping, Vector3 worldDirection)
        {
            return mapping.MultiplyVector(worldDirection);
        }
    }
}
