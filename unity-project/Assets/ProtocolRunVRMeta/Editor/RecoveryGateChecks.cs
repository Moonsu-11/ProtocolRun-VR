using System;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEditor;
using UnityEngine;

namespace ProtocolRunVR.MetaHands.Editor
{
    // A small dependency-free editor check for the SAME policy class used by the actuator.
    // This is not a test of Unity physics, Meta SDK lifecycle, hand tracking, or Gemini.
    public static class RecoveryGateChecks
    {
        [MenuItem("Tools/ProtocolRun VR/Run Recovery Gate Checks")]
        public static void Run()
        {
            // Regression: 0.2.0 blocked the normal Grab Wizard Hand+Grab pairing.
            // These exercise the same layout predicate used by Start and SameSetup.
            Check(ProtocolRunMetaObject.SupportsGrabLayout(new[] { typeof(HandGrabInteractable), typeof(GrabInteractable) }), "Wizard's Hand Grab + companion Grab layout must be accepted.");
            Check(ProtocolRunMetaObject.SupportsGrabLayout(new[] { typeof(HandGrabInteractable) }), "Hand-only objects remain supported.");
            Check(!ProtocolRunMetaObject.SupportsGrabLayout(new[] { typeof(GrabInteractable) }), "A controller-only object cannot satisfy this hand study.");
            Check(!ProtocolRunMetaObject.SupportsGrabLayout(new[] { typeof(HandGrabInteractable), typeof(RayInteractable) }), "Unknown alternate paths must still be rejected.");
            Check(!ProtocolRunMetaObject.SupportsGrabLayout(Array.Empty<Type>()), "Missing hand components cannot be silently added.");

            var c = new RecoveryGate("CUBE_C", false, true, true);
            c.ObserveGrab(); c.ObserveRelease();
            Check(c.Phase == RecoveryPhase.Protected && !c.CanInject(false) && !c.CanRestore(false), "C stays protected even if flags are supplied.");
            ExpectFailure(c.ConfirmInjection);
            ExpectFailure(c.ConfirmRestore);

            var a = new RecoveryGate("CUBE_A", true, false, false);
            a.ObserveGrab(); a.ObserveRelease();
            Check(a.Phase == RecoveryPhase.Ready && !a.CanInject(false), "A cannot be faulted.");

            var b = new RecoveryGate("CUBE_B", true, true, true);
            Check(!b.CanInject(false), "Cannot inject before observed normal grab and release.");
            b.ObserveRelease();
            Check(!b.CanInject(false), "Release without a preceding grab is insufficient.");
            b.ObserveGrab();
            Check(!b.CanInject(true), "Cannot inject while held or before release.");
            b.ObserveRelease();
            Check(b.CanInject(false) && !b.CanInject(true), "Only a released, warmed-up B is eligible.");
            b.ConfirmInjection();
            Check(!b.CanInject(false) && b.CanRestore(false) && !b.CanRestore(true), "No duplicate injection or restore while held.");
            b.ObserveGrab();
            Check(b.Phase == RecoveryPhase.FaultInjected, "A pre-restore grab is not a successful retest.");
            b.ConfirmRestore();
            Check(b.Phase == RecoveryPhase.AwaitingRegrab, "Restoring settings alone is not success.");
            b.ObserveRelease();
            Check(b.Phase == RecoveryPhase.AwaitingRegrab, "Release alone does not verify recovery.");
            a.ObserveGrab();
            Check(b.Phase == RecoveryPhase.AwaitingRegrab, "Another object's grab cannot verify B.");
            b.ObserveGrab();
            Check(b.Phase == RecoveryPhase.RegrabObserved && !b.CanInject(false) && !b.CanRestore(false), "New same-object grab completes the local cycle once.");
            b.Block(); b.ObserveGrab(); b.ObserveRelease();
            Check(b.Phase == RecoveryPhase.Blocked && !b.CanInject(false), "Blocked runs cannot silently resume.");
            Debug.Log("[ProtocolRun] Recovery gate checks passed. This does NOT validate the Meta SDK or headset integration.");
        }

        private static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Recovery gate check failed: " + message);
        }

        private static void ExpectFailure(Action action)
        {
            try { action(); } catch (InvalidOperationException) { return; }
            throw new InvalidOperationException("Expected the recovery gate to reject this action.");
        }
    }
}
