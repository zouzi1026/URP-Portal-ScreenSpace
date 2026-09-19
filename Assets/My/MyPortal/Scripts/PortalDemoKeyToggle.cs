using UnityEngine;

namespace MyPortal
{
    /// <summary>
    /// 演示用：按键开关传送门，方便快速验证开关门效果。
    /// 正式项目里应该由你自己的逻辑（踩进触发区、机关、剧情、UI）调用 PortalGate.Open / Close。
    /// </summary>
    public class PortalDemoKeyToggle : MonoBehaviour
    {
        [SerializeField] private KeyCode openKey = KeyCode.O;
        [SerializeField] private KeyCode closeKey = KeyCode.P;
        [SerializeField] private PortalGate[] gates;

        private void Update()
        {
            if (Input.GetKeyDown(openKey))
                SetOpen(true);

            if (Input.GetKeyDown(closeKey))
                SetOpen(false);
        }

        private void SetOpen(bool open)
        {
            if (gates == null)
                return;

            for (int i = 0; i < gates.Length; i++)
            {
                if (gates[i] == null)
                    continue;

                if (open)
                    gates[i].Open();
                else
                    gates[i].Close();
            }
        }
    }
}
