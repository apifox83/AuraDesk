// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
//  POSTE PANEL v2 â€” navigation par onglets, événements JS purs
// â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

let _panel       = null;
let _panelPoste  = null;
let _activeTab   = 'machine';

// â”€â”€ Ouvrir â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

function openResPopover(btnEl, event) {
  event.stopPropagation();
  event.preventDefault();

  const card = btnEl.closest('.pcard');
  if (!card) return;
  const posteId = card.dataset.id;
  const poste   = postes.find(p => p.Id === posteId);
  if (!poste) return;
  _panelPoste = poste;
  _activeTab  = 'machine';

  if (!_panel) {
    _panel = document.createElement('div');
    _panel.id = 'poste-panel';
    _panel.className = 'poste-panel';
    _panel.addEventListener('click',     e => e.stopPropagation());
    _panel.addEventListener('mousedown', e => e.stopPropagation());
    document.body.appendChild(_panel);

    document.addEventListener('click', e => {
      if (_panel && _panel.style.display !== 'none' &&
          !_panel.contains(e.target) &&
          !e.target.classList.contains('scr-settings-btn'))
        closePostePanel();
    });
  }

  buildPanel(poste);
  positionPanel(btnEl);

  sendToPoste(posteId, { type: 'get_resolutions' });
  sendToPoste(posteId, { type: 'get_adapters' });
}

function positionPanel(btnEl) {
  const rect = btnEl.getBoundingClientRect();
  const pw   = 300;
  let   left = rect.left;
  if (left + pw > window.innerWidth - 10) left = window.innerWidth - pw - 10;
  _panel.style.top     = (rect.bottom + 6) + 'px';
  _panel.style.left    = left + 'px';
  _panel.style.display = 'flex';
}

function closePostePanel() {
  if (_panel) _panel.style.display = 'none';
  _panelPoste = null;
}

// â”€â”€ Construction du panneau â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

function buildPanel(poste) {
  _panel.innerHTML = '';

  // â”€â”€ Header â”€â”€
  const header = el('div', 'pp-header');
  const title  = el('div', 'pp-title');
  const ic     = el('div', 'pp-ic'); ic.textContent = osIcon(poste.Os);
  const info   = el('div');
  const name   = el('div', 'pp-name'); name.textContent = poste.Name;
  const ip     = el('div', 'pp-ip');  ip.textContent  = poste.Ip;
  info.append(name, ip);
  title.append(ic, info);
  const closeBtn = el('div', 'pp-close'); closeBtn.textContent = 'âœ•';
  closeBtn.addEventListener('click', e => { e.stopPropagation(); closePostePanel(); });
  header.append(title, closeBtn);

  // â”€â”€ Nav â”€â”€
  const nav = el('div', 'pp-nav');
  const tabs = [
    { id: 'machine', icon: 'ti-device-desktop', label: 'Machine' },
    { id: 'screens', icon: 'ti-device-tv',      label: 'Ã‰crans'  },
    { id: 'network', icon: 'ti-network',         label: 'Réseau'  },
    { id: 'resol',   icon: 'ti-adjustments',     label: 'Résol.'  },
  ];
  tabs.forEach(t => {
    const btn = el('div', 'pp-nav-btn' + (t.id === _activeTab ? ' active' : ''));
    btn.dataset.tab = t.id;
    const ico = document.createElement('img');
    ico.src = t.icon; ico.alt = t.label;
    const lbl = el('span'); lbl.textContent = t.label;
    btn.append(ico, lbl);
    btn.addEventListener('click', e => { e.stopPropagation(); switchTab(t.id); });
    nav.appendChild(btn);
  });

  // â”€â”€ Viewport scrollable â”€â”€
  const viewport = el('div', 'pp-viewport');
  const slider   = el('div', 'pp-slider');
  slider.id = 'pp-slider';

  slider.appendChild(buildTabMachine(poste));
  slider.appendChild(buildTabScreens(poste));
  slider.appendChild(buildTabNetwork(poste));
  slider.appendChild(buildTabResol(poste));

  viewport.appendChild(slider);
  _panel.append(header, nav, viewport);

  activateTab(_activeTab, false);
}

function switchTab(tabId) {
  _activeTab = tabId;
  activateTab(tabId, true);
}

function activateTab(tabId, animate) {
  const tabs  = ['machine','screens','network','resol'];
  const idx   = tabs.indexOf(tabId);
  const slider = document.getElementById('pp-slider');
  if (!slider) return;

  slider.style.transition = animate ? 'transform 0.25s cubic-bezier(0.4,0,0.2,1)' : 'none';
  slider.style.transform  = 'translateX(-' + (idx * 300) + 'px)';

  _panel.querySelectorAll('.pp-nav-btn').forEach((btn, i) => {
    btn.classList.toggle('active', i === idx);
  });
}

// â”€â”€ Tab Machine â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

function buildTabMachine(poste) {
  const tab = el('div', 'pp-tab');

  tab.appendChild(secTitle('Informations machine'));
  tab.appendChild(editRow('Nom machine', poste.Name, (val) => {
    sendToPoste(poste.Id, { type: 'set_hostname', hostname: val });
    const p = postes.find(x => x.Id === poste.Id);
    if (p) p.Name = val;
    const nameEl = _panel.querySelector('.pp-name');
    if (nameEl) nameEl.textContent = val;
  }));
  tab.appendChild(readRow('OS',         poste.Os));
  tab.appendChild(readRow('ID',         poste.Id));
  tab.appendChild(readRow('IP LAN',     poste.Ip));
  tab.appendChild(readRow('IP virtuelle', poste.VirtualIp || 'â€”'));

  return tab;
}

// â”€â”€ Tab Ã‰crans â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

function buildTabScreens(poste) {
  const tab = el('div', 'pp-tab');
  tab.id = 'pp-tab-screens';
  tab.appendChild(secTitle('Ã‰crans connectés'));

  const screens = poste.Screens || [];
  if (screens.length === 0) {
    const empty = el('div', 'pp-empty'); empty.textContent = 'Chargement...';
    tab.appendChild(empty);
  } else {
    screens.forEach((s, i) => {
      const row = el('div', 'pp-screen-row' + (i === 0 ? ' pp-screen-active' : ''));
      row.id = 'pp-scr-row-' + i;

      const dot  = el('div', 'pp-scr-dot'); dot.id = 'pp-scr-dot-' + i; dot.textContent = i === 0 ? 'â—' : 'â—‹';
      const info = el('div');
      const nm   = el('div', 'pp-scr-name'); nm.textContent = s.Name;
      const res  = el('div', 'pp-scr-res');  res.textContent = s.Width + 'Ã—' + s.Height + ' @' + s.Hz + 'Hz';
      info.append(nm, res);

      if (i === 0) {
        const badge = el('span', 'pp-scr-badge'); badge.textContent = 'ACTIF';
        row.append(dot, info, badge);
      } else {
        row.append(dot, info);
      }

      row.addEventListener('click', e => {
        e.stopPropagation();
        _panel.querySelectorAll('.pp-screen-row').forEach((r, j) => {
          r.classList.toggle('pp-screen-active', j === i);
          const d = document.getElementById('pp-scr-dot-' + j);
          if (d) d.textContent = j === i ? 'â—' : 'â—‹';
        });
        sendToPoste(poste.Id, { type: 'set_active_screen', screenId: s.Id });
      });

      tab.appendChild(row);
    });
  }
  return tab;
}

// â”€â”€ Tab Réseau â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

function buildTabNetwork(poste) {
  const tab = el('div', 'pp-tab');
  tab.id = 'pp-tab-network';
  tab.appendChild(secTitle('Cartes réseau'));

  const adapters = poste.Adapters || [];
  if (adapters.length === 0) {
    const empty = el('div', 'pp-empty'); empty.textContent = 'Chargement...';
    tab.appendChild(empty);
  } else {
    adapters.forEach(a => {
      const card = el('div', 'pp-adp');

      const hdr   = el('div', 'pp-adp-hdr');
      const dot   = el('span', a.IsUp ? 'pp-adp-dot pp-adp-up' : 'pp-adp-dot pp-adp-dn');
      dot.textContent = 'â—';
      const aname = el('span', 'pp-adp-name'); aname.textContent = a.Name;
      const state = el('span', 'pp-adp-state'); state.textContent = a.IsUp ? 'actif' : 'inactif';
      hdr.append(dot, aname, state);
      card.appendChild(hdr);

      card.appendChild(editRow('IP',         a.Ip      || 'â€”', val => sendToPoste(poste.Id, { type:'set_network', adapter:a.Name, field:'ip',      value:val })));
      card.appendChild(editRow('Masque',     a.Mask    || 'â€”', val => sendToPoste(poste.Id, { type:'set_network', adapter:a.Name, field:'mask',    value:val })));
      card.appendChild(editRow('Passerelle', a.Gateway || 'â€”', val => sendToPoste(poste.Id, { type:'set_network', adapter:a.Name, field:'gateway', value:val })));
      card.appendChild(readRow('MAC', a.Mac || 'â€”'));

      tab.appendChild(card);
    });
  }
  return tab;
}

// â”€â”€ Tab Résolution â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

function buildTabResol(poste) {
  const tab = el('div', 'pp-tab');
  tab.id = 'pp-tab-resol';
  tab.appendChild(secTitle('Résolution des écrans'));

  const screens = poste.Screens || [];
  if (screens.length === 0) {
    const empty = el('div', 'pp-empty'); empty.textContent = 'Chargement...';
    tab.appendChild(empty);
  } else {
    screens.forEach((s, si) => {
      const row  = el('div', 'pp-resol-row');
      const lbl  = el('div', 'pp-resol-lbl'); lbl.textContent = s.Name;
      const cur  = el('div', 'pp-resol-cur'); cur.textContent = s.Width + 'Ã—' + s.Height + ' @' + s.Hz + 'Hz';
      row.append(lbl, cur);
      tab.appendChild(row);

      if (s.Modes && s.Modes.length > 0) {
        const selRow = el('div', 'pp-resol-sel-row');
        const sel    = document.createElement('select');
        sel.className = 'res-sel';
        s.Modes.forEach(m => {
          const opt = document.createElement('option');
          opt.value = m.W + '|' + m.H + '|' + m.Hz;
          opt.textContent = m.W + 'Ã—' + m.H + ' @' + m.Hz + 'Hz';
          if (m.W === s.Width && m.H === s.Height) opt.selected = true;
          sel.appendChild(opt);
        });

        const applyBtn = el('button', 'res-apply'); applyBtn.textContent = 'Appliquer';
        applyBtn.disabled = true;
        sel.addEventListener('change', () => { applyBtn.disabled = false; });
        applyBtn.addEventListener('click', e => {
          e.stopPropagation();
          const [w, h, hz] = sel.value.split('|').map(Number);
          const permanent  = _panel.querySelector('input[name="res-mode-panel"]:checked')?.value === 'perm';
          const revertSec  = permanent ? 0 : parseInt(_panel.querySelector('#res-revsec-panel')?.value || '15');
          if (poste.Screens) _resPrevState[s.Id] = { w: s.Width, h: s.Height, hz: s.Hz };
          sendToPoste(poste.Id, { type:'set_resolution', screenId:s.Id, w, h, hz, permanent, revertSec });
          applyBtn.disabled = true; applyBtn.textContent = '...';
          if (!permanent && revertSec > 0) startCountdown('panel', revertSec, s.Id);
        });

        selRow.append(sel, applyBtn);
        tab.appendChild(selRow);
      }
    });

    const modeDiv = document.createElement('div');
    modeDiv.innerHTML = resModeBlock('panel') + resCountdownBlock('panel');
    tab.appendChild(modeDiv);
  }
  return tab;
}

// â”€â”€ Helpers DOM â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

function el(tag, cls) {
  const e = document.createElement(tag);
  if (cls) e.className = cls;
  return e;
}

function secTitle(text) {
  const d = el('div', 'pp-sec-ttl'); d.textContent = text; return d;
}

function readRow(label, value) {
  const row  = el('div', 'pp-row');
  const lbl  = el('span', 'pp-lbl'); lbl.textContent = label;
  const val  = el('span', 'pp-val'); val.textContent = value || 'â€”';
  row.append(lbl, val); return row;
}

function editRow(label, value, onConfirm) {
  const row      = el('div', 'pp-row');
  const lbl      = el('span', 'pp-lbl'); lbl.textContent = label;
  const editable = el('div', 'pp-editable');
  const valSpan  = el('span', 'pp-val'); valSpan.textContent = value || 'â€”';
  const editBtn  = el('div', 'pp-edit-ic'); editBtn.textContent = 'âœ';

  editBtn.addEventListener('click', e => {
    e.stopPropagation();
    const cur = valSpan.textContent;
    const inp = document.createElement('input');
    inp.className = 'pp-input'; inp.value = cur;

    const ok  = el('div', 'pp-edit-ic pp-edit-ok');  ok.textContent = 'âœ“';
    const can = el('div', 'pp-edit-ic pp-edit-cancel'); can.textContent = 'âœ•';

    editable.innerHTML = '';
    editable.append(inp, ok, can);
    inp.focus();

    const confirm = () => {
      const v = inp.value.trim(); if (!v) return;
      valSpan.textContent = v;
      editable.innerHTML = '';
      editable.append(valSpan, editBtn);
      onConfirm(v);
    };
    const cancel = () => {
      editable.innerHTML = '';
      editable.append(valSpan, editBtn);
    };

    ok.addEventListener('click',  e => { e.stopPropagation(); confirm(); });
    can.addEventListener('click', e => { e.stopPropagation(); cancel();  });
    inp.addEventListener('keydown', e => {
      if (e.key === 'Enter')  confirm();
      if (e.key === 'Escape') cancel();
    });
  });

  editable.append(valSpan, editBtn);
  row.append(lbl, editable);
  return row;
}

// â”€â”€ Mise Ã  jour dynamique â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

function onAdaptersList(msg) {
  const p = postes.find(x => x.Id === msg.posteId || x.Ip === msg.ip);
  if (!p) return;
  p.Adapters = msg.adapters;
  if (_panelPoste && _panelPoste.Id === p.Id) {
    const networkTab = document.getElementById('pp-tab-network');
    if (networkTab) {
      networkTab.innerHTML = '';
      const rebuilt = buildTabNetwork(p);
      networkTab.append(...rebuilt.childNodes);
    }
  }
}

// â”€â”€ Envoi vers poste â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

function sendToPoste(posteId, obj) {
  if (ws        && ws.readyState        === 1) ws.send(JSON.stringify(obj));
  if (wsRemote  && wsRemote.readyState  === 1) wsRemote.send(JSON.stringify(obj));
}


