using System;

namespace ProtocolRunVR.MetaHands
{
    // Local actuator guard, not the server's protocol firewall or AI diagnosis.
    public enum RecoveryPhase { Warmup, Ready, FaultInjected, AwaitingRegrab, RegrabObserved, Protected, Blocked }

    public sealed class RecoveryGate
    {
        public RecoveryPhase Phase { get; private set; }
        public string ObjectId { get; }
        public bool ExpectedGrabbable { get; }
        public bool RestoreAllowed { get; }
        public bool DemoControlsAllowed { get; }
        private bool observedGrab;

        public RecoveryGate(string objectId, bool expectedGrabbable, bool restoreAllowed, bool demoControlsAllowed)
        {
            if (string.IsNullOrWhiteSpace(objectId)) throw new ArgumentException("An object ID is required.");
            ObjectId = objectId;
            ExpectedGrabbable = expectedGrabbable;
            RestoreAllowed = expectedGrabbable && restoreAllowed;
            DemoControlsAllowed = expectedGrabbable && demoControlsAllowed;
            Phase = expectedGrabbable ? RecoveryPhase.Warmup : RecoveryPhase.Protected;
        }

        public void ObserveGrab()
        {
            if (Phase == RecoveryPhase.Warmup) observedGrab = true;
            else if (Phase == RecoveryPhase.AwaitingRegrab) Phase = RecoveryPhase.RegrabObserved;
        }

        public void ObserveRelease()
        {
            if (Phase == RecoveryPhase.Warmup && observedGrab) Phase = RecoveryPhase.Ready;
        }

        public bool CanInject(bool held) => !held && DemoControlsAllowed && RestoreAllowed && Phase == RecoveryPhase.Ready;
        public bool CanRestore(bool held) => !held && RestoreAllowed && Phase == RecoveryPhase.FaultInjected;

        public void ConfirmInjection()
        {
            if (!CanInject(false)) throw new InvalidOperationException("Injection is not allowed in this phase.");
            Phase = RecoveryPhase.FaultInjected;
        }

        public void ConfirmRestore()
        {
            if (!CanRestore(false)) throw new InvalidOperationException("Restoration is not allowed in this phase.");
            Phase = RecoveryPhase.AwaitingRegrab;
        }

        public void Block() => Phase = RecoveryPhase.Blocked;
    }
}
