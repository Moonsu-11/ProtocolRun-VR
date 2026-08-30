#if PRVR_XRI_3
using UnityEngine.XR.Interaction.Toolkit.Interactables;
#else
using UnityEngine.XR.Interaction.Toolkit;
#endif
using UnityEngine;

namespace ProtocolRunVR
{
    [RequireComponent(typeof(Collider))]
    public class StudyDropZone : MonoBehaviour
    {
        public XriStudyAdapter adapter;
        private bool sent;
        private void OnTriggerStay(Collider other)
        {
            if (sent || !adapter || adapter.client.Current == null || adapter.client.Current.step != 4) return;
            var obj = other.GetComponentInParent<XRGrabInteractable>();
            if (obj == adapter.target && !obj.isSelected) { adapter.Placed(); sent = true; }
        }
    }
}
