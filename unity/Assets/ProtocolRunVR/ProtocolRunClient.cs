using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace ProtocolRunVR
{
    [Serializable] public class EventData
    {
        public string object_id = "", text = "";
        public int observed_mask, interactor_mask, difficulty;
        public bool tracked, collider_enabled, near_target, input_received;
        public float fps, head_x, head_y, head_z, left_x, left_y, left_z, right_x, right_y, right_z;
    }
    [Serializable] public class StudyEvent
    {
        public string event_id, occurred_at, kind;
        public int seq;
        public EventData data;
    }
    [Serializable] public class Batch { public List<StudyEvent> events = new List<StudyEvent>(); }
    [Serializable] public class Step { public string id, instruction; }
    [Serializable] public class Protocol { public string id, target_id; public int baseline_mask; }
    [Serializable] public class Session
    {
        public string id, status, protocol_hash;
        public int step, progress, last_seq, revision;
        public Step current_step;
        public Protocol protocol;
    }
    [Serializable] public class Command
    {
        public string id, action, object_id, status;
        public int baseline_mask, step;
        public double expires_at;
    }
    [Serializable] public class Ack { public bool success; public int observed_mask; public string message = ""; }
    [Serializable] public class Envelope
    {
        public Session session;
        public List<Command> commands;
        public List<string> accepted_ids;
        public int last_seq;
    }
    [Serializable] public class AckRecord { public string command_id; public Ack ack; }
    [Serializable] public class DiskState
    {
        public int next_seq = 1;
        public string protocol_hash = "";
        public List<StudyEvent> pending = new List<StudyEvent>();
        public List<AckRecord> acknowledgements = new List<AckRecord>();
    }
    [Serializable] public class TextEvent : UnityEvent<string> { }

    public class ProtocolRunClient : MonoBehaviour
    {
        [Header("Session credentials — no Google or researcher key here")]
        public string serverUrl = "http://127.0.0.1:8080";
        public string sessionId = "";
        [SerializeField] private string sessionToken = "";
        public XriStudyAdapter adapter;
        public TextEvent onInstruction = new TextEvent();
        public TextEvent onConnectionStatus = new TextEvent();
        public Session Current { get; private set; }
        public bool Connected { get; private set; }
        public bool Blocked { get; private set; }
        private DiskState disk;
        private string path;
        private bool uploading;

        private void Start()
        {
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            { Block("HTTPS required outside localhost"); return; }
            if (string.IsNullOrWhiteSpace(sessionToken) || sessionId.Length != 32)
            { Block("Enter session ID and session token from the dashboard"); return; }
            foreach (char c in sessionId) if (!Uri.IsHexDigit(c)) { Block("Invalid session ID"); return; }
            path = Path.Combine(Application.persistentDataPath, "prvr-" + sessionId + ".json");
            try
            {
                disk = File.Exists(path) ? JsonUtility.FromJson<DiskState>(File.ReadAllText(path)) : new DiskState();
                if (disk == null || disk.pending == null || disk.acknowledgements == null) throw new Exception("Invalid buffer");
            }
            catch (Exception) { Block("Offline buffer unreadable. Preserve file and create a new session."); return; }
            StartCoroutine(Initialize());
        }

        private IEnumerator Initialize()
        {
            while (!Blocked && Current == null)
            {
                yield return Request("GET", "/client", null, body =>
                {
                    var s = JsonUtility.FromJson<Session>(body);
                    if (!string.IsNullOrEmpty(disk.protocol_hash) && disk.protocol_hash != s.protocol_hash)
                    { Block("Protocol changed. Create a new session."); return; }
                    if (disk.pending.Count == 0) disk.next_seq = s.last_seq + 1;
                    disk.protocol_hash = s.protocol_hash;
                    Save(); Apply(s);
                    if (adapter == null || !adapter.Configure(s.protocol)) Block("Check target, practice object and both interactor references. Target baseline must match protocol.");
                });
                if (Current == null) yield return new WaitForSecondsRealtime(2);
            }
            if (Blocked) yield break;
            StartCoroutine(FlushLoop());
            StartCoroutine(PollLoop());
            StartCoroutine(AgentLoop());
        }

        public void Emit(string kind, EventData data = null)
        {
            if (Blocked || Current == null || disk == null) return;
            if (disk.pending.Count >= 2000) { Block("Offline event buffer full. Stop and reconnect; no events were discarded."); return; }
            disk.pending.Add(new StudyEvent { event_id = Guid.NewGuid().ToString("N"), seq = disk.next_seq++,
                occurred_at = DateTime.UtcNow.ToString("o"), kind = kind, data = data ?? new EventData() });
            Save();
        }

        public void HelpGrab() { Emit("help_request", new EventData { text = "물건이 잘 안 잡혀요" }); }
        public void HelpInstructions() { Emit("help_request", new EventData { text = "무엇을 해야 할지 모르겠어요" }); }
        public void PauseStudy() { Emit("pause_request"); adapter?.SetStudyPaused(true); }
        public void SubmitSurvey(int difficulty, string response)
        { if (Current != null && Current.step == 5) Emit("survey_completed", new EventData { difficulty = difficulty, text = response ?? "" }); }

        private IEnumerator FlushLoop()
        {
            while (!Blocked)
            {
                if (disk.pending.Count > 0)
                {
                    uploading = true;
                    var batch = new Batch { events = disk.pending.GetRange(0, Math.Min(25, disk.pending.Count)) };
                    yield return Request("POST", "/events", JsonUtility.ToJson(batch), body =>
                    {
                        var r = JsonUtility.FromJson<Envelope>(body);
                        var accepted = new HashSet<string>(r.accepted_ids ?? new List<string>());
                        disk.pending.RemoveAll(e => accepted.Contains(e.event_id)); Save(); Apply(r.session);
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
                Envelope envelope = null;
                yield return Request("GET", "/commands", null, body => envelope = JsonUtility.FromJson<Envelope>(body));
                if (envelope != null)
                {
                    Apply(envelope.session);
                    foreach (var cmd in envelope.commands ?? new List<Command>())
                    {
                        if (Blocked) break;
                        // Drain previous observations before opening the retest evidence window.
                        if (cmd.action == "retest" && (disk.pending.Count > 0 || uploading)) continue;
                        var cached = disk.acknowledgements.Find(a => a.command_id == cmd.id);
                        Ack ack;
                        if (cached != null) ack = cached.ack;
                        else
                        {
                            bool valid = cmd.object_id == Current.protocol.target_id && cmd.step == Current.step &&
                                cmd.baseline_mask == Current.protocol.baseline_mask && cmd.expires_at > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                            ack = valid ? adapter.Execute(cmd) : new Ack { success = false, message = "Local command firewall rejected command" };
                            disk.acknowledgements.Add(new AckRecord { command_id = cmd.id, ack = ack }); Save();
                        }
                        yield return Request("POST", "/commands/" + cmd.id + "/ack", JsonUtility.ToJson(ack), body =>
                        {
                            var response = JsonUtility.FromJson<Envelope>(body); Apply(response.session);
                        });
                    }
                }
                yield return new WaitForSecondsRealtime(1);
            }
        }

        private IEnumerator AgentLoop()
        {
            while (!Blocked)
            {
                // Awaited server work continues even when the researcher's browser is closed.
                yield return Request("POST", "/tick", "{}", _ => { }, true);
                yield return new WaitForSecondsRealtime(3);
            }
        }

        private IEnumerator Request(string method, string suffix, string json, Action<string> success, bool agent = false)
        {
            using (var req = new UnityWebRequest(serverUrl.TrimEnd('/') + "/api/sessions/" + sessionId + suffix, method))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                if (json != null) { req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)); req.SetRequestHeader("Content-Type", "application/json"); }
                req.SetRequestHeader("Authorization", "Bearer " + sessionToken);
                req.timeout = agent ? 150 : 15;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    Connected = true; onConnectionStatus.Invoke("Connected");
                    try { success(req.downloadHandler.text); }
                    catch (Exception) { Block("Unexpected server response; check SDK/API version."); }
                }
                else if (req.responseCode == 401 || req.responseCode == 409 || req.responseCode == 422 || req.responseCode == 429)
                { Block("Request rejected (" + req.responseCode + "). Preserve offline buffer and check server/session settings."); }
                else if (!agent)
                { Connected = false; onConnectionStatus.Invoke("Disconnected · buffering locally"); }
                else onConnectionStatus.Invoke("Agent unavailable · no automatic recovery");
            }
        }

        private void Apply(Session s)
        {
            if (s == null || (Current != null && s.revision < Current.revision)) return;
            Current = s;
            adapter?.SetStudyPaused(Blocked || s.status == "recovering" || s.status == "manual_review" || s.status == "completed");
            string instruction = s.status == "manual_review" ? "실험을 잠시 멈췄습니다. 연구자에게 알려주세요." :
                s.status == "recovering" ? "장비 상태를 확인하고 있습니다. 잠시 기다려주세요." : s.current_step.instruction;
            onInstruction.Invoke(instruction);
        }

        private void Save()
        {
            if (disk == null || string.IsNullOrEmpty(path)) return;
            try
            {
                var tmp = path + ".tmp"; File.WriteAllText(tmp, JsonUtility.ToJson(disk));
                if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
            }
            catch (Exception) { Block("Could not persist offline events. Study stopped; check disk permissions."); }
        }

        private void Block(string reason)
        { Blocked = true; Connected = false; adapter?.SetStudyPaused(true); onConnectionStatus.Invoke(reason); Debug.LogError("[ProtocolRun] " + reason); }
    }
}
