"use client";
import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { Activity, ArrowDownToLine, ArrowRight, Check, Circle, Copy, Link2, Loader2, Plus, Radio, ShieldCheck, Unplug, Workflow } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Empty, EmptyDescription, EmptyHeader, EmptyTitle } from "@/components/ui/empty";
import { Progress } from "@/components/ui/progress";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";

type Data = Record<string, string | number | boolean>;
type EventRecord = { event_id: string; seq: number; kind: string; received_at: number; data: Data };
type Audit = { id: string; kind: string; at: number; detail: Record<string, unknown> };
type Protocol = { id: string; title: string; target_id: string; adapter: string; practice_id: string; protected_id: string; demo_faults_allowed: boolean; auto_inject_target_fault: boolean; near_distance_m: number; baseline_mask: number; max_recoveries: number; failure_threshold: number; allowed_actions: string[] };
type Session = { id: string; created_at: number; last_seen: number | null; step: number; status: string; agent_busy: boolean; agent_job?: string | null; progress: number; protocol: Protocol; current_step: { instruction: string }; event_count: number; recent: EventRecord[]; telemetry: Data; audit: Audit[]; recoveries: number; diagnosis: { summary: string; category: string; model?: string; evidence_ids: string[] } | null; verification: { result: string } | null; report: { text: string } | null; segments: { from_seq: number; to_seq: number }[]; survey: { difficulty: number; text: string } | null; step_history: { id: string; elapsed_seconds: number }[] };
type Connection = { url: string; token: string };
const defaultProtocol: Protocol = { id: "meta-hands-v1", title: "Three-cube hand interaction study", adapter: "meta_hands", target_id: "CUBE_B", practice_id: "CUBE_A", protected_id: "CUBE_C", demo_faults_allowed: true, auto_inject_target_fault: true, near_distance_m: 0.18, baseline_mask: 1, max_recoveries: 1, failure_threshold: 3, allowed_actions: ["restore_hand_grab_baseline"] };
const steps = ["장비 확인", "잡기 연습", "목표 접근", "목표 잡기", "목표 배치", "설문"];
const labels: Record<string, string> = { running: "진행 중", recovering: "복구 중", retest: "재시험", manual_review: "연구자 확인 필요", completed: "완료" };
const eventLabels: Record<string, string> = { grab_attempt: "잡기 입력", grab_failed: "잡기 실패", grab_success: "잡기 성공", telemetry: "장비 상태", target_found: "목표 발견", fault_injected: "데모 장애 주입", help_request: "참가자 도움 요청", placed: "목표 배치", released: "물체 놓기", equipment_ready: "장비 확인 완료", practice_completed: "연습 완료", survey_completed: "설문 완료", pause_request: "중지 요청", consent: "참가 동의", object_registered: "물체 기준값 등록", object_observation: "물체 관찰", non_grabbable_attempt: "비대상 물체 입력", client_error: "로컬 안전 정지" };
const clock = (n: number) => new Date(n * 1000).toLocaleTimeString("ko-KR", { hour12: false });

export default function Dashboard() {
  const [url, setUrl] = useState(""); const [token, setToken] = useState("");
  const [connection, setConnection] = useState<Connection | null>(null);
  const [sessions, setSessions] = useState<Session[]>([]); const [protocols, setProtocols] = useState<Protocol[]>([defaultProtocol]);
  const [selected, setSelected] = useState(""); const [protocolId, setProtocolId] = useState(defaultProtocol.id);
  const [error, setError] = useState(""); const [notice, setNotice] = useState(""); const [busy, setBusy] = useState(false);
  const [lastRefresh, setLastRefresh] = useState<number | null>(null);
  const [now, setNow] = useState(0);
  const [pair, setPair] = useState<{ id: string; token: string } | null>(null);
  const [editor, setEditor] = useState(JSON.stringify({ ...defaultProtocol, id: "meta-hands-custom-v1" }, null, 2));
  const s = sessions.find(x => x.id === selected);
  const fresh = !!s?.last_seen && now / 1000 - s.last_seen < 10;

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);

  const request = useCallback(async (path: string, method = "GET", body?: unknown, creds = connection) => {
    if (!creds) throw new Error("먼저 Cloud Run 서버에 연결해주세요.");
    const r = await fetch(creds.url + path, { method, headers: { Authorization: "Bearer " + creds.token, ...(body !== undefined ? { "Content-Type": "application/json" } : {}) }, body: body !== undefined ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(15000), cache: "no-store" });
    if (!r.ok) { const d = await r.json().catch(() => ({})); throw new Error(`${r.status} · ${typeof d.detail === "string" ? d.detail : "요청 처리 실패"}`); }
    return r;
  }, [connection]);
  useEffect(() => {
    if (!connection) return;
    let cancelled = false; let timer: ReturnType<typeof setTimeout>;
    async function loop() {
      try { const data = await (await request("/api/sessions")).json(); if (!cancelled) { setSessions(data.sessions); setSelected(prev => prev || data.sessions[0]?.id || ""); setLastRefresh(Date.now()); setError(""); } }
      catch (e) { if (!cancelled) setError(e instanceof Error ? e.message : "연결 오류"); }
      if (!cancelled) timer = setTimeout(loop, 2000);
    }
    void loop(); return () => { cancelled = true; clearTimeout(timer); };
  }, [connection, request]);
  async function connect() {
    setBusy(true); setError("");
    try {
      const parsed = new URL(url.trim());
      const local = ["localhost", "127.0.0.1"].includes(window.location.hostname) && ["localhost", "127.0.0.1"].includes(parsed.hostname) && parsed.protocol === "http:";
      if ((!local && parsed.protocol !== "https:") || parsed.username || parsed.password || parsed.search || parsed.hash || parsed.pathname !== "/") throw new Error("Cloud Run HTTPS 주소의 도메인까지만 입력해주세요.");
      if (token.length < 32) throw new Error("32자 이상의 연구자 토큰이 필요합니다.");
      const next = { url: parsed.origin, token }; const data = await (await request("/api/protocols", "GET", undefined, next)).json();
      setProtocols(data.protocols); setConnection(next); setNotice("연결 완료. 세션을 만들어 Unity에 연결하세요.");
    } catch (e) { setError(e instanceof Error ? e.message : "연결 실패"); } finally { setBusy(false); }
  }
  function disconnect() { setConnection(null); setSessions([]); setSelected(""); setToken(""); setPair(null); setLastRefresh(null); setError(""); setNotice(""); }
  async function newSession() {
    setBusy(true);
    try { const d = await (await request("/api/sessions", "POST", { protocol_id: protocolId })).json(); setSessions(prev => [d.session, ...prev]); setSelected(d.session.id); setPair({ id: d.session.id, token: d.session_token }); }
    catch (e) { setError(e instanceof Error ? e.message : "세션 생성 실패"); } finally { setBusy(false); }
  }
  async function saveProtocol() {
    setBusy(true);
    try { const p = await (await request("/api/protocols", "POST", JSON.parse(editor))).json(); setProtocols(prev => [...prev, p]); setProtocolId(p.id); setNotice("새 프로토콜 저장 완료. 진행 중인 세션에는 영향을 주지 않습니다."); setError(""); }
    catch (e) { setError(e instanceof Error ? e.message : "저장 실패"); } finally { setBusy(false); }
  }
  async function download(format: "json" | "csv") {
    if (!s) return;
    try { const blob = await (await request(`/api/sessions/${s.id}/export/${format}`)).blob(); const href = URL.createObjectURL(blob); const a = document.createElement("a"); a.href = href; a.download = `protocolrun-${s.id}.${format}`; a.click(); setTimeout(() => URL.revokeObjectURL(href), 1000); }
    catch (e) { setError(e instanceof Error ? e.message : "다운로드 실패"); }
  }
  async function copy(value: string) { try { await navigator.clipboard.writeText(value); setNotice("복사했습니다."); } catch { setNotice("필드를 선택해 직접 복사해주세요."); } }

  return <div className="workspace">
    <header className="topbar"><Link href="/" className="brand"><span className="brand-icon"><Workflow size={21}/></span>ProtocolRun<span className="brand-vr">VR</span></Link><div className="top-meta"><span className="mono">STUDY OPERATIONS / 0.5 RC6</span><Badge variant="outline">연구자 콘솔</Badge></div></header>
    <main className="main-shell">
      <div className="page-heading"><div><div className="eyebrow">Observe. Recover. Verify.</div><h1>실험은 계속, 근거는 남도록.</h1><p>Unity의 행동 로그에서 장애를 확인하고, 허용된 복구와 재시험을 추적합니다.</p></div><div className="connection-pill"><span className={connection && !error ? "dot live" : "dot"}/>{connection ? error ? "연결 확인 필요" : "API 연결됨" : "서버 연결 대기"}</div></div>
      {error && <div className="alert error" role="alert">{error}<small>Cloud Run 주소·토큰·CORS를 확인해주세요. 기존 값은 마지막 수신 데이터입니다.</small></div>}
      {notice && <div className="notice" role="status">{notice}<button aria-label="알림 닫기" onClick={() => setNotice("")}>×</button></div>}
      <div className="console-grid"><aside className="left-column">
        <section className="panel connection-panel"><div className="panel-title"><Link2 size={17}/><h2>Cloud Run 연결</h2></div><label htmlFor="server">API 주소</label><Input id="server" placeholder="https://your-service.run.app" value={url} onChange={e => setUrl(e.target.value)} disabled={!!connection} autoComplete="off"/><label htmlFor="token">연구자 토큰</label><Input id="token" type="password" placeholder="PRVR_ADMIN_TOKEN" value={token} onChange={e => setToken(e.target.value)} disabled={!!connection} autoComplete="off"/><p className="hint">토큰은 이 탭의 메모리에만 유지됩니다. Google API 키를 입력하지 마세요.</p><Button className="wide" variant={connection ? "outline" : "default"} disabled={busy} onClick={connection ? disconnect : connect}>{busy ? <Loader2 className="animate-spin"/> : connection ? <Unplug/> : <Link2/>}{connection ? "연결 해제" : "서버 연결"}</Button></section>
        <section className="panel"><div className="panel-title"><Radio size={17}/><h2>세션</h2><span className="count">{sessions.length}</span></div><label htmlFor="protocol-select">실험 프로토콜</label><Select value={protocolId} onValueChange={setProtocolId}><SelectTrigger id="protocol-select" className="wide"><SelectValue/></SelectTrigger><SelectContent>{protocols.map(p => <SelectItem key={p.id} value={p.id}>{p.id}</SelectItem>)}</SelectContent></Select><Button className="wide new-session" disabled={!connection || busy} onClick={newSession}><Plus/>새 세션</Button><div className="session-list">{sessions.length ? sessions.map(item => <button key={item.id} className={selected === item.id ? "session selected" : "session"} onClick={() => setSelected(item.id)}><div><span className="mono">{item.id.slice(0, 8)}</span><span>{labels[item.status] || item.status}</span></div><small>{new Date(item.created_at * 1000).toLocaleString("ko-KR")}</small></button>) : <p className="hint">서버에 연결하면 생성한 세션이 여기에 표시됩니다.</p>}</div></section>
        <div className="guard-note"><ShieldCheck size={21}/><div><strong>Protocol Firewall</strong><p>원래 설정만 복원합니다.<br/>목표·난이도·참가자 응답은 바꾸지 않습니다.</p></div></div>
      </aside><section className="right-column">
        <div className="metrics"><div><span>Unity 장비</span><strong className={fresh ? "green" : "muted"}>{fresh ? "수신 중" : "미연결"}</strong><small>{s?.last_seen ? `마지막 수신 ${clock(s.last_seen)}` : "실제 장비 데이터 대기"}</small></div><div><span>수집 이벤트</span><strong>{s?.event_count ?? "—"}</strong><small>중복 전송 제외</small></div><div><span>최근 프레임</span><strong>{typeof s?.telemetry?.fps === "number" ? Math.round(s.telemetry.fps) : "—"}<em> FPS</em></strong><small>Unity 측정값 · 순간값</small></div><div><span>복구 검증</span><strong className={s?.verification ? "green" : "muted"}>{s?.verification ? "통과" : "대기"}</strong><small>동일 물체 재잡기로 확인</small></div></div>
        <Tabs defaultValue="monitor"><TabsList variant="line" className="main-tabs"><TabsTrigger value="monitor">실시간 관찰</TabsTrigger><TabsTrigger value="protocol">프로토콜</TabsTrigger><TabsTrigger value="report">분석·내보내기</TabsTrigger></TabsList>
          <TabsContent value="monitor"><section className="panel study-panel"><div className="section-heading"><div><span className="eyebrow">CURRENT STUDY</span><h2>{s?.protocol.title || "Object interaction study"}</h2></div><Badge variant="outline">{s ? labels[s.status] : "세션 대기"}</Badge></div><ol className="step-track">{steps.map((name, i) => <li key={name} className={s && i < s.step ? "done" : s && i === s.step ? "active" : ""}><span>{s && i < s.step ? <Check size={14}/> : i + 1}</span>{name}</li>)}</ol><Progress value={s?.progress || 0} className="study-progress"/><div className="instruction"><span>참가자 안내</span><p>{s?.current_step.instruction || "세션을 만들고 Unity 프로젝트를 연결해주세요."}</p></div></section>
          <div className="evidence-grid"><section className="panel"><div className="panel-title"><Activity size={17}/><h2>행동 타임라인</h2><Badge variant="outline">최근 60개</Badge></div>{s?.recent.length ? <div className="event-scroll"><Table><TableHeader><TableRow><TableHead>시각</TableHead><TableHead>이벤트</TableHead><TableHead>관찰값</TableHead></TableRow></TableHeader><TableBody>{[...s.recent].reverse().map(e => <TableRow key={e.event_id}><TableCell className="mono time-cell">{clock(e.received_at)}<small>#{e.seq}</small></TableCell><TableCell><span className={e.kind === "grab_failed" ? "event-bad" : e.kind === "grab_success" ? "green" : ""}>{eventLabels[e.kind] || e.kind}</span></TableCell><TableCell className="event-value">{e.kind === "telemetry" ? `${Math.round(Number(e.data.fps || 0))} FPS` : e.data.text ? String(e.data.text) : e.data.object_id ? `${e.data.object_id} · ${s.protocol.adapter === "meta_hands" ? `Hand ${e.data.enabled_hand_grab_count}/${e.data.hand_grab_count}` : `mask ${e.data.observed_mask}`}` : "—"}</TableCell></TableRow>)}</TableBody></Table></div> : <Empty className="timeline-empty"><EmptyHeader><Radio size={26}/><EmptyTitle>아직 수신된 행동이 없습니다</EmptyTitle><EmptyDescription>Unity가 연결되면 입력, 잡기 결과, 장비 상태가 순서대로 표시됩니다. 예시 데이터를 실측값으로 표시하지 않습니다.</EmptyDescription></EmptyHeader></Empty>}</section>
          <section className="panel agent-panel"><div className="panel-title"><ShieldCheck size={17}/><h2>진단과 복구 근거</h2></div><div className="agent-status"><span className={s?.diagnosis || s?.agent_busy ? "dot live" : "dot"}/>{s?.agent_busy ? `Gemini ${s.agent_job || "agent"} · 처리 중` : s?.diagnosis?.model || "Gemini 3.5 Flash · 호출 대기"}</div>{s?.agent_busy ? <p className="agent-placeholder">실제 증거로 하나의 복구 Tool 결정을 생성 중입니다. Unity Play와 양손 추적을 유지하세요. 호출당 최대 약 1분이며 실패 시 안전하게 재시도합니다.</p> : s?.diagnosis ? <><h3>{s.diagnosis.category}</h3><p>{s.diagnosis.summary}</p><div className="evidence-ids">{s.diagnosis.evidence_ids.map(id => <code key={id}>{id}</code>)}</div></> : <p className="agent-placeholder">반복 실패나 도움 요청이 발생하면 에이전트가 실제 로그를 확인합니다.</p>}<ol className="recovery-track">{[["증거 기반 진단", !!s?.diagnosis], ["허용 범위 검사", !!s?.audit.some(a => a.kind === "firewall" && a.detail.allowed === true)], ["Unity 설정 복원", !!s?.audit.some(a => a.kind === "command_ack" && a.detail.success === true && (s.protocol.adapter === "meta_hands" ? a.detail.action === "restore_hand_grab_baseline" && a.detail.baseline_match === true : a.detail.action === "restore_interaction_layer" && a.detail.observed_mask === s.protocol.baseline_mask))], ["동일 과제 재시험 검증", !!s?.verification]].map(([label, done]) => <li key={String(label)}>{done ? <Check size={17} className="green"/> : <Circle size={17}/>}<span>{label}</span></li>)}</ol><p className="hint">재시험 증거가 없으면 검증 완료로 표시하지 않습니다.</p></section></div>
          <section className="panel audit-panel"><div className="panel-title"><Workflow size={17}/><h2>운영 감사 기록</h2></div>{s?.audit.length ? <div className="audit-list">{[...s.audit].reverse().map(a => <details key={a.id}><summary><span className="mono">{clock(a.at)}</span><strong>{a.kind}</strong></summary><pre>{JSON.stringify(a.detail, null, 2)}</pre></details>)}</div> : <p className="hint">에이전트 호출, Firewall 승인·거부, 실행 응답, 재시험 결과가 여기에 남습니다.</p>}</section></TabsContent>
          <TabsContent value="protocol"><section className="panel protocol-editor"><span className="eyebrow">IMMUTABLE PROTOCOL</span><h2>연구 조건을 먼저 고정하세요</h2><p>이번 구현은 장비 확인 → A 연습 → B 탐색 → 장애 진단·복구 → 배치 → 설문 순서입니다. 아래 설정으로 새 프로토콜을 만들 수 있습니다. 이미 시작한 세션의 설정은 바뀌지 않습니다.</p><label htmlFor="protocol-json">프로토콜 JSON</label><Textarea id="protocol-json" value={editor} onChange={e => setEditor(e.target.value)} className="json-editor" spellCheck={false}/><Button onClick={saveProtocol} disabled={!connection || busy}>새 프로토콜 저장<ArrowRight/></Button><p className="hint">Meta 손 추적: A는 정상 연습 물체, B는 시작 시 자동으로 잡기 경로가 비활성화되는 복구 대상, C는 의도적으로 잡을 수 없는 물체입니다. B의 등록된 원래 컴포넌트 상태만 복원합니다.</p></section></TabsContent>
          <TabsContent value="report"><section className="panel"><div className="section-heading"><div><span className="eyebrow">EVIDENCE EXPORT</span><h2>행동·설문 통합 보고서</h2></div><div className="download-actions"><Button variant="outline" disabled={!s || !connection} onClick={() => download("json")}><ArrowDownToLine/>JSON</Button><Button variant="outline" disabled={!s || !connection} onClick={() => download("csv")}><ArrowDownToLine/>CSV</Button></div></div>{s ? <><div className="report-summary"><Badge variant="outline">{labels[s.status]}</Badge><p className="mono">Session {s.id}</p><p>격리 구간 {s.segments.length}개 · 복구 시도 {s.recoveries}회 · 검증 {s.verification ? "통과" : "미완료"}</p></div><Table><TableHeader><TableRow><TableHead>완료 단계</TableHead><TableHead>서버 기준 경과 시간</TableHead></TableRow></TableHeader><TableBody>{s.step_history.map(h => <TableRow key={h.id}><TableCell>{h.id}</TableCell><TableCell>{h.elapsed_seconds.toFixed(1)}초</TableCell></TableRow>)}</TableBody></Table><p className="hint">위 시간에는 기술 장애·통신 지연이 포함됩니다. 연구 성과 수치로 해석하지 마세요. 원본 이벤트는 삭제하지 않고 격리 여부를 내보냅니다.</p><h3>참가자 응답</h3><p>{s.survey ? `난이도 ${s.survey.difficulty}/7 · ${s.survey.text || "자유 응답 없음"}` : "설문 응답 대기"}</p><h3>AI 운영 요약</h3><div className="report-text">{s.report?.text || "실험 종료 후 Unity가 연결된 상태에서 에이전트가 보고서를 생성합니다. 생성 실패는 감사 기록에서 확인하세요."}</div></> : <Empty><EmptyHeader><EmptyTitle>분석할 세션을 선택하세요</EmptyTitle><EmptyDescription>실제 세션의 원본 기록과 분석 결과만 내보냅니다.</EmptyDescription></EmptyHeader></Empty>}</section></TabsContent>
        </Tabs><footer className="console-footer"><span>Google ADK + Gemini · Cloud Run API · Firestore</span><span>{lastRefresh ? `마지막 갱신 ${new Date(lastRefresh).toLocaleTimeString("ko-KR")}` : "GCP 배포·Unity 연결 전"}</span></footer>
      </section></div>
    </main>
    <Dialog open={!!pair} onOpenChange={open => { if (!open) setPair(null); }}><DialogContent><DialogHeader><DialogTitle>Unity에 세션 연결</DialogTitle><DialogDescription>연결 JSON을 복사해서 Unity의 Tools → ProtocolRun VR → Configure Connection에 붙여넣으세요. Play를 다시 시작할 때는 새 세션이 필요합니다.</DialogDescription></DialogHeader><Button onClick={() => copy(JSON.stringify({ server_url: connection?.url, session_id: pair?.id, session_token: pair?.token }, null, 2))}><Copy/>연결 JSON 복사</Button><label>Session ID</label><div className="copy-row"><Input readOnly value={pair?.id || ""}/><Button variant="outline" aria-label="세션 ID 복사" onClick={() => copy(pair?.id || "")}><Copy/></Button></div><label>Session Token</label><div className="copy-row"><Input readOnly type="password" value={pair?.token || ""}/><Button variant="outline" aria-label="세션 토큰 복사" onClick={() => copy(pair?.token || "")}><Copy/></Button></div><p className="hint">Server URL: {connection?.url}<br/>이 토큰은 해당 세션에만 접근합니다. 소스 저장소에 올리지 마세요.</p></DialogContent></Dialog>
  </div>;
}
