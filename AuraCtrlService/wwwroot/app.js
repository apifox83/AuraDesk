const WS_PORT = 47201;
let ws = null;
let sel = null;
let previewOn = true;
let postes = [];
let inputStates = { ok: true, om: true, mk: true, mm: true };
let notifCount = 0;
let lastFrame = null;
let inSession = false;
let previewW = 1280;
let previewH = 720;

function connect() {
  const token = new URLSearchParams(window.location.search).get('token') || '';
  ws = new WebSocket('ws://localhost:' + WS_PORT + '/ws/?token=' + token);
  ws.onopen = () => { send({ type: 'get_postes' }); send({ type: 'set_preview', enabled: previewOn }); };
  ws.onmessage = (e) => handleMessage(JSON.parse(e.data));
  ws.onclose   = () => setTimeout(connect, 3000);
  ws.onerror   = () => ws.close();
}

function send(obj) { if (ws && ws.readyState === 1) ws.send(JSON.stringify(obj)); }

function handleMessage(msg) {
  switch (msg.type) {
    case 'postes_list':
      postes = msg.data; renderPostes(); updateStats();
      if (window.electronAPI) window.electronAPI.updatePostesCount(postes.filter(p => p.IsOnline).length);
      break;
    case 'screen_frame':
      lastFrame = 'data:image/jpeg;base64,' + msg.data;
      updateScreenPreviews(); updateMainScreen();
      break;
    case 'request_control': addNotif(msg); break;
    case 'set_input':       applyInputState(msg); break;
    case 'disconnect_all':  closeSession(); break;
  }
}

function updateScreenPreviews() {
  if (!previewOn || !lastFrame) return;
  document.querySelectorAll('.scr-prev img').forEach(img => { img.src = lastFrame; });
}

function updateMainScreen() {
  const img = document.getElementById('main-screen-img');
  if (!img || !lastFrame) return;
  img.src = lastFrame;
  if (img.naturalWidth)  previewW = img.naturalWidth;
  if (img.naturalHeight) previewH = img.naturalHeight;
}

function nav(v) {
  document.querySelectorAll('.content').forEach(c => c.classList.add('hidden'));
  document.querySelectorAll('.ni').forEach(n => n.classList.remove('active'));
  document.getElementById('view-' + v).classList.remove('hidden');
  document.getElementById('ni-' + v).classList.add('active');
  inSession = (v === 'session');
}

function updateStats() {
  const online = postes.filter(p => p.IsOnline).length;
  document.getElementById('stat-online').textContent = online;
  document.getElementById('stat-total').textContent  = postes.length;
}

function osIcon(os) {
  if (os === 'Win32NT') return '🖥'; if (os === 'Unix') return '🐧'; return '⚡';
}

const PLACEHOLDER = '<svg viewBox="0 0 240 135" xmlns="http://www.w3.org/2000/svg" style="width:100%;height:100%">' +
  '<rect width="240" height="135" fill="#040810"/>' +
  '<rect x="55" y="15" width="130" height="82" rx="5" fill="none" stroke="#1c2840" stroke-width="2"/>' +
  '<rect x="60" y="20" width="120" height="72" fill="#0a1628"/>' +
  '<rect x="98" y="97" width="44" height="6" rx="2" fill="#1c2840"/>' +
  '<rect x="86" y="103" width="68" height="4" rx="2" fill="#1c2840"/>' +
  '<text x="120" y="61" text-anchor="middle" fill="#243050" font-size="10" font-family="system-ui">hors ligne</text>' +
  '</svg>';

function renderPostes() {
  const g = document.getElementById('postes-grid');
  g.innerHTML = postes.map(p => {
    const online = p.IsOnline;
    const isSel  = sel === p.Id;
    const prevContent = (previewOn && online && lastFrame)
      ? '<img src="' + lastFrame + '" style="width:100%;height:100%;object-fit:cover;display:block;">'
      : PLACEHOLDER;
    const hb = online ? '<div class="hbbar"><div class="hbfill" id="hb-' + p.Id + '" style="width:80%"></div></div>' : '';
    return '<div class="pcard ' + (online ? '' : 'off') + ' ' + (isSel ? 'sel' : '') + '" data-id="' + p.Id + '" onclick="selPoste(\'' + p.Id + '\')">' +
      '<div class="scr-prev" onclick="togglePreview(\'' + p.Id + '\',event)" ondblclick="openSessionDbl(\'' + p.Id + '\',event)">' +
        prevContent +
      '</div>' +
      '<div class="pinfo"><div class="ptop"><div class="pleft"><div class="pic">' + osIcon(p.Os) + '</div>' +
      '<div><div class="pname">' + p.Name + '</div><div class="pip">' + p.Ip + '</div></div>' +
      '</div><span class="pbadge ' + (online ? 'bon' : 'boff') + '">' + (online ? 'en ligne' : 'hors ligne') + '</span></div>' +
      hb + '</div></div>';
  }).join('');
  setInterval(() => {
    postes.filter(p => p.IsOnline).forEach(p => {
      const hb = document.getElementById('hb-' + p.Id);
      if (hb) { let w = parseInt(hb.style.width); hb.style.width = Math.max(10, w - 5) + '%'; }
    });
  }, 3000);
}

function togglePreview(id, e) {
  e.stopPropagation();
  const card = document.querySelector('.pcard[data-id="' + id + '"]');
  if (card) card.classList.toggle('expanded');
}

function openSessionDbl(id, e) {
  e.stopPropagation();
  sel = id;
  openSession();
}

function selPoste(id) {
  sel = sel === id ? null : id; renderPostes();
  const p = postes.find(x => x.Id === id);
  const c = document.getElementById('conn-content');
  if (sel && p) {
    c.innerHTML = '<div class="sel-row"><div class="sel-ic">' + osIcon(p.Os) + '</div>' +
      '<div><div class="sel-name">' + p.Name + '</div><div class="sel-ip">' + p.Ip + '</div></div></div>' +
      '<button class="cbtn" onclick="openSession()">🖥 Prendre la main</button>';
  } else { c.innerHTML = '<div class="no-sel">Sélectionner un poste en ligne</div>'; }
}

function openSession() {
  const p = postes.find(x => x.Id === sel); if (!p) return;
  document.getElementById('sess-name').textContent = p.Name;
  document.getElementById('sess-ip').textContent   = p.Ip;
  document.getElementById('ni-session').style.display = 'flex';
  send({ type: 'request_control', from: 'local', target: p.Id });
  nav('session'); attachInputCapture();
}

function closeSession() {
  document.getElementById('ni-session').style.display = 'none';
  sel = null; detachInputCapture(); renderPostes();
  document.getElementById('conn-content').innerHTML = '<div class="no-sel">Sélectionner un poste en ligne</div>';
  nav('postes');
}

function disconnect() { send({ type: 'disconnect_all' }); closeSession(); }

let _mouseMoveThrottle = null;

function attachInputCapture() {
  const screen = document.getElementById('main-screen-img'); if (!screen) return;
  screen.addEventListener('mousemove',   onScreenMouseMove);
  screen.addEventListener('mousedown',   onScreenMouseDown);
  screen.addEventListener('dblclick',    onScreenDblClick);
  screen.addEventListener('contextmenu', onScreenRightClick);
  screen.addEventListener('wheel',       onScreenWheel, { passive: false });
  document.addEventListener('keydown',   onKeyDown);
  document.addEventListener('keyup',     onKeyUp);
}

function detachInputCapture() {
  const screen = document.getElementById('main-screen-img'); if (!screen) return;
  screen.removeEventListener('mousemove',   onScreenMouseMove);
  screen.removeEventListener('mousedown',   onScreenMouseDown);
  screen.removeEventListener('dblclick',    onScreenDblClick);
  screen.removeEventListener('contextmenu', onScreenRightClick);
  screen.removeEventListener('wheel',       onScreenWheel);
  document.removeEventListener('keydown',   onKeyDown);
  document.removeEventListener('keyup',     onKeyUp);
}

function getScaledCoords(e) {
  const rect = e.target.getBoundingClientRect();
  return {
    x: Math.round((e.clientX - rect.left) * (previewW / rect.width)),
    y: Math.round((e.clientY - rect.top)  * (previewH / rect.height))
  };
}

function onScreenMouseMove(e) {
  if (!inputStates.mk) return;
  if (_mouseMoveThrottle) return;
  _mouseMoveThrottle = setTimeout(() => { _mouseMoveThrottle = null; }, 30);
  const { x, y } = getScaledCoords(e); send({ type: 'mouse_move', x, y });
}

function onScreenMouseDown(e) {
  if (e.button === 2 || !inputStates.mk) return;
  const { x, y } = getScaledCoords(e); send({ type: 'mouse_click', x, y, button: 'left' });
}

function onScreenDblClick(e) {
  if (!inputStates.mk) return;
  const { x, y } = getScaledCoords(e); send({ type: 'mouse_click', x, y, button: 'left', double: true });
}

function onScreenRightClick(e) {
  e.preventDefault(); if (!inputStates.mk) return;
  const { x, y } = getScaledCoords(e); send({ type: 'mouse_click', x, y, button: 'right' });
}

function onScreenWheel(e) {
  e.preventDefault(); if (!inputStates.mk) return;
  send({ type: 'mouse_scroll', delta: e.deltaY > 0 ? -1 : 1 });
}

function onKeyDown(e) {
  if (!inSession || !inputStates.mm) return;
  if (['F5','F12','Tab'].includes(e.key)) e.preventDefault();
  send({ type: 'key_down', vk: e.keyCode });
}

function onKeyUp(e) {
  if (!inSession || !inputStates.mm) return;
  send({ type: 'key_up', vk: e.keyCode });
}

function tglInput(k) {
  inputStates[k] = !inputStates[k];
  document.getElementById('tgl-' + k).classList.toggle('on', inputStates[k]);
  const msg = { type: 'set_input', target: k.startsWith('o') ? 'owner' : 'controller' };
  if (k === 'ok' || k === 'mk') msg.keyboard = inputStates[k];
  if (k === 'om' || k === 'mm') msg.mouse    = inputStates[k];
  send(msg);
  document.getElementById('lock-overlay').classList.toggle('show', !inputStates.ok && !inputStates.om);
}

function applyInputState(msg) {
  if (msg.target === 'owner') {
    if (msg.keyboard !== undefined) { inputStates.ok = msg.keyboard; document.getElementById('tgl-ok').classList.toggle('on', msg.keyboard); }
    if (msg.mouse    !== undefined) { inputStates.om = msg.mouse;    document.getElementById('tgl-om').classList.toggle('on', msg.mouse); }
  }
  document.getElementById('lock-overlay').classList.toggle('show', !inputStates.ok && !inputStates.om);
}

function addNotif(msg) {
  notifCount++; updateNotifBadge();
  const list = document.getElementById('notif-list');
  const initials = msg.from ? msg.from.substring(0, 2).toUpperCase() : 'XX';
  const t = new Date().toLocaleTimeString('fr-FR');
  const card = document.createElement('div'); card.className = 'notif-card';
  card.innerHTML = '<div class="notif-top"><div class="notif-av">' + initials + '</div>' +
    '<div><div class="notif-title">' + (msg.from || 'Inconnu') + ' demande la prise de main</div>' +
    '<div class="notif-sub">LAN — ' + t + '</div></div>' +
    '<div class="notif-time">À l\'instant</div></div>' +
    '<div class="notif-btns">' +
    '<div class="nbtn nbtn-acc" onclick="acceptNotif(this,\'' + msg.from + '\')">✓ Accepter</div>' +
    '<div class="nbtn nbtn-ref" onclick="refuseNotif(this)">✕ Refuser</div></div>';
  list.prepend(card);
}

function acceptNotif(btn, from) {
  btn.closest('.notif-card').remove(); notifCount = Math.max(0, notifCount - 1); updateNotifBadge();
  send({ type: 'grant_control', to: from });
}

function refuseNotif(btn) {
  btn.closest('.notif-card').remove(); notifCount = Math.max(0, notifCount - 1); updateNotifBadge();
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
  const t = document.getElementById('tgl-p-' + k); const on = t.classList.toggle('on');
  if (k === 'prev') { previewOn = on; send({ type: 'set_preview', enabled: on }); renderPostes(); }
}

function grantHand(btn) { btn.textContent = 'Reprendre'; btn.onclick = () => revokeHand(btn); send({ type: 'grant_control', to: btn.closest('.obs-item').dataset.id }); }
function revokeHand(btn) { btn.textContent = 'Passer la main'; btn.onclick = () => grantHand(btn); send({ type: 'revoke_control' }); }
function kickObs(btn) { btn.closest('.obs-item').remove(); }

setInterval(() => { if (ws && ws.readyState === 1) send({ type: 'get_postes' }); }, 5000);
window.addEventListener('DOMContentLoaded', connect);


// ── Service control ───────────────────────────────────────────────────────────

function svcUpdateUI(status) {
  const badge = document.getElementById('svc-badge');
  const txt   = document.getElementById('svc-status-txt');
  if (!badge || !txt) return;
  badge.className = 'svc-badge ' + status;
  if (status === 'running') {
    badge.textContent = '● En service';
    txt.textContent   = 'AuraCtrlService tourne normalement';
  } else if (status === 'stopped') {
    badge.textContent = '● Arrêté';
    txt.textContent   = 'Le service est arrêté';
  } else {
    badge.textContent = '? Inconnu';
    txt.textContent   = 'Statut indéterminé';
  }
}

function svcStart()   { if (window.electronAPI) { svcUpdateUI('unknown'); window.electronAPI.serviceStart(); } }
function svcStop()    { if (window.electronAPI) { svcUpdateUI('unknown'); window.electronAPI.serviceStop(); } }
function svcRestart() { if (window.electronAPI) { svcUpdateUI('unknown'); window.electronAPI.serviceRestart(); } }

if (window.electronAPI) {
  window.electronAPI.onServiceStatus((status) => svcUpdateUI(status));
  window.electronAPI.serviceStatus();
}
