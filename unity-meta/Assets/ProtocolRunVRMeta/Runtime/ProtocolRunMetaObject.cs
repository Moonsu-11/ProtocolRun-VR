using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

namespace ProtocolRunVR.MetaHands
{
    [Serializable]
    public class MetaObjectObservation
    {
        public string event_id, run_id, baseline_id, object_id, event_type, source, utc, phase, detail;
        public long sequence;
        public double monotonic_seconds;
        public bool expected_grabbable, restore_allowed, held, baseline_match, collider_enabled;
        public int hand_grab_count, enabled_hand_grab_count;
        public int companion_grab_count, enabled_companion_grab_count;
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("ProtocolRun VR/Meta Study Object")]
    public sealed class ProtocolRunMetaObject : MonoBehaviour
    {
        public const string ModuleVersion = "0.4.0-rc2";
        [SerializeField] private string objectId;
        [SerializeField] private bool expectedGrabbable = true;
        [SerializeField] private bool allowBaselineRestore;
        [Tooltip("Researcher-only fault controls. Turn off for actual participant data collection.")]
        [SerializeField] private bool allowDemoControls;

        public event Action<MetaObjectObservation> Observed;
        public string ObjectId => gate == null ? objectId : gate.ObjectId;
        public bool Initialized => initialized;
        public int GrabVersion { get; private set; }
        public bool ServerManaged { get; set; }
        public Func<bool> ServerFaultAllowed { private get; set; }
        public string Phase => gate == null ? "Not initialized" : gate.Phase.ToString();
        public string LastMessage { get; private set; } = "Enter Play mode to initialize.";
        public bool IsHeld => selections.Values.Any(set => set.Count > 0);
        public bool CanInject => isActiveAndEnabled && !changing && gate != null && gate.CanInject(IsHeld)
            && (!ServerManaged || (ServerFaultAllowed != null && ServerFaultAllowed()));
        public bool CanRestoreDemo => !ServerManaged && isActiveAndEnabled && !changing && gate != null && gate.DemoControlsAllowed && gate.CanRestore(IsHeld);
        private bool HandIsHeld => selections.Any(pair => pair.Key is HandGrabInteractable && pair.Value.Count > 0);

        private RecoveryGate gate;
        private HandGrabInteractable[] grabs;
        private GrabInteractable[] companionGrabs;
        private bool[] baselineEnabled;
        private bool[] companionBaselineEnabled;
        private Collider[] colliders;
        private Rigidbody body;
        private readonly Dictionary<MonoBehaviour, HashSet<IInteractorView>> selections = new Dictionary<MonoBehaviour, HashSet<IInteractorView>>();
        private readonly Dictionary<MonoBehaviour, Action<IInteractorView>> added = new Dictionary<MonoBehaviour, Action<IInteractorView>>();
        private readonly Dictionary<MonoBehaviour, Action<IInteractorView>> removed = new Dictionary<MonoBehaviour, Action<IInteractorView>>();
        private bool initialized, changing;
        private long sequence;
        private string runId, baselineId;

        private void Reset() => objectId = gameObject.name;

        // Meta Grab Wizard can put BOTH direct grab types on the same child.
        // Keep the allowlist narrow, but do not reject the wizard's normal pairing.
        public static bool SupportsGrabLayout(IEnumerable<Type> componentTypes)
        {
            if (componentTypes == null) return false;
            var types = componentTypes.ToArray();
            return types.Contains(typeof(HandGrabInteractable))
                && types.All(t => t == typeof(HandGrabInteractable) || t == typeof(GrabInteractable));
        }

        private IEnumerator Start()
        {
            // Let SDK components finish Start before reading their Collider/selection data.
            yield return null;
            runId = Guid.NewGuid().ToString("N");
            baselineId = Guid.NewGuid().ToString("N");
            objectId = (objectId ?? "").Trim();
            if (objectId.Length == 0) { Reject("Set Object Id before entering Play mode."); yield break; }
            gate = new RecoveryGate(objectId, expectedGrabbable, allowBaselineRestore, allowDemoControls);
            var peers = FindObjectsByType<ProtocolRunMetaObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (peers.Any(p => p != this && (p.objectId ?? "").Trim() == objectId))
            { Block("Duplicate Object Id in the loaded scene."); yield break; }

            grabs = GetComponentsInChildren<HandGrabInteractable>(true);
            companionGrabs = GetComponentsInChildren<GrabInteractable>(true);
            colliders = GetComponentsInChildren<Collider>(true);
            if (colliders.Length == 0 || !colliders.Any(c => c && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger))
            { Block("An active non-trigger object Collider is required."); yield break; }

            // Capture both direct grab paths. Unknown distance/ray/custom paths still
            // fail closed; permitting a companion without disabling it would leave a bypass.
            var views = GetComponentsInChildren<MonoBehaviour>(true).OfType<IInteractableView>().ToArray();
            if (!gate.ExpectedGrabbable)
            {
                if (views.Length != 0 || GetComponentsInChildren<Grabbable>(true).Length != 0)
                { Block("Protected object must not contain grab/interactable components."); yield break; }
                initialized = true;
                Emit("object_registered", "configuration", "Intentionally non-grabbable; restoration prohibited.");
                yield break;
            }
            if (!SupportsGrabLayout(views.Select(view => view.GetType())))
            {
                string detected = string.Join(", ", views.Select(view => view.GetType().FullName).Distinct());
                Block("Expected HandGrabInteractable with optional GrabInteractable companions. Found: " + (detected.Length == 0 ? "none" : detected));
                yield break;
            }
            body = GetComponent<Rigidbody>();
            if (!body || grabs.Any(g => g.Rigidbody != body) || companionGrabs.Any(g => g.Rigidbody != body))
            { Block("Attach Meta Study Object to the cube's Rigidbody root; all direct grab components must reference that Rigidbody."); yield break; }
            if (grabs.Any(g => !g.isActiveAndEnabled || g.SelectingInteractorViews.Any())
                || companionGrabs.Any(g => !g.gameObject.activeInHierarchy || g.SelectingInteractorViews.Any()))
            { Block("Start with Hand Grabs enabled, interaction objects active, and the cube released. Restart Play after fixing the setup."); yield break; }

            baselineEnabled = grabs.Select(g => g.enabled).ToArray();
            // A disabled companion remains disabled after restoration.
            companionBaselineEnabled = companionGrabs.Select(g => g.enabled).ToArray();
            foreach (var grab in grabs.Cast<MonoBehaviour>().Concat(companionGrabs.Cast<MonoBehaviour>()))
            {
                var captured = grab;
                var view = (IInteractableView)captured;
                selections[captured] = new HashSet<IInteractorView>();
                added[captured] = interactor => OnSelection(captured, interactor, true);
                removed[captured] = interactor => OnSelection(captured, interactor, false);
                view.WhenSelectingInteractorViewAdded += added[captured];
                view.WhenSelectingInteractorViewRemoved += removed[captured];
            }
            initialized = true;
            Emit("baseline_captured", "configuration", "Grab and release once in this Play session before fault injection.");
        }

        private void OnSelection(MonoBehaviour grab, IInteractorView interactor, bool selecting)
        {
            if (!initialized || !isActiveAndEnabled || !selections.ContainsKey(grab)) return;
            bool wasHeld = IsHeld;
            bool wasHandHeld = HandIsHeld;
            bool changed = selecting ? selections[grab].Add(interactor) : selections[grab].Remove(interactor);
            bool nowHeld = IsHeld;
            if (!changed || changing) return; // Enable/disable callbacks are not evidence of a human regrab.
            if (selecting && gate.Phase == RecoveryPhase.FaultInjected)
            { Block("Selection occurred during the injected fault; an interaction path is still active."); return; }
            if (selecting && !BaselineMatches())
            { Block("Grab observed with changed baseline or object setup."); return; }
            if (!wasHandHeld && HandIsHeld)
            {
                var before = gate.Phase;
                gate.ObserveGrab();
                GrabVersion++;
                Emit("grab_success", "sdk_selection", "Hand Grab selection began.");
                if (before == RecoveryPhase.AwaitingRegrab && gate.Phase == RecoveryPhase.RegrabObserved)
                    Emit("local_regrab_observed", "sdk_selection", "A new same-object grab followed restoration. The server must independently verify the acknowledged retest window.");
            }
            else if (selecting && grab is GrabInteractable)
            {
                // This path counts for the held-object guard, but cannot complete a HAND retest.
                Emit("companion_grab_selected", "sdk_companion_selection", "GrabInteractable selected; not Hand Grab retest evidence.");
            }
            if (!nowHeld && wasHeld)
            {
                gate.ObserveRelease();
                Emit("released", "sdk_selection", "All direct grab paths released the object.");
            }
        }

        private bool SameSetup()
        {
            if (!initialized || !gate.ExpectedGrabbable || !body || grabs == null) return false;
            var current = GetComponentsInChildren<HandGrabInteractable>(true);
            var currentCompanions = GetComponentsInChildren<GrabInteractable>(true);
            var currentColliders = GetComponentsInChildren<Collider>(true);
            return current.Length == grabs.Length && current.All(g => grabs.Contains(g))
                && currentCompanions.Length == companionGrabs.Length && currentCompanions.All(g => companionGrabs.Contains(g))
                && currentColliders.Length == colliders.Length && currentColliders.All(c => colliders.Contains(c))
                && SupportsGrabLayout(GetComponentsInChildren<MonoBehaviour>(true).OfType<IInteractableView>().Select(v => v.GetType()))
                && grabs.All(g => g && g.gameObject.activeInHierarchy && g.Rigidbody == body)
                && companionGrabs.All(g => g && g.gameObject.activeInHierarchy && g.Rigidbody == body)
                && colliders.Any(c => c && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger);
        }

        private bool BaselineMatches() => SameSetup()
            && grabs.Select((g, i) => g.enabled == baselineEnabled[i]).All(value => value)
            && companionGrabs.Select((g, i) => g.enabled == companionBaselineEnabled[i]).All(value => value);
        private bool AnyActualSelection() => IsHeld
            || (grabs != null && grabs.Any(g => g && g.SelectingInteractorViews.Any()))
            || (companionGrabs != null && companionGrabs.Any(g => g && g.SelectingInteractorViews.Any()));

        [ContextMenu("ProtocolRun / DEMO Inject Hand Grab Fault")]
        public void InjectDemoFault()
        {
            if (!Application.isPlaying || !CanInject) { Reject("Injection denied: enable demo/restore permissions before Play, then grab and release once."); return; }
            if (!BaselineMatches() || AnyActualSelection()) { Reject("Injection denied: changed baseline, missing components, or object currently held."); return; }
            changing = true;
            try
            {
                foreach (var grab in grabs) grab.enabled = false;
                foreach (var grab in companionGrabs) grab.enabled = false;
                if (grabs.Any(g => g.enabled) || companionGrabs.Any(g => g.enabled))
                    throw new InvalidOperationException("A direct grab path did not disable.");
                gate.ConfirmInjection();
            }
            catch (Exception error) { Block("Injection failed: " + error.Message); return; }
            finally { changing = false; }
            Emit("fault_injected", "researcher_demo", "Disabled captured HandGrabInteractable and GrabInteractable paths. Collider, Rigidbody and object pose were not edited.");
        }

        [ContextMenu("ProtocolRun / DEMO Restore Baseline")]
        public void RestoreDemoBaseline()
        {
            if (!Application.isPlaying || !CanRestoreDemo) { Reject("Manual restoration denied in the current phase or by the captured permissions."); return; }
            RestoreBaseline("researcher_demo");
        }

        public bool TryRestoreFromServer(string expectedBaselineId, string expectedRunId)
        {
            if (!ServerManaged || !Application.isPlaying || !isActiveAndEnabled || changing || gate == null
                || expectedBaselineId != baselineId || expectedRunId != runId || !gate.CanRestore(IsHeld))
            { Reject("Server restoration denied by the local identity/phase guard."); return false; }
            return RestoreBaseline("server_command");
        }

        private bool RestoreBaseline(string source)
        {
            if (!SameSetup() || AnyActualSelection() || grabs.Any(g => g.enabled) || companionGrabs.Any(g => g.enabled))
            { Reject("Restoration denied: setup changed, an unexpected grab path is enabled, or object currently held."); return false; }
            changing = true;
            try
            {
                for (int i = 0; i < grabs.Length; i++) grabs[i].enabled = baselineEnabled[i];
                for (int i = 0; i < companionGrabs.Length; i++) companionGrabs[i].enabled = companionBaselineEnabled[i];
                if (!BaselineMatches() || AnyActualSelection()) throw new InvalidOperationException("Restored settings could not be confirmed while the object was released.");
                gate.ConfirmRestore();
            }
            catch (Exception error) { Block("Restoration failed: " + error.Message); return false; }
            finally { changing = false; }
            Emit("baseline_restored", source, "Settings restored; awaiting a NEW same-object Hand Grab selection.");
            return true;
        }

        private void OnDisable()
        {
            if (initialized && gate != null && gate.Phase != RecoveryPhase.Protected && Application.isPlaying)
                Block("Study component disabled during a run. Restart Play; no silent baseline recapture or automatic restoration.");
        }

        private void OnDestroy()
        {
            foreach (var pair in added) if (pair.Key) ((IInteractableView)pair.Key).WhenSelectingInteractorViewAdded -= pair.Value;
            foreach (var pair in removed) if (pair.Key) ((IInteractableView)pair.Key).WhenSelectingInteractorViewRemoved -= pair.Value;
        }

        private void Reject(string message)
        {
            LastMessage = message;
            Debug.LogWarning("[ProtocolRun] " + message, this);
        }

        private void Block(string message)
        {
            gate?.Block();
            Emit("local_manual_review", "local_guard", message);
        }

        public float DistanceTo(Vector3 point)
        {
            if (colliders == null) return float.PositiveInfinity;
            float distance = float.PositiveInfinity;
            foreach (var c in colliders)
                if (c && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger)
                    distance = Mathf.Min(distance, Vector3.Distance(point, c.ClosestPoint(point)));
            return distance;
        }

        public MetaObjectObservation Snapshot()
        {
            bool hasCollider = colliders != null && colliders.Any(c => c && c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger);
            bool protectedUnchanged = initialized && hasCollider && GetComponentsInChildren<Grabbable>(true).Length == 0
                && !GetComponentsInChildren<MonoBehaviour>(true).OfType<IInteractableView>().Any();
            bool baseline = gate != null && gate.Phase != RecoveryPhase.Blocked && (gate.ExpectedGrabbable ? BaselineMatches() : protectedUnchanged);
            return new MetaObjectObservation
            {
                run_id = runId, baseline_id = baselineId,
                object_id = gate == null ? objectId : gate.ObjectId,
                utc = DateTime.UtcNow.ToString("O"),
                monotonic_seconds = Time.realtimeSinceStartupAsDouble,
                expected_grabbable = gate != null && gate.ExpectedGrabbable,
                restore_allowed = gate != null && gate.RestoreAllowed, held = IsHeld,
                hand_grab_count = grabs == null ? 0 : grabs.Length,
                enabled_hand_grab_count = grabs == null ? 0 : grabs.Count(g => g && g.enabled),
                companion_grab_count = companionGrabs == null ? 0 : companionGrabs.Length,
                enabled_companion_grab_count = companionGrabs == null ? 0 : companionGrabs.Count(g => g && g.enabled),
                phase = Phase, collider_enabled = hasCollider, baseline_match = baseline
            };
        }

        private void Emit(string kind, string source, string detail)
        {
            LastMessage = detail;
            var observation = Snapshot();
            observation.event_id = Guid.NewGuid().ToString("N"); observation.event_type = kind;
            observation.source = source; observation.detail = detail; observation.sequence = ++sequence;
            Debug.Log("[ProtocolRun] " + JsonUtility.ToJson(observation), this);
            // One failing future subscriber must not break the SDK's selection callbacks.
            if (Observed != null)
                foreach (Action<MetaObjectObservation> subscriber in Observed.GetInvocationList())
                    try { subscriber(observation); } catch (Exception error) { Debug.LogException(error, this); }
        }
    }
}
