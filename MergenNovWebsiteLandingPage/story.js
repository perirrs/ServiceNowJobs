/* Mergen | Follow one ticket
   A single thread is drawn on a fixed canvas. Scroll position decides its shape:
   knot → measured line → designed route → running loop. One ticket rides it. */
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
    teal: [72, 209, 204], blue: [47, 127, 232], green: [76, 218, 132],
    red: [255, 90, 79], amber: [255, 181, 71], slate: [128, 152, 170], white: [240, 250, 252],
  };

  /* ───────── Chapters ───────── */
  const secs = $$('[data-ch]');
  const ids = secs.map(s => s.dataset.ch);
  const IX = Object.fromEntries(ids.map((id, i) => [id, i]));

  const canvas = $('#stage');
  const ctx = canvas.getContext('2d');
  let W = 0, H = 0, VH = 0, DPR = 1, mobile = false;
  let tops = [], hs = [], storyEnd = 0, docH = 1;

  function measure() {
    W = window.innerWidth; VH = window.innerHeight; mobile = W < 1000; H = mobile ? Math.round(VH * .42) : VH;
    DPR = Math.min(window.devicePixelRatio || 1, 2);
    canvas.width = Math.round(W * DPR); canvas.height = Math.round(H * DPR);
    tops = secs.map(s => s.getBoundingClientRect().top + window.scrollY);
    hs = secs.map(s => s.offsetHeight);
    const last = secs.length - 1;
    storyEnd = tops[last] + hs[last];
    docH = Math.max(1, document.documentElement.scrollHeight - VH);
    if (typeof measureKnot === 'function') measureKnot();
  }

  /* ───────── Thread shapes: each fills [x, y, depth] × N in pixels ───────── */
  const N = 360;
  const bufA = new Float32Array(N * 3), bufB = new Float32Array(N * 3), P = new Float32Array(N * 3);
  let rot = 0;

  const narrow = () => (W < 1200 ? .9 : 1);
  const KNOT = (r, mess = 0) => (mobile ? { cx: .5, cy: .27, r: r * .92, mess } : { cx: .69, cy: .5, r: r * narrow(), mess });
  const LINE = () => (mobile ? { x0: .08, x1: .92, y: .55 } : { x0: .46, x1: .95, y: .5 });
  const ROUTE = () => (mobile ? { h: 1, cy: .55, amp: .2 } : { h: 0, cx: .72, amp: .09 });
  const LOOP = () => (mobile ? { cx: .5, cy: .56, a: .36 } : { cx: .7, cy: .5, a: .19 });

  function shKnot(out, o, sy, ph = 0, sc = 1) {
    const cx = o.cx * W, cy = o.cy * H, R = o.r * Math.min(W, H) * sc;
    // A (3,4) torus knot: a clean woven form. `mess` bends it out of true as the ticket struggles.
    const mess = o.mess || 0;
    const rz = rot + sy * .0007 + ph * .9, rx = .95 + ph * .2, wob = Math.sin(rz * .5) * .22;
    const cZ = Math.cos(rz), sZ = Math.sin(rz), cX = Math.cos(rx), sX = Math.sin(rx), cW = Math.cos(wob), sW = Math.sin(wob);
    const stem = (H * .8) / R;
    for (let i = 0; i < N; i++) {
      const s = i / (N - 1), a = s * Math.PI * 2 + ph;
      const rad = .64 + .33 * Math.cos(4 * a);
      const x = rad * Math.cos(3 * a) + mess * (.2 * Math.sin(5 * a + .7) + .1 * Math.sin(11 * a + 2.1));
      const y = rad * Math.sin(3 * a) + mess * (.2 * Math.cos(7 * a + 1.9) + .1 * Math.cos(13 * a));
      const z = .33 * Math.sin(4 * a) + mess * .28 * Math.sin(9 * a + .2);
      const xa = x * cZ - y * sZ, ya = x * sZ + y * cZ;          // spin in its own plane
      const y1 = ya * cX - z * sX, zb = ya * sX + z * cX;        // tilt toward the viewer
      const x1 = xa * cW + zb * sW, z2 = -xa * sW + zb * cW;     // slow wobble
      const k = 1 / (1 - z2 * .2);
      // loose ends: the thread arrives from above and leaves below
      const w = smooth(0, .1, s) * (1 - smooth(.9, 1, s));
      const px = x1 * k * w, py = y1 * k * w + lerp(-stem, stem, s) * (1 - w);
      out[i * 3] = cx + px * R; out[i * 3 + 1] = cy + py * R;
      out[i * 3 + 2] = lerp(.5, clamp((z2 + .75) / 1.5), w);
    }
  }
  function shLine(out, o) {
    for (let i = 0; i < N; i++) {
      const s = i / (N - 1);
      out[i * 3] = lerp(o.x0, o.x1, s) * W; out[i * 3 + 1] = o.y * H; out[i * 3 + 2] = .7;
    }
  }
  function shRoute(out, o) {
    for (let i = 0; i < N; i++) {
      const s = i / (N - 1), w = Math.sin(s * Math.PI * 3);
      if (o.h) { out[i * 3] = lerp(-.1, 1.1, s) * W; out[i * 3 + 1] = o.cy * H + o.amp * H * w; }
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
  // The ticket's as-is path: a zigzag between the nine queues it gets bounced through
  const REG = () => (mobile ? { x0: .08, x1: .92, y0: .26, y1: .84 } : { x0: .47, x1: .94, y0: .27, y1: .8 });
  const ZIG = () => (mobile ? { cx: .5, cy: .55, r: .3 } : { cx: .705, cy: .535, r: .2 });
  const ZN = [[.3, -.75], [.05, .1], [.55, 0], [.2, .5], [.8, .3], [.35, .85], [.95, .7], [.5, .45], [.12, .8], [.75, 1], [.75, 1.7]];
  let ZS = null;
  function shZig(out) {
    if (!ZS) ZS = [0, ...HANDOFFS, 1];
    const R = REG();
    let j = 0;
    for (let i = 0; i < N; i++) {
      const s = i / (N - 1);
      while (j < ZS.length - 2 && s > ZS[j + 1]) j++;
      const t = clamp((s - ZS[j]) / (ZS[j + 1] - ZS[j]));
      out[i * 3] = lerp(R.x0, R.x1, lerp(ZN[j][0], ZN[j + 1][0], t)) * W;
      out[i * 3 + 1] = lerp(R.y0, R.y1, lerp(ZN[j][1], ZN[j + 1][1], t)) * H;
      out[i * 3 + 2] = .8;
    }
  }
  const SHAPES = { knot: shKnot, zig: shZig, line: shLine, route: shRoute, loop: shLoop };

  /* ───────── The crowd: every other ticket in the estate ───────── */
  const M = 130;
  const SRC = [
    ['Email', [72, 209, 204], .3], ['Portal', [47, 127, 232], .22], ['Phone', [255, 181, 71], .16],
    ['Chat', [167, 139, 250], .14], ['Monitoring', [76, 218, 132], .12], ['Walk-up', [244, 114, 182], .06],
  ];
  const TEAMW = [.27, .2, .18, .15, .12, .08];
  let seed = 48213;
  const rnd = () => { seed = (seed + 0x6D2B79F5) | 0; let t = Math.imul(seed ^ (seed >>> 15), 1 | seed); t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t; return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
  const pick = w => { let r = rnd(), i = 0; while (i < w.length - 1 && r > w[i]) r -= w[i++]; return i; };
  const teamCount = [0, 0, 0, 0, 0, 0];
  const TK = Array.from({ length: M }, () => {
    const team = pick(TEAMW);
    return { rx: rnd(), ry: rnd(), ph: rnd() * 6.28, f: rnd(), age: rnd(), spd: rnd(), src: pick(SRC.map(s => s[2])), team, rank: teamCount[team]++ };
  });
  const hubX = c => (mobile ? lerp(.1, .9, c / 5) : lerp(.61, .95, c / 5));
  const HUB_Y = .15;
  const colX = c => (mobile ? lerp(.12, .88, c / 5) : lerp(.5, .93, c / 5)) * W;
  const cp = [0, 0], cq = [0, 0];
  function crowdPos(lay, tk, t, out) {
    if (lay === 'cols') {
      const cell = mobile ? 15 : 28, per = Math.max(2, Math.min(3, Math.floor(((colX(1) - colX(0)) - 12) / cell)));
      const base = (mobile ? .9 : .78) * H;
      out[0] = colX(tk.team) + ((tk.rank % per) - (per - 1) / 2) * cell;
      out[1] = base - Math.floor(tk.rank / per) * (mobile ? 12 : 20);
    } else if (lay === 'flow') {
      const p = pt((tk.f + t * (.03 + tk.spd * .04)) % 1);
      out[0] = p[0]; out[1] = p[1];
    } else {
      const R = REG();
      if (lay === 'scatterH' && !mobile) { R.x0 = .6; R.y0 = .24; R.y1 = .6; }   // keep clear of the headline and the record card
      out[0] = lerp(R.x0 - .04, R.x1 + .04, tk.rx) * W + (REDUCE ? 0 : Math.sin(t * .4 + tk.ph) * 9);
      out[1] = lerp(R.y0 - .04, R.y1 + .07, tk.ry) * H + (REDUCE ? 0 : Math.cos(t * .33 + tk.ph) * 7);
    }
  }
  function crowdState(k, p) {
    switch (ids[k]) {
      case 'hero': return { la: 'scatterH', lb: 'scatterH', lt: 0, a: .95, red: 0, sc: 1, link: .6 };
      case 'knot': { const q = knotQ(p); return { la: 'scatter', lb: 'scatter', lt: 0, a: .95, red: q, sc: 1, link: lerp(.6, .1, q) }; }
      case 'call': { const t = smooth(0, .6, p); return { la: 'scatter', lb: 'scatter', lt: 0, a: lerp(.95, .45, t), red: lerp(1, .4, t), sc: 1, link: 0 }; }
      case 'listen': { const t = smooth(.04, .4, p); return { la: 'scatter', lb: 'cols', lt: t, a: lerp(.45, .95, t), red: lerp(.4, .15, t), sc: 1, link: 0 }; }
      case 'measure': return { la: 'cols', lb: 'cols', lt: 0, a: 0, red: .15, sc: 1, link: 0 };
      case 'golive': return { la: 'flow', lb: 'flow', lt: 0, a: smooth(.72, 1, p) * .9, red: 0, sc: .2, link: 0 };
      case 'stay': return { la: 'flow', lb: 'flow', lt: 0, a: .9, red: 0, sc: .2, link: 0 };
    }
    return { la: 'flow', lb: 'flow', lt: 0, a: 0, red: 0, sc: .2, link: 0 };
  }
  function drawCrowd(k, p, m, t) {
    const A = crowdState(k, p), B = m > 0 ? crowdState(k + 1, 0) : A, e = m > 0 ? ease(m) : 0;
    const a = lerp(A.a, B.a, e); if (a < .01) return;
    const red = lerp(A.red, B.red, e), sc = lerp(A.sc, B.sc, e), link = lerp(A.link, B.link, e) * (mobile ? 0 : 1);
    const ms = mobile ? .62 : 1;
    for (const tk of TK) {
      crowdPos(A.la, tk, t, cp); if (A.lt > 0) { crowdPos(A.lb, tk, t, cq); cp[0] = lerp(cp[0], cq[0], A.lt); cp[1] = lerp(cp[1], cq[1], A.lt); }
      if (e > 0) { crowdPos(B.la, tk, t, cq); cp[0] = lerp(cp[0], cq[0], e); cp[1] = lerp(cp[1], cq[1], e); }
      const x = cp[0], y = cp[1];
      const col = mix(SRC[tk.src][1], C.red, red * (.35 + .65 * tk.age));
      if (link > .01) {
        ctx.beginPath(); ctx.moveTo(hubX(tk.src) * W, HUB_Y * H + 12); ctx.lineTo(x, y);
        ctx.strokeStyle = rgba(SRC[tk.src][1], link * .16 * a); ctx.lineWidth = 1; ctx.stroke();
      }
      if (sc < .5) {
        ctx.beginPath(); ctx.arc(x, y, 2.6, 0, 7); ctx.fillStyle = rgba(C.green, a * (.4 + .6 * tk.age)); ctx.fill();
        continue;
      }
      const w = 24 * ms * sc, h = 15 * ms * sc, x0 = x - w / 2, y0 = y - h / 2;
      ctx.fillStyle = `rgba(7,28,41,${a})`; ctx.fillRect(x0, y0, w, h);
      ctx.strokeStyle = rgba(col, .55 * a); ctx.lineWidth = 1; ctx.strokeRect(x0 + .5, y0 + .5, w - 1, h - 1);
      ctx.fillStyle = rgba(col, a); ctx.fillRect(x0, y0, 3 * ms, h);
      ctx.fillStyle = rgba(col, .5 * a); ctx.fillRect(x0 + 6 * ms, y0 + 4 * ms, w * .5, 1.5); ctx.fillRect(x0 + 6 * ms, y0 + 9 * ms, w * .32, 1.5);
    }
  }

  /* ───────── What the stage looks like at chapter k, progress p ───────── */
  const NUM = ['dash', 'built', 'grid', 'rings', 'spin', 'tick', 'tickU', 'trail', 'scan', 'ghost', 'weave', 'ta', 'drawn', 'fx', 'fy'];
  const base = () => ({ weave: 0, ta: 1, drawn: 1, dash: 0, built: 1, grid: 0, rings: 0, spin: .25, tick: 0, tickU: .5, trail: 0, scan: 0, ghost: 0, fx: .7, fy: .5 });

  // The knot chapter's clock follows the timestamps printed in the copy
  const knotSteps = $$('#knot .step[data-min]');
  let anch = [[0, 0], [1, 3248]];
  function measureKnot() {
    const k = IX.knot, span = Math.max(1, hs[k] - VH);
    const a = knotSteps.map(el => {
      const r = el.getBoundingClientRect();
      return [clamp((r.top + window.scrollY + r.height / 2 - (mobile ? (H + VH) / 2 : VH * .5) - tops[k]) / span), +el.dataset.min];
    });
    if (a.length >= 2) anch = a;
  }
  const knotQ = p => clamp((p - anch[0][0]) / Math.max(.001, anch[anch.length - 1][0] - anch[0][0]));
  function knotMin(p) {
    if (p <= anch[0][0]) return anch[0][1];
    for (let i = 1; i < anch.length; i++) {
      if (p <= anch[i][0]) return lerp(anch[i - 1][1], anch[i][1], (p - anch[i - 1][0]) / Math.max(.001, anch[i][0] - anch[i - 1][0]));
    }
    return anch[anch.length - 1][1];
  }

  function state(k, p) {
    const s = base();
    if (mobile) { s.fx = .5; s.fy = .55; }
    switch (ids[k]) {
      case 'hero':
        return Object.assign(s, { shape: 'zig', o: ZIG(), cA: C.red, cB: C.red, ta: 0, drawn: .12, tick: 1, tickU: .12 });
      case 'knot': {
        const q = knotQ(p), u = lerp(.12, .88, q);
        return Object.assign(s, { shape: 'zig', o: ZIG(), cA: C.amber, cB: C.red, ta: smooth(0, .05, q), drawn: u, tick: 1, tickU: u });
      }
      case 'call': {
        const t = smooth(0, .6, p);
        return Object.assign(s, { shape: 'zig', o: ZIG(), cA: mix(C.amber, C.slate, t), cB: mix(C.red, C.slate, t), ta: lerp(1, .6, t), drawn: .88, rings: smooth(.1, .45, p), tick: lerp(1, .6, t), tickU: .88 });
      }
      case 'listen': {
        const t = smooth(.04, .4, p);
        return Object.assign(s, { shape: 'zig', o: ZIG(), cA: C.slate, cB: C.slate, ta: lerp(.6, 0, t), drawn: .88, tick: lerp(.6, 0, t), tickU: .88 });
      }
      case 'measure': {
        const sc = lerp(.0, 1, smooth(.06, .7, p));
        const c = mix(C.slate, C.red, smooth(.5, .75, p));
        return Object.assign(s, { shape: 'line', o: LINE(), cA: c, cB: c, scan: sc, spin: 0 });
      }
      case 'propose':
        return Object.assign(s, { shape: 'route', o: ROUTE(), cA: C.teal, cB: C.blue, dash: 1, built: 0, grid: 1, spin: 0 });
      case 'build':
        return Object.assign(s, { shape: 'route', o: ROUTE(), cA: C.teal, cB: mix(C.blue, C.green, p), dash: 1, built: smooth(.03, .92, p), grid: .5, spin: 0 });
      case 'golive':
        return Object.assign(s, { shape: 'route', o: ROUTE(), cA: C.teal, cB: C.green, tick: 1, trail: 1, tickU: lerp(.1, .9, smooth(.04, .9, p)), spin: 0 });
      case 'stay':
        return Object.assign(s, { shape: 'loop', o: LOOP(), cA: C.teal, cB: C.green, tick: 1, trail: 1, tickU: .9, spin: 0 });
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

  /* ───────── Annotations pinned to the thread ───────── */
  const STOPS = [.22, .36, .5, .64, .78];      // the five stations on the designed route
  const SIDES = ['r', 'r', 'l', 'l', 'r'];
  const HANDOFFS = [.14, .17, .20, .30, .42, .50, .58, .70, .86];
  const GROUPS = ['Email inbox', 'Phone queue', 'Legacy portal', 'Service desk', 'App team', 'Infrastructure', 'Hosting vendor', 'App team, again', 'Platform team'];

  const PINS = {
    hero: SRC.map(([t, c], i) => ({ kind: 'fix', cls: 'pin--hub', keep: 1, pos: () => [hubX(i) * W, HUB_Y * H], t: `<i style="background:rgb(${c})"></i>${t}`, on: () => true })),
    knot: [
      ...SRC.map(([t, c], i) => ({ kind: 'fix', cls: 'pin--hub', keep: 1, pos: () => [hubX(i) * W, HUB_Y * H], t: `<i style="background:rgb(${c})"></i>${t}`, on: () => true })),
      ...GROUPS.map((t, i) => ({ kind: 'on', cls: 'pin--ring', s: HANDOFFS[i], side: [0, 1, 0, 0, 1, 1, 0, 1, 1][i] ? 'b' : 'a', far: 16, t, on: () => true, hot: (p, S) => S.tickU >= HANDOFFS[i] })),
    ],
    listen: [
      'Service desk', 'App owners', 'Infrastructure', 'Hosting vendor', 'Security', 'No owner',
    ].map((t, i) => ({ kind: 'fix', cls: 'pin--ring pin--calm', pos: () => [colX(i), .78 * H + 34], t, d: `${teamCount[i]} stuck here`, on: p => p >= .3 + i * .04 })),
    measure: [
      { kind: 'on', cls: 'pin--cap', s: 0, side: 'b', t: 'Mon 09:02', d: 'Opened', on: () => true },
      { kind: 'on', cls: 'pin--cap', s: 1, side: 'b', t: 'Wed 15:10', d: 'Resolved', on: (p, S) => S.scan >= .98 },
      { kind: 'on', cls: 'pin--gap', s: .1, side: 'a', t: '3 duplicate records', d: 'No single intake', on: (p, S) => S.scan >= .1 },
      { kind: 'on', cls: 'pin--gap', s: .3, side: 'b', t: '19 h finding the owner', d: 'No service map', on: (p, S) => S.scan >= .3 },
      { kind: 'on', cls: 'pin--gap', s: .52, side: 'a', t: '4 queues, 4 re-explanations', d: 'Manual routing', on: (p, S) => S.scan >= .52 },
      { kind: 'on', cls: 'pin--gap', s: .74, side: 'b', t: '22 h waiting on a vendor', d: 'Handoff by email', on: (p, S) => S.scan >= .74 },
      { kind: 'on', cls: 'pin--gap', s: .93, side: 'a', t: 'A predictable failure', d: 'No expiry monitoring', on: (p, S) => S.scan >= .93 },
      { kind: 'on', cls: 'pin--sum', s: .5, side: 'a', far: 150, t: '54 h 08 m elapsed', d: '12 minutes of work', on: p => p >= .8 },
    ],
    propose: [
      ['One intake', 'Every channel, one record'], ['AI triage', 'Cause named in seconds'], ['Service map', 'What runs on what'],
      ['Right owner, once', 'No re-explaining'], ['Resolve and learn', 'The fix becomes knowledge'],
    ].map(([t, d], i) => ({ kind: 'on', cls: 'pin--station', s: STOPS[i], side: SIDES[i], t, d, on: p => p >= .08 + i * .17 })),
    build: [
      ['ITSM', 'Incident, Request, Knowledge'], ['Now Assist', 'AI agents for triage'], ['ITOM + CMDB', 'Discovery, Service Mapping'],
      ['Integration Hub', 'Vendor, HR, security'], ['Analytics + ATF', 'Measured, regression-tested'],
    ].map(([t, d], i) => ({ kind: 'on', cls: 'pin--station', s: STOPS[i], side: SIDES[i], t, d, on: (p, S) => S.built >= STOPS[i] })),
    golive: [
      ['09:02', 'Opened. Duplicates merged.'], ['09:02', 'Cause: expired certificate'], ['09:03', 'Routed to platform team'],
      ['09:09', 'Certificate renewed'], ['09:14', 'Resolved. Article published.'],
    ].map(([t, d], i) => ({ kind: 'on', cls: 'pin--live', s: STOPS[i], side: SIDES[i], t, d, on: (p, S) => S.tickU >= STOPS[i] })),
    stay: [
      { kind: 'lobe', cls: 'pin--loop', dx: -.6, dy: 0, t: 'Advise', d: 'Assess<br>Roadmap<br>Business case', on: p => p >= .02 },
      { kind: 'lobe', cls: 'pin--loop', dx: 0, dy: -.8, t: 'Build', d: 'Implement · Integrate · Automate', on: p => p >= .1 },
      { kind: 'lobe', cls: 'pin--loop', dx: .6, dy: 0, t: 'Run', d: 'Hypercare<br>Support<br>Upgrades', on: p => p >= .18 },
    ],
  };
  const pinRoot = $('#pins');
  for (const set of Object.values(PINS)) {
    for (const pin of set) {
      const el = document.createElement('div');
      el.className = `pin ${pin.kind === 'ring' ? 'pin--ring' : ''} ${pin.cls || ''} ${pin.side || ''}`;
      el.innerHTML = `<b>${pin.t}</b>${pin.d ? `<span>${pin.d}</span>` : ''}`;
      pinRoot.appendChild(el);
      pin.el = el; pin.shown = false; pin.isHot = false;
    }
  }
  let pinK = -1;
  function hidePins(k) {
    const set = PINS[ids[k]]; if (!set) return;
    for (const pin of set) { if (pin.shown) { pin.el.classList.remove('is-on', 'is-hot'); pin.shown = false; pin.isHot = false; } }
  }

  function drawPins(k, p, m, A, S) {
    if (k !== pinK) {
      // clear every other chapter's labels, whatever their flags say: a fast jump can leave one lit
      for (const [id, other] of Object.entries(PINS)) {
        if (id === ids[k]) continue;
        for (const pin of other) { pin.el.classList.remove('is-on', 'is-hot'); pin.shown = false; pin.isHot = false; }
      }
      pinK = k;
    }
    const set = PINS[ids[k]];
    if (!set || mobile) return;
    const live = m < .1;
    const R = A.shape === 'knot' ? A.o.r * Math.min(W, H) : 0;
    const cx = A.o.cx * W, cy = A.o.cy * H;
    for (const pin of set) {
      const on = (live || pin.keep) && pin.on(p, S);
      const hot = on && !!(pin.hot && pin.hot(p, S));
      if (on !== pin.shown) { pin.el.classList.toggle('is-on', on); pin.shown = on; }
      if (hot !== pin.isHot) { pin.el.classList.toggle('is-hot', hot); pin.isHot = hot; }
      if (!on) continue;
      let lx, ly, tf;
      if (pin.kind === 'fix') {
        const q = pin.pos();
        pin.el.style.transform = `translate3d(${q[0].toFixed(1)}px,${q[1].toFixed(1)}px,0) translate(-50%,-50%)`;
        continue;
      }
      if (pin.kind === 'lobe') {
        const a = A.o.a * W;
        pin.el.style.transform = `translate3d(${(cx + pin.dx * a).toFixed(1)}px,${(cy + pin.dy * a).toFixed(1)}px,0) translate(-50%,-50%)`;
        continue;
      }
      const [x, y] = pt(pin.s);
      if (pin.kind === 'ring') {
        const a = (pin.ang * Math.PI) / 180, rr = Math.max(R * 1.36, 150);
        lx = cx + Math.cos(a) * rr; ly = cy + Math.sin(a) * rr * .96;
        tf = 'translate(-50%,-50%)';
        const dx = x - lx, dy = y - ly, len = Math.hypot(dx, dy) || 1, cut = 30 / len;
        ctx.beginPath(); ctx.moveTo(lx + dx * cut, ly + dy * cut); ctx.lineTo(x, y);
        ctx.strokeStyle = hot ? rgba(C.red, .55) : pin.cls ? rgba(C.teal, .4) : 'rgba(255,255,255,.1)';
        ctx.lineWidth = 1; ctx.stroke();
        ctx.beginPath(); ctx.arc(x, y, 2.6, 0, 7);
        ctx.fillStyle = hot ? rgba(C.red, 1) : pin.cls ? rgba(C.teal, .95) : 'rgba(255,255,255,.3)'; ctx.fill();
      } else {
        const far = pin.far || 46;
        if (pin.side === 'a') { lx = x; ly = y - far; tf = 'translate(-50%,-100%)'; }
        else if (pin.side === 'b') { lx = x; ly = y + far; tf = 'translate(-50%,0)'; }
        else if (pin.side === 'l') { lx = x - 24; ly = y; tf = 'translate(-100%,-50%)'; }
        else { lx = x + 24; ly = y; tf = 'translate(0,-50%)'; }
        if ((pin.side === 'a' || pin.side === 'b') && !pin.far) {
          const dir = pin.side === 'a' ? -1 : 1;
          ctx.beginPath(); ctx.moveTo(x, y + dir * 10); ctx.lineTo(x, y + dir * (far - 8));
          ctx.strokeStyle = 'rgba(255,255,255,.22)'; ctx.lineWidth = 1; ctx.stroke();
        }
      }
      pin.el.style.transform = `translate3d(${lx.toFixed(1)}px,${ly.toFixed(1)}px,0) ${tf}`;
    }
  }

  /* ───────── Canvas layers ───────── */
  function drawGlow(S) {
    const gx = S.fx * W, gy = S.fy * H, r = Math.max(W, H) * .55;
    const c = mix(S.cA, S.cB, .5);
    const g = ctx.createRadialGradient(gx, gy, 0, gx, gy, r);
    g.addColorStop(0, rgba(c, .16)); g.addColorStop(.5, rgba(c, .05)); g.addColorStop(1, rgba(c, 0));
    ctx.fillStyle = g; ctx.fillRect(0, 0, W, H);
  }
  function drawGrid(S) {
    if (S.grid < .01) return;
    const step = 56;
    ctx.lineWidth = 1;
    for (let x = (W * .5) % step, i = 0; x < W; x += step, i++) {
      ctx.strokeStyle = rgba(C.teal, (i % 4 === 0 ? .1 : .045) * S.grid);
      ctx.beginPath(); ctx.moveTo(x + .5, 0); ctx.lineTo(x + .5, H); ctx.stroke();
    }
    for (let y = (H * .5) % step, i = 0; y < H; y += step, i++) {
      ctx.strokeStyle = rgba(C.teal, (i % 4 === 0 ? .1 : .045) * S.grid);
      ctx.beginPath(); ctx.moveTo(0, y + .5); ctx.lineTo(W, y + .5); ctx.stroke();
    }
  }
  function drawRings(S, A, t) {
    if (S.rings < .01 || A.shape !== 'zig') return;
    const cx = A.o.cx * W, cy = A.o.cy * H, R = A.o.r * Math.min(W, H);
    for (let j = 0; j < 3; j++) {
      const f = REDUCE ? (j + 1) / 3.5 : ((t * .22 + j / 3) % 1);
      ctx.beginPath(); ctx.arc(cx, cy, R * (1.05 + f * 2.1), 0, 7);
      ctx.strokeStyle = rgba(C.teal, (1 - f) * .42 * S.rings); ctx.lineWidth = 1.2; ctx.stroke();
    }
  }
  // Two fainter strands make the knot read as a tangle of several threads. They fade as it is understood.
  const SEG = Math.ceil((N - 1) / 2), order = new Uint16Array(SEG);
  const G = [new Float32Array(N * 3), new Float32Array(N * 3)];
  function drawGhosts(S, A, sy) {
    if (S.ghost < .02 || A.shape !== 'knot') return;
    const col = mix(S.cA, S.cB, .5), i0 = (N * .1) | 0, i1 = (N * .9) | 0;
    ctx.lineJoin = 'round'; ctx.lineWidth = 1;
    G.forEach((g, n) => {
      shKnot(g, A.o, sy, n ? 2.3 : 1.1, n ? .86 : 1.12);
      ctx.beginPath(); ctx.moveTo(g[i0 * 3], g[i0 * 3 + 1]);
      for (let i = i0 + 1; i <= i1; i++) ctx.lineTo(g[i * 3], g[i * 3 + 1]);
      ctx.strokeStyle = rgba(col, (n ? .16 : .22) * S.ghost); ctx.stroke();
    });
  }
  function drawThread(S, k, m, u) {
    ctx.lineCap = 'round'; ctx.lineJoin = 'round';
    const mid = mix(S.cA, S.cB, .5);
    ctx.beginPath(); ctx.moveTo(P[0], P[1]);
    for (let i = 1; i < N; i++) ctx.lineTo(P[i * 3], P[i * 3 + 1]);
    const ua = S.drawn > .99 ? S.ta : 0;
    // a wide soft bloom under the strand, so it reads as a lit, glossy ribbon
    ctx.strokeStyle = rgba(mid, .035 * ua); ctx.lineWidth = 64; ctx.stroke();
    ctx.strokeStyle = rgba(mid, .08 * ua); ctx.lineWidth = 30; ctx.stroke();
    ctx.strokeStyle = rgba(mid, .16 * ua); ctx.lineWidth = 12; ctx.stroke();

    const scanOn = ids[k] === 'measure' && m === 0;
    // Paint back to front, each piece over a dark sleeve, so the strand visibly passes over and under itself
    const wv = S.weave;
    for (let n = 0; n < SEG; n++) order[n] = n;
    if (wv > .01) order.sort((p, q) => P[p * 6 + 2] - P[q * 6 + 2]);
    for (let n = 0; n < SEG; n++) {
      const i = order[n] * 2;
      const j = Math.min(N - 1, i + 2), s = i / (N - 1), d = P[i * 3 + 2];
      if (s > S.drawn || S.ta < .01) continue;
      let a = lerp(.22 + .78 * d, .55 + .45 * d, wv) * S.ta, w = 2.4 + 3.6 * d + wv * (1.2 + 2.4 * d), col = mix(S.cA, S.cB, s);
      if (wv > .01) {
        col = mix([4, 18, 27], col, lerp(1, .45 + .55 * d, wv)); a = lerp(a, 1, wv);
        ctx.lineCap = 'butt'; ctx.strokeStyle = `rgba(4,18,27,${wv})`; ctx.lineWidth = w + 7;
        ctx.beginPath(); ctx.moveTo(P[i * 3], P[i * 3 + 1]); ctx.lineTo(P[i * 3 + 3], P[i * 3 + 4]); ctx.lineTo(P[j * 3], P[j * 3 + 1]); ctx.stroke();
        ctx.lineCap = 'round';
      }
      if (S.dash > .01) {
        if (s > S.built) { a *= ((i >> 1) % 4) >= 2 ? 1 - S.dash : lerp(1, .6, S.dash); w = lerp(w, 1.3, S.dash); }
        else w += .9 * S.dash;
      }
      if (scanOn) {
        if (s <= S.scan) { col = C.red; a = .95; w = 2.6; } else { col = C.slate; a = .4; w = 1.4; }
      }
      if (S.trail > .01 && s <= u) {
        const b = (1 - clamp((u - s) / .16)) * S.trail;
        if (b > 0) { a = Math.min(1, a + b * .4); w += 2 * b; col = mix(col, C.white, b * .55); }
      }
      ctx.strokeStyle = rgba(col, a); ctx.lineWidth = w;
      ctx.beginPath(); ctx.moveTo(P[i * 3], P[i * 3 + 1]);
      ctx.lineTo(P[i * 3 + 3], P[i * 3 + 4]); ctx.lineTo(P[j * 3], P[j * 3 + 1]); ctx.stroke();
      // gloss: a thin specular line along the nearer side of the strand
      const dashed = S.dash > .01 && s > S.built;
      if (!scanOn && !dashed && w > 2.2) {
        ctx.strokeStyle = `rgba(255,255,255,${(.5 * a * (.35 + .65 * d)).toFixed(3)})`; ctx.lineWidth = Math.max(.8, w * .28);
        const off = -w * .22;
        ctx.beginPath(); ctx.moveTo(P[i * 3], P[i * 3 + 1] + off);
        ctx.lineTo(P[i * 3 + 3], P[i * 3 + 4] + off); ctx.lineTo(P[j * 3], P[j * 3 + 1] + off); ctx.stroke();
      }
    }
  }
  function drawStations(k, p, m, S, u) {
    const id = ids[k];
    if (id !== 'propose' && id !== 'build' && id !== 'golive') return;
    const out = id === 'golive' ? 1 - smooth(0, .3, m) : 1;
    STOPS.forEach((s, i) => {
      let vis = 1, fill = 0, hot = 0;
      if (id === 'propose') vis = smooth(.06 + i * .17, .12 + i * .17, p);
      else if (id === 'build') fill = smooth(s - .03, s + .01, S.built);
      else { fill = 1; hot = u >= s ? Math.max(0, 1 - (u - s) / .1) : 0; }
      vis *= out; if (vis < .01) return;
      const [x, y] = pt(s);
      const col = id === 'golive' ? mix(C.teal, C.green, .7) : C.teal;
      if (hot > 0) { ctx.beginPath(); ctx.arc(x, y, 10 + 26 * (1 - hot), 0, 7); ctx.strokeStyle = rgba(C.green, hot * .7 * out); ctx.lineWidth = 2; ctx.stroke(); }
      ctx.beginPath(); ctx.arc(x, y, 9, 0, 7); ctx.fillStyle = `rgba(4,18,27,${vis})`; ctx.fill();
      ctx.strokeStyle = rgba(col, vis); ctx.lineWidth = 2; ctx.stroke();
      if (fill > 0) { ctx.beginPath(); ctx.arc(x, y, 4.5 * fill, 0, 7); ctx.fillStyle = rgba(col, vis); ctx.fill(); }
    });
  }
  function drawMeasure(k, p, m, S) {
    if (ids[k] !== 'measure' || m > .1) return;
    // end caps
    for (const s of [0, 1]) {
      const [x, y] = pt(s);
      ctx.beginPath(); ctx.moveTo(x, y - 12); ctx.lineTo(x, y + 12);
      ctx.strokeStyle = 'rgba(255,255,255,.7)'; ctx.lineWidth = 2; ctx.stroke();
    }
    // the gaps, as the scan reaches them
    for (const pin of PINS.measure) {
      if (pin.cls !== 'pin--gap' || S.scan < pin.s) continue;
      const [x, y] = pt(pin.s);
      ctx.beginPath(); ctx.arc(x, y, 6, 0, 7); ctx.fillStyle = 'rgba(4,18,27,1)'; ctx.fill();
      ctx.strokeStyle = rgba(C.red, 1); ctx.lineWidth = 2; ctx.stroke();
    }
    // the twelve minutes that were actually work
    if (p >= .8) {
      const [x, y] = pt(.985);
      ctx.beginPath(); ctx.moveTo(x - 3, y); ctx.lineTo(x + 3, y);
      ctx.strokeStyle = rgba(C.teal, 1); ctx.lineWidth = 7; ctx.stroke();
    }
    if (S.scan > .001 && S.scan < .999) {
      const [x, y] = pt(S.scan);
      const g = ctx.createLinearGradient(0, y - 110, 0, y + 110);
      g.addColorStop(0, 'rgba(255,255,255,0)'); g.addColorStop(.5, 'rgba(255,255,255,.9)'); g.addColorStop(1, 'rgba(255,255,255,0)');
      ctx.strokeStyle = g; ctx.lineWidth = 1.5;
      ctx.beginPath(); ctx.moveTo(x, y - 110); ctx.lineTo(x, y + 110); ctx.stroke();
    }
  }
  const chip = $('#chip');
  let chipOn = false, chipOk = false;
  function drawTicket(S, k, m, u) {
    const a = S.tick;
    const id = ids[k];
    const wantChip = !mobile && a > .7 && ((id === 'knot') || (id === 'golive' && m < .2) || id === 'hero');
    if (wantChip !== chipOn) { chip.classList.toggle('is-on', wantChip); chipOn = wantChip; }
    if (a < .02) return;
    const [x, y] = pt(u);
    const ok = k >= IX.golive;
    if (ok !== chipOk) { chip.classList.toggle('is-ok', ok); chipOk = ok; }
    const col = ok ? C.green : C.red;
    const g = ctx.createRadialGradient(x, y, 0, x, y, 30);
    g.addColorStop(0, rgba(col, .55 * a)); g.addColorStop(1, rgba(col, 0));
    ctx.fillStyle = g; ctx.beginPath(); ctx.arc(x, y, 30, 0, 7); ctx.fill();
    ctx.beginPath(); ctx.arc(x, y, 6, 0, 7); ctx.fillStyle = rgba(col, a); ctx.fill();
    ctx.beginPath(); ctx.arc(x, y, 2.4, 0, 7); ctx.fillStyle = `rgba(255,255,255,${a})`; ctx.fill();
    if (wantChip) chip.style.transform = `translate3d(${(x + 14).toFixed(1)}px,${(y - 30).toFixed(1)}px,0)`;
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
    if (h < 24) return `${h}h ${mm}m`;
    return `${Math.floor(h / 24)}d ${String(h % 24).padStart(2, '0')}h ${mm}m`;
  };
  const TOTAL_MIN = 54 * 60 + 8, TOTAL_LOST = 7820;
  let hudMode = '';
  function updateHud(k, p, S, sy) {
    const on = k >= IX.knot && sy < storyEnd - VH * .9;
    if (on !== hud.classList.contains('is-on')) hud.classList.toggle('is-on', on);
    if (!on) return;
    let mode, tag, st, group, min, hand, lost;
    if (k === IX.knot) {
      const u = S.tickU;
      hand = HANDOFFS.filter(h => u >= h).length;
      group = hand ? GROUPS[hand - 1] : 'Unassigned';
      const done = u >= .875;
      st = done ? 'Resolved' : u < .3 ? 'New' : u < .58 ? 'In Progress' : u < .7 ? 'On Hold · awaiting vendor' : 'In Progress';
      min = knotMin(p); lost = TOTAL_LOST * (min / TOTAL_MIN); mode = done ? 'base' : 'bad'; tag = done ? 'AS-IS BASELINE' : 'LIVE';
    } else if (k < IX.golive) {
      hand = 9; group = 'Platform team'; st = 'Resolved, eventually'; min = TOTAL_MIN; lost = TOTAL_LOST; mode = 'base'; tag = 'AS-IS BASELINE';
    } else if (k === IX.golive) {
      const u = S.tickU, q = clamp((u - .1) / .8);
      hand = u >= STOPS[2] ? 1 : 0;
      group = u < STOPS[0] ? 'Unassigned' : u < STOPS[2] ? 'AI triage' : 'Platform team';
      st = u < STOPS[0] ? 'New' : u < STOPS[4] ? 'In Progress' : 'Resolved';
      min = 12 * q; lost = 68 * q; mode = 'ok'; tag = 'NEW ROUTE';
    } else {
      hand = 1; group = 'Platform team'; st = 'Closed'; min = 12; lost = 68; mode = 'ok'; tag = 'RUNNING';
    }
    if (mode !== hudMode) { hud.classList.toggle('is-ok', mode === 'ok'); hud.classList.toggle('is-base', mode === 'base'); hudMode = mode; }
    setText('tag', tag); setText('state', st); setText('group', group);
    setText('elapsed', fmtElapsed(min)); setText('hand', `${hand}×`); setText('lost', Math.round(lost).toLocaleString('en-US'));
  }

  /* ───────── Rail ───────── */
  const rail = $('#rail');
  const railItems = [];
  secs.forEach((s, i) => {
    if (!s.dataset.label) return;
    const a = document.createElement('a');
    a.href = `#${s.id}`; a.innerHTML = `<i>${String(i).padStart(2, '0')}</i><span>${s.dataset.label}</span>`;
    rail.appendChild(a); railItems[i] = a;
  });
  let railK = -1;
  const progress = $('#progress');

  /* ───────── Frame loop ───────── */
  let sy = window.scrollY, last = performance.now(), loopT = 0, bodyState = '';
  function frame(now) {
    requestAnimationFrame(frame);
    const raw = (now - last) / 1000, dt = Math.min(.05, raw); last = now;
    const target = window.scrollY;
    // exponential smoothing on real elapsed time, so a slow or throttled frame catches up instead of lagging
    sy = REDUCE ? target : sy + (target - sy) * (1 - Math.exp(-raw * 9));
    if (Math.abs(target - sy) < .1) sy = target;

    progress.style.transform = `scaleX(${clamp(sy / docH).toFixed(4)})`;
    const bs = sy < VH * .5 ? 'at-hero' : sy > storyEnd - VH * .6 ? 'past-story' : '';
    if (bs !== bodyState) { document.body.classList.remove('at-hero', 'past-story'); if (bs) document.body.classList.add(bs); bodyState = bs; }

    let k = 0;
    for (let i = 0; i < secs.length; i++) if (sy >= tops[i]) k = i;
    const p = clamp((sy - tops[k]) / Math.max(1, hs[k] - VH));
    const m = k < secs.length - 1 ? clamp((sy - (tops[k] + hs[k] - VH)) / VH) : 0;

    if (k !== railK) {
      if (railItems[railK]) railItems[railK].classList.remove('is-active');
      if (railItems[k]) railItems[k].classList.add('is-active');
      railK = k;
    }

    const A = state(k, p);
    let S = A;
    SHAPES[A.shape](bufA, A.o, sy);
    if (m > 0) {
      const B = state(k + 1, 0), e = ease(m);
      SHAPES[B.shape](bufB, B.o, sy);
      for (let i = 0; i < N * 3; i++) P[i] = lerp(bufA[i], bufB[i], e);
      S = blend(A, B, e);
    } else P.set(bufA);

    if (!REDUCE) rot += dt * .16 * S.spin;
    let u = S.tickU;
    if (k === IX.stay) { if (!REDUCE) loopT += dt; u = (.9 + loopT * .075) % 1; } else loopT = 0;

    updateHud(k, p, S, sy);
    if (sy > storyEnd) return;                  // the stage is covered: nothing to draw

    ctx.setTransform(DPR, 0, 0, DPR, 0, 0);
    ctx.clearRect(0, 0, W, H);
    drawGlow(S);
    drawGrid(S);
    drawRings(S, A, now / 1000);
    drawCrowd(k, p, m, now / 1000);
    drawThread(S, k, m, u);
    if (ids[k] === 'knot' || ids[k] === 'call') {            // the nine queues
      const fa = S.ta * (ids[k] === 'call' ? 1 - m : 1);
      HANDOFFS.forEach(h => {
        const q = pt(h), hit = u >= h;
        ctx.beginPath(); ctx.arc(q[0], q[1], 5, 0, 7); ctx.fillStyle = `rgba(4,18,27,${fa})`; ctx.fill();
        ctx.strokeStyle = hit ? rgba(S.cB, fa) : `rgba(255,255,255,${.35 * fa})`; ctx.lineWidth = 1.5; ctx.stroke();
      });
    }
    drawPins(k, p, m, A, S);
    drawStations(k, p, m, S, u);
    drawMeasure(k, p, m, S);
    drawTicket(S, k, m, u);
  }

  /* ───────── Reveals ───────── */
  const io = new IntersectionObserver(es => {
    for (const e of es) e.target.classList.toggle('in', e.isIntersecting);
  }, { rootMargin: '-12% 0px -16% 0px', threshold: .15 });
  $$('.step, .ch-head').forEach(el => io.observe(el));

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
    const t0 = performance.now(), dur = 1400;
    const tick = now => {
      const t = clamp((now - t0) / dur), e = 1 - Math.pow(1 - t, 3);
      el.textContent = Math.round(to * e);
      if (t < 1) requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  }

  /* ───────── The return: savings estimate ───────── */
  const RATE = { deflect: .30, effort: .35, outage: .40, tool: 120000 };
  const money = v => (v >= 1e6 ? `$${(v / 1e6).toFixed(v >= 1e7 ? 1 : 2)}M` : `$${Math.round(v / 1e3).toLocaleString('en-US')}K`);
  // capability tabs
  $$('.capsx').forEach(root => {
    const tabs = $$('[data-tab]', root), panels = $$('[data-panel]', root);
    tabs.forEach(t => t.addEventListener('click', () => {
      tabs.forEach(x => x.setAttribute('aria-selected', x === t));
      panels.forEach(x => x.classList.toggle('is-on', x.dataset.panel === t.dataset.tab));
    }));
  });

  const inp = { t: $('#iTickets'), c: $('#iCost'), h: $('#iHours'), o: $('#iOutage'), n: $('#iTools') };

  function calc() {
    const t = +inp.t.value, c = +inp.c.value, h = +inp.h.value, o = +inp.o.value * 1000, n = +inp.n.value;
    $('#oTickets').textContent = t.toLocaleString('en-US');
    $('#oCost').textContent = `$${c}`;
    $('#oHours').textContent = `${h} h`;
    $('#oOutage').textContent = money(o);
    $('#oTools').textContent = n;
    const yr = t * 12;
    const rows = [
      yr * RATE.deflect * c,
      yr * (1 - RATE.deflect) * RATE.effort * c,
      h * RATE.outage * o,
      n * RATE.tool,
    ];
    const total = rows.reduce((a, b) => a + b, 0), max = Math.max(...rows, 1);
    $('#cTotal').textContent = money(total);
    ['A', 'B', 'C', 'D'].forEach((id, i) => {
      $(`#c${id}`).textContent = money(rows[i]);
      $(`#b${id}`).style.width = `${(rows[i] / max) * 100}%`;
    });
    // 36 months: nothing during a 4-month build, linear ramp over 5 months, then full run-rate
    let cum = 0; const pts = [[0, 0]];
    for (let mth = 1; mth <= 36; mth++) { cum += (total / 12) * clamp((mth - 4) / 5); pts.push([mth, cum]); }
    $('#c36').textContent = money(cum);
    const X = mth => (mth / 36) * 600, Y = v => 200 - (v / (cum || 1)) * 172;
    const d = pts.map(([mth, v], i) => `${i ? 'L' : 'M'}${X(mth).toFixed(1)},${Y(v).toFixed(1)}`).join('');
    $('#curveLine').setAttribute('d', d);
    $('#curveArea').setAttribute('d', `${d}L600,200L0,200Z`);
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
