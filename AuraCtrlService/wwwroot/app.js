const WS_PORT = 47201;
let ws = null;
let sel = null;
let previewOn = true;
let postes = [];
let inputStates = { ok: true, om: true, mk: true, mm: true };
let hbTimers = {};
let notifCount = 1;

function connect() {
  ws = new WebSocket(`ws://localhost:${WS_PORT}/ws/`);
  ws.onopen = () => { console.log('WS connecté'); send({ type: 'get_postes' }); };
  ws.onmessage = (e) => handleMessage(JSON.parse(e.data));
  ws.onclose = () => setTimeout(connect, 3000);
  ws.onerror = () => ws.close();
}

function send(obj) {
  if (ws && ws.readyState === 1) ws.send(JSON.stringify(obj));
}

function handleMessage(msg) {
  switch (msg.type) {
    case 'postes_list': postes = msg.data; renderPostes(); updateStats(); break;
    case 'request_control': addNotif(msg); break;
    case 'set_input': applyInputState(msg); break;
    case 'disconnect_all': closeSession(); break;
  }
}

function nav(v) {
  document.querySelectorAll('.content').forEach(c => c.classList.add('hidden'));
  document.querySelectorAll('.ni').forEach(n => n.classList.remove('active'));
  document.getElementById('view-' + v).classList.remove('hidden');
  document.getElementById('ni-' + v).classList.add('active');
  if (v === 'session') startMainCursor();
}

function updateStats() {
  const online = postes.filter(p => p.isOnline).length;
  document.getElementById('stat-online').textContent = online;
  document.getElementById('stat-total').textContent = postes.length;
}

function osIcon(os) {
  if (os === 'Win32NT') return '🖥';
  if (os === 'Unix') return '🐧';
  return '⚡';
}

function renderPostes() {
  const g = document.getElementById('postes-grid');
  g.innerHTML = postes.map(p => {
    const online = p.isOnline;
    const isSel = sel === p.id;
    const prev = previewOn && online ? screenMock(p) : '';
    const hb = online ? '<div class="hbbar"><div class="hbfill" id="hb-' + p.id + '" style="width:80%"></div></div>' : '';
    return '<div class="pcard ' + (online ? '' : 'off') + ' ' + (isSel ? 'sel' : '') + '" onclick="' + (online ? 'selPoste(\'' + p.id + '\')' : '') + '">' +
      '<div class="scr-prev ' + (previewOn && online ? '' : 'hide') + '" id="prev-' + p.id + '">' + prev + '</div>' +
      '<div class="pinfo"><div class="ptop"><div class="pleft">' +
      '<div class="pic">' + osIcon(p.os) + '</div>' +
      '<div><div class="pname">' + p.name + '</div><div class="pip">' + p.ip + '</div></div>' +
      '</div><span class="pbadge ' + (online ? 'bon' : 'boff') + '">' + (online ? 'en ligne' : 'hors ligne') + '</span></div>' +
      hb + '</div></div>';
  }).join('');
  animCursors();
}

function screenMock(p) {
  const cx = Math.floor(Math.random() * 65) + 15;
  const cy = Math.floor(Math.random() * 55) + 10;
  const t = new Date().toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  return '<div style="width:100%;height:100%;position:relative;background:#040810;">' +
    '<div style="position:absolute;top:8%;left:5%;width:54%;height:50%;background:rgba(14,20,36,0.96);border:0.5px solid rgba(232,160,32,0.18);border-radius:2px;">' +
    '<div style="height:7px;background:#0e2440;display:flex;align-items:center;padding:0 3px;gap:2px;">' +
    '<div style="width:3px;height:3px;border-radius:50%;background:#c04040;"></div>' +
    '<div style="width:3px;height:3px;border-radius:50%;background:#c08010;"></div>' +
    '<div style="width:3px;height:3px;border-radius:50%;background:#208040;"></div></div>' +
    '<div style="padding:2px 3px;">' +
    '<div style="height:2px;background:rgba(232,160,32,0.28);margin-bottom:2px;width:55%;"></div>' +
    '<div style="height:2px;background:rgba(128,144,168,0.18);margin-bottom:2px;width:85%;"></div>' +
    '<div style="height:2px;background:rgba(128,144,168,0.18);margin-bottom:2px;width:70%;"></div></div></div>' +
    '<div style="position:absolute;top:35%;left:36%;width:50%;height:40%;background:rgba(14,20,36,0.96);border:0.5px solid rgba(232,160,32,0.12);border-radius:2px;">' +
    '<div style="height:7px;background:#0e2440;"></div>' +
    '<div style="padding:2px 3px;">' +
    '<div style="height:2px;background:rgba(128,144,168,0.18);margin-bottom:2px;width:75%;"></div>' +
    '<div style="height:2px;background:rgba(232,160,32,0.2);margin-bottom:2px;width:50%;"></div></div></div>' +
    '<div id="cur-' + p.id + '" style="position:absolute;width:3px;height:3px;background:#fff;border-radius:50%;left:' + cx + '%;top:' + cy + '%;transition:all 2.5s ease-in-out;"></div>' +
    '<div style="position:absolute;bottom:0;left:0;right:0;height:13%;background:rgba(10,16,28,0.97);display:flex;align-items:center;padding:0 5px;gap:3px;">' +
    '<div style="width:7px;height:7px;border-radius:1px;background:var(--acc);"></div>' +
    '<div style="width:7px;height:7px;border-radius:1px;background:var(--acc);"></div>' +
    '<div style="width:7px;height:7px;border-radius:1px;background:rgba(232,160,32,0.25);"></div>' +
    '<span style="margin-left:auto;font-size:6px;color:var(--t3);font-family:monospace;">' + t + '</span></div>' +
    '<span style="position:absolute;top:3px;right:4px;font-size:7px;color:var(--acc);background:rgba(0,0,0,0.7);padding:1px 4px;border-radius:2px;font-family:monospace;">LIVE</span></div>';
}

function animCursors() {
  postes.filter(p => p.isOnline).forEach(p => {
    if (hbTimers[p.id]) return;
    hbTimers[p.id] = setInterval(() => {
      const el = document.getElementById('cur-' + p.id);
      if (el) { el.style.left = (Math.random() * 65 + 15) + '%'; el.style.top = (Math.random() * 55 + 10) + '%'; }
      const hb = document.getElementById('hb-' + p.id);
      if (hb) { let w = parseInt(hb.style.width); hb.style.width = Math.max(10, w - 8) + '%'; }
    }, 3000);
  });
}

function selPoste(id) {
  sel = sel === id ? null : id;
  renderPostes();
  const p = postes.find(x => x.id === id);
  const c = document.getElementById('conn-content');
  if (sel && p) {
    c.innerHTML = '<div class="sel-row"><div class="sel-ic">' + osIcon(p.os) + '</div>' +
      '<div><div class="sel-name">' + p.name + '</div><div class="sel-ip">' + p.ip + '</div></div></div>' +
      '<button class="cbtn" onclick="openSession()">🖥 Prendre la main</button>';
  } else {
    c.innerHTML = '<div class="no-sel">Sélectionner un poste en ligne</div>';
  }
}

function openSession() {
  const p = postes.find(x => x.id === sel);
  if (!p) return;
  document.getElementById('sess-name').textContent = p.name;
  document.getElementById('sess-ip').textContent = p.ip;
  document.getElementById('ni-session').style.display = 'flex';
  send({ type: 'request_control', from: 'local', target: p.id });
  nav('session');
}

function closeSession() {
  document.getElementById('ni-session').style.display = 'none';
  sel = null;
  renderPostes();
  document.getElementById('conn-content').innerHTML = '<div class="no-sel">Sélectionner un poste en ligne</div>';
  nav('postes');
}

function disconnect() {
  send({ type: 'disconnect_all' });
  closeSession();
}

let mainCurTimer = null;
function startMainCursor() {
  if (mainCurTimer) return;
  mainCurTimer = setInterval(() => {
    const el = document.getElementById('main-cur');
    if (el) { el.style.left = (Math.random() * 70 + 10) + '%'; el.style.top = (Math.random() * 65 + 5) + '%'; }
    const clk = document.getElementById('main-clk');
    if (clk) clk.textContent = new Date().toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  }, 2000);
}

function tglInput(k) {
  inputStates[k] = !inputStates[k];
  document.getElementById('tgl-' + k).classList.toggle('on', inputStates[k]);
  const msg = { type: 'set_input', target: k.startsWith('o') ? 'owner' : 'controller' };
  if (k === 'ok' || k === 'mk') msg.keyboard = inputStates[k];
  if (k === 'om' || k === 'mm') msg.mouse = inputStates[k];
  send(msg);
  document.getElementById('lock-overlay').classList.toggle('show', !inputStates.ok && !inputStates.om);
}

function applyInputState(msg) {
  if (msg.target === 'owner') {
    if (msg.keyboard !== undefined) { inputStates.ok = msg.keyboard; document.getElementById('tgl-ok').classList.toggle('on', msg.keyboard); }
    if (msg.mouse !== undefined) { inputStates.om = msg.mouse; document.getElementById('tgl-om').classList.toggle('on', msg.mouse); }
  }
  document.getElementById('lock-overlay').classList.toggle('show', !inputStates.ok && !inputStates.om);
}

function addNotif(msg) {
  notifCount++;
  updateNotifBadge();
  const list = document.getElementById('notif-list');
  const initials = msg.from ? msg.from.substring(0, 2).toUpperCase() : 'XX';
  const t = new Date().toLocaleTimeString('fr-FR');
  const card = document.createElement('div');
  card.className = 'notif-card';
  card.innerHTML = '<div class="notif-top"><div class="notif-av">' + initials + '</div>' +
    '<div><div class="notif-title">' + (msg.from || 'Inconnu') + ' demande la prise de main</div>' +
    '<div class="notif-sub">LAN — ' + t + '</div></div>' +
    '<div class="notif-time">A l\'instant</div></div>' +
    '<div class="notif-btns">' +
    '<div class="nbtn nbtn-acc" onclick="acceptNotif(this,\'' + msg.from + '\')">✓ Accepter</div>' +
    '<div class="nbtn nbtn-ref" onclick="refuseNotif(this)">✕ Refuser</div></div>';
  list.prepend(card);
}

function acceptNotif(btn, from) {
  btn.closest('.notif-card').remove();
  notifCount = Math.max(0, notifCount - 1);
  updateNotifBadge();
  send({ type: 'grant_control', to: from });
}

function refuseNotif(btn) {
  btn.closest('.notif-card').remove();
  notifCount = Math.max(0, notifCount - 1);
  updateNotifBadge();
  const list = document.getElementById('notif-list');
  if (!list.querySelector('.notif-card')) list.innerHTML = '<div class="no-sel">Aucune demande en attente</div>';
}

function updateNotifBadge() {
  ['notif-badge', 'nb2'].forEach(id => {
    const el = document.getElementById(id);
    if (el) { el.textContent = notifCount; el.style.display = notifCount > 0 ? 'inline' : 'none'; }
  });
}

function tglParam(k) {
  const t = document.getElementById('tgl-p-' + k);
  const on = t.classList.toggle('on');
  if (k === 'prev') { previewOn = on; renderPostes(); }
}

function grantHand(btn) {
  btn.textContent = 'Reprendre';
  btn.onclick = () => revokeHand(btn);
  send({ type: 'grant_control', to: btn.closest('.obs-item').dataset.id });
}

function revokeHand(btn) {
  btn.textContent = 'Passer la main';
  btn.onclick = () => grantHand(btn);
  send({ type: 'revoke_control' });
}

function kickObs(btn) { btn.closest('.obs-item').remove(); }

setInterval(() => { if (ws && ws.readyState === 1) send({ type: 'get_postes' }); }, 5000);

window.addEventListener('DOMContentLoaded', connect);
