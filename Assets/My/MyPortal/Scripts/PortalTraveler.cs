using UnityEngine;

namespace MyPortal
{
    /// <summary>
    /// IPortalTraveler 的通用实现：给任意物体挂上它就能穿门。
    ///
    /// 它不负责"怎么移动"——移动仍然由你原来的控制器或物理负责，
    /// 它只提供判定采样点，并在穿越瞬间把物体搬到新的位置 / 朝向。
    ///
    /// 如果物体的移动逻辑自己缓存了状态（例如带插值或累计角度的控制器），
    /// 请让那个控制器直接实现 IPortalTraveler，而不是挂这个组件。
    /// </summary>
    [DisallowMultipleComponent]
    public class PortalTraveler : MonoBehaviour, IPortalTraveler
    {
        public enum SampleOrigin
        {
            /// <summary>用 Transform 轴心判定，适合人物、道具。</summary>
            TransformPivot,
            /// <summary>用刚体质心判定，适合轴心偏在一侧、形状不规则的刚体。</summary>
            RigidbodyCenterOfMass
        }

        [Tooltip("判定用哪一个点")]
        [SerializeField] private SampleOrigin sampleOrigin = SampleOrigin.TransformPivot;
        [Tooltip("穿门时是否把刚体的线速度 / 角速度一起按门的方向换算")]
        [SerializeField] private bool mirrorVelocity = true;

        private Rigidbody body;

        public Vector3 SamplePosition
        {
            get
            {
                if (sampleOrigin == SampleOrigin.RigidbodyCenterOfMass && body != null)
                    return body.worldCenterOfMass;

                return transform.position;
            }
        }

        public Quaternion Rotation
        {
            get { return transform.rotation; }
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
        }

        private void OnEnable()
        {
            PortalTravelerRegistry.Register(this);
        }

        private void OnDisable()
        {
            PortalTravelerRegistry.Unregister(this);
        }

        public void OnTeleported(Vector3 position, Quaternion rotation, Transform entryGate, Transform exitGate)
        {
            // position 是"采样点"该去的世界坐标，所以要把轴心与采样点的差值补回来，
            // 否则质心采样的物体会整体错位。
            Vector3 pivotOffset = transform.position - SamplePosition;
            transform.SetPositionAndRotation(position + pivotOffset, rotation);

            if (!mirrorVelocity || body == null)
                return;

            // 位置和朝向用的是同一个映射矩阵，速度也必须用同一个，
            // 否则会出现"物体进去了、动量却没跟着进去"的割裂感。
            Matrix4x4 mapping = PortalMapping.Build(entryGate, exitGate);
            body.velocity = PortalMapping.MapDirection(mapping, body.velocity);
            body.angularVelocity = PortalMapping.MapDirection(mapping, body.angularVelocity);
        }
    }
}
