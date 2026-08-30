"""Local SQLite or transactional Firestore; raw events are append-only through this API."""
import copy
import json
import sqlite3
import threading


def fresh_events(incoming, existing):
    fresh = []
    seen = dict(existing)
    for event in incoming:
        prior = seen.get(event["event_id"])
        if prior is not None:
            if prior != event:
                raise ValueError("event_id_conflict")
        else:
            fresh.append(event)
            seen[event["event_id"]] = event
    return fresh


class SQLiteStore:
    def __init__(self, path="protocolrun.sqlite3"):
        self.lock = threading.RLock()
        self.db = sqlite3.connect(path, check_same_thread=False)
        self.db.execute("PRAGMA journal_mode=WAL")
        self.db.executescript("""
            CREATE TABLE IF NOT EXISTS records (bucket TEXT, id TEXT, body TEXT, PRIMARY KEY(bucket,id));
            CREATE TABLE IF NOT EXISTS events (sid TEXT, id TEXT, seq INTEGER, body TEXT, PRIMARY KEY(sid,id));
            CREATE TABLE IF NOT EXISTS audits (sid TEXT, id TEXT, body TEXT, PRIMARY KEY(sid,id));
        """)

    def get(self, bucket, key):
        with self.lock:
            row = self.db.execute("SELECT body FROM records WHERE bucket=? AND id=?", (bucket, key)).fetchone()
            return json.loads(row[0]) if row else None

    def create(self, bucket, key, body):
        with self.lock, self.db:
            self.db.execute("INSERT INTO records VALUES (?,?,?)", (bucket, key, json.dumps(body)))

    def list(self, bucket):
        with self.lock:
            return [json.loads(r[0]) for r in self.db.execute("SELECT body FROM records WHERE bucket=? ORDER BY rowid DESC LIMIT 100", (bucket,))]

    def update(self, sid, fn, events=None):
        incoming = events or []
        with self.lock:
            try:
                self.db.execute("BEGIN IMMEDIATE")
                s = self.get("sessions", sid)
                if s is None:
                    raise KeyError(sid)
                old_audit = {a["id"] for a in s["audit"]}
                known = {}
                for event in incoming:
                    row = self.db.execute("SELECT body FROM events WHERE sid=? AND id=?", (sid, event["event_id"])).fetchone()
                    if row:
                        known[event["event_id"]] = json.loads(row[0])["original"]
                fresh = fresh_events(incoming, known)
                fn(s, fresh)
                s["revision"] += 1
                for e in fresh:
                    enriched = next(x for x in s["recent"] if x["event_id"] == e["event_id"])
                    self.db.execute("INSERT INTO events VALUES (?,?,?,?)", (sid, e["event_id"], e["seq"], json.dumps({"original": e, "record": enriched})))
                for a in s["audit"]:
                    if a["id"] not in old_audit:
                        self.db.execute("INSERT INTO audits VALUES (?,?,?)", (sid, a["id"], json.dumps(a)))
                self.db.execute("UPDATE records SET body=? WHERE bucket='sessions' AND id=?", (json.dumps(s), sid))
                self.db.commit()
                return copy.deepcopy(s)
            except Exception:
                self.db.rollback()
                raise

    def events(self, sid):
        with self.lock:
            return [json.loads(r[0])["record"] for r in self.db.execute("SELECT body FROM events WHERE sid=? ORDER BY seq", (sid,))]

    def audits(self, sid):
        with self.lock:
            return [json.loads(r[0]) for r in self.db.execute("SELECT body FROM audits WHERE sid=? ORDER BY rowid", (sid,))]


class FirestoreStore:
    def __init__(self, project, database="(default)"):
        from google.cloud import firestore
        self.fs = firestore
        self.db = firestore.Client(project=project, database=database)

    def ref(self, bucket, key):
        return self.db.collection("prvr_" + bucket).document(key)

    def get(self, bucket, key):
        snap = self.ref(bucket, key).get()
        return snap.to_dict() if snap.exists else None

    def create(self, bucket, key, body):
        self.ref(bucket, key).create(body)

    def list(self, bucket):
        return [s.to_dict() for s in self.db.collection("prvr_" + bucket).limit(100).stream()]

    def update(self, sid, fn, events=None):
        incoming = events or []
        ref = self.ref("sessions", sid)

        @self.fs.transactional
        def run(tx):
            snap = ref.get(transaction=tx)
            if not snap.exists:
                raise KeyError(sid)
            s = snap.to_dict()
            old_audit = {a["id"] for a in s["audit"]}
            known = {}
            # Firestore requires all reads before the first write.
            for e in incoming:
                ev = ref.collection("events").document(e["event_id"]).get(transaction=tx)
                if ev.exists:
                    known[e["event_id"]] = ev.to_dict()["original"]
            fresh = fresh_events(incoming, known)
            fn(s, fresh)
            s["revision"] += 1
            for e in fresh:
                enriched = next(x for x in s["recent"] if x["event_id"] == e["event_id"])
                tx.create(ref.collection("events").document(e["event_id"]), {"original": e, "record": enriched, "seq": e["seq"]})
            for a in s["audit"]:
                if a["id"] not in old_audit:
                    tx.create(ref.collection("audits").document(a["id"]), a)
            tx.set(ref, s)
            return s

        return run(self.db.transaction())

    def events(self, sid):
        return [x.to_dict()["record"] for x in self.ref("sessions", sid).collection("events").order_by("seq").stream()]

    def audits(self, sid):
        return [x.to_dict() for x in self.ref("sessions", sid).collection("audits").order_by("at").stream()]
