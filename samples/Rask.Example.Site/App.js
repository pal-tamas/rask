// Scoped module for the landing app (window.Rask.App.*). Everything document-level lives here; the
// counter and install tabs are pure Rask state. `init` is called once from App.OnRenderedAsync with
// the hero <canvas> (an ElementRef revived to the real DOM node).

export function init(canvas) {
  wireThemeToggle();
  wireReveals();
  wireBars();
  wireHeroCanvas(canvas);
}

// ---- theme toggle: the pre-boot snippet (index.html) sets the initial theme from localStorage/OS;
// this stamps BOTH data-theme and data-bs-theme on <html> and persists the choice so it carries across
// the site, docs and playground on the same origin. ----
function wireThemeToggle() {
  var btn = document.getElementById('themeToggle');
  if (!btn) return;
  btn.addEventListener('click', function () {
    var root = document.documentElement;
    var cur = root.getAttribute('data-theme');
    if (!cur) cur = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    var next = cur === 'dark' ? 'light' : 'dark';
    root.setAttribute('data-theme', next);
    root.setAttribute('data-bs-theme', next);
    try { localStorage.setItem('rask-theme', next); } catch (e) { /* private mode: session-only */ }
  });
}

// ---- scroll reveals ----
function wireReveals() {
  var io = new IntersectionObserver(function (entries) {
    entries.forEach(function (e) {
      if (!e.isIntersecting) return;
      e.target.classList.add('in');
      io.unobserve(e.target);
    });
  }, { threshold: 0.18 });
  document.querySelectorAll('.reveal').forEach(function (el) { io.observe(el); });
}

// ---- benchmark bars grow when scrolled into view ----
function wireBars() {
  var wrap = document.getElementById('bars');
  if (!wrap) return;
  var io = new IntersectionObserver(function (entries) {
    entries.forEach(function (e) {
      if (!e.isIntersecting) return;
      wrap.classList.add('run');
      wrap.querySelectorAll('.bar').forEach(function (bar) { bar.style.height = bar.dataset.h + 'px'; });
      io.unobserve(wrap);
    });
  }, { threshold: 0.3 });
  io.observe(wrap);
}

// ---- hero wire visualization: the packet race ----
function wireHeroCanvas(canvas) {
  if (!canvas) return;
  var reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  var ctx = canvas.getContext('2d');
  var W = canvas.width, H = canvas.height;
  function css(n) { return getComputedStyle(document.documentElement).getPropertyValue(n).trim(); }

  var LB = H * 0.30;          // top lane — a full-page payload
  var LR = H * 0.72;          // bottom lane — Rask's minimal diff
  var X0 = 36, X1 = W - 36, span = X1 - X0;
  var RASK_SPD = 0.60;        // ~1.7 s per crossing
  var BLZ_SPD = 0.165;        // ~6 s — a lumbering, heavy payload

  var trail = [], particles = [], rings = [], flashes = [];
  var prevR = 0, prevB = 0, t0 = null, last = null;

  function roundRect(x, y, w, h, r) {
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.arcTo(x + w, y, x + w, y + h, r);
    ctx.arcTo(x + w, y + h, x, y + h, r);
    ctx.arcTo(x, y + h, x, y, r);
    ctx.arcTo(x, y, x + w, y, r);
    ctx.closePath();
  }
  function wire(y) {
    ctx.strokeStyle = css('--line'); ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(X0, y); ctx.lineTo(X1, y); ctx.stroke();
    ctx.fillStyle = css('--muted');
    ctx.beginPath(); ctx.arc(X0, y, 3.5, 0, 7); ctx.fill();
    ctx.beginPath(); ctx.arc(X1, y, 3.5, 0, 7); ctx.fill();
  }
  function burst(x, y, col, n, speed) {
    for (var i = 0; i < n; i++) {
      var ang = Math.random() * Math.PI * 2;
      var sp = speed * (0.25 + Math.random() * 0.75);
      particles.push({ x: x, y: y, vx: Math.cos(ang) * sp, vy: Math.sin(ang) * sp - speed * 0.15,
                       life: 1, col: col, r: 1 + Math.random() * 2.2 });
    }
    rings.push({ x: x, y: y, r: 4, life: 1, col: col });
  }
  function blazorBlock(x) {
    var col = css('--blazor');
    var w = 56, h = 26, xx = x - w / 2, yy = LB - h / 2;
    ctx.save();
    ctx.shadowBlur = 12; ctx.shadowColor = 'rgba(0,0,0,.55)';
    ctx.fillStyle = col; roundRect(xx, yy, w, h, 5); ctx.fill();
    ctx.shadowBlur = 0; ctx.strokeStyle = 'rgba(255,255,255,.14)'; ctx.lineWidth = 1;
    for (var s = 1; s < 5; s++) { ctx.beginPath(); ctx.moveTo(xx + w * s / 5, yy + 4); ctx.lineTo(xx + w * s / 5, yy + h - 4); ctx.stroke(); }
    ctx.restore();
  }
  function raskBolt(x) {
    var col = css('--accent');
    ctx.save(); ctx.lineCap = 'round';
    for (var i = 1; i < trail.length; i++) {
      var a = i / trail.length;
      ctx.globalAlpha = a * 0.85; ctx.strokeStyle = col; ctx.lineWidth = a * 8;
      ctx.beginPath(); ctx.moveTo(trail[i - 1].x, LR); ctx.lineTo(trail[i].x, LR); ctx.stroke();
    }
    ctx.restore();
    ctx.save();
    ctx.shadowBlur = 26; ctx.shadowColor = col; ctx.fillStyle = col;
    ctx.beginPath(); ctx.arc(x, LR, 6, 0, 7); ctx.fill();
    ctx.shadowBlur = 12; ctx.shadowColor = '#fff'; ctx.fillStyle = '#fff';
    ctx.beginPath(); ctx.arc(x, LR, 2.4, 0, 7); ctx.fill();
    ctx.restore();
  }
  function stepFx(dt) {
    for (var i = rings.length - 1; i >= 0; i--) {
      var rg = rings[i]; rg.r += 120 * dt; rg.life -= dt * 1.5;
      if (rg.life <= 0) { rings.splice(i, 1); continue; }
      ctx.save(); ctx.globalAlpha = Math.max(0, rg.life) * 0.6;
      ctx.strokeStyle = rg.col; ctx.lineWidth = 2;
      ctx.beginPath(); ctx.arc(rg.x, rg.y, rg.r, 0, 7); ctx.stroke(); ctx.restore();
    }
    ctx.save();
    for (var j = particles.length - 1; j >= 0; j--) {
      var p = particles[j];
      p.x += p.vx * dt * 60; p.y += p.vy * dt * 60; p.vy += 46 * dt; p.life -= dt * 1.35;
      if (p.life <= 0) { particles.splice(j, 1); continue; }
      ctx.globalAlpha = Math.max(0, p.life); ctx.fillStyle = p.col;
      ctx.shadowBlur = 8; ctx.shadowColor = p.col;
      ctx.beginPath(); ctx.arc(p.x, p.y, p.r, 0, 7); ctx.fill();
    }
    ctx.restore();
    ctx.save();
    for (var k = flashes.length - 1; k >= 0; k--) {
      var f = flashes[k]; f.life -= dt * 0.85; f.y -= 20 * dt;
      if (f.life <= 0) { flashes.splice(k, 1); continue; }
      ctx.globalAlpha = Math.max(0, f.life); ctx.fillStyle = f.col;
      ctx.font = '700 13px ui-monospace, monospace'; ctx.textAlign = 'center';
      ctx.fillText(f.txt, f.x, f.y);
    }
    ctx.restore();
  }
  function labels() {
    ctx.textAlign = 'left';
    ctx.fillStyle = css('--muted'); ctx.font = '11px ui-monospace, monospace';
    ctx.fillText('Full page · 24 KB', X0, LB - 22);
    ctx.fillStyle = css('--accent-ink'); ctx.font = '700 11px ui-monospace, monospace';
    ctx.fillText('Rask diff · 41 B', X0, LR + 28);
  }

  function frame(ts) {
    if (t0 === null) { t0 = ts; last = ts; }
    var dt = Math.min(0.05, (ts - last) / 1000); last = ts;
    var el = (ts - t0) / 1000;
    ctx.clearRect(0, 0, W, H);
    wire(LB); wire(LR);

    var bph = (el * BLZ_SPD) % 1;
    blazorBlock(X0 + bph * span);
    if (bph < prevB) burst(X1, LB, css('--blazor'), 12, 55);
    prevB = bph;

    var rph = (el * RASK_SPD) % 1;
    var rx = X0 + rph * span;
    if (rph < prevR) {
      trail = [{ x: rx }];
      burst(X1, LR, css('--accent'), 24, 150);
      flashes.push({ x: X1 - 8, y: LR - 18, txt: 'delivered · 41 B', col: css('--accent-ink'), life: 1 });
    }
    prevR = rph;
    trail.push({ x: rx }); if (trail.length > 20) trail.shift();
    raskBolt(rx);

    labels();
    stepFx(dt);
    requestAnimationFrame(frame);
  }

  if (reduce) {
    ctx.clearRect(0, 0, W, H); wire(LB); wire(LR);
    blazorBlock(X0 + 0.34 * span);
    trail = [];
    for (var q = 0; q < 16; q++) trail.push({ x: X0 + (0.5 + q * 0.028) * span });
    raskBolt(X0 + 0.94 * span);
    rings.push({ x: X1, y: LR, r: 12, life: 0.85, col: css('--accent') });
    stepFx(0.016); labels();
  } else {
    requestAnimationFrame(frame);
  }
}
