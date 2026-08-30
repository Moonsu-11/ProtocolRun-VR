'use strict';
const $ = id => document.getElementById(id);
let token = '', sessions = [], selected = '', timer, pairing = null, polling = false;
$('origin').textContent = location.origin;
function error(e) { $('error').hidden = false; $('error').textContent = e.message || String(e); }
function clearError() { $('error').hidden = true; }
async function api(path, method = 'GET', body) {
  const response = await fetch(path, {method, headers:{Authorization:`Bearer ${token}`, ...(body !== undefined ? {'Content-Type':'application/json'} : {})}, body:body !== undefined ? JSON.stringify(body) : undefined, cache:'no-store', signal:AbortSignal.timeout(15000)});
  if (!response.ok) { const d = await response.json().catch(()=>({})); throw Error(`${response.status}: ${typeof d.detail==='string' ? d.detail : 'Request failed'}`); }
  return response;
}
function active() { return sessions.find(s => s.id === selected); }
function render() {
  const s = active(); $('sessions').replaceChildren();
  sessions.forEach(item => { const button = document.createElement('button'); button.className = 'session'+(item.id===selected?' active':''); button.textContent = `${item.id.slice(0,8)} · ${item.status}`; button.onclick = () => { selected=item.id;render(); }; $('sessions').append(button); });
  $('json').disabled = $('csv').disabled = !s;
  if (!s) return;
  $('device').textContent = s.last_seen && Date.now()/1000-s.last_seen<10 ? 'Receiving' : 'Offline';
  $('count').textContent = s.event_count; $('fps').textContent = Number.isFinite(s.telemetry?.fps) ? Math.round(s.telemetry.fps) : '—';
  $('verified').textContent = s.verification?.result==='passed' ? 'Passed' : 'Pending';
  $('study').textContent = s.protocol.title; $('progress').value=s.progress;
  $('instruction').textContent=s.current_step.instruction;
  $('status').textContent=`${s.status} · step ${Math.min(s.step+1,6)}/6 · ${s.recoveries} recovery attempt(s)`;
  const latestAgentError=[...(s.audit||[])].reverse().find(a=>a.kind==='agent_error');
  $('diagnosis').textContent=s.agent_job ? `Gemini ${s.agent_job} is making one evidence-based tool decision. Keep Unity Play active and both hands tracked; each call is limited to one minute and safely retries on failure.` : s.diagnosis ? `${s.diagnosis.category}: ${s.diagnosis.summary}\nEvidence: ${s.diagnosis.evidence_ids.join(', ')}` : s.status==='manual_review' && latestAgentError ? `Gemini did not finish: ${latestAgentError.detail.error_type}. Create a new session after correcting the model connection.` : 'Waiting for actual evidence and a Gemini tool call.';
  $('commands').textContent=(s.commands||[]).map(c=>`${c.action}: ${c.status}${c.ack ? ' / executed='+c.ack.success : ''}`).join(' → ');
  $('events').replaceChildren();
  [...s.recent].reverse().forEach(e=>{const tr=document.createElement('tr');const data=e.data;const values=[`#${e.seq}`,e.kind,data.text||`${data.object_id||''}${data.hand_grab_count!==undefined?' · Hand '+data.enabled_hand_grab_count+'/'+data.hand_grab_count+' · tracked '+data.tracked:''}`];values.forEach(v=>{const td=document.createElement('td');td.textContent=v;tr.append(td);});$('events').append(tr);});
  $('survey').textContent=s.survey ? `Difficulty ${s.survey.difficulty}/7. ${s.survey.text}` : 'Survey not submitted.';
  $('report').textContent=s.report?.text || 'Report not generated. Keep Unity running through the report step.';
  $('audit').textContent=JSON.stringify(s.audit,null,2);
}
async function refresh() {
  if (polling || !token) return; polling=true;
  try { const data=await (await api('/api/sessions')).json(); if(!token)return; sessions=data.sessions; selected=selected||sessions[0]?.id||'';render();clearError(); }
  catch(e){error(e);} finally{polling=false;if(token)timer=setTimeout(refresh,2000);}
}
$('connect').onclick=async()=>{
  token=$('token').value.trim();if(token.length<32){error(Error('Use the researcher token from your private server configuration.'));token='';return;}
  try{
    const data=await(await api('/api/protocols')).json();const runtime=await(await api('/api/runtime')).json();
    $('protocol').replaceChildren(); data.protocols.forEach(p=>{const option=document.createElement('option');option.value=p.id;option.textContent=p.id;$('protocol').append(option);});
    $('protocol').value='meta-hands-v1';$('editor').value=JSON.stringify({...data.protocols.find(p=>p.id==='meta-hands-v1'),id:'meta-hands-custom-v1'},null,2);
    $('runtime').textContent=`${runtime.version} · ${runtime.store} · ${runtime.model} · ${runtime.cloud_run?'Cloud Run':'local runtime'}. Model access is not yet verified.`;
    $('token').value='';$('token').disabled=true;$('connect').hidden=true;$('disconnect').hidden=false;$('create').disabled=$('save').disabled=false;
    clearError();clearTimeout(timer);await refresh();
  }catch(e){token='';error(e);}
};
$('disconnect').onclick=()=>{token='';sessions=[];selected='';pairing=null;clearTimeout(timer);location.reload();};
$('create').onclick=async()=>{
  $('create').disabled=true;
  try{const data=await(await api('/api/sessions','POST',{protocol_id:$('protocol').value})).json();sessions.unshift(data.session);selected=data.session.id;pairing={server_url:location.origin,session_id:selected,session_token:data.session_token};$('pair-json').textContent=JSON.stringify(pairing,null,2);$('pair').showModal();render();}
  catch(e){error(e);}finally{$('create').disabled=false;}
};
$('copy').onclick=async()=>{try{await navigator.clipboard.writeText(JSON.stringify(pairing,null,2));$('notice').textContent='Private connection JSON copied. Paste into Unity, not into a repository or chat.';}catch{error(Error('Open Show connection JSON and copy it manually.'));}};
$('close-pair').onclick=()=>{$('pair').close();pairing=null;$('pair-json').textContent='';};
$('pair').addEventListener('cancel',()=>{pairing=null;$('pair-json').textContent='';});
$('save').onclick=async()=>{try{const p=await(await api('/api/protocols','POST',JSON.parse($('editor').value))).json();const option=document.createElement('option');option.value=p.id;option.textContent=p.id;$('protocol').append(option);$('protocol').value=p.id;$('notice').textContent='New immutable protocol saved.';}catch(e){error(e);}};
for(const format of ['json','csv'])$(format).onclick=async()=>{const s=active();if(!s)return;try{const blob=await(await api(`/api/sessions/${s.id}/export/${format}`)).blob();const url=URL.createObjectURL(blob);const a=document.createElement('a');a.href=url;a.download=`protocolrun-${s.id}.${format}`;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}catch(e){error(e);}};
