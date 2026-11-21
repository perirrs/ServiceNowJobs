/* Mergen home: one field of light.
   About a thousand points sit on a fixed canvas. Every scene has a shape (the Mergen gem, a globe, six clusters,
   a rising path, the infinity loop, then an emblem for each capability). Scroll decides the shape: over the last
   screen of a scene the points swirl out of one form and settle into the next. */
(() => {
  'use strict';
  const $ = (s, r = document) => r.querySelector(s), $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
  const lerp = (a, b, t) => a + (b - a) * t;
  const ease = t => t * t * (3 - 2 * t);
  const TAU = Math.PI * 2;
  const REDUCE = matchMedia('(prefers-reduced-motion: reduce)').matches;

  const secs = $$('[data-ch]');
  const canvas = $('#stage'), ctx = canvas.getContext('2d');
  let W = 0, H = 0, DPR = 1, mobile = false, tops = [], hs = [], docH = 1, storyEnd = 0;
  const N = innerWidth < 1000 ? 520 : 1100;

  /* ───────── Shapes: each is a list of weighted samplers in a unit square, x right, y down, z toward the viewer ───────── */
  let seed = 11;
  const rnd = () => { seed = (seed + 0x6D2B79F5) | 0; let t = Math.imul(seed ^ (seed >>> 15), 1 | seed); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
  const j = (a = 1) => (rnd() - .5) * a;
  const ring = (cx, cy, r, th = .04) => u => { const a = u * TAU, rr = r + j(th); return [cx + Math.cos(a) * rr, cy + Math.sin(a) * rr, j(.12)]; };
  const seg = (x0, y0, x1, y1, th = .03) => u => [lerp(x0, x1, u) + j(th), lerp(y0, y1, u) + j(th), j(.1)];
  const box = (x, y, w, h) => () => [x + rnd() * w, y + rnd() * h, j(.14)];
  const disk = (cx, cy, r) => () => { const a = rnd() * TAU, rr = Math.sqrt(rnd()) * r; return [cx + Math.cos(a) * rr, cy + Math.sin(a) * rr, j(.12)]; };
  const ball = (cx, cy, r) => (u, k, n) => { const y = 1 - 2 * (k + .5) / n, q = Math.sqrt(1 - y * y), a = k * 2.39996; return [cx + Math.cos(a) * q * r, cy + y * r, Math.sin(a) * q * r]; };
  const poly = (pts, closed, th, w) => {
    const out = [], n = closed ? pts.length : pts.length - 1; let tot = 0; const len = [];
    for (let i = 0; i < n; i++) { const a = pts[i], b = pts[(i + 1) % pts.length]; len[i] = Math.hypot(b[0] - a[0], b[1] - a[1]); tot += len[i]; }
    for (let i = 0; i < n; i++) { const a = pts[i], b = pts[(i + 1) % pts.length]; out.push([w * len[i] / tot, seg(a[0], a[1], b[0], b[1], th)]); }
    return out;
  };
  const hex = r => Array.from({ length: 6 }, (_, k) => [Math.cos(TAU * k / 6 - TAU / 4) * r, Math.sin(TAU * k / 6 - TAU / 4) * r]);
  const rect = (x, y, w, h, th, wt) => poly([[x, y], [x + w, y], [x + w, y + h], [x, y + h]], true, th, wt);
  const cloudPt = () => {
    const C = [[-.42, .12, .4], [0, -.2, .52], [.46, .1, .38]];
    for (let t = 0; t < 40; t++) {
      const c = C[(rnd() * 3) | 0], a = rnd() * TAU, x = c[0] + Math.cos(a) * c[2], y = c[1] + Math.sin(a) * c[2];
      if (y < .5 && C.every(o => o === c || Math.hypot(x - o[0], y - o[1]) > o[2] - .02)) return [x + j(.03), y + j(.03), j(.14)];
    }
    return [j(.8), .5, 0];
  };

  const DEF = {
    gem: () => [...poly(hex(.98), true, .035, 3), ...poly(hex(.7), true, .02, 1.2), ...poly([[-.3, .32], [-.3, -.3], [0, .1], [.3, -.3], [.3, .32]], false, .05, 2.6)],
    sphere: () => [[1, ball(0, 0, .94)]],
    six: () => [...Array.from({ length: 6 }, (_, k) => [1, ball(Math.cos(TAU * k / 6 - TAU / 4) * .7, Math.sin(TAU * k / 6 - TAU / 4) * .7, .2)]), [1.1, ring(0, 0, .7, .012)]],
    rise: () => [...poly([[-1, .62], [-.5, .62], [-.22, .08], [.22, .08], [.5, -.52], [1, -.52]], false, .035, 4),
      [.8, ring(-.75, .62, .14, .02)], [.8, ring(0, .08, .14, .02)], [.8, ring(.75, -.52, .14, .02)], [.3, seg(1, -.52, .84, -.68, .02)], [.3, seg(1, -.52, .84, -.36, .02)]],
    quad: () => [[-1, -1], [1, -1], [-1, 1], [1, 1]].flatMap(([a, b]) => [[1, ring(a * .46, b * .46, .3, .035)], [.25, disk(a * .46, b * .46, .07)]]),
    inf: () => [[1, u => { const a = u * TAU, d = 1 + Math.sin(a) ** 2; return [1.22 * Math.cos(a) / d + j(.1), 1.22 * Math.sin(a) * Math.cos(a) / d + j(.1), Math.sin(a) * .38 + j(.05)]; }]],
    now: () => [[4, ring(0, 0, .74, .2)], [1, ring(0, 0, .4, .03)], [.6, disk(0, 0, .1)]],
    growth: () => [[3, u => [-.9 + 1.8 * u + j(.04), .62 - 1.25 * Math.pow(u, 2.1) + j(.04), j(.14)]], [1, seg(-.95, .74, .95, .74, .014)], [1, seg(-.95, .74, -.95, -.8, .014)],
      [.4, seg(.9, -.63, .64, -.6, .02)], [.4, seg(.9, -.63, .84, -.36, .02)], ...[.25, .5, .75].map(u => [.35, disk(-.9 + 1.8 * u, .62 - 1.25 * Math.pow(u, 2.1), .07)])],
    tiles: () => [0, 1, 2].flatMap(r => [0, 1, 2].map(c => [1, box(-.81 + c * .62, -.81 + r * .62, .5, .5)])),
    neural: () => {
      const L = [[-.78, [-.55, 0, .55]], [0, [-.78, -.26, .26, .78]], [.78, [-.55, 0, .55]]], out = [];
      L.forEach(([x, ys]) => ys.forEach(y => out.push([1, disk(x, y, .1)])));
      for (let l = 0; l < 2; l++) L[l][1].forEach(y0 => L[l + 1][1].forEach(y1 => out.push([.33, seg(L[l][0], y0, L[l + 1][0], y1, .008)])));
      return out;
    },
    stack: () => [0, 1, 2, 3].flatMap(k => { const y = -.8 + k * .43; return [...rect(-.82, y, 1.64, .3, .014, 1.5), [.3, disk(-.62, y + .15, .06)], [.4, seg(-.4, y + .15, .62, y + .15, .012)]]; }),
    cloud: () => [[4, cloudPt], [1, seg(-.52, .5, .56, .5, .03)]],
    shield: () => [...poly([[0, -.92], [.72, -.62], [.72, .05], [.46, .56], [0, .92], [-.46, .56], [-.72, .05], [-.72, -.62]], true, .035, 4), ...poly([[-.3, 0], [-.06, .26], [.34, -.26]], false, .05, 1.5)],
    bars: () => [...[.45, .75, .6, 1.05, 1.4].map((h, k) => [h, box(-.88 + k * .37, .75 - h, .27, h)]), [.5, seg(-.95, .8, .95, .8, .012)]],
    target: () => [[2, ring(0, 0, .9, .03)], [1.5, ring(0, 0, .58, .03)], [1, ring(0, 0, .27, .03)], [.4, disk(0, 0, .07)], [1, seg(.98, -.98, .08, -.08, .02)]],
  };
  const SPIN = { sphere: 1, six: 1 };              // these turn continuously; the flat emblems only sway
  const SCALE = { inf: .86, gem: 1.02 };
  function build(parts) {
    const out = new Float32Array(N * 3), tot = parts.reduce((s, p) => s + p[0], 0);
    let i = 0;
    parts.forEach((p, pi) => {
      const n = pi === parts.length - 1 ? N - i : Math.min(N - i, Math.round(N * p[0] / tot));
      for (let k = 0; k < n; k++, i++) { const v = p[1]((k + rnd()) / n, k, n); out[i * 3] = v[0]; out[i * 3 + 1] = v[1]; out[i * 3 + 2] = v[2]; }
    });
    return out;
  }
  const SH = {};
  const names = secs.map(s => s.dataset.shape || 'gem');
  names.forEach(n => { if (!SH[n]) { seed = 11 + n.length * 7; SH[n] = build(DEF[n]()); } });

  /* ───────── Light: five soft sprites from green through teal to blue ───────── */
  const COLS = [[126, 240, 176], [76, 218, 132], [72, 209, 204], [60, 180, 240], [47, 155, 255]];
  const sprites = COLS.map(c => {
    const s = document.createElement('canvas'); s.width = s.height = 64;
    const g = s.getContext('2d'), gr = g.createRadialGradient(32, 32, 0, 32, 32, 32);
    gr.addColorStop(0, 'rgba(255,255,255,1)'); gr.addColorStop(.18, `rgba(${c},.95)`); gr.addColorStop(.45, `rgba(${c},.28)`); gr.addColorStop(1, `rgba(${c},0)`);
    g.fillStyle = gr; g.fillRect(0, 0, 64, 64);
    return s;
  });
  const ph = new Float32Array(N), rr = new Float32Array(N), cur = new Float32Array(N * 2);
  for (let i = 0; i < N; i++) { ph[i] = Math.random() * TAU; rr[i] = Math.random(); }
  let placed = false, mx = -9999, my = -9999;
  addEventListener('pointermove', e => { mx = e.clientX; my = e.clientY; }, { passive: true });

  function measure() {
    W = innerWidth; H = innerHeight; mobile = W < 1000;
    DPR = Math.min(devicePixelRatio || 1, mobile ? 1.5 : 2);
    canvas.width = Math.round(W * DPR); canvas.height = Math.round(H * DPR);
    tops = secs.map(s => s.getBoundingClientRect().top + scrollY);
    hs = secs.map(s => s.offsetHeight);
    const lastStory = secs.length - 2;
    storyEnd = tops[lastStory] + hs[lastStory];
    docH = Math.max(1, document.documentElement.scrollHeight - H);
    layoutGallery();
  }

  /* ───────── Chrome: rail, record bar, progress ───────── */
  const rail = $('#rail'), railItems = [];
  secs.forEach((s, i) => {
    if (!s.dataset.label) return;
    const a = document.createElement('a');
    a.href = `#${s.id}`; a.innerHTML = `<i>${String(i).padStart(2, '0')}</i><span>${s.dataset.label}</span>`;
    rail.appendChild(a); railItems[i] = a;
  });
  const hud = $('#hud'), hudTag = $('#hudTag'), hudScene = $('#hudScene'), hudLit = $('#hudLit'), progress = $('#progress');
  hud.classList.add('is-ok');
  let shownK = -1, bodyState = '';
  function chrome(k, sy) {
    progress.style.transform = `scaleX(${clamp(sy / docH).toFixed(4)})`;
    const inEnd = sy > tops[secs.length - 1] - H * .5;
    const bs = sy < H * .5 ? 'at-hero' : (sy > storyEnd - H * .6 && !inEnd) || inEnd ? 'past-story' : '';
    if (bs !== bodyState) { document.body.classList.remove('at-hero', 'past-story'); if (bs) document.body.classList.add(bs); bodyState = bs; }
    hud.classList.toggle('is-on', sy > H * .5 && sy < storyEnd - H * .7);
    if (k === shownK) return;
    if (railItems[shownK]) railItems[shownK].classList.remove('is-active');
    if (railItems[k]) railItems[k].classList.add('is-active');
    shownK = k;
    const s = secs[k];
    hudTag.textContent = `SCENE ${String(k).padStart(2, '0')}`;
    hudScene.textContent = s.dataset.label || 'Unlock';
    hudLit.textContent = `${s.dataset.cap || (k > 5 ? 9 : 0)} / 9`;
  }

  /* ───────── Industries gallery: scroll moves it sideways on wide screens, a swipe on phones ───────── */
  const gal = $('.hgal'), gView = $('.hgal__view'), gTrack = $('.hgal__track'), gN = $('#hgalN'), gFill = $('#hgalFill');
  let gOver = 0;
  function layoutGallery() {
    if (!gal) return;
    if (mobile) { gal.style.height = ''; gTrack.style.transform = ''; return; }
    gOver = Math.max(0, gTrack.scrollWidth - gView.clientWidth);
    gal.style.height = `${H + gOver}px`;
  }
  function gallery() {
    if (!gal) return;
    const cards = gTrack.children.length;
    let q;
    if (mobile) q = gView.scrollLeft / Math.max(1, gView.scrollWidth - gView.clientWidth);
    else { q = clamp(-gal.getBoundingClientRect().top / Math.max(1, gOver)); gTrack.style.transform = `translate3d(${(-q * gOver).toFixed(1)}px,0,0)`; }
    gFill.style.transform = `scaleX(${clamp(q).toFixed(3)})`;
    gN.textContent = String(Math.min(cards, 1 + Math.round(q * (cards - 1)))).padStart(2, '0');
  }

  /* ───────── Labels drawn on the infinity loop ───────── */
  const CAPS = ['ServiceNow', 'Salesforce', 'Microsoft', 'AI / ML', 'IMS', 'Cloud', 'Cybersecurity', 'Data Analytics', 'Advisory'];
  function loopLabels(alpha, cx, cy, S) {
    if (alpha < .02 || mobile) return;
    ctx.globalCompositeOperation = 'source-over';
    ctx.font = '600 12px "JetBrains Mono", monospace'; ctx.textAlign = 'center';
    CAPS.forEach((t, i) => {
      const a = (i + .5) / 9 * TAU - TAU / 4;                       // spaced round an ellipse just outside the loop
      const x = cx + Math.cos(a) * S * 1.42, y = cy + Math.sin(a) * S * .92;
      ctx.fillStyle = `rgba(234,255,251,${(.9 * alpha).toFixed(3)})`; ctx.fillText(t.toUpperCase(), x, y);
    });
  }

  /* ───────── Frame ───────── */
  let sy = scrollY, last = performance.now(), T = 0;
  function frame(now) {
    requestAnimationFrame(frame);
    const raw = (now - last) / 1000, dt = Math.min(.05, raw); last = now;
    const target = scrollY;
    sy = REDUCE ? target : sy + (target - sy) * (1 - Math.exp(-raw * 12));
    if (Math.abs(target - sy) < .1) sy = target;
    if (!REDUCE) T += dt;

    let k = 0;
    for (let i = 0; i < secs.length; i++) if (sy >= tops[i] - 1) k = i;
    const m = k < secs.length - 1 ? clamp((sy - (tops[k] + hs[k] - H)) / (H * .85)) : 0;
    const p = clamp((sy - tops[k]) / Math.max(1, hs[k] - H));
    chrome(k, sy);
    gallery();

    const covered = sy > storyEnd + 4 && sy < tops[secs.length - 1] - H;
    if (covered) return;

    ctx.setTransform(DPR, 0, 0, DPR, 0, 0);
    ctx.clearRect(0, 0, W, H);
    const cx = (mobile ? .5 : .71) * W, cy = (mobile ? .46 : .5) * H;
    const S0 = mobile ? Math.min(W * .36, H * .26) : Math.min(W * .19, H * .34);
    const nA = names[k], nB = names[Math.min(secs.length - 1, k + 1)];
    const A = SH[nA], B = SH[nB];
    const rotA = SPIN[nA] ? T * .28 : Math.sin(T * .45) * .34, rotB = SPIN[nB] ? T * .28 : Math.sin(T * .45) * .34;
    const cA = Math.cos(rotA), sA = Math.sin(rotA), cB = Math.cos(rotB), sB = Math.sin(rotB);
    const S = S0 * lerp(SCALE[nA] || 1, SCALE[nB] || 1, ease(m));
    const fade = mobile ? .5 : 1;

    // a soft bloom behind the form
    ctx.globalCompositeOperation = 'source-over';
    const g = ctx.createRadialGradient(cx, cy, 0, cx, cy, S * 2.1);
    g.addColorStop(0, `rgba(72,209,204,${.13 * fade})`); g.addColorStop(.5, `rgba(47,155,255,${.05 * fade})`); g.addColorStop(1, 'rgba(47,155,255,0)');
    ctx.fillStyle = g; ctx.fillRect(0, 0, W, H);

    ctx.globalCompositeOperation = 'lighter';
    const follow = REDUCE || !placed ? 1 : 1 - Math.exp(-raw * 11);
    const base = mobile ? 3.2 : 4.2;
    for (let i = 0; i < N; i++) {
      const e = m <= 0 ? 0 : ease(clamp(m * 1.5 - rr[i] * .5));
      const ax = A[i * 3], az = A[i * 3 + 2], bx = B[i * 3], bz = B[i * 3 + 2];
      let x = lerp(ax * cA + az * sA, bx * cB + bz * sB, e);
      let y = lerp(A[i * 3 + 1], B[i * 3 + 1], e);
      const z = lerp(az * cA - ax * sA, bz * cB - bx * sB, e);
      const sw = Math.sin(e * Math.PI) * .5;                       // the swirl between two forms
      x += Math.cos(ph[i] + T * .7) * sw + Math.sin(T * .9 + ph[i]) * .012;
      y += Math.sin(ph[i] * 1.7 + T * .6) * sw + Math.cos(T * .8 + ph[i] * 1.3) * .012;
      const pz = 1 / (1 - z * .22);
      let tx = cx + x * S * pz, ty = cy + y * S * pz;
      const dx = tx - mx, dy = ty - my, d2 = dx * dx + dy * dy;
      if (d2 < 16900) { const d = Math.sqrt(d2) || 1, f = (130 - d) / 130; tx += dx / d * f * 46; ty += dy / d * f * 46; }
      const X = cur[i * 2] += (tx - cur[i * 2]) * follow, Y = cur[i * 2 + 1] += (ty - cur[i * 2 + 1]) * follow;
      const depth = clamp((z + 1) / 2), r = base * (.55 + 1.25 * depth) * (1 + sw * .6);
      ctx.globalAlpha = (.3 + .7 * depth) * fade;
      ctx.drawImage(sprites[clamp(Math.floor((x + 1.15) / 2.3 * 5), 0, 4)], X - r, Y - r, r * 2, r * 2);
    }
    placed = true;
    ctx.globalAlpha = 1;
    if (nA === 'inf') loopLabels((1 - m * 2) * ease(clamp(p * 4 + .3)), cx, cy, S);
    else if (nB === 'inf' && m > .7) loopLabels((m - .7) / .3 * .7, cx, cy, S);
  }

  /* ───────── Copy arrives with the scene ───────── */
  const io = new IntersectionObserver(es => { for (const e of es) e.target.classList.toggle('in', e.isIntersecting); }, { rootMargin: '-10% 0px -12% 0px', threshold: .12 });
  $$('.step, .ch-head').forEach(el => (REDUCE ? el.classList.add('in') : io.observe(el)));
  $$('.statx, .pill4').forEach(e => e.classList.add('gl'));
  if (gView) gView.addEventListener('scroll', gallery, { passive: true });

  measure();
  addEventListener('resize', measure); addEventListener('load', measure);
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(measure);
  if ('ResizeObserver' in window) new ResizeObserver(measure).observe(document.body);
  requestAnimationFrame(frame);
})();
