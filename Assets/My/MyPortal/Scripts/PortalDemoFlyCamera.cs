using UnityEngine;

namespace MyPortal
{
    /// <summary>
    /// 演示用的自由飞行镜头：WASD 平移、QE 升降、按住鼠标右键转视角、Shift 加速。
    ///
    /// 它自己实现 IPortalTraveler，而不是挂 PortalTraveler —— 因为它内部缓存了 yaw / pitch，
    /// 穿门时必须一起同步，否则下一帧会按旧角度把镜头掰回去。
    /// 这一步先用它验证穿越逻辑；之后换成真正的角色控制器时，让角色控制器实现同一个接口即可。
    /// </summary>
    public class PortalDemoFlyCamera : MonoBehaviour, IPortalTraveler
    {
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float boostMultiplier = 3f;
        [SerializeField] private float lookSensitivity = 2f;

        private float yaw;
        private float pitch;

        public Vector3 SamplePosition
        {
            get { return transform.position; }
        }

        public Quaternion Rotation
        {
            get { return transform.rotation; }
        }

        private void OnEnable()
        {
            SyncAnglesFromTransform();
            PortalTravelerRegistry.Register(this);
        }

        private void OnDisable()
        {
            PortalTravelerRegistry.Unregister(this);
        }

        private void Update()
        {
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * lookSensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * lookSensitivity, -89f, 89f);
            }

            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            Vector3 direction = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) direction += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) direction += Vector3.back;
            if (Input.GetKey(KeyCode.A)) direction += Vector3.left;
            if (Input.GetKey(KeyCode.D)) direction += Vector3.right;
            if (Input.GetKey(KeyCode.E)) direction += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) direction += Vector3.down;

            if (direction == Vector3.zero)
                return;

            float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? boostMultiplier : 1f);
            transform.position += transform.rotation * direction.normalized * (speed * Time.deltaTime);
        }

        public void OnTeleported(Vector3 position, Quaternion rotation, Transform entryGate, Transform exitGate)
        {
            transform.SetPositionAndRotation(position, rotation);
            SyncAnglesFromTransform();
        }

        private void SyncAnglesFromTransform()
        {
            Vector3 euler = transform.eulerAngles;
            yaw = euler.y;
            pitch = euler.x > 180f ? euler.x - 360f : euler.x;
        }
    }
}
