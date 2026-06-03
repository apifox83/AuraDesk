// ═══════════════════════════════════════════════════════════
//  AuraDesk — Carbon Background + Dynamic Light Engine
// ═══════════════════════════════════════════════════════════

(function() {
  const canvas = document.createElement('canvas');
  canvas.id = 'carbon-canvas';
  canvas.style.cssText = 'position:fixed;inset:0;width:100%;height:100%;z-index:0;pointer-events:none;';
  document.body.prepend(canvas);
  document.body.style.background = 'transparent';
  document.getElementById('app').style.position = 'relative';
  document.getElementById('app').style.zIndex = '1';

  const ctx = canvas.getContext('2d');
  let W, H;
  let lightX = 0.3, lightY = 0.3;
  let targetX = 0.3, targetY = 0.3;

  // Wave state
  let waveActive   = false;
  let waveProgress = 0;
  let waveAngle    = 0;
  let nextWave     = 4000 + Math.random() * 8000;
  let lastWaveTime = performance.now();

  function resize() {
    W = canvas.width  = window.innerWidth;
    H = canvas.height = window.innerHeight;
    draw();
  }

  function draw() {
    ctx.clearRect(0, 0, W, H);

    // ── Base noire ──
    ctx.fillStyle = '#060a12';
    ctx.fillRect(0, 0, W, H);

    // ── Fibre carbone ──
    const s = 4;
    for (let y = 0; y < H; y += s) {
      for (let x = 0; x < W; x += s) {
        const col = Math.floor(x / s);
        const row = Math.floor(y / s);
        if ((col + row) % 2 === 0) {
          const sub = (col % 2 === 0 && row % 2 === 0) || (col % 2 !== 0 && row % 2 !== 0);
          ctx.fillStyle = sub ? 'rgba(255,255,255,0.045)' : 'rgba(255,255,255,0.018)';
          ctx.fillRect(x, y, s, s);
        }
      }
    }

    // Grille micro
    ctx.strokeStyle = 'rgba(255,255,255,0.01)';
    ctx.lineWidth = 0.5;
    for (let x = 0; x < W; x += s * 2) {
      ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, H); ctx.stroke();
    }
    for (let y = 0; y < H; y += s * 2) {
      ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(W, y); ctx.stroke();
    }

    // ── Reflet principal (position fenêtre) ──
    const lx = lightX * W;
    const ly = lightY * H;
    const g1 = ctx.createRadialGradient(lx, ly, 0, lx, ly, W * 0.65);
    g1.addColorStop(0,   'rgba(0,212,200,0.07)');
    g1.addColorStop(0.35,'rgba(139,92,246,0.03)');
    g1.addColorStop(1,   'transparent');
    ctx.fillStyle = g1;
    ctx.fillRect(0, 0, W, H);

    // Reflet secondaire opposé
    const g2 = ctx.createRadialGradient(W - lx, H - ly, 0, W - lx, H - ly, W * 0.45);
    g2.addColorStop(0,   'rgba(139,92,246,0.04)');
    g2.addColorStop(1,   'transparent');
    ctx.fillStyle = g2;
    ctx.fillRect(0, 0, W, H);

    // ── Vague de reflet ──
    if (waveActive) {
      const wx = Math.cos(waveAngle);
      const wy = Math.sin(waveAngle);
      const wpos = waveProgress * (W + H) * 1.5 - H * 0.5;

      const x0 = W * 0.5 + wx * wpos - wy * H;
      const y0 = H * 0.5 + wy * wpos + wx * H;
      const x1 = W * 0.5 + wx * wpos + wy * H;
      const y1 = H * 0.5 + wy * wpos - wx * H;

      const wg = ctx.createLinearGradient(
        x0 - wx * 80, y0 - wy * 80,
        x0 + wx * 80, y0 + wy * 80
      );
      const alpha = Math.sin(waveProgress * Math.PI) * 0.045;
      wg.addColorStop(0,   'transparent');
      wg.addColorStop(0.4, `rgba(0,212,200,${alpha * 0.4})`);
      wg.addColorStop(0.5, `rgba(200,230,255,${alpha})`);
      wg.addColorStop(0.6, `rgba(139,92,246,${alpha * 0.4})`);
      wg.addColorStop(1,   'transparent');

      ctx.save();
      ctx.strokeStyle = wg;
      ctx.lineWidth = 280;
      ctx.beginPath();
      // Onde sinusoïdale perpendiculaire à la direction
      const steps = 80;
      const amp   = 55;           // amplitude sinusoïde
      const freq  = 6;            // nombre de cycles visibles
      const phase = waveProgress * Math.PI * 4;
      for (let i = 0; i <= steps; i++) {
        const t2 = i / steps;
        // Point le long de la ligne (x0,y0)→(x1,y1)
        const px = x0 + (x1 - x0) * t2;
        const py = y0 + (y1 - y0) * t2;
        // Déplacement perpendiculaire sinusoïdal
        const sine = Math.sin(t2 * Math.PI * 2 * freq + phase) * amp;
        const ox = px + (-wy) * sine;
        const oy = py + ( wx) * sine;
        if (i === 0) ctx.moveTo(ox, oy);
        else         ctx.lineTo(ox, oy);
      }
      ctx.stroke();
      ctx.restore();
    }

    // ── Vignette ──
    const vig = ctx.createRadialGradient(W/2, H/2, H*0.25, W/2, H/2, H*0.85);
    vig.addColorStop(0, 'transparent');
    vig.addColorStop(1, 'rgba(0,0,0,0.45)');
    ctx.fillStyle = vig;
    ctx.fillRect(0, 0, W, H);
  }

  function animate(now) {
    // Interpolation douce du reflet
    lightX += (targetX - lightX) * 0.035;
    lightY += (targetY - lightY) * 0.035;



    draw();
    requestAnimationFrame(animate);
  }

  // Position fenêtre → reflet (Electron)
  if (window.electronAPI?.onWindowMove) {
    window.electronAPI.onWindowMove((x, y) => {
      targetX = x / screen.width;
      targetY = y / screen.height;
    });
  }

  // Fallback web — souris
  document.addEventListener('mousemove', e => {
    targetX = e.screenX / screen.width;
    targetY = e.screenY / screen.height;
  });

  window.addEventListener('resize', resize);
  resize();
  requestAnimationFrame(animate);
})();







