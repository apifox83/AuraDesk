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
      postes = msg.data; renderPostes(); updateStats(); setTimeout(renderPostes, 500);
      if (window.electronAPI) window.electronAPI.updatePostesCount(postes.filter(p => p.IsOnline).length);
      break;
    case 'screen_frame':
      lastFrame = 'data:image/jpeg;base64,' + msg.data;
      updateScreenPreviews(); updateMainScreen();
      break;
    case 'request_control':    addNotif(msg);           break;
    case 'resolutions_list':   onResolutionsList(msg);   break;
    case 'resolution_changed': onResolutionChanged(msg); break;
    case 'resolution_reverted':onResolutionReverted(msg); break;
    case 'adapters_list':       onAdaptersList(msg);        break;
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
  if (os === 'Win32NT') return 'ðŸ–¥'; if (os === 'Unix') return 'ðŸ§'; return 'âš¡';
}

const PLACEHOLDER = '<svg viewBox="0 0 240 135" xmlns="http://www.w3.org/2000/svg" style="width:100%;height:100%">' +
  '<rect width="240" height="135" fill="#040810"/>' +
  '<rect x="55" y="15" width="130" height="82" rx="5" fill="none" stroke="#1c2840" stroke-width="2"/>' +
  '<rect x="60" y="20" width="120" height="72" fill="#0a1628"/>' +
  '<rect x="98" y="97" width="44" height="6" rx="2" fill="#1c2840"/>' +
  '<rect x="86" y="103" width="68" height="4" rx="2" fill="#1c2840"/>' +
  '<text x="120" y="61" text-anchor="middle" fill="#243050" font-size="10" font-family="system-ui">hors ligne</text>' +
  '</svg>';

function buildScreenBadge(p) {
  if (!p.IsOnline) return '';
  const screens = p.Screens;
  if (!screens || screens.length === 0) return '';
  const count = screens.length;
  // Résolution = celle du premier écran (ou le plus grand)
  const main = screens.reduce((a, b) => (b.Width * b.Height > a.Width * a.Height ? b : a), screens[0]);
  const res = main.Width + '×' + main.Height;
  const monTxt = count > 1 ? '⊞ ×' + count : '⊞ ×1';
  return '<div class="scr-badge">' +
    '<span class="scr-badge-mon">' + monTxt + '</span>' +
    '<span class="scr-badge-sep">|</span>' +
    '<span class="scr-badge-res">' + res + '</span>' +
    '</div>';
}

function buildScreenBadge(p) {
  if (!p.IsOnline || !p.Screens || p.Screens.length === 0) return '';
  const count = p.Screens.length;
  const main  = p.Screens.reduce((a, b) => (b.Width * b.Height > a.Width * a.Height ? b : a), p.Screens[0]);
  const res   = main.Width + 'x' + main.Height;
  const mon   = count > 1 ? 'x' + count : 'x1';
  return '<div class="scr-badge"><span class="scr-badge-mon">' + mon + '</span><span class="scr-badge-sep">|</span><span class="scr-badge-res">' + res + '</span></div>';
}

function renderPostes() {
  const g = document.getElementById('postes-grid');
  g.innerHTML = postes.map(p => {
    const online = p.IsOnline;
    const isSel  = sel === p.Id;
    const prevContent = (previewOn && online && lastFrame)
      ? '<img src="' + lastFrame + '" style="width:100%;height:100%;object-fit:cover;display:block;">'
      : PLACEHOLDER;
    const screenBadge = buildScreenBadge(p);
    const hb = online ? '<div class="hbbar"><div class="hbfill" id="hb-' + p.Id + '" style="width:80%"></div></div>' : '';
    return '<div class="pcard ' + (online ? '' : 'off') + ' ' + (isSel ? 'sel' : '') + '" data-id="' + p.Id + '" onclick="selPoste(\'' + p.Id + '\')">' +
      '<div class="scr-prev" onclick="togglePreview(\'' + p.Id + '\',event)" ondblclick="openSessionDbl(\'' + p.Id + '\',event)">' +
        prevContent + screenBadge + (online ? '<div class="scr-settings-btn" onclick="openResPopover(this,event)" title="Infos poste">&#9881;</div>' : '') + (online ? '<div class="scr-settings-btn" onclick="openResPopover(this,event)" title="Infos poste">&#9881;</div>' : '') +
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
      '<button class="cbtn" onclick="openSession()">ðŸ–¥ Prendre la main</button>';
  } else { c.innerHTML = '<div class="no-sel">SÃ©lectionner un poste en ligne</div>'; }
}

let wsRemote = null;

function openSession() {
  const p = postes.find(x => x.Id === sel); if (!p) return;
  document.getElementById('sess-name').textContent = p.Name;
  document.getElementById('sess-ip').textContent   = p.Ip;
  document.getElementById('ni-session').style.display = 'flex';
  nav('session');

  // Connexion WebSocket directe vers le poste distant
  if (wsRemote) { wsRemote.close(); wsRemote = null; }
  wsRemote = new WebSocket('ws://' + p.Ip + ':47201/ws/');
  wsRemote.onopen = () => {
    console.log('Connecté au poste distant: ' + p.Ip);
    wsRemote.send(JSON.stringify({ type: 'set_preview', enabled: true }));
  };
  wsRemote.onmessage = (e) => {
    const msg = JSON.parse(e.data);
    if (msg.type === 'screen_frame') {
      lastFrame = 'data:image/jpeg;base64,' + msg.data;
      const img = document.getElementById('main-screen-img');
      if (img) img.src = lastFrame;
    }
  };
  wsRemote.onclose = () => { console.log('Déconnecté du poste distant'); };
  wsRemote.onerror = (e) => { console.error('Erreur WebSocket distant', e); };

  attachInputCapture();
}

function closeSession() {
  document.getElementById('ni-session').style.display = 'none';
  sel = null; detachInputCapture(); renderPostes();
  document.getElementById('conn-content').innerHTML = '<div class="no-sel">SÃ©lectionner un poste en ligne</div>';
  nav('postes');
}

function sendRemote(obj) {
  if (wsRemote && wsRemote.readyState === 1) wsRemote.send(JSON.stringify(obj));
}

function disconnect() {
  if (wsRemote) { wsRemote.close(); wsRemote = null; }
  closeSession();
}

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
  const { x, y } = getScaledCoords(e); sendRemote({ type: 'mouse_move', x, y });
}

function onScreenMouseDown(e) {
  if (e.button === 2 || !inputStates.mk) return;
  const { x, y } = getScaledCoords(e); sendRemote({ type: 'mouse_click', x, y, button: 'left' });
}

function onScreenDblClick(e) {
  if (!inputStates.mk) return;
  const { x, y } = getScaledCoords(e); sendRemote({ type: 'mouse_click', x, y, button: 'left', double: true });
}

function onScreenRightClick(e) {
  e.preventDefault(); if (!inputStates.mk) return;
  const { x, y } = getScaledCoords(e); sendRemote({ type: 'mouse_click', x, y, button: 'right' });
}

function onScreenWheel(e) {
  e.preventDefault(); if (!inputStates.mk) return;
  sendRemote({ type: 'mouse_scroll', delta: e.deltaY > 0 ? -1 : 1 });
}

function onKeyDown(e) {
  if (!inSession || !inputStates.mm) return;
  if (['F5','F12','Tab'].includes(e.key)) e.preventDefault();
  sendRemote({ type: 'key_down', vk: e.keyCode });
}

function onKeyUp(e) {
  if (!inSession || !inputStates.mm) return;
  sendRemote({ type: 'key_up', vk: e.keyCode });
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
    '<div class="notif-sub">LAN â€” ' + t + '</div></div>' +
    '<div class="notif-time">Ã€ l\'instant</div></div>' +
    '<div class="notif-btns">' +
    '<div class="nbtn nbtn-acc" onclick="acceptNotif(this,\'' + msg.from + '\')">âœ“ Accepter</div>' +
    '<div class="nbtn nbtn-ref" onclick="refuseNotif(this)">âœ• Refuser</div></div>';
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


// â”€â”€ Service control â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

function svcUpdateUI(status) {
  const badge = document.getElementById('svc-badge');
  const txt   = document.getElementById('svc-status-txt');
  if (!badge || !txt) return;
  badge.className = 'svc-badge ' + status;
  if (status === 'running') {
    badge.textContent = 'â— En service';
    txt.textContent   = 'AuraDeskService tourne normalement';
  } else if (status === 'stopped') {
    badge.textContent = 'â— ArrÃªtÃ©';
    txt.textContent   = 'Le service est arrÃªtÃ©';
  } else {
    badge.textContent = '? Inconnu';
    txt.textContent   = 'Statut indÃ©terminÃ©';
  }
}

function svcStart()   { if (window.electronAPI) { svcUpdateUI('unknown'); window.electronAPI.serviceStart(); } }
function svcStop()    { if (window.electronAPI) { svcUpdateUI('unknown'); window.electronAPI.serviceStop(); } }
function svcRestart() { if (window.electronAPI) { svcUpdateUI('unknown'); window.electronAPI.serviceRestart(); } }

if (window.electronAPI) {
  window.electronAPI.onServiceStatus((status) => svcUpdateUI(status));
  window.electronAPI.serviceStatus();
}









// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
//  RÃ‰SOLUTION MANAGER â€” Ã  coller Ã  la fin de app.js
// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

// â”€â”€ Ã‰tat â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
let _resScreens   = [];          // donnÃ©es reÃ§ues du backend
let _resPrevState = {};          // { screenId: {w,h,hz} } avant changement temporaire
let _cdInterval   = null;        // countdown timer
let _popoverTarget = null;       // poste courant du popover

// â”€â”€ RÃ©ception des messages WebSocket (Ã  ajouter dans handleMessage) â”€â”€â”€â”€â”€â”€â”€
//
//  case 'resolutions_list':   onResolutionsList(msg);   break;
//  case 'resolution_changed': onResolutionChanged(msg); break;
//  case 'resolution_reverted':onResolutionReverted(msg);break;

function onResolutionsList(msg) {
  _resScreens = msg.screens;
  renderResPopover();
  renderResSession();
}

function onResolutionChanged(msg) {
  if (!msg.success) { showResError('Ã‰chec du changement de rÃ©solution'); return; }
  // Mettre Ã  jour _resScreens
  const s = _resScreens.find(x => x.id === msg.screenId);
  if (s) { s.current = { w: msg.w, h: msg.h, hz: s.current.hz }; }
  renderResSession();
  updateScreenBadge(msg.screenId, msg.w, msg.h);
}

function onResolutionReverted(msg) {
  stopCountdown();
  const s = _resScreens.find(x => x.id === msg.screenId);
  if (s && _resPrevState[msg.screenId]) {
    s.current = _resPrevState[msg.screenId];
    delete _resPrevState[msg.screenId];
  }
  renderResSession();
  updateScreenBadge(msg.screenId, msg.w, msg.h);
}

// â”€â”€ Popover sur miniature â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

function openResPopover(postId, btnEl, event) {
  event.stopPropagation();
  _popoverTarget = postId;

  // CrÃ©er le popover s'il n'existe pas
  let pop = document.getElementById('res-popover');
  if (!pop) {
    pop = document.createElement('div');
    pop.id = 'res-popover';
    pop.className = 'res-popover';
    pop.innerHTML = '<div class="res-pop-ttl">RÃ©solution des Ã©crans</div>' +
      '<div id="res-pop-body">Chargement...</div>';
    document.body.appendChild(pop);
    // Fermer au clic extÃ©rieur
    document.addEventListener('click', (e) => {
      if (!pop.contains(e.target)) closeResPopover();
    });
  }

  // Positionner prÃ¨s du bouton
  const rect = btnEl.getBoundingClientRect();
  pop.style.top  = (rect.bottom + 6) + 'px';
  pop.style.left = Math.min(rect.left, window.innerWidth - 290) + 'px';
  pop.classList.add('show');

  // Demander les rÃ©solutions au backend
  send({ type: 'get_resolutions' });
}

function closeResPopover() {
  const pop = document.getElementById('res-popover');
  if (pop) pop.classList.remove('show');
  _popoverTarget = null;
}

function renderResPopover() {
  const body = document.getElementById('res-pop-body');
  if (!body) return;

  if (!_resScreens || _resScreens.length === 0) {
    body.innerHTML = '<div style="font-size:11px;color:var(--t3);">Aucun Ã©cran dÃ©tectÃ©</div>';
    return;
  }

  body.innerHTML =
    _resScreens.map((s, si) => resMonitorBlock(s, si, 'pop')).join('') +
    resModeBlock('pop') +
    resCountdownBlock('pop');
}

function resMonitorBlock(s, si, ctx) {
  const opts = s.modes.map(m =>
    `<option value="${m.W}|${m.H}|${m.Hz}" ${m.W===s.current.w&&m.H===s.current.h?'selected':''}>` +
    `${m.W}Ã—${m.H} @ ${m.Hz}Hz</option>`
  ).join('');
  return `<div class="res-monitor-row">
    <div class="res-monitor-name">${s.name}</div>
    <div class="res-monitor-cur">Actuel : ${s.current.w}Ã—${s.current.h} @ ${s.current.hz}Hz</div>
    <div class="res-selrow">
      <select class="res-sel" id="res-sel-${ctx}-${si}" onchange="resMarkDirty('${ctx}',${si})">${opts}</select>
      <button class="res-apply" id="res-apply-${ctx}-${si}" onclick="applyResolution('${s.id}','${ctx}',${si})" disabled>Appliquer</button>
    </div>
  </div>`;
}

function resModeBlock(ctx) {
  return `<div class="res-mode-row">
    <label><input type="radio" name="res-mode-${ctx}" value="temp" checked> Temporaire</label>
    <input class="res-revsec" type="number" id="res-revsec-${ctx}" value="15" min="5" max="60">s
    <label><input type="radio" name="res-mode-${ctx}" value="perm"> Permanent</label>
  </div>`;
}

function resCountdownBlock(ctx) {
  return `<div class="res-countdown" id="res-cd-${ctx}">
    <span class="res-cd-txt" id="res-cd-txt-${ctx}">Retour dans <b id="res-cd-n-${ctx}">15</b>s</span>
    <button class="res-cd-keep" onclick="keepResolution('${ctx}')">âœ“ Conserver</button>
  </div>`;
}

function resMarkDirty(ctx, si) {
  const btn = document.getElementById(`res-apply-${ctx}-${si}`);
  if (btn) btn.disabled = false;
}

function applyResolution(screenId, ctx, si) {
  const sel  = document.getElementById(`res-sel-${ctx}-${si}`);
  if (!sel) return;
  const [w, h, hz] = sel.value.split('|').map(Number);

  const modeRadio = document.querySelector(`input[name="res-mode-${ctx}"]:checked`);
  const permanent = modeRadio ? modeRadio.value === 'perm' : false;
  const revertSec = permanent ? 0 : parseInt(document.getElementById(`res-revsec-${ctx}`)?.value || '15');

  // Sauvegarder l'Ã©tat actuel
  const screen = _resScreens.find(x => x.id === screenId);
  if (screen) _resPrevState[screenId] = { ...screen.current };

  // Envoyer au backend du poste DISTANT via wsRemote
  const msg = { type: 'set_resolution', screenId, w, h, hz, permanent, revertSec };
  if (wsRemote && wsRemote.readyState === 1) wsRemote.send(JSON.stringify(msg));
  else send(msg);  // fallback local

  // DÃ©sactiver le bouton
  const btn = document.getElementById(`res-apply-${ctx}-${si}`);
  if (btn) { btn.disabled = true; btn.textContent = '...'; }

  // DÃ©marrer le countdown si temporaire
  if (!permanent && revertSec > 0) startCountdown(ctx, revertSec, screenId);
}

function startCountdown(ctx, sec, screenId) {
  stopCountdown();
  let remaining = sec;
  const cd = document.getElementById(`res-cd-${ctx}`);
  const nn = document.getElementById(`res-cd-n-${ctx}`);
  if (cd) cd.classList.add('show');
  if (nn) nn.textContent = remaining;

  _cdInterval = setInterval(() => {
    remaining--;
    if (nn) nn.textContent = remaining;
    if (remaining <= 0) {
      stopCountdown();
      // Le backend va revenir automatiquement â€” on attend l'event resolution_reverted
    }
  }, 1000);
}

function stopCountdown() {
  if (_cdInterval) { clearInterval(_cdInterval); _cdInterval = null; }
  document.querySelectorAll('.res-countdown').forEach(el => el.classList.remove('show'));
}

function keepResolution(ctx) {
  stopCountdown();
  send({ type: 'keep_resolution' });
  // RÃ©activer les boutons apply
  document.querySelectorAll('.res-apply').forEach(b => { b.textContent = 'Appliquer'; });
}

function showResError(msg) {
  console.error('[AuraDesk] RÃ©solution:', msg);
  // TODO: toast UI si souhaitÃ©
}

// â”€â”€ Panel dans la session active â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

function renderResSession() {
  const container = document.getElementById('res-sess-monitors');
  if (!container) return;

  if (!_resScreens || _resScreens.length === 0) {
    container.innerHTML = '<div style="font-size:11px;color:var(--t3);">Infos non disponibles</div>';
    return;
  }

  container.innerHTML = _resScreens.map((s, si) =>
    `<div class="res-sess-row">
      <span class="res-sess-lbl">${s.name}</span>
      <span class="res-sess-cur">${s.current.w}Ã—${s.current.h} @ ${s.current.hz}Hz</span>
      <div class="res-selrow" style="flex:1;justify-content:flex-end;">
        <select class="res-sel" id="res-sel-sess-${si}" style="max-width:150px;" onchange="resMarkDirty('sess',${si})">
          ${s.modes.map(m => `<option value="${m.W}|${m.H}|${m.Hz}" ${m.W===s.current.w&&m.H===s.current.h?'selected':''}>${m.W}Ã—${m.H}@${m.Hz}</option>`).join('')}
        </select>
        <button class="res-apply" id="res-apply-sess-${si}" onclick="applyResolution('${s.id}','sess',${si})" disabled>â‡„</button>
      </div>
    </div>`
  ).join('') + resCountdownBlock('sess') + resModeBlock('sess');
}

// Mise Ã  jour du badge sur la carte poste aprÃ¨s changement
function updateScreenBadge(screenId, w, h) {
  // Mettre Ã  jour dans postes[] pour que renderPostes() soit cohÃ©rent au prochain rafraÃ®chi
  postes.forEach(p => {
    if (p.Screens) {
      const s = p.Screens.find(x => x.Id === screenId);
      if (s) { s.Width = w; s.Height = h; }
    }
  });
  // Re-render immÃ©diat des cartes
  renderPostes();
}

// â”€â”€ RequÃªte initiale rÃ©solutions Ã  l'ouverture de session â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// (appeler dans openSession() aprÃ¨s connexion wsRemote)
function requestResolutions() {
  if (wsRemote && wsRemote.readyState === 1)
    wsRemote.send(JSON.stringify({ type: 'get_resolutions' }));
}





