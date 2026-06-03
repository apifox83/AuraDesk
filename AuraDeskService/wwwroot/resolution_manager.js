// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
//  RÃ‰SOLUTION MANAGER â€” Ã  coller Ã  la fin de app.js
// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

// â”€â”€ Ã‰tat â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

