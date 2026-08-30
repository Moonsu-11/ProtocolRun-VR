using UnityEngine;

namespace ProtocolRunVR.MetaHands
{
    [RequireComponent(typeof(BoxCollider))]
    [AddComponentMenu("ProtocolRun VR/Meta Study Drop Zone")]
    public sealed class MetaStudyDropZone : MonoBehaviour
    {
        public ProtocolRunMetaSession session;
        private BoxCollider zone;
        private bool sent;
        private float settled;
        private void Awake() { zone = GetComponent<BoxCollider>(); }
        private void Update()
        {
            if (!session || session.StudyPaused || session.Current.step != 4 || sent || !zone.enabled) return;
            var target = session.Target;
            if (!target || target.IsHeld) { settled = 0; return; }
            // Test target center in the oriented box; broad AABB overlap is insufficient.
            Vector3 point = zone.transform.InverseTransformPoint(target.transform.position) - zone.center;
            Vector3 half = zone.size * 0.5f;
            if (Mathf.Abs(point.x) > half.x || Mathf.Abs(point.y) > half.y || Mathf.Abs(point.z) > half.z) { settled = 0; return; }
            var body = target.GetComponent<Rigidbody>();
            if (!body || body.linearVelocity.sqrMagnitude > 0.0225f) { settled = 0; return; }
            settled += Time.unscaledDeltaTime;
            if (settled < 0.5f) return;
            var d = session.Data(target);
            if (!d.baseline_match) return;
            d.inside_drop_zone = true; d.source = "drop_zone"; d.settled_seconds = Mathf.Min(settled, 10);
            session.Emit("placed", d); sent = true;
        }
    }
}
