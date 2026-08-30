using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Oculus.Interaction.Input;
using UnityEngine;
using UnityEngine.Networking;

namespace ProtocolRunVR.MetaHands
{
    [DisallowMultipleComponent]
    [AddComponentMenu("ProtocolRun VR/Meta Network Session")]
    public sealed class ProtocolRunMetaSession : MonoBehaviour
    {
        [Tooltip("Optional explicit Meta IHand components. Auto-discovery uses exactly one concrete Hand per side.")]
        public MonoBehaviour leftHandSource, rightHandSource;
        public Transform head;
        public ProtocolRunMetaObject[] studyObjects;
        public MetaStudyHud hud;
        public MetaSessionState Current { get; private set; }
        public bool Consented { get; private set; }
        public bool Blocked { get; private set; }
        public bool Connected { get; private set; }
        public bool StudyPaused => Blocked || Current == null || !Consented || Current.status == "recovering" || Current.status == "manual_review" || Current.status == "completed";
        public string ConnectionStatus { get; private set; } = "Configure connection before Play.";
        public string Instruction => Blocked ? ConnectionStatus : Current == null ? "Waiting for server connection." :
            !Consented ? "Demo consent: upload anonymous hand poses, interactions and survey. No audio/video. Pinch near AGREE to begin." :
            Current.status == "recovering" ? "Release all objects. Checking equipment. Please wait." :
            Current.status == "manual_review" ? "Study paused. Please contact the researcher." : Current.current_step.instruction;
        public static string ConnectionPath => Path.Combine(Application.persistentDataPath, "ProtocolRunVR", "connection.json");
        public int PendingCount => disk == null ? 0 : disk.pending.Count;
        public bool HasTrackedHand => leftTracked || rightTracked;
        public bool CanInjectFault => !StudyPaused && Current.protocol.demo_faults_allowed && (Current.step == 2 || Current.step == 3);
        private MetaConnection connection;
        private MetaDiskState disk;
        private IHand left, right;
        private Pose leftPose, rightPose;
        private bool leftTracked, rightTracked, leftPinched = true, rightPinched = true;
        private bool uploading, networkFatal, equipmentSent, practiceSent, foundSent;
        private readonly HashSet<string> practiced = new HashSet<string>();
        private readonly HashSet<string> practiceHeld = new HashSet<string>();
        private string path, journalPath, restoreCommandId = "", retestCommandId = "";
        private float telemetryAt, networkRttMs;

        private IEnumerator Start()
        {
            yield return null;
            yield return null;
            if (!head && Camera.main) head = Camera.main.transform;
            if (studyObjects == null || studyObjects.Length == 0)
                studyObjects = FindObjectsByType<ProtocolRunMetaObject>(FindObjectsSortMode.None);
            var sources = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Where(m => m.GetType() == typeof(Hand)).ToArray();
            if (!leftHandSource) leftHandSource = UniqueHand(sources, Handedness.Left);
            if (!rightHandSource) rightHandSource = UniqueHand(sources, Handedness.Right);
            left = leftHandSource as IHand; right = rightHandSource as IHand;
            if (left == null || right == null || left == right || !head)
            { StopStudy("Assign one left/right Meta Hand source and the HMD camera on Meta Network Session."); yield break; }
            try
            {
                connection = JsonUtility.FromJson<MetaConnection>(File.ReadAllText(ConnectionPath));
                if (connection == null || !ValidConnection(connection)) throw new InvalidOperationException();
                path = Path.Combine(Path.GetDirectoryName(ConnectionPath), "session-" + connection.session_id + ".json");
                journalPath = path + ".events.jsonl";
                // A new Play has new physical baselines. Never silently resume old recovery commands.
                if (File.Exists(path)) throw new InvalidOperationException("previous_run");
                disk = new MetaDiskState { runtime_id = Guid.NewGuid().ToString("N") };
            }
            catch (Exception)
            { StopStudy("Configure a NEW dashboard session using Tools > ProtocolRun VR > Configure Connection. Existing run journals are retained."); yield break; }
            while (!Blocked && Current == null)
            {
                yield return Request("GET", "/client", null, body =>
                {
                    var state = JsonUtility.FromJson<MetaSessionState>(body);
                    if (state == null || state.protocol == null || state.protocol.adapter != "meta_hands" || state.last_seq != 0
                        || studyObjects.Length != 3 || studyObjects.Any(o => !o || !o.Initialized || o.IsHeld || !o.Snapshot().baseline_match))
                    { StopStudy("Use a NEW meta-hands-v1 session with three released, healthy study objects."); return; }
                    var ids = new HashSet<string>(studyObjects.Select(o => o.ObjectId));
                    if (!ids.SetEquals(new[] { state.protocol.target_id, state.protocol.practice_id, state.protocol.protected_id }))
                    { StopStudy("Object IDs differ from the server protocol."); return; }
                    foreach (var o in studyObjects)
                    {
                        var d = o.Snapshot();
                        if (d.expected_grabbable != (o.ObjectId != state.protocol.protected_id) || d.restore_allowed != (o.ObjectId == state.protocol.target_id))
                        { StopStudy("Object roles/restore permissions differ from the protocol."); return; }
                    }
                    disk.protocol_hash = state.protocol_hash;
                    Apply(state); Save();
                    foreach (var o in studyObjects)
                    { o.ServerManaged = true; o.ServerFaultAllowed = () => CanInjectFault; o.Observed += OnObservation; }
                });
                if (Current == null) yield return new WaitForSecondsRealtime(2);
            }
            if (Blocked) yield break;
            StartCoroutine(FlushLoop()); StartCoroutine(PollLoop()); StartCoroutine(AgentLoop());
        }

        private static MonoBehaviour UniqueHand(MonoBehaviour[] sources, Handedness side)
        {
            var matches = sources.Where(m => ((IHand)m).Handedness == side).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }
        public static bool ValidConnection(MetaConnection c)
        {
            if (c == null || !Uri.TryCreate(c.server_url, UriKind.Absolute, out var uri)
                || !(uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback))
                || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/") return false;
            return c.session_id != null && c.session_id.Length == 32 && c.session_id.All(Uri.IsHexDigit)
                && c.session_token != null && c.session_token.Length >= 32;
        }
        private static bool ReadHand(IHand hand, out Pose pose)
        {
            pose = Pose.identity;
            return hand != null && hand.IsConnected && hand.IsTrackedDataValid && hand.IsHighConfidence && hand.GetRootPose(out pose);
        }

        private static bool ReadInteractionPoint(IHand hand, out Pose pose)
        {
            pose = Pose.identity;
            if (hand == null || !hand.IsConnected || !hand.IsTrackedDataValid || !hand.IsHighConfidence) return false;
            // UI presses and near-object pinch attempts happen at the fingertip, not at the wrist.
            // Fall back to the tracked root only when the index-tip joint is temporarily unavailable.
            return hand.GetJointPose(HandJointId.HandIndexTip, out pose) || hand.GetRootPose(out pose);
        }
        private void Update()
        {
            leftTracked = ReadHand(left, out leftPose); rightTracked = ReadHand(right, out rightPose);
            ProcessHand(left, leftTracked, leftPose, "left", ref leftPinched);
            ProcessHand(right, rightTracked, rightPose, "right", ref rightPinched);
            if (Blocked || Current == null || !Consented) return;
            if (!StudyPaused && Current.step == 0 && leftTracked && rightTracked && !equipmentSent)
            { equipmentSent = true; Emit("equipment_ready", new MetaEventData { left_tracked = true, right_tracked = true }); }
            if (!StudyPaused && Current.step == 1 && !practiceSent && practiced.Contains(Current.protocol.practice_id) && practiced.Contains(Current.protocol.target_id))
            { practiceSent = true; Emit("practice_completed"); }
            var target = Target;
            if (!StudyPaused && Current.step == 2 && !foundSent && target)
            {
                bool near = (leftTracked && target.DistanceTo(leftPose.position) <= Current.protocol.near_distance_m)
                    || (rightTracked && target.DistanceTo(rightPose.position) <= Current.protocol.near_distance_m);
                if (near) { foundSent = true; var d = Data(target); d.tracked = true; d.near_target = true; d.source = "hand_proximity"; Emit("target_found", d); }
            }
            if (Time.realtimeSinceStartup >= telemetryAt && Current.status != "completed")
            {
                var protectedObject = studyObjects.First(o => o.ObjectId == Current.protocol.protected_id);
                if (!protectedObject.Snapshot().baseline_match)
                { Emit("client_error", new MetaEventData { text = "Protected object configuration changed." }); StopStudy("Protected cube changed. Researcher review required."); return; }
                telemetryAt = Time.realtimeSinceStartup + 1;
                var d = Data(target); d.fps = Time.unscaledDeltaTime > 0 ? 1f / Time.unscaledDeltaTime : 0;
                d.left_tracked = leftTracked; d.right_tracked = rightTracked; d.network_rtt_ms = networkRttMs;
                d.head_position = head.position; d.head_rotation = head.rotation;
                if (leftTracked) { d.left_position = leftPose.position; d.left_rotation = leftPose.rotation; }
                if (rightTracked) { d.right_position = rightPose.position; d.right_rotation = rightPose.rotation; }
                Emit("telemetry", d);
            }
        }
        private void ProcessHand(IHand hand, bool tracked, Pose pose, string side, ref bool previous)
        {
            if (!tracked) { previous = true; return; }
            bool pinched = hand.GetIndexFingerIsPinching();
            bool rising = pinched && !previous; previous = pinched;
            if (!rising || Blocked) return;
            if (!ReadInteractionPoint(hand, out var interactionPose)) return;
            if (hud && hud.TryPinch(interactionPose.position)) return;
            if (StudyPaused || (Current.step != 3 && Current.step != 1)) return;
            ProtocolRunMetaObject nearest = null; float distance = Current.protocol.near_distance_m;
            foreach (var o in studyObjects)
            { float d = o.DistanceTo(interactionPose.position); if (d <= distance) { distance = d; nearest = o; } }
            if (!nearest) return;
            if (!nearest.Snapshot().expected_grabbable)
            { var protectedData = Data(nearest); protectedData.source = "pinch_proximity"; Emit("non_grabbable_attempt", protectedData); return; }
            if (nearest.IsHeld) return;
            string attempt = Guid.NewGuid().ToString("N");
            var data = AttemptData(nearest, side, attempt, distance, true);
            Emit("grab_attempt", data);
            StartCoroutine(ObserveAttempt(nearest, hand, side, attempt, nearest.GrabVersion));
        }
        private IEnumerator ObserveAttempt(ProtocolRunMetaObject o, IHand hand, string side, string attempt, int startVersion)
        {
            float until = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < until)
            {
                if (StudyPaused || !ReadInteractionPoint(hand, out _) || o.GrabVersion != startVersion || o.IsHeld) yield break;
                yield return null;
            }
            if (!ReadInteractionPoint(hand, out var pose) || o.DistanceTo(pose.position) > Current.protocol.near_distance_m) yield break;
            Emit("grab_failed", AttemptData(o, side, attempt, o.DistanceTo(pose.position), true));
        }
        private MetaEventData AttemptData(ProtocolRunMetaObject o, string side, string attempt, float distance, bool tracked)
        {
            var d = Data(o); d.attempt_id = attempt; d.hand = side; d.distance_m = distance;
            d.tracked = tracked; d.near_target = true; d.input_received = true; d.source = "pinch_proximity"; return d;
        }
        public ProtocolRunMetaObject Target => Current == null || studyObjects == null ? null : studyObjects.FirstOrDefault(o => o && o.ObjectId == Current.protocol.target_id);
        public MetaEventData Data(ProtocolRunMetaObject o)
        {
            var d = o ? JsonUtility.FromJson<MetaEventData>(JsonUtility.ToJson(o.Snapshot())) : new MetaEventData();
            d.tracked = leftTracked || rightTracked; d.restore_command_id = restoreCommandId; d.retest_command_id = retestCommandId;
            return d;
        }
        private void OnObservation(MetaObjectObservation observation)
        {
            if (!Consented || Blocked) return;
            var d = JsonUtility.FromJson<MetaEventData>(JsonUtility.ToJson(observation));
            d.tracked = leftTracked || rightTracked; d.restore_command_id = restoreCommandId; d.retest_command_id = retestCommandId;
            string kind = observation.event_type;
            if (kind == "local_manual_review") { Emit("client_error", d); StopStudy(observation.detail); return; }
            if (!new[] { "grab_success", "released", "fault_injected" }.Contains(kind)) kind = "object_observation";
            Emit(kind, d);
            if (Current.step == 1 && !StudyPaused)
            {
                if (kind == "grab_success" && d.tracked && d.baseline_match) practiceHeld.Add(d.object_id);
                if (kind == "released" && practiceHeld.Contains(d.object_id) && d.baseline_match) practiced.Add(d.object_id);
            }
        }
        public void AcceptConsent()
        {
            if (Blocked || Current == null || Consented) return;
            if (studyObjects.Any(o => o.IsHeld || !o.Snapshot().baseline_match)) return;
            Consented = true;
            Emit("consent", new MetaEventData { accepted = true, version = "demo-consent-v1", source = "participant_button" });
            foreach (var o in studyObjects) Emit("object_registered", Data(o));
        }
        public void HelpGrab() { if (Consented) Emit("help_request", new MetaEventData { object_id = Current.protocol.target_id, text = "I cannot pick up the object.", source = "participant_button" }); }
        public void PauseStudy() { if (Consented) { Emit("pause_request", new MetaEventData { source = "participant_button" }); StopStudy("Participant requested a pause. Researcher review required."); } }
        public void InjectDemoFault() { if (CanInjectFault) Target?.InjectDemoFault(); }
        public void SubmitSurvey(int difficulty, string text = "")
        { if (!StudyPaused && Current.step == 5 && difficulty >= 1 && difficulty <= 7 && (text ?? "").Length <= 1000) Emit("survey_completed", new MetaEventData { difficulty = difficulty, text = text ?? "", source = "participant_button" }); }
        public void Emit(string kind, MetaEventData data = null)
        {
            if (Blocked || !Consented || Current == null || disk == null) return;
            if (disk.pending.Count >= 2000) { StopStudy("Offline buffer full. Stop and preserve the journal; no events discarded."); return; }
            var e = new MetaEvent { event_id = Guid.NewGuid().ToString("N"), seq = disk.next_seq++, occurred_at = DateTime.UtcNow.ToString("O"), kind = kind, data = data ?? new MetaEventData() };
            disk.pending.Add(e);
            try { File.AppendAllText(journalPath, JsonUtility.ToJson(e) + "\n"); }
            catch (Exception) { StopStudy("Cannot write event journal. Check disk permissions."); }
            Save();
        }
        private IEnumerator FlushLoop()
        {
            while (!networkFatal)
            {
                if (disk.pending.Count > 0)
                {
                    uploading = true;
                    var batch = new MetaBatch { events = disk.pending.GetRange(0, Math.Min(25, disk.pending.Count)) };
                    yield return Request("POST", "/events", JsonUtility.ToJson(batch), body =>
                    {
                        var result = JsonUtility.FromJson<MetaEnvelope>(body);
                        var accepted = new HashSet<string>(result.accepted_ids ?? new List<string>());
                        disk.pending.RemoveAll(e => accepted.Contains(e.event_id)); Save(); Apply(result.session);
                    });
                    uploading = false;
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }
        private IEnumerator PollLoop()
        {
            while (!Blocked)
            {
                MetaEnvelope envelope = null;
                yield return Request("GET", "/commands", null, body => envelope = JsonUtility.FromJson<MetaEnvelope>(body));
                if (envelope != null)
                {
                    Apply(envelope.session);
                    // ACK may have reached the server even if its response was lost.
                    // Replay the saved ACK, never execute its actuator a second time.
                    foreach (var unconfirmed in disk.acknowledgements.Where(a => !a.confirmed).ToArray())
                    { if (!Blocked) yield return ConfirmAck(unconfirmed); }
                    foreach (var cmd in envelope.commands ?? new List<MetaCommand>())
                    {
                        if (Blocked) break;
                        var cached = disk.acknowledgements.Find(a => a.command_id == cmd.id);
                        if (cached == null && !HasTrackedHand) continue;
                        if (cached == null && studyObjects.Any(o => o.IsHeld)) continue;
                        if (cached == null && cmd.action == "retest" && (disk.pending.Count > 0 || uploading)) continue;
                        if (cached == null)
                        {
                            var snap = Target.Snapshot();
                            bool valid = cmd.object_id == Current.protocol.target_id && cmd.step == 3 && Current.step == 3
                                && Current.status == "recovering" && cmd.protocol_hash == disk.protocol_hash
                                && cmd.baseline_id == snap.baseline_id && cmd.run_id == snap.run_id
                                && cmd.expires_at > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            var ack = new MetaAck { success = false, baseline_id = snap.baseline_id, run_id = snap.run_id, held = snap.held };
                            if (valid)
                            {
                                if (cmd.action == "pause") ack.success = true;
                                else if (cmd.action == "restore_hand_grab_baseline" && Current.protocol.allowed_actions.Contains(cmd.action))
                                { ack.success = Target.TryRestoreFromServer(cmd.baseline_id, cmd.run_id); if (ack.success) restoreCommandId = cmd.id; }
                                else if (cmd.action == "retest") ack.success = snap.baseline_match && !snap.held && !string.IsNullOrEmpty(restoreCommandId);
                            }
                            var after = Target.Snapshot(); ack.baseline_match = after.baseline_match; ack.held = after.held;
                            ack.message = ack.success ? "Local guarded command executed." : "Local command guard denied execution.";
                            cached = new MetaAckRecord { command_id = cmd.id, action = cmd.action, ack = ack };
                            disk.acknowledgements.Add(cached); Save();
                        }
                        if (Blocked) break;
                        yield return ConfirmAck(cached);
                    }
                }
                yield return new WaitForSecondsRealtime(1);
            }
        }
        private IEnumerator ConfirmAck(MetaAckRecord record)
        {
            yield return Request("POST", "/commands/" + record.command_id + "/ack", JsonUtility.ToJson(record.ack), body =>
            {
                Apply(JsonUtility.FromJson<MetaEnvelope>(body).session);
                record.confirmed = true; Save();
                if (record.action == "retest" && record.ack.success && Current.status == "retest") retestCommandId = record.command_id;
            });
        }
        private IEnumerator AgentLoop()
        {
            while (!Blocked)
            { yield return Request("POST", "/tick", "{}", _ => { }, true); yield return new WaitForSecondsRealtime(3); }
        }
        private IEnumerator Request(string method, string suffix, string json, Action<string> success, bool agent = false)
        {
            using (var req = new UnityWebRequest(connection.server_url.TrimEnd('/') + "/api/sessions/" + connection.session_id + suffix, method))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                if (json != null) { req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)); req.SetRequestHeader("Content-Type", "application/json"); }
                req.SetRequestHeader("Authorization", "Bearer " + connection.session_token); req.timeout = agent ? 65 : 15;
                double started = Time.realtimeSinceStartupAsDouble;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    if (!agent && !Blocked) { Connected = true; networkRttMs = (float)((Time.realtimeSinceStartupAsDouble - started) * 1000); ConnectionStatus = "API connected"; }
                    try { success(req.downloadHandler.text); }
                    catch (Exception) { StopStudy("Unexpected response. Check API/SDK versions."); }
                }
                else if (req.responseCode == 401 || req.responseCode == 409 || req.responseCode == 422 || req.responseCode == 429)
                { networkFatal = true; StopStudy("Server rejected request (" + req.responseCode + "). Preserve local journal; check session and protocol."); }
                else if (!agent && !Blocked) { Connected = false; ConnectionStatus = "Disconnected: buffering locally"; }
                else if (!Blocked) ConnectionStatus = "Agent unavailable: no automatic recovery. Check server logs.";
            }
        }
        private void Apply(MetaSessionState s)
        {
            if (s == null || (Current != null && s.revision < Current.revision)) return;
            if (s.id != connection.session_id || (disk.protocol_hash != null && disk.protocol_hash != s.protocol_hash))
            { StopStudy("Session/protocol identity changed."); return; }
            Current = s;
        }
        private void Save()
        {
            if (disk == null || string.IsNullOrEmpty(path)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp"; File.WriteAllText(tmp, JsonUtility.ToJson(disk));
                if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
            }
            catch (Exception) { StopStudy("Cannot save offline queue. Preserve current run; check disk permissions."); }
        }
        private void StopStudy(string reason)
        { Blocked = true; Connected = false; ConnectionStatus = reason; Debug.LogError("[ProtocolRun] " + reason, this); }
        private void OnDestroy()
        { if (studyObjects != null) foreach (var o in studyObjects) if (o) { o.Observed -= OnObservation; o.ServerFaultAllowed = null; } }
    }
}
