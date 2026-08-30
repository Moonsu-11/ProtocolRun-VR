using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
#if PRVR_XRI_3
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
#endif

namespace ProtocolRunVR
{
    public class XriStudyAdapter : MonoBehaviour
    {
        public ProtocolRunClient client;
        public XRGrabInteractable target, practice;
        public XRBaseInteractor leftInteractor, rightInteractor;
        public Camera head;
        public string targetId = "target_blue";
        public float nearDistance = 0.25f;
        [Tooltip("Researcher-only demo control. Do not enable during actual data collection.")]
        public bool allowDemoFaultInjection;
        private int baseline;
        private bool configured, paused, leftGrip, rightGrip, equipmentSent, foundSent, practiceHeld, practiceSent;
        private float gazeSeconds, nextTelemetry;
        private bool leftEnabled, rightEnabled;
        private Collider targetCollider;

        private void OnEnable()
        {
            if (target != null) { target.selectEntered.AddListener(OnGrab); target.selectExited.AddListener(OnRelease); }
            if (practice != null) { practice.selectEntered.AddListener(OnPracticeGrab); practice.selectExited.AddListener(OnPracticeRelease); }
        }
        private void OnDisable()
        {
            if (target != null) { target.selectEntered.RemoveListener(OnGrab); target.selectExited.RemoveListener(OnRelease); }
            if (practice != null) { practice.selectEntered.RemoveListener(OnPracticeGrab); practice.selectExited.RemoveListener(OnPracticeRelease); }
        }
        public bool Configure(Protocol p)
        {
            if (!target || !practice || target == practice || !leftInteractor || !rightInteractor || !client || p.target_id != targetId) return false;
            targetCollider = target.GetComponentInChildren<Collider>();
            if (!targetCollider || target.interactionLayers.value != p.baseline_mask ||
                (leftInteractor.interactionLayers.value & p.baseline_mask) == 0 ||
                (rightInteractor.interactionLayers.value & p.baseline_mask) == 0) return false;
            baseline = p.baseline_mask;
            head = head ? head : Camera.main;
            configured = head != null;
            return configured;
        }
        private bool Tracked(XRNode node)
        {
            var d = InputDevices.GetDeviceAtXRNode(node);
            return d.isValid && d.TryGetFeatureValue(CommonUsages.isTracked, out bool value) && value;
        }
        private EventData Observe(XRBaseInteractor hand = null)
        {
            return new EventData { object_id = targetId, observed_mask = target.interactionLayers.value,
                interactor_mask = hand ? hand.interactionLayers.value : 0,
                tracked = Tracked(XRNode.LeftHand) && Tracked(XRNode.RightHand),
                collider_enabled = targetCollider && targetCollider.enabled && targetCollider.gameObject.activeInHierarchy,
                near_target = hand && targetCollider && Vector3.Distance(hand.transform.position, targetCollider.ClosestPoint(hand.transform.position)) <= nearDistance,
                input_received = hand != null };
        }
        private void Update()
        {
            if (!configured || client.Current == null || client.Blocked) return;
            int step = client.Current.step;
            if (step == 0 && !equipmentSent && Tracked(XRNode.LeftHand) && Tracked(XRNode.RightHand))
            { equipmentSent = true; client.Emit("equipment_ready", Observe()); }
            if (!paused && step == 2 && !foundSent)
            {
                bool looking = Physics.Raycast(head.transform.position, head.transform.forward, out RaycastHit hit, 10) && hit.collider.GetComponentInParent<XRGrabInteractable>() == target;
                gazeSeconds = looking ? gazeSeconds + Time.unscaledDeltaTime : 0;
                if (gazeSeconds >= 1.5f) { foundSent = true; client.Emit("target_found", Observe()); }
            }
            CheckGrip(XRNode.LeftHand, leftInteractor, ref leftGrip);
            CheckGrip(XRNode.RightHand, rightInteractor, ref rightGrip);
            if (Time.unscaledTime >= nextTelemetry)
            {
                nextTelemetry = Time.unscaledTime + 2;
                var d = Observe(); d.fps = Time.unscaledDeltaTime > 0 ? 1 / Time.unscaledDeltaTime : 0;
                var h = head.transform.position; var l = leftInteractor.transform.position; var r = rightInteractor.transform.position;
                d.head_x = h.x; d.head_y = h.y; d.head_z = h.z; d.left_x = l.x; d.left_y = l.y; d.left_z = l.z; d.right_x = r.x; d.right_y = r.y; d.right_z = r.z;
                client.Emit("telemetry", d);
            }
        }
        private void CheckGrip(XRNode node, XRBaseInteractor hand, ref bool previous)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            bool pressed = device.TryGetFeatureValue(CommonUsages.gripButton, out bool value) && value;
            if (pressed && !previous && !paused && client.Current.step == 3)
            { var d = Observe(hand); client.Emit("grab_attempt", d); StartCoroutine(CheckFailedGrab(hand)); }
            previous = pressed;
        }
        private IEnumerator CheckFailedGrab(XRBaseInteractor hand)
        {
            yield return new WaitForSecondsRealtime(0.3f);
            if (!paused && client.Current.step == 3 && !target.isSelected) client.Emit("grab_failed", Observe(hand));
        }
        private void OnGrab(SelectEnterEventArgs args)
        { if (configured && !paused) client.Emit("grab_success", Observe()); }
        private void OnRelease(SelectExitEventArgs args)
        { if (configured && !paused) client.Emit("released", Observe()); }
        private void OnPracticeGrab(SelectEnterEventArgs args)
        { if (configured && !paused && client.Current.step == 1) practiceHeld = true; }
        private void OnPracticeRelease(SelectExitEventArgs args)
        { if (configured && practiceHeld && !practiceSent && !paused && client.Current.step == 1) { practiceSent = true; client.Emit("practice_completed"); } }
        public void Placed()
        { if (configured && !paused && client.Current.step == 4 && !target.isSelected) client.Emit("placed", Observe()); }

        public void SetStudyPaused(bool value)
        {
            if (paused == value) return;
            paused = value;
            if (!leftInteractor || !rightInteractor) return;
            // HMD rendering/tracking and the Unity timescale are intentionally untouched.
            if (value) { leftEnabled = leftInteractor.enabled; rightEnabled = rightInteractor.enabled; leftInteractor.enabled = false; rightInteractor.enabled = false; }
            else { leftInteractor.enabled = leftEnabled; rightInteractor.enabled = rightEnabled; }
        }
        public Ack Execute(Command command)
        {
            if (!configured) return new Ack { success = false, message = "Adapter not configured" };
            switch (command.action)
            {
                case "pause": SetStudyPaused(true); return new Ack { success = true };
                case "restore_interaction_layer":
                    if (!paused || command.baseline_mask != baseline || target.isSelected) return new Ack { success = false, message = "Restore precondition failed" };
                    target.interactionLayers = baseline;
                    return new Ack { success = target.interactionLayers.value == baseline, observed_mask = target.interactionLayers.value };
                case "retest":
                    // Resume only after the server accepts this ACK and returns retest status.
                    return new Ack { success = paused && target.interactionLayers.value == baseline, observed_mask = target.interactionLayers.value };
                default: return new Ack { success = false, message = "Unknown action" };
            }
        }
        [ContextMenu("DEMO ONLY / Inject Layer Mismatch")]
        public void InjectLayerMismatch()
        {
            if (!allowDemoFaultInjection || !configured || paused || client.Current.step != 3 || target.isSelected) return;
            target.interactionLayers = 0; // Nothing: incompatible even with an interactor using Everything.
            client.Emit("fault_injected", Observe());
        }
    }
}
