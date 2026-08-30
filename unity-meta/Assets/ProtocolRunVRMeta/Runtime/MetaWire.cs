using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProtocolRunVR.MetaHands
{
    [Serializable] public sealed class MetaConnection
    {
        public string server_url, session_id, session_token;
    }
    [Serializable] public sealed class MetaEventData : MetaObjectObservation
    {
        public string text = "", attempt_id = "", hand = "", version = "";
        public string restore_command_id = "", retest_command_id = "";
        public bool tracked, left_tracked, right_tracked, near_target, input_received, inside_drop_zone, accepted;
        public float distance_m, fps, network_rtt_ms, settled_seconds;
        public int difficulty;
        public Vector3 head_position, left_position, right_position;
        public Quaternion head_rotation, left_rotation, right_rotation;
    }
    [Serializable] public sealed class MetaEvent
    {
        public string event_id, occurred_at, kind;
        public int seq;
        public MetaEventData data;
    }
    [Serializable] public sealed class MetaBatch { public List<MetaEvent> events = new List<MetaEvent>(); }
    [Serializable] public sealed class MetaProtocol
    {
        public string id, adapter, target_id, practice_id, protected_id;
        public string[] allowed_actions;
        public bool demo_faults_allowed, auto_inject_target_fault;
        public float near_distance_m;
    }
    [Serializable] public sealed class MetaStep { public string id, instruction; }
    [Serializable] public sealed class MetaSessionState
    {
        public string id, status, protocol_hash;
        public int step, progress, revision, last_seq;
        public bool agent_busy;
        public MetaStep current_step;
        public MetaProtocol protocol;
    }
    [Serializable] public sealed class MetaCommand
    {
        public string id, action, object_id, status, baseline_id, run_id, protocol_hash;
        public int step;
        public double expires_at;
    }
    [Serializable] public sealed class MetaAck
    {
        public bool success, baseline_match, held;
        public string message = "", baseline_id = "", run_id = "";
    }
    [Serializable] public sealed class MetaEnvelope
    {
        public MetaSessionState session;
        public List<MetaCommand> commands;
        public List<string> accepted_ids;
    }
    [Serializable] public sealed class MetaAckRecord { public string command_id, action; public MetaAck ack; public bool confirmed; }
    [Serializable] public sealed class MetaDiskState
    {
        public string protocol_hash, runtime_id;
        public int next_seq = 1;
        public List<MetaEvent> pending = new List<MetaEvent>();
        public List<MetaAckRecord> acknowledgements = new List<MetaAckRecord>();
    }
}
