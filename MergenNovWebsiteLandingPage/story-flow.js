/* Mergen | ServiceNow scroll story
   One fixed canvas stage. Every chapter is a pinned scene with its own composition:
   the graphic takes the screen, a giant word drifts behind it, and the copy appears as
   pinned captions (lower third, side columns, panels) that swap as you scroll. */
(() => {
  'use strict';

  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
  const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
  const lerp = (a, b, t) => a + (b - a) * t;
  const smooth = (a, b, v) => { const t = clamp((v - a) / (b - a)); return t * t * (3 - 2 * t); };
  const ease = t => (t < .5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2);
  const mix = (a, b, t) => [lerp(a[0], b[0], t), lerp(a[1], b[1], t), lerp(a[2], b[2], t)];
  const rgba = (c, a) => `rgba(${c[0] | 0},${c[1] | 0},${c[2] | 0},${a})`;
  const REDUCE = matchMedia('(prefers-reduced-motion: reduce)').matches;

  const C = {
    teal: [72, 209, 204], blue: [47, 127, 232], green: [76, 218, 132], red: [255, 90, 79],
    amber: [255, 181, 71], slate: [128, 152, 170], white: [240, 250, 252], ink: [4, 18, 27], cream: [244, 239, 230], navy: [11, 31, 44],
  };

  /* ───────── Chapters and their captions ───────── */
  const secs = $$('[data-ch]');
  const ids = secs.map(s => s.dataset.ch);
  const IX = Object.fromEntries(ids.map((id, i) => [id, i]));
  const caps = secs.map(s => (s.dataset.ch === 'hero' ? [] : $$(':scope > .ch-head, :scope > .step', s)));
  secs.forEach((s, i) => s.style.setProperty('--n', caps[i].length));

  const canvas = $('#stage'), stageCtx = canvas.getContext('2d'), bigword = $('#bigword');
  let ctx = stageCtx;

  // Below 1000px there is no fixed stage. Every chapter gets its own graphic in the page flow,
  // which scrolls with the copy and is driven by its own passage through the screen.
  const figs = secs.map((s, k) => {
    const el = document.createElement('div'), cv = document.createElement('canvas');
    el.className = 'fig'; el.dataset.fig = ids[k]; el.setAttribute('aria-hidden', 'true'); el.appendChild(cv);
    const anchor = $(k === 0 ? '.hero__body' : '.ch-head', s);
    if (anchor) anchor.after(el); else s.prepend(el);
    return { k, el, cv, c: cv.getContext('2d'), top: 0, w: 0, h: 0 };
  });
  let W = 0, H = 0, VH = 0, DPR = 1, mobile = false, LIGHT = false;
  let tops = [], hs = [], storyEnd = 0, docH = 1;
  const fg = a => (LIGHT ? rgba(C.navy, a) : `rgba(255,255,255,${a})`);
  const bg = a => rgba(LIGHT ? C.cream : C.ink, a);

  function measure() {
    W = window.innerWidth; VH = window.innerHeight; mobile = W < 1000;
    H = mobile ? Math.round(VH * .42) : VH;
    DPR = Math.min(window.devicePixelRatio || 1, 2);
    if (!mobile) { canvas.width = Math.round(W * DPR); canvas.height = Math.round(H * DPR); }
    else for (const f of figs) {
      f.top = f.el.getBoundingClientRect().top + window.scrollY; f.w = f.el.clientWidth; f.h = f.el.clientHeight;
      const cw = Math.round(f.w * DPR), ch = Math.round(f.h * DPR);
      if (f.cv.width !== cw || f.cv.height !== ch) { f.cv.width = cw; f.cv.height = ch; }
    }
    tops = secs.map(s => s.getBoundingClientRect().top + window.scrollY);
    hs = secs.map(s => s.offsetHeight);
    storyEnd = tops[secs.length - 1] + hs[secs.length - 1];
    docH = Math.max(1, document.documentElement.scrollHeight - VH);
  }

  /* ───────── Thread shapes: each fills [x, y, depth] × N in stage pixels ───────── */
  const N = 360;
  const bufA = new Float32Array(N * 3), bufB = new Float32Array(N * 3), P = new Float32Array(N * 3);
  const STOPS = [.22, .36, .5, .64, .78];
  const HANDOFFS = [.14, .17, .20, .30, .42, .50, .58, .70, .86];
  const GROUPS = ['Email inbox', 'Phone queue', 'Legacy portal', 'Service desk', 'App team', 'Infrastructure', 'Hosting vendor', 'App team, again', 'Platform team'];

  const REG = () => (mobile ? { x0: .14, x1: .86, y0: .14, y1: .9 } : { x0: .1, x1: .9, y0: .27, y1: .62 });
  const ZN = [[.3, -.9], [.04, .12], [.5, 0], [.2, .55], [.78, .3], [.36, .9], [.96, .72], [.56, .5], [.1, .86], [.74, 1], [.74, 2.2]];
  const ZS = [0, ...HANDOFFS, 1];
  function shZig(out) {
    const R = REG(); let j = 0;
    for (let i = 0; i < N; i++) {
      const s = i / (N - 1);
      while (j < ZS.length - 2 && s > ZS[j + 1]) j++;
      const t = clamp((s - ZS[j]) / (ZS[j + 1] - ZS[j]));
      out[i * 3] = lerp(R.x0, R.x1, lerp(ZN[j][0], ZN[j + 1][0], t)) * W;
      out[i * 3 + 1] = lerp(R.y0, R.y1, lerp(ZN[j][1], ZN[j + 1][1], t)) * H;
      out[i * 3 + 2] = .8;
    }
  }
  function shLine(out, o) {
    for (let i = 0; i < N; i++) {
      const s = i / (N - 1);
      if (o.v) { out[i * 3] = o.x * W; out[i * 3 + 1] = lerp(o.y0, o.y1, s) * H; }
      else { out[i * 3] = lerp(o.x0, o.x1, s) * W; out[i * 3 + 1] = o.y * H; }
      out[i * 3 + 2] = .7;
    }
  }
  // h: horizontal road (optionally panned, so the world moves past a fixed point). v: road seen from above.
  function shRoute(out, o) {
    for (let i = 0; i < N; i++) {
      const s = i / (N - 1), w = Math.sin(s * Math.PI * 3);
      if (o.h) { out[i * 3] = (lerp(o.x0, o.x1, s) - o.pan) * W; out[i * 3 + 1] = o.cy * H + o.amp * H * w; }
      else { out[i * 3] = o.cx * W + o.amp * W * w; out[i * 3 + 1] = lerp(-.08, 1.08, s) * H; }
      out[i * 3 + 2] = .8;
    }
  }
  function shLoop(out, o) {
    const a = o.a * W;
    for (let i = 0; i < N; i++) {
      const th = (i / (N - 1)) * Math.PI * 2, sn = Math.sin(th), cs = Math.cos(th), d = 1 + sn * sn;
      out[i * 3] = o.cx * W + (a * cs) / d; out[i * 3 + 1] = o.cy * H + (a * sn * cs) / d * 1.25; out[i * 3 + 2] = .85;
    }
  }
  const SHAPES = { zig: shZig, line: shLine, route: shRoute, loop: shLoop };
  const ZIG = () => ({ cx: .5, cy: mobile ? .5 : .44, r: mobile ? .3 : .16 });
  const LINE = () => (mobile ? { v: 1, x: .13, y0: .08, y1: .84 } : { x0: .08, x1: .92, y: .42 });
  const VROAD = { h: 0, cx: .5, amp: .17 };
  const rideS = p => clamp((p + .08) / 1.16, .02, .98);   // thread position that sits level with the reader's eye
  const ROAD = pan => mobile ? VROAD : ({ h: 1, x0: pan == null ? -.1 : -.2, x1: pan == null ? 1.1 : 2.8, pan: pan || 0, cy: .4, amp: .1 });
  const panFor = s => 3 * s - .7;                       // pan that puts thread position s at screen centre
  const TOPDOWN = () => (mobile ? VROAD : { h: 0, cx: .5, amp: .07 });
  const LOOP = () => (mobile ? { cx: .5, cy: .52, a: .4 } : { cx: .5, cy: .4, a: .2 });

  /* ───────── The backlog chapter's clock follows the timestamps in its captions ───────── */
  let anch = [[0, 0], [1, 3248]], anchD = anch;
  (() => {
    const list = caps[IX.knot], n = list.length, a = [];
    list.forEach((el, i) => { if (el.dataset.min != null) a.push([(i + .5) / n, +el.dataset.min]); });
    if (a.length >= 2) anch = anchD = a;
  })();
  const anchM = [[0, 0], [.3, 158], [.62, 1513], [.9, 3248]];
  const knotQ = p => mobile ? clamp(p / .9) : clamp((p - anch[0][0]) / Math.max(.001, anch[anch.length - 1][0] - anch[0][0]));
  function knotMin(p) {
    const anch = mobile ? anchM : anchD;
    if (p <= anch[0][0]) return anch[0][1];
    for (let i = 1; i < anch.length; i++) if (p <= anch[i][0]) return lerp(anch[i - 1][1], anch[i][1], (p - anch[i - 1][0]) / Math.max(.001, anch[i][0] - anch[i - 1][0]));
    return anch[anch.length - 1][1];
  }

  /* ───────── Stage state for chapter k at progress p ───────── */
  const NUM = ['dash', 'built', 'grid', 'rings', 'tick', 'tickU', 'trail', 'scan', 'ta', 'drawn', 'fx', 'fy'];
  const base = () => ({ ta: 1, drawn: 1, dash: 0, built: 1, grid: 0, rings: 0, tick: 0, tickU: .5, trail: 0, scan: 0, fx: .5, fy: mobile ? .55 : .42 });
  function state(k, p) {
    const s = base();
    switch (ids[k]) {
      case 'hero': return Object.assign(s, { shape: 'zig', o: ZIG(), cA: C.red, cB: C.red, ta: 0, drawn: .12, tick: 0, tickU: .12, fx: mobile ? .5 : .78 });
      case 'knot': { const q = knotQ(p), u = lerp(.12, .88, q); return Object.assign(s, { shape: 'zig', o: ZIG(), cA: C.amber, cB: C.red, ta: smooth(0, .05, q + .02), drawn: u, tick: 1, tickU: u }); }
      case 'call': { const t = smooth(0, .5, p); return Object.assign(s, { shape: 'zig', o: ZIG(), cA: mix(C.amber, C.slate, t), cB: mix(C.red, C.slate, t), ta: lerp(1, .22, t), drawn: .88, rings: smooth(.1, .5, p), tick: lerp(1, .5, t), tickU: .88 }); }
      case 'listen': { const t = smooth(.04, .35, p); return Object.assign(s, { shape: 'zig', o: ZIG(), cA: C.slate, cB: C.slate, ta: lerp(.22, 0, t), drawn: .88, tick: lerp(.5, 0, t), tickU: .88 }); }
      case 'measure': { const c = mix(C.slate, C.red, smooth(.45, .7, p)); return Object.assign(s, { shape: 'line', o: LINE(), cA: c, cB: c, scan: mobile ? clamp((p - .1) / .74) : smooth(.12, .68, p) }); }
      case 'propose': return Object.assign(s, { shape: 'route', o: ROAD(), cA: C.teal, cB: C.blue, dash: 1, built: 0, grid: 1, drawn: mobile ? clamp(rideS(p) + .1) : 1 });
      case 'build': { const sc = mobile ? rideS(p) : lerp(.2, .8, smooth(.06, .96, p)); return Object.assign(s, { shape: 'route', o: ROAD(panFor(sc)), cA: C.teal, cB: mix(C.blue, C.green, p), dash: 1, built: sc, grid: .5, tick: 1, tickU: sc }); }
      case 'golive': return Object.assign(s, { shape: 'route', o: TOPDOWN(), cA: C.teal, cB: C.green, tick: 1, trail: 1, tickU: mobile ? rideS(p) : lerp(.1, .9, smooth(.1, .78, p)) });
      case 'stay': return Object.assign(s, { shape: 'loop', o: LOOP(), cA: C.teal, cB: C.green, tick: 1, trail: 1, tickU: .9 });
    }
    return s;
  }
  function blend(A, B, t) {
    const o = { shape: A.shape, o: A.o, cA: mix(A.cA, B.cA, t), cB: mix(A.cB, B.cB, t) };
    for (const key of NUM) o[key] = lerp(A[key], B[key], t);
    return o;
  }
  const pt = s => {
    const f = clamp(s) * (N - 1), i = Math.min(N - 2, f | 0), t = f - i;
    return [lerp(P[i * 3], P[i * 3 + 3], t), lerp(P[i * 3 + 1], P[i * 3 + 4], t)];
  };

  /* ───────── The crowd: every other ticket in the estate ───────── */
  const M = 140;
  const SRC = [['Email', [72, 209, 204], .3], ['Portal', [47, 127, 232], .22], ['Phone', [255, 181, 71], .16], ['Chat', [167, 139, 250], .14], ['Monitoring', [76, 218, 132], .12], ['Walk-up', [244, 114, 182], .06]];
  const TEAMW = [.27, .2, .18, .15, .12, .08];
  let seed = 48213;
  const rnd = () => { seed = (seed + 0x6D2B79F5) | 0; let t = Math.imul(seed ^ (seed >>> 15), 1 | seed); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
  const pick = w => { let r = rnd(), i = 0; while (i < w.length - 1 && r > w[i]) r -= w[i++]; return i; };
  const teamCount = [0, 0, 0, 0, 0, 0];
  const TK = Array.from({ length: M }, () => { const team = pick(TEAMW); return { rx: rnd(), ry: rnd(), ph: rnd() * 6.28, f: rnd(), age: rnd(), spd: rnd(), src: pick(SRC.map(s => s[2])), team, rank: teamCount[team]++ }; });
  const hubX = (c, hero) => (mobile ? lerp(.1, .9, c / 5) : hero ? lerp(.61, .95, c / 5) : lerp(.12, .88, c / 5));
  const HUB_Y = .15;
  const colX = c => (mobile ? lerp(.12, .88, c / 5) : lerp(.2, .8, c / 5)) * W;
  const COL_BASE = () => (mobile ? .72 : .62) * H;
  const cp = [0, 0], cq = [0, 0];
  function crowdPos(lay, tk, t, out) {
    if (lay === 'cols') {
      const cell = mobile ? 15 : 34, per = mobile ? Math.max(2, Math.min(3, Math.floor((colX(1) - colX(0) - 10) / cell))) : 4;
      out[0] = colX(tk.team) + ((tk.rank % per) - (per - 1) / 2) * cell;
      out[1] = COL_BASE() - Math.floor(tk.rank / per) * (mobile ? 12 : 23);
    } else if (lay === 'flow') {
      const p = pt((tk.f + t * (.03 + tk.spd * .04)) % 1); out[0] = p[0]; out[1] = p[1];
    } else {
      let x0 = .04, x1 = .96, y0 = .22, y1 = .66;
      if (mobile) { x0 = .05; x1 = .95; y0 = .12; y1 = .95; }
      else if (lay === 'scatterH') { x0 = .58; x1 = .98; y0 = .22; y1 = .6; }
      out[0] = lerp(x0, x1, tk.rx) * W + (REDUCE ? 0 : Math.sin(t * .4 + tk.ph) * 9);
      out[1] = lerp(y0, y1, tk.ry) * H + (REDUCE ? 0 : Math.cos(t * .33 + tk.ph) * 7);
    }
  }
  function crowdState(k, p) {
    switch (ids[k]) {
      case 'hero': return { la: 'scatterH', lb: 'scatterH', lt: 0, a: .95, red: 0, sc: 1, link: .6, hub: 0 };
      case 'knot': { const q = knotQ(p); return { la: 'scatter', lb: 'scatter', lt: 0, a: .9, red: q, sc: 1, link: lerp(.6, .1, q), hub: 1 }; }
      case 'call': { const t = smooth(0, .5, p); return { la: 'scatter', lb: 'scatter', lt: 0, a: lerp(.9, .14, t), red: lerp(1, .4, t), sc: 1, link: 0, hub: 1 }; }
      case 'listen': { const t = smooth(.04, .35, p); return { la: 'scatter', lb: 'cols', lt: t, a: lerp(.14, .95, t), red: lerp(.4, .15, t), sc: lerp(1, mobile ? 1 : 1.3, t), link: 0, hub: 1 }; }
      case 'measure': return { la: 'cols', lb: 'cols', lt: 0, a: 0, red: .15, sc: 1.3, link: 0, hub: 1 };
      case 'golive': return { la: 'flow', lb: 'flow', lt: 0, a: smooth(.8, 1, p) * .9, red: 0, sc: .2, link: 0, hub: 1 };
      case 'stay': return { la: 'flow', lb: 'flow', lt: 0, a: .9, red: 0, sc: .2, link: 0, hub: 1 };
    }
    return { la: 'flow', lb: 'flow', lt: 0, a: 0, red: 0, sc: .2, link: 0, hub: 1 };
  }
  function drawCrowd(k, p, m, t) {
    const A = crowdState(k, p), B = m > 0 ? crowdState(k + 1, 0) : A, e = m > 0 ? ease(m) : 0;
    const a = lerp(A.a, B.a, e); if (a < .01) return;
    const red = lerp(A.red, B.red, e), sc = lerp(A.sc, B.sc, e), link = lerp(A.link, B.link, e) * (mobile ? 0 : 1), hub = lerp(A.hub, B.hub, e);
    const ms = mobile ? .66 : 1;
    for (const tk of TK) {
      crowdPos(A.la, tk, t, cp); if (A.lt > 0) { crowdPos(A.lb, tk, t, cq); cp[0] = lerp(cp[0], cq[0], A.lt); cp[1] = lerp(cp[1], cq[1], A.lt); }
      if (e > 0) { crowdPos(B.la, tk, t, cq); cp[0] = lerp(cp[0], cq[0], e); cp[1] = lerp(cp[1], cq[1], e); }
      const x = cp[0], y = cp[1], col = mix(SRC[tk.src][1], C.red, red * (.35 + .65 * tk.age));
      if (link > .01) {
        ctx.beginPath(); ctx.moveTo(lerp(hubX(tk.src, 1), hubX(tk.src, 0), hub) * W, HUB_Y * H + 12); ctx.lineTo(x, y);
        ctx.strokeStyle = rgba(SRC[tk.src][1], link * .16 * a); ctx.lineWidth = 1; ctx.stroke();
      }
      if (sc < .5) { ctx.beginPath(); ctx.arc(x, y, 2.6, 0, 7); ctx.fillStyle = rgba(C.green, a * (.4 + .6 * tk.age)); ctx.fill(); continue; }
      const w = 24 * ms * sc, h = 15 * ms * sc, x0 = x - w / 2, y0 = y - h / 2;
      ctx.fillStyle = `rgba(7,28,41,${a})`; ctx.fillRect(x0, y0, w, h);
      ctx.strokeStyle = rgba(col, .55 * a); ctx.lineWidth = 1; ctx.strokeRect(x0 + .5, y0 + .5, w - 1, h - 1);
      ctx.fillStyle = rgba(col, a); ctx.fillRect(x0, y0, 3 * ms, h);
      ctx.fillStyle = rgba(col, .5 * a); ctx.fillRect(x0 + 6 * ms, y0 + h * .28, w * .5, 1.5); ctx.fillRect(x0 + 6 * ms, y0 + h * .6, w * .32, 1.5);
    }
  }

  /* ───────── Labels pinned to the graphic ───────── */
  const hubPins = hero => SRC.map(([t, c], i) => ({ kind: 'fix', cls: 'pin--hub', keep: 1, pos: () => [hubX(i, hero) * W, HUB_Y * H], t: `<i style="background:rgb(${c})"></i>${t}`, on: () => true }));
  const stopSide = i => (mobile ? 'b' : ['b', 'a', 'a', 'a', 'b'][i]);
  const PINS = {
    hero: hubPins(1),
    knot: [...hubPins(0), ...GROUPS.map((t, i) => ({ kind: 'on', cls: 'pin--ring', s: HANDOFFS[i], side: [0, 1, 0, 0, 1, 1, 0, 1, 1][i] ? 'b' : 'a', far: 16, t, on: () => true, hot: (p, S) => S.tickU >= HANDOFFS[i] }))],
    listen: ['Service desk', 'App owners', 'Infrastructure', 'Hosting vendor', 'Security', 'No owner'].map((t, i) => ({ kind: 'fix', cls: 'pin--ring pin--calm', pos: () => [colX(i), COL_BASE() + 36], t, d: `${teamCount[i]} stuck here`, on: p => p >= .3 + i * .03 })),
    measure: [
      { kind: 'on', cls: 'pin--cap', s: 0, side: 'b', t: 'Mon 09:02', d: 'Opened', on: () => true },
      { kind: 'on', cls: 'pin--cap', s: 1, side: 'b', t: 'Wed 15:10', d: 'Resolved', on: (p, S) => S.scan >= .98 },
      { kind: 'on', cls: 'pin--gap', s: .1, side: 'a', t: '3 duplicate records', d: 'No single intake', on: (p, S) => S.scan >= .1 },
      { kind: 'on', cls: 'pin--gap', s: .3, side: 'b', t: '19 h finding the owner', d: 'No service map', on: (p, S) => S.scan >= .3 },
      { kind: 'on', cls: 'pin--gap', s: .52, side: 'a', t: '4 queues, 4 re-explanations', d: 'Manual routing', on: (p, S) => S.scan >= .52 },
      { kind: 'on', cls: 'pin--gap', s: .74, side: 'b', t: '22 h waiting on a vendor', d: 'Handoff by email', on: (p, S) => S.scan >= .74 },
      { kind: 'on', cls: 'pin--gap', s: .93, side: 'a', t: 'A predictable failure', d: 'No expiry monitoring', on: (p, S) => S.scan >= .93 },
      { kind: 'on', cls: 'pin--sum', s: .5, side: 'a', far: 150, t: '54 h 08 m elapsed', d: '12 minutes of work', on: p => p >= .72 },
    ],
    propose: [['One intake', 'Every channel, one record'], ['AI triage', 'Cause named in seconds'], ['Service map', 'What runs on what'], ['Right owner, once', 'No re-explaining'], ['Resolve and learn', 'The fix becomes knowledge']]
      .map(([t, d], i) => ({ kind: 'on', cls: 'pin--station pin--c', s: STOPS[i], side: stopSide(i), far: 30, t, d, on: p => p >= .2 + i * .14 })),
    build: [['ITSM', 'Incident · Request · Knowledge'], ['Now Assist', 'AI agents for triage'], ['ITOM + CMDB', 'Discovery · Service Mapping'], ['Integration Hub', 'Vendor · HR · security'], ['Analytics + ATF', 'Measured · regression-tested']]
      .map(([t, d], i) => ({ kind: 'on', cls: 'pin--mod', s: STOPS[i], side: 'b', far: 64, t, d: `<small>Stop ${i + 1} of 5</small>${d}`, on: () => true, hot: (p, S) => S.built >= STOPS[i] })),
    golive: [['09:02', 'Opened. Duplicates merged.'], ['09:02', 'Cause: expired certificate'], ['09:03', 'Routed to platform team'], ['09:09', 'Certificate renewed'], ['09:14', 'Resolved. Article published.']]
      .map(([t, d], i) => ({ kind: 'on', cls: 'pin--live', s: STOPS[i], side: ['r', 'r', 'l', 'l', 'r'][i], t, d, on: (p, S) => S.tickU >= STOPS[i] })),
    stay: [
      { kind: 'lobe', cls: 'pin--loop', dx: -.6, dy: 0, t: 'Advise', d: 'Assess<br>Roadmap<br>Business case', on: p => p >= .02 },
      { kind: 'lobe', cls: 'pin--loop', dx: 0, dy: -.72, t: 'Build', d: 'Implement · Integrate · Automate', on: p => p >= .08 },
      { kind: 'lobe', cls: 'pin--loop', dx: .6, dy: 0, t: 'Run', d: 'Hypercare<br>Support<br>Upgrades', on: p => p >= .14 },
    ],
  };
  const pinRoot = $('#pins');
  for (const set of Object.values(PINS)) for (const pin of set) {
    const el = document.createElement('div');
    el.className = `pin ${pin.cls || ''} ${pin.side || ''}`;
    el.innerHTML = `<b>${pin.t}</b>${pin.d ? `<span>${pin.d}</span>` : ''}`;
    pinRoot.appendChild(el); pin.el = el; pin.shown = false; pin.isHot = false;
  }
  let pinK = -1;
  function drawPins(k, p, m, A, S) {
    if (k !== pinK) {
      for (const pin of PINS[ids[pinK]] || []) if (pin.shown) { pin.el.classList.remove('is-on', 'is-hot'); pin.shown = pin.isHot = false; }
      pinK = k;
    }
    const set = PINS[ids[k]]; if (!set || mobile) return;
    const live = m < .1;
    for (const pin of set) {
      const on = (live || pin.keep) && pin.on(p, S), hot = on && !!(pin.hot && pin.hot(p, S));
      if (on !== pin.shown) { pin.el.classList.toggle('is-on', on); pin.shown = on; }
      if (hot !== pin.isHot) { pin.el.classList.toggle('is-hot', hot); pin.isHot = hot; }
      if (!on) continue;
      let lx, ly, tf = 'translate(-50%,-50%)';
      if (pin.kind === 'fix') [lx, ly] = pin.pos();
      else if (pin.kind === 'lobe') { lx = (A.o.cx + pin.dx * A.o.a) * W; ly = A.o.cy * H + pin.dy * A.o.a * W; }
      else {
        const [x, y] = pt(pin.s), far = pin.far || 46;
        if (pin.side === 'a') { lx = x; ly = y - far; tf = 'translate(-50%,-100%)'; }
        else if (pin.side === 'b') { lx = x; ly = y + far; tf = 'translate(-50%,0)'; }
        else if (pin.side === 'l') { lx = x - 24; ly = y; tf = 'translate(-100%,-50%)'; }
        else { lx = x + 24; ly = y; tf = 'translate(0,-50%)'; }
        if ((pin.side === 'a' || pin.side === 'b') && far > 24) {
          const dir = pin.side === 'a' ? -1 : 1;
          ctx.beginPath(); ctx.moveTo(x, y + dir * 12); ctx.lineTo(x, y + dir * (far - 6));
          ctx.strokeStyle = fg(.25); ctx.lineWidth = 1; ctx.stroke();
        }
      }
      pin.el.style.transform = `translate3d(${lx.toFixed(1)}px,${ly.toFixed(1)}px,0) ${tf}`;
    }
  }

  /* ───────── Canvas layers ───────── */
  function drawGlow(S) {
    const gx = S.fx * W, gy = S.fy * H, r = Math.max(W, H) * .6, c = mix(S.cA, S.cB, .5);
    const g = ctx.createRadialGradient(gx, gy, 0, gx, gy, r);
    g.addColorStop(0, rgba(c, LIGHT ? .1 : .16)); g.addColorStop(.5, rgba(c, .05)); g.addColorStop(1, rgba(c, 0));
    ctx.fillStyle = g; ctx.fillRect(0, 0, W, H);
  }
  function drawGrid(S, sy) {
    if (S.grid < .01) return;
    const step = 56, off = (sy * .15) % step;
    ctx.lineWidth = 1;
    for (let x = -off, i = 0; x < W; x += step, i++) { ctx.strokeStyle = rgba(C.teal, (i % 4 === 0 ? .1 : .045) * S.grid); ctx.beginPath(); ctx.moveTo(x + .5, 0); ctx.lineTo(x + .5, H); ctx.stroke(); }
    for (let y = (H * .5) % step, i = 0; y < H; y += step, i++) { ctx.strokeStyle = rgba(C.teal, (i % 4 === 0 ? .1 : .045) * S.grid); ctx.beginPath(); ctx.moveTo(0, y + .5); ctx.lineTo(W, y + .5); ctx.stroke(); }
  }
  function drawRings(S, A, t) {
    if (S.rings < .01 || A.shape !== 'zig') return;
    const cx = A.o.cx * W, cy = A.o.cy * H, R = A.o.r * Math.min(W, H);
    for (let j = 0; j < 4; j++) {
      const f = REDUCE ? (j + 1) / 4.5 : ((t * .2 + j / 4) % 1);
      ctx.beginPath(); ctx.arc(cx, cy, R * (1 + f * 4), 0, 7);
      ctx.strokeStyle = rgba(C.teal, (1 - f) * .45 * S.rings); ctx.lineWidth = 1.2; ctx.stroke();
    }
  }
  function drawThread(S, k, m, u) {
    if (S.ta < .01) return;
    ctx.lineCap = 'round'; ctx.lineJoin = 'round';
    const mid = mix(S.cA, S.cB, .5);
    if (S.drawn > .99) {
      ctx.beginPath(); ctx.moveTo(P[0], P[1]);
      for (let i = 1; i < N; i++) ctx.lineTo(P[i * 3], P[i * 3 + 1]);
      ctx.strokeStyle = rgba(mid, .05 * S.ta); ctx.lineWidth = 28; ctx.stroke();
      ctx.strokeStyle = rgba(mid, .1 * S.ta); ctx.lineWidth = 10; ctx.stroke();
    }
    const scanOn = ids[k] === 'measure' && m === 0;
    for (let i = 0; i < N - 1; i += 2) {
      const j = Math.min(N - 1, i + 2), s = i / (N - 1);
      if (s > S.drawn) break;
      let a = S.ta, w = 2.8, col = mix(S.cA, S.cB, s);
      if (S.dash > .01) {
        if (s > S.built) { a *= ((i >> 1) % 4) >= 2 ? 1 - S.dash : lerp(1, .6, S.dash); w = lerp(w, 1.4, S.dash); } else w += 1.2 * S.dash;
      }
      if (scanOn) { if (s <= S.scan) { col = C.red; a = 1; w = 3.4; } else { col = C.slate; a = .5; w = 1.6; } }
      if (S.trail > .01 && s <= u) { const b = (1 - clamp((u - s) / .16)) * S.trail; if (b > 0) { w += 2.4 * b; col = mix(col, C.white, b * .55); } }
      ctx.strokeStyle = rgba(col, a); ctx.lineWidth = w;
      ctx.beginPath(); ctx.moveTo(P[i * 3], P[i * 3 + 1]); ctx.lineTo(P[i * 3 + 3], P[i * 3 + 4]); ctx.lineTo(P[j * 3], P[j * 3 + 1]); ctx.stroke();
    }
  }
  function drawQueues(k, m, S, u) {
    const id = ids[k]; if (id !== 'knot' && id !== 'call') return;
    const fa = S.ta * (id === 'call' ? 1 - m : 1);
    for (const h of HANDOFFS) {
      const q = pt(h), hit = u >= h;
      ctx.beginPath(); ctx.arc(q[0], q[1], 6, 0, 7); ctx.fillStyle = bg(fa); ctx.fill();
      ctx.strokeStyle = hit ? rgba(S.cB, fa) : fg(.4 * fa); ctx.lineWidth = 2; ctx.stroke();
    }
  }
  function drawStations(k, p, m, S, u) {
    const id = ids[k]; if (id !== 'propose' && id !== 'build' && id !== 'golive') return;
    const out = id === 'golive' ? 1 - smooth(0, .3, m) : 1;
    STOPS.forEach((s, i) => {
      let vis = 1, fill = 0, hot = 0;
      if (id === 'propose') vis = smooth(.18 + i * .14, .24 + i * .14, p);
      else if (id === 'build') fill = smooth(s - .02, s + .005, S.built);
      else { fill = 1; hot = u >= s ? Math.max(0, 1 - (u - s) / .1) : 0; }
      vis *= out; if (vis < .01) return;
      const [x, y] = pt(s), col = id === 'golive' ? mix(C.teal, C.green, .7) : C.teal;
      if (hot > 0) { ctx.beginPath(); ctx.arc(x, y, 12 + 30 * (1 - hot), 0, 7); ctx.strokeStyle = rgba(C.green, hot * .7 * out); ctx.lineWidth = 2; ctx.stroke(); }
      ctx.beginPath(); ctx.arc(x, y, 11, 0, 7); ctx.fillStyle = bg(vis); ctx.fill();
      ctx.strokeStyle = rgba(col, vis); ctx.lineWidth = 2.5; ctx.stroke();
      if (fill > 0) { ctx.beginPath(); ctx.arc(x, y, 5.5 * fill, 0, 7); ctx.fillStyle = rgba(col, vis); ctx.fill(); }
    });
  }
  function drawMeasure(k, p, m, S) {
    if (ids[k] !== 'measure' || m > .1) return;
    const V = mobile;
    for (const s of [0, 1]) { const [x, y] = pt(s); ctx.beginPath(); if (V) { ctx.moveTo(x - 12, y); ctx.lineTo(x + 12, y); } else { ctx.moveTo(x, y - 14); ctx.lineTo(x, y + 14); } ctx.strokeStyle = fg(.8); ctx.lineWidth = 2; ctx.stroke(); }
    for (const pin of PINS.measure) {
      if (pin.cls !== 'pin--gap' || S.scan < pin.s) continue;
      const [x, y] = pt(pin.s);
      ctx.beginPath(); ctx.arc(x, y, 7, 0, 7); ctx.fillStyle = bg(1); ctx.fill(); ctx.strokeStyle = rgba(C.red, 1); ctx.lineWidth = 2.5; ctx.stroke();
    }
    if (!V && p >= .72) { const [x, y] = pt(.985); ctx.beginPath(); ctx.moveTo(x - 3, y); ctx.lineTo(x + 3, y); ctx.strokeStyle = rgba([10, 111, 120], 1); ctx.lineWidth = 9; ctx.stroke(); }
    if (V && S.scan > .001 && S.scan < .999) {
      const [x, y] = pt(S.scan), g = ctx.createLinearGradient(0, 0, W, 0);
      g.addColorStop(0, fg(.9)); g.addColorStop(1, fg(0));
      ctx.strokeStyle = g; ctx.lineWidth = 1.5; ctx.beginPath(); ctx.moveTo(x - 16, y); ctx.lineTo(W, y); ctx.stroke();
    } else if (S.scan > .001 && S.scan < .999) {
      const [x, y] = pt(S.scan), g = ctx.createLinearGradient(0, y - 130, 0, y + 130);
      g.addColorStop(0, fg(0)); g.addColorStop(.5, fg(.9)); g.addColorStop(1, fg(0));
      ctx.strokeStyle = g; ctx.lineWidth = 1.5; ctx.beginPath(); ctx.moveTo(x, y - 130); ctx.lineTo(x, y + 130); ctx.stroke();
    }
  }
  /* ───────── Flow mode: labels are drawn on the graphic itself ───────── */
  const MONO = '"JetBrains Mono", monospace', SANS = 'Poppins, sans-serif';
  const strip = s => String(s).replace(/<small>.*?<\/small>/g, '').replace(/<br>/g, ' · ').replace(/<[^>]+>/g, '');
  function tag(x, y, t, align, col) {
    ctx.font = `500 10px ${MONO}`; ctx.textBaseline = 'middle'; ctx.textAlign = align;
    const w = ctx.measureText(t).width + 8, x0 = align === 'left' ? x - 4 : align === 'right' ? x - w + 4 : x - w / 2;
    ctx.fillStyle = bg(.82); ctx.fillRect(x0, y - 8, w, 16);
    ctx.fillStyle = col; ctx.fillText(t, x, y + .5);
  }
  function label(x, y, t, d, align, col) {
    ctx.textAlign = align; ctx.textBaseline = 'middle';
    ctx.font = `600 13.5px ${SANS}`; ctx.fillStyle = col; ctx.fillText(t, x, d ? y - 8 : y);
    if (d) { ctx.font = `500 9.5px ${MONO}`; ctx.fillStyle = fg(.62); ctx.fillText(d, x, y + 9); }
  }
  function drawLabels(k, p, S, u) {
    const id = ids[k], set = PINS[id] || [];
    if (id === 'hero' || id === 'knot') {
      ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.font = `500 8.5px ${MONO}`;
      SRC.forEach(([t, c], i) => { const x = hubX(i) * W; ctx.fillStyle = rgba(c, 1); ctx.fillRect(x - 3, 8, 6, 6); ctx.fillStyle = fg(.7); ctx.fillText(t.toUpperCase(), x, 25); });
    }
    if (id === 'knot') GROUPS.forEach((t, i) => {
      const [x, y] = pt(HANDOFFS[i]), hit = u >= HANDOFFS[i], right = x < W * .5;
      tag(x + (right ? 14 : -14), y, t, right ? 'left' : 'right', hit ? rgba(C.red, 1) : fg(.45));
    });
    if (id === 'listen') {
      ctx.globalAlpha = smooth(.22, .42, p);
      set.forEach((pin, i) => {
        const x = colX(i), y = COL_BASE();
        ctx.textAlign = 'center'; ctx.textBaseline = 'middle'; ctx.font = `700 12px ${MONO}`; ctx.fillStyle = rgba(C.teal, 1); ctx.fillText(teamCount[i], x, y + 20);
        ctx.save(); ctx.translate(x + 4, y + 34); ctx.rotate(-Math.PI / 4);
        ctx.textAlign = 'right'; ctx.font = `500 8.5px ${MONO}`; ctx.fillStyle = fg(.72); ctx.fillText(pin.t, 0, 0); ctx.restore();
      });
      ctx.globalAlpha = 1;
    }
    if (id === 'measure') for (const pin of set) {
      if (pin.cls === 'pin--sum') {
        if (S.scan >= .99) { label(W / 2, H * .93, pin.t, pin.d, 'center', fg(1)); }
        continue;
      }
      if (!pin.on(p, S)) continue;
      const [x, y] = pt(pin.s);
      label(x + 22, y, pin.t, pin.d, 'left', pin.cls === 'pin--gap' ? '#c92a1f' : fg(1));
    }
    if (id === 'propose' || id === 'build' || id === 'golive') STOPS.forEach((s, i) => {
      const pin = set[i]; let on = true, col = rgba(C.teal, 1);
      if (id === 'propose') on = p >= .2 + i * .14;
      else if (id === 'build') col = S.built >= s ? rgba(C.teal, 1) : fg(.45);
      else { on = u >= s; col = rgba(C.green, 1); }
      if (!on) return;
      const [x, y] = pt(s), left = x > W * .5;
      label(x + (left ? -24 : 24), y, strip(pin.t), strip(pin.d), left ? 'right' : 'left', col);
    });
    if (id === 'stay') for (const pin of set) label((S.o.cx + pin.dx * S.o.a) * W, S.o.cy * H + pin.dy * S.o.a * W, pin.t, '', 'center', fg(1));
  }

  let hudK = 0;
  function frameFlow(now, dt, sy) {
    if (capEl) { capEl.classList.remove('is-active'); capEl = null; }
    if (bodyLight) { bodyLight = false; document.body.classList.remove('light'); }
    const mid = sy + VH * .55, t = now / 1000;
    let k = 0, hp = 0;
    for (let i = 0; i < secs.length; i++) if (mid >= tops[i]) k = i;
    for (const f of figs) {
      if (!f.w || !f.h) continue;
      const q = clamp((mid - f.top) / f.h);
      if (f.k === k) hp = q;
      if (f.top > sy + VH + 80 || f.top + f.h < sy - 80) continue;
      ctx = f.c; W = f.w; H = f.h; LIGHT = ids[f.k] === 'measure';
      const A = state(f.k, q);
      SHAPES[A.shape](bufA, A.o); P.set(bufA);
      let u = A.tickU;
      if (f.k === IX.stay) { if (!REDUCE) loopT += dt; u = (.9 + loopT * .075) % 1; }
      ctx.setTransform(DPR, 0, 0, DPR, 0, 0);
      ctx.clearRect(0, 0, W, H);
      drawGlow(A); drawGrid(A, 0); drawRings(A, A, t); drawCrowd(f.k, q, 0, t);
      drawThread(A, f.k, 0, u); drawQueues(f.k, 0, A, u); drawStations(f.k, q, 0, A, u);
      drawMeasure(f.k, q, 0, A); drawTicket(A, f.k, 0, u); drawLabels(f.k, q, A, u);
    }
    LIGHT = false; ctx = stageCtx;
    updateHud(k, hp, state(k, hp), sy);
  }

  const chip = $('#chip');
  let chipOn = false, chipOk = false;
  function drawTicket(S, k, m, u) {
    const id = ids[k], a = id === 'hero' ? 1 : S.tick;
    const want = !mobile && a > .7 && (id === 'knot' || id === 'hero' || (id === 'golive' && m < .2));
    if (want !== chipOn) { chip.classList.toggle('is-on', want); chipOn = want; }
    if (a < .02) return;
    let x, y;
    if (id === 'hero' && !mobile) { x = lerp(.62 * W, pt(.12)[0], ease(m)); y = lerp(.3 * H, pt(.12)[1], ease(m)); } else [x, y] = pt(u);
    const ok = k >= IX.propose;
    if (ok !== chipOk) { chip.classList.toggle('is-ok', ok); chipOk = ok; }
    const col = ok ? C.green : C.red;
    const g = ctx.createRadialGradient(x, y, 0, x, y, 34);
    g.addColorStop(0, rgba(col, .55 * a)); g.addColorStop(1, rgba(col, 0));
    ctx.fillStyle = g; ctx.beginPath(); ctx.arc(x, y, 34, 0, 7); ctx.fill();
    ctx.beginPath(); ctx.arc(x, y, 7, 0, 7); ctx.fillStyle = rgba(col, a); ctx.fill();
    ctx.beginPath(); ctx.arc(x, y, 2.6, 0, 7); ctx.fillStyle = `rgba(255,255,255,${a})`; ctx.fill();
    if (want) chip.style.transform = `translate3d(${(x + 16).toFixed(1)}px,${(y - 32).toFixed(1)}px,0)`;
  }

  /* ───────── HUD: the incident record keeps score ───────── */
  const hud = $('#hud');
  const hudEl = { tag: $('#hudTag'), state: $('#hudState'), group: $('#hudGroup'), elapsed: $('#hudElapsed'), hand: $('#hudHand'), lost: $('#hudLost') };
  const hudLast = {};
  const setText = (key, v) => { if (hudLast[key] !== v) { hudLast[key] = v; hudEl[key].textContent = v; } };
  const fmtElapsed = min => {
    min = Math.round(min);
    if (min < 60) return `${min}m`;
    const h = Math.floor(min / 60), mm = String(min % 60).padStart(2, '0');
    return h < 24 ? `${h}h ${mm}m` : `${Math.floor(h / 24)}d ${String(h % 24).padStart(2, '0')}h ${mm}m`;
  };
  const TOTAL_MIN = 54 * 60 + 8, TOTAL_LOST = 7820;
  let hudMode = '';
  function updateHud(k, p, S, sy) {
    const on = k >= IX.knot && sy < storyEnd - VH * .9;
    if (on !== hud.classList.contains('is-on')) hud.classList.toggle('is-on', on);
    if (!on) return;
    let mode, tag, st, group, min, hand, lost;
    if (k === IX.knot) {
      const u = S.tickU, done = u >= .875;
      hand = HANDOFFS.filter(h => u >= h).length; group = hand ? GROUPS[hand - 1] : 'Unassigned';
      st = done ? 'Resolved' : u < .3 ? 'New' : u < .58 ? 'In Progress' : u < .7 ? 'On Hold · awaiting vendor' : 'In Progress';
      min = knotMin(p); lost = TOTAL_LOST * (min / TOTAL_MIN); mode = done ? 'base' : 'bad'; tag = done ? 'AS-IS BASELINE' : 'LIVE';
    } else if (k < IX.golive) { hand = 9; group = 'Platform team'; st = 'Resolved, eventually'; min = TOTAL_MIN; lost = TOTAL_LOST; mode = 'base'; tag = 'AS-IS BASELINE'; }
    else if (k === IX.golive) {
      const u = S.tickU, q = clamp((u - .1) / .8);
      hand = u >= STOPS[2] ? 1 : 0; group = u < STOPS[0] ? 'Unassigned' : u < STOPS[2] ? 'AI triage' : 'Platform team';
      st = u < STOPS[0] ? 'New' : u < STOPS[4] ? 'In Progress' : 'Resolved'; min = 12 * q; lost = 68 * q; mode = 'ok'; tag = 'NEW ROUTE';
    } else { hand = 1; group = 'Platform team'; st = 'Closed'; min = 12; lost = 68; mode = 'ok'; tag = 'RUNNING'; }
    if (mode !== hudMode) { hud.classList.toggle('is-ok', mode === 'ok'); hud.classList.toggle('is-base', mode === 'base'); hudMode = mode; }
    setText('tag', tag); setText('state', st); setText('group', group);
    setText('elapsed', fmtElapsed(min)); setText('hand', `${hand}×`); setText('lost', Math.round(lost).toLocaleString('en-US'));
  }

  /* ───────── Rail ───────── */
  const rail = $('#rail'), railItems = [];
  secs.forEach((s, i) => {
    if (!s.dataset.label) return;
    const a = document.createElement('a');
    a.href = `#${s.id}`; a.innerHTML = `<i>${String(i).padStart(2, '0')}</i><span>${s.dataset.label}</span>`;
    rail.appendChild(a); railItems[i] = a;
  });
  const progress = $('#progress');

  /* ───────── Frame loop ───────── */
  let sy = window.scrollY, last = performance.now(), loopT = 0, bodyState = '', railK = -1, capEl = null, wordK = -1, bodyLight = false;
  function frame(now) {
    requestAnimationFrame(frame);
    const raw = (now - last) / 1000, dt = Math.min(.05, raw); last = now;
    const target = window.scrollY;
    sy = REDUCE ? target : sy + (target - sy) * (1 - Math.exp(-raw * 9));
    if (Math.abs(target - sy) < .1) sy = target;

    progress.style.transform = `scaleX(${clamp(sy / docH).toFixed(4)})`;
    const bs = sy < VH * .5 ? 'at-hero' : sy > storyEnd - VH * .6 ? 'past-story' : '';
    if (bs !== bodyState) { document.body.classList.remove('at-hero', 'past-story'); if (bs) document.body.classList.add(bs); bodyState = bs; }

    if (mobile) { frameFlow(now, dt, sy); return; }

    let k = 0;
    for (let i = 0; i < secs.length; i++) if (sy >= tops[i]) k = i;
    const p = clamp((sy - tops[k]) / Math.max(1, hs[k] - VH));
    const m = k < secs.length - 1 ? clamp((sy - (tops[k] + hs[k] - VH)) / VH) : 0;

    if (k !== railK) { if (railItems[railK]) railItems[railK].classList.remove('is-active'); if (railItems[k]) railItems[k].classList.add('is-active'); railK = k; }

    // captions: one pinned at a time, swapped by progress; none while the scene is changing
    const list = caps[k], n = list.length;
    const el = n && m < .1 && sy < storyEnd - VH ? list[Math.min(n - 1, Math.floor(p * n))] : null;
    if (el !== capEl) { if (capEl) capEl.classList.remove('is-active'); if (el) el.classList.add('is-active'); capEl = el; }

    // theme and the giant word belong to whichever scene owns most of the screen
    const eff = m > .5 ? k + 1 : k, light = ids[eff] === 'measure' && sy < storyEnd - VH * .5;
    if (light !== bodyLight) { bodyLight = light; document.body.classList.toggle('light', light); }
    LIGHT = light;
    if (eff !== wordK) { bigword.textContent = secs[eff].dataset.word || ''; wordK = eff; }
    const wp = eff === k ? p : 0, wo = eff === k ? 1 - smooth(.25, .5, m) : smooth(.5, .85, m);
    bigword.style.opacity = wo.toFixed(3);
    bigword.style.transform = `translate3d(${lerp(12, -55, wp).toFixed(2)}vw,-50%,0)`;

    const A = state(k, p);
    let S = A;
    SHAPES[A.shape](bufA, A.o);
    if (m > 0) {
      const B = state(k + 1, 0), e = ease(m);
      SHAPES[B.shape](bufB, B.o);
      for (let i = 0; i < N * 3; i++) P[i] = lerp(bufA[i], bufB[i], e);
      S = blend(A, B, e);
    } else P.set(bufA);

    let u = S.tickU;
    if (k === IX.stay) { if (!REDUCE) loopT += dt; u = (.9 + loopT * .075) % 1; } else loopT = 0;

    updateHud(k, p, S, sy);
    if (sy > storyEnd) return;

    ctx.setTransform(DPR, 0, 0, DPR, 0, 0);
    ctx.clearRect(0, 0, W, H);
    drawGlow(S);
    drawGrid(S, sy);
    drawRings(S, A, now / 1000);
    drawCrowd(k, p, m, now / 1000);
    drawThread(S, k, m, u);
    drawQueues(k, m, S, u);
    drawPins(k, p, m, A, S);
    drawStations(k, p, m, S, u);
    drawMeasure(k, p, m, S);
    drawTicket(S, k, m, u);
  }

  /* ───────── Below the story ───────── */
  const once = new IntersectionObserver(es => {
    for (const e of es) {
      if (!e.isIntersecting) continue;
      once.unobserve(e.target);
      if (e.target.matches('.curve')) e.target.classList.add('in');
      if (e.target.dataset.count) countUp(e.target);
    }
  }, { threshold: .4 });
  $$('.curve, [data-count]').forEach(el => once.observe(el));
  function countUp(el) {
    const to = +el.dataset.count;
    if (REDUCE) { el.textContent = to; return; }
    const t0 = performance.now();
    const tick = now => { const t = clamp((now - t0) / 1400); el.textContent = Math.round(to * (1 - Math.pow(1 - t, 3))); if (t < 1) requestAnimationFrame(tick); };
    requestAnimationFrame(tick);
  }

  // flow mode: copy fades up as it arrives
  const arrive = new IntersectionObserver(es => { for (const e of es) if (e.isIntersecting) { e.target.classList.add('in'); arrive.unobserve(e.target); } }, { threshold: .08, rootMargin: '0px 0px -6% 0px' });
  caps.flat().forEach(el => (REDUCE ? el.classList.add('in') : arrive.observe(el)));

  // capability tabs
  $$('.capsx').forEach(root => {
    const tabs = $$('[data-tab]', root), panels = $$('[data-panel]', root);
    tabs.forEach(t => t.addEventListener('click', () => {
      tabs.forEach(x => x.setAttribute('aria-selected', x === t));
      panels.forEach(x => x.classList.toggle('is-on', x.dataset.panel === t.dataset.tab));
    }));
  });

  // savings estimate
  const RATE = { deflect: .30, effort: .35, outage: .40, tool: 120000 };
  const money = v => (v >= 1e6 ? `$${(v / 1e6).toFixed(v >= 1e7 ? 1 : 2)}M` : `$${Math.round(v / 1e3).toLocaleString('en-US')}K`);
  const inp = { t: $('#iTickets'), c: $('#iCost'), h: $('#iHours'), o: $('#iOutage'), n: $('#iTools') };
  function calc() {
    const t = +inp.t.value, c = +inp.c.value, h = +inp.h.value, o = +inp.o.value * 1000, n = +inp.n.value;
    $('#oTickets').textContent = t.toLocaleString('en-US'); $('#oCost').textContent = `$${c}`; $('#oHours').textContent = `${h} h`;
    $('#oOutage').textContent = money(o); $('#oTools').textContent = n;
    const yr = t * 12, rows = [yr * RATE.deflect * c, yr * (1 - RATE.deflect) * RATE.effort * c, h * RATE.outage * o, n * RATE.tool];
    const total = rows.reduce((a, b) => a + b, 0), max = Math.max(...rows, 1);
    $('#cTotal').textContent = money(total);
    ['A', 'B', 'C', 'D'].forEach((id, i) => { $(`#c${id}`).textContent = money(rows[i]); $(`#b${id}`).style.width = `${(rows[i] / max) * 100}%`; });
    let cum = 0; const pts = [[0, 0]];
    for (let mth = 1; mth <= 36; mth++) { cum += (total / 12) * clamp((mth - 4) / 5); pts.push([mth, cum]); }
    $('#c36').textContent = money(cum);
    const X = mth => (mth / 36) * 600, Y = v => 200 - (v / (cum || 1)) * 172;
    const d = pts.map(([mth, v], i) => `${i ? 'L' : 'M'}${X(mth).toFixed(1)},${Y(v).toFixed(1)}`).join('');
    $('#curveLine').setAttribute('d', d); $('#curveArea').setAttribute('d', `${d}L600,200L0,200Z`);
    const dot = $('#curveDot'); dot.setAttribute('cx', 600); dot.setAttribute('cy', Y(cum));
  }
  Object.values(inp).forEach(el => el.addEventListener('input', calc));
  calc();

  /* ───────── Boot ───────── */
  measure();
  window.addEventListener('resize', measure);
  window.addEventListener('load', measure);
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(measure);
  if ('ResizeObserver' in window) new ResizeObserver(measure).observe(document.body);
  requestAnimationFrame(frame);
})();
