/* Mergen landing: the high-end motion layer. Runs last, after the story engine and effects.js.
   Smooth inertial scroll, masked word-rise headlines in gradient ink, glass cards that tilt and catch the light,
   magnetic buttons, a pointer glow, a type band driven by scroll speed, and sheets that open as they arrive. */
(() => {
  'use strict';
  const $ = (s, r = document) => r.querySelector(s), $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
  const el = (t, c) => { const e = document.createElement(t); if (c) e.className = c; return e; };
  const REDUCE = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const WIDE = innerWidth >= 1000;
  const FINE = matchMedia('(hover: hover) and (pointer: fine)').matches;
  const root = document.documentElement;
  if (REDUCE) root.classList.add('fx-static');

  /* ───────── Smooth scroll (wide screens with a mouse or trackpad; touch keeps native scrolling) ───────── */
  let lenis = null;
  if (window.Lenis && WIDE && FINE && !REDUCE) {
    try {
      lenis = new window.Lenis({ lerp: .085, wheelMultiplier: 1, smoothWheel: true });
      window.__go = y => lenis.scrollTo(y, { duration: 1.2 });
      document.addEventListener('click', e => {
        const a = e.target.closest('a[href^="#"]');
        if (!a || a.getAttribute('href').length < 2) return;
        const t = $(a.getAttribute('href'));
        if (t) { e.preventDefault(); lenis.scrollTo(t, { duration: 1.4 }); }
      });
    } catch (e) { lenis = null; }
  }

  /* ───────── Headlines: split into words, each rising out of its own mask, painted with one shared gradient ───────── */
  const heads = $$('.hero h1, .ch-head h2, .sec-head h2, .cta h2, .capsx__title, .ceo h2');
  heads.forEach(h => {
    const text = h.textContent.trim().replace(/\s+/g, ' ');
    h.setAttribute('aria-label', text);
    h.textContent = '';
    text.split(' ').forEach((w, i) => {
      const wd = el('span', 'wd'), wi = el('span', 'wi');
      wi.textContent = w; wi.style.setProperty('--w', i);
      wd.setAttribute('aria-hidden', 'true'); wd.append(wi); h.append(wd, ' ');
    });
  });
  const paint = () => heads.forEach(h => {
    const R = h.getBoundingClientRect();
    if (!R.width) return;
    for (const wd of h.children) {
      const r = wd.getBoundingClientRect(), cs = getComputedStyle(wd), wi = wd.firstChild;
      wi.style.backgroundSize = `${R.width.toFixed(1)}px ${R.height.toFixed(1)}px`;
      wi.style.backgroundPosition = `${(R.left - r.left - parseFloat(cs.paddingLeft)).toFixed(1)}px ${(R.top - r.top - parseFloat(cs.paddingTop)).toFixed(1)}px`;
      wi.classList.add('g');
    }
  });
  paint();
  addEventListener('resize', paint); addEventListener('load', paint);
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(paint);

  /* the story's introduction arrives like any other block, on both layouts */
  const arrive = new IntersectionObserver(es => { for (const e of es) if (e.isIntersecting) e.target.classList.add('in'); }, { threshold: .1 });
  $$('.opener .ch-head, .opener .step').forEach(e => (REDUCE ? e.classList.add('in') : arrive.observe(e)));

  /* ───────── Glass panes ───────── */
  $$('.svc, .hs__card, .fx-flip > .step:not(.svc), .stack > .step, .case, .abr, .calc__out').forEach(e => e.classList.add('gl'));
  $$('.news > a').forEach(e => e.classList.add('gl', 'gl--light'));
  $$('.proof, .adv, .industries, .partner, .ceo').forEach(e => e.classList.add('aur'));
  if (FINE) {
    $$('.gl, .ind--pick p').forEach(e => e.addEventListener('pointermove', ev => {
      const r = e.getBoundingClientRect();
      e.style.setProperty('--mx', `${(ev.clientX - r.left).toFixed(0)}px`);
      e.style.setProperty('--my', `${(ev.clientY - r.top).toFixed(0)}px`);
    }, { passive: true }));
    if (!REDUCE) {
      $$('.case, .calc__out, .partner__grid > .abr, .cap .abr, .ind--pick p, .news > a').forEach(e => {
        e.dataset.tilt = ''; e.parentNode.classList.add('persp');
        e.addEventListener('pointermove', ev => {
          const r = e.getBoundingClientRect();
          const nx = (ev.clientX - r.left) / r.width * 2 - 1, ny = (ev.clientY - r.top) / r.height * 2 - 1;
          e.style.rotate = `${(-ny).toFixed(3)} ${nx.toFixed(3)} 0 ${(Math.hypot(nx, ny) * 5).toFixed(2)}deg`;
        }, { passive: true });
        e.addEventListener('pointerleave', () => { e.style.rotate = ''; });
      });
      /* magnetic buttons */
      $$('.btn').forEach(b => {
        b.addEventListener('pointermove', ev => {
          const r = b.getBoundingClientRect();
          b.style.translate = `${((ev.clientX - r.left - r.width / 2) * .22).toFixed(1)}px ${((ev.clientY - r.top - r.height / 2) * .32).toFixed(1)}px`;
        }, { passive: true });
        b.addEventListener('pointerleave', () => { b.style.translate = ''; });
      });
    }
  }

  /* module pills: staggered arrival; on the ServiceNow page a pill opens its tab */
  $$('.mods').forEach(ul => $$('li', ul).forEach((li, i) => li.style.setProperty('--i', i)));
  $$('[data-go-tab]').forEach(a => a.addEventListener('click', () => { const b = $(`.capsx [data-tab="${a.dataset.goTab}"]`); if (b) b.click(); }));

  /* ───────── A band of type between proof and the advantage ───────── */
  const bandRows = [];
  const adv = $('#advantage');
  if (adv) {
    const band = el('div', 'band'); band.setAttribute('aria-hidden', 'true');
    for (let r = 0; r < 2; r++) {
      const row = el('div', 'band__row');
      const words = r ? ['Discover', 'Design', 'Deliver', 'Optimize'] : ['Advise', 'Build', 'Run'];
      for (let i = 0; i < 24; i++) { const s = el('span'); s.textContent = words[i % words.length]; row.append(s); }
      band.append(row); bandRows.push(row);
    }
    adv.before(band);
  }

  /* ───────── Sheets, parallax, pointer light ───────── */
  const sheets = REDUCE ? [] : $$('.return, #cases, #insights, .trust');
  sheets.forEach(s => s.classList.add('sheet'));
  const heroBody = $('.hero__body'), ctaH = $('.cta h2'), ceoImg = $('.ceo img');
  const marquees = $$('.mq__track');

  let cur = null, spot = null, px = innerWidth / 2, py = innerHeight / 2, cx = px, cy = py, sx = px, sy2 = py;
  if (FINE && WIDE && !REDUCE) {
    cur = el('div', 'cur'); spot = el('div', 'glow-spot');
    document.body.append(spot, cur);
    addEventListener('pointermove', e => {
      px = e.clientX; py = e.clientY; cur.classList.add('is-on');
      const t = e.target.closest ? e.target : null;
      cur.classList.toggle('is-drag', !!(t && t.closest('.news--car')));
      cur.classList.toggle('is-link', !!(t && !t.closest('.news--car') && t.closest('a, button, [role="button"], input, .ind--pick h3')));
    }, { passive: true });
    document.addEventListener('pointerleave', () => cur.classList.remove('is-on'));
  }

  let lastY = scrollY, vel = 0, bandX = 0, scrolled = false;
  function frame(t) {
    requestAnimationFrame(frame);
    if (lenis) lenis.raf(t);
    const y = scrollY, VH = innerHeight;
    vel += ((y - lastY) - vel) * .12; lastY = y;                 // smoothed pixels per frame
    const sc = y > 40;
    if (sc !== scrolled) { root.classList.toggle('fx-scrolled', sc); scrolled = sc; }
    if (REDUCE) return;

    if (cur) {
      cx += (px - cx) * .22; cy += (py - cy) * .22; sx += (px - sx) * .06; sy2 += (py - sy2) * .06;
      cur.style.transform = `translate3d(${cx.toFixed(1)}px,${cy.toFixed(1)}px,0)`;
      spot.style.transform = `translate3d(${sx.toFixed(1)}px,${sy2.toFixed(1)}px,0)`;
    }

    if (heroBody && y < VH * 1.2) {
      heroBody.style.translate = `0 ${(y * -.18).toFixed(1)}px`;
      heroBody.style.opacity = clamp(1 - y / (VH * .7)).toFixed(3);
    }
    for (const s of sheets) {
      const top = s.getBoundingClientRect().top;
      if (top > VH * 1.2 || top < -VH) continue;
      const q = 1 - clamp(top / (VH * .85)), e = q * q * (3 - 2 * q);
      s.style.setProperty('--si', `${((1 - e) * 7).toFixed(2)}%`);
      s.style.setProperty('--sr', `${((1 - e) * 56).toFixed(1)}px`);
    }
    if (bandRows.length) {
      const r = bandRows[0].parentNode.getBoundingClientRect();
      if (r.bottom > -100 && r.top < VH + 100) {
        bandX += 1.1 + Math.abs(vel) * .9;
        bandRows.forEach((row, i) => {
          const half = row.scrollWidth / 2 || 1, x = bandX * (i ? .8 : 1) % half;
          row.style.transform = `translate3d(${(i ? x - half : -x).toFixed(1)}px,0,0) skewX(${clamp(vel * (i ? .5 : -.5), -14, 14).toFixed(2)}deg)`;
        });
      }
    }
    if (ctaH) {
      const r = ctaH.getBoundingClientRect();
      if (r.top < VH && r.bottom > 0) { const q = clamp(1 - (r.top - VH * .35) / (VH * .65)); ctaH.style.scale = (.82 + .18 * q).toFixed(3); ctaH.style.transformOrigin = '0 50%'; }
    }
    if (ceoImg) {
      const r = ceoImg.getBoundingClientRect();
      if (r.top < VH && r.bottom > 0) ceoImg.style.translate = `0 ${(((r.top + r.height / 2) / VH - .5) * -46).toFixed(1)}px`;
    }
    const rate = 1 + Math.min(5, Math.abs(vel) * .18);
    for (const m of marquees) { const a = m.getAnimations && m.getAnimations()[0]; if (a) a.playbackRate = rate; }
  }
  requestAnimationFrame(frame);
})();
