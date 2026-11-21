/* Mergen landing: one kind of motion per section. Runs after the story engine, on both layouts.
   Wide screens drive the slider, accordion and card stack from scroll; phones get swipe and tap versions. */
(() => {
  'use strict';
  const $ = (s, r = document) => r.querySelector(s), $$ = (s, r = document) => [...r.querySelectorAll(s)];
  const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
  const el = (t, c, h) => { const e = document.createElement(t); if (c) e.className = c; if (h != null) e.innerHTML = h; return e; };
  const pad = n => String(n).padStart(2, '0');
  const REDUCE = matchMedia('(prefers-reduced-motion: reduce)').matches;
  const WIDE = innerWidth >= 1000;
  const jobs = [];                                   // run once per scrolled frame
  document.documentElement.classList.add('fx');

  /* ───────── Propose: a slider. Scroll moves it on wide screens, a swipe moves it on phones ───────── */
  function slider(ch) {
    const cards = $$(':scope > .step:not(.svc)', ch), n = cards.length;
    if (n < 2) return;
    const hs = el('div', 'hs'), pin = el('div', 'hs__pin'), view = el('div', 'hs__view'), track = el('div', 'hs__track');
    const bar = el('div', 'hs__bar', `<b>01</b><i><em></em></i><span>${pad(n)}</span><button type="button" aria-label="Previous card">←</button><button type="button" aria-label="Next card">→</button>`);
    cards[0].before(hs); hs.append(pin); pin.append(view, bar); view.append(track);
    cards.forEach(c => { c.classList.add('hs__card'); track.append(c); });
    hs.style.setProperty('--n', n);
    const num = $('b', bar), fill = $('em', bar), [prev, next] = $$('button', bar);
    const stepW = () => cards[0].offsetWidth + (parseFloat(getComputedStyle(track).columnGap) || 0);
    let cur = -1;
    const show = pos => {
      const i = Math.round(pos);
      if (i !== cur) { cur = i; num.textContent = pad(i + 1); cards.forEach((c, j) => c.classList.toggle('is-on', j === i)); prev.disabled = i === 0; next.disabled = i === n - 1; }
      fill.style.transform = `scaleX(${(pos / (n - 1)).toFixed(3)})`;
    };
    if (WIDE) {
      const run = () => Math.max(1, hs.offsetHeight - pin.offsetHeight);
      jobs.push(() => {
        const raw = clamp((innerHeight * .2 - hs.getBoundingClientRect().top) / run()) * (n - 1);
        const i = Math.min(n - 2, Math.floor(raw)), f = clamp((raw - i - .3) / .4);      // each card rests, then slides
        const pos = i + f * f * (3 - 2 * f);
        track.style.transform = `translate3d(${(-pos * stepW()).toFixed(1)}px,0,0)`;
        show(pos);
      });
      const go = d => scrollTo({ top: scrollY + hs.getBoundingClientRect().top - innerHeight * .2 + run() * clamp((cur + d) / (n - 1)) + 2, behavior: 'smooth' });
      prev.onclick = () => go(-1); next.onclick = () => go(1);
    } else {
      view.addEventListener('scroll', () => show(clamp(view.scrollLeft / stepW(), 0, n - 1)), { passive: true });
      prev.onclick = () => view.scrollBy({ left: -stepW(), behavior: 'smooth' });
      next.onclick = () => view.scrollBy({ left: stepW(), behavior: 'smooth' });
      show(0);
    }
  }

  /* ───────── Build: an accordion. Scroll opens each sprint in turn on wide screens, a tap opens it on phones ───────── */
  function accordion(ch) {
    const items = $$(':scope > .step:not(.svc)', ch), n = items.length;
    if (n < 2) return;
    const acc = el('div', 'acc'), pin = el('div', 'acc__pin');
    items[0].before(acc); acc.append(pin); acc.style.setProperty('--n', n);
    let cur = -1;
    const set = i => { if (i === cur) return; cur = i; items.forEach((it, j) => { it.classList.toggle('is-on', j === i); it.classList.toggle('is-done', j < i); $('.when', it).setAttribute('aria-expanded', j === i); }); };
    items.forEach((it, i) => {
      const head = $('.when', it), body = el('div', 'acc__body'), inner = el('div');
      [...it.children].filter(c => c !== head).forEach(c => inner.append(c));
      body.append(inner); it.append(body); it.classList.add('acc__item'); pin.append(it);
      head.setAttribute('role', 'button'); head.tabIndex = 0;
      const pick = () => {
        if (!WIDE) return set(i);
        const run = acc.offsetHeight - pin.offsetHeight;
        scrollTo({ top: scrollY + acc.getBoundingClientRect().top - innerHeight * .18 + run * (i + .5) / n, behavior: 'smooth' });
      };
      head.addEventListener('click', pick);
      head.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); pick(); } });
    });
    if (WIDE) jobs.push(() => {
      const q = clamp((innerHeight * .18 - acc.getBoundingClientRect().top) / Math.max(1, acc.offsetHeight - pin.offsetHeight));
      set(Math.min(n - 1, Math.floor(q * n)));
      pin.style.setProperty('--q', q.toFixed(3));
    });
    else set(0);
  }

  /* ───────── Stay: cards that stack as they arrive ───────── */
  function stack(ch) {
    const cards = $$(':scope > .step:not(.svc)', ch);
    if (cards.length < 2) return;
    const box = el('div', 'stack');
    cards[0].before(box);
    cards.forEach((c, i) => { c.style.setProperty('--i', i); box.append(c); });
  }

  /* ───────── Listen: the copy lights up word by word as it crosses the screen ───────── */
  function scrub(ch) {
    if (REDUCE) return;
    const ps = $$(':scope > .step p:not(.when)', ch).map(p => {
      const words = p.textContent.trim().split(/\s+/);
      p.textContent = '';
      const spans = words.map(w => { const s = el('span', 'w'); s.textContent = w; p.append(s, ' '); return s; });
      return { p, spans, lit: 0 };
    });
    ch.classList.add('fx-scrub');
    jobs.push(() => {
      for (const o of ps) {
        const r = o.p.getBoundingClientRect();
        const lit = Math.round(clamp((innerHeight * .9 - r.top) / (innerHeight * .34 + r.height)) * o.spans.length);
        if (lit === o.lit) continue;
        for (let i = Math.min(lit, o.lit); i < Math.max(lit, o.lit); i++) o.spans[i].classList.toggle('on', i < lit);
        o.lit = lit;
      }
    });
  }

  const ch = id => $(`#${id}`);
  if (ch('listen')) scrub(ch('listen'));
  if (ch('measure')) ch('measure').classList.add('fx-flip');
  if (ch('propose')) slider(ch('propose'));
  if (ch('build')) accordion(ch('build'));
  if (ch('stay')) stack(ch('stay'));

  /* ───────── Below the story: each block arrives its own way, once ───────── */
  const seen = new IntersectionObserver(es => {
    for (const e of es) if (e.isIntersecting) { e.target.classList.add('on'); seen.unobserve(e.target); }
  }, { threshold: .12, rootMargin: '0px 0px -7% 0px' });
  const rv = (sel, kind, stagger) => $$(sel).forEach(e => {
    e.dataset.rv = kind;
    if (stagger) e.style.setProperty('--i', [...e.parentNode.children].indexOf(e));
    if (REDUCE) e.classList.add('on'); else seen.observe(e);
  });
  rv('.sec-head, .capsx__title, .capsx__lead, .cta h2, .cta .lead, .ceo__grid > div', 'up');
  rv('.calc__in', 'left'); rv('.calc__out', 'right');
  rv('.bench article, .stats li, .pillars li', 'up', true);
  rv('.case, .partner__grid > .abr', 'scale');
  rv('.principles article', 'right', true);
  rv('.adv__grid:not(.adv__grid--line) li', 'flip', true);
  rv('.adv__grid--line li', 'draw', true);
  rv('.ceo img', 'clip');

  /* ───────── Case studies and insights: a drag-and-arrow carousel ───────── */
  $$('.news').forEach(list => {
    list.classList.add('news--car');
    $$(':scope > a', list).forEach((a, i) => { a.style.setProperty('--i', i); a.draggable = false; });
    const nav = el('div', 'car__nav', '<i><em></em></i><button type="button" aria-label="Previous">←</button><button type="button" aria-label="Next">→</button>');
    list.after(nav);
    const fill = $('em', nav), [prev, next] = $$('button', nav);
    const upd = () => {
      const max = list.scrollWidth - list.clientWidth;
      fill.style.transform = `scaleX(${clamp((list.scrollLeft + list.clientWidth) / Math.max(1, list.scrollWidth)).toFixed(3)})`;
      prev.disabled = list.scrollLeft < 4; next.disabled = list.scrollLeft > max - 4;
      nav.hidden = max < 4;
    };
    list.addEventListener('scroll', upd, { passive: true });
    addEventListener('resize', upd); addEventListener('load', upd); upd();
    prev.onclick = () => list.scrollBy({ left: -list.clientWidth * .8, behavior: 'smooth' });
    next.onclick = () => list.scrollBy({ left: list.clientWidth * .8, behavior: 'smooth' });
    let down = false, moved = false, x0 = 0, s0 = 0;
    list.addEventListener('pointerdown', e => { if (e.pointerType !== 'mouse') return; down = true; moved = false; x0 = e.clientX; s0 = list.scrollLeft; });
    addEventListener('pointermove', e => {
      if (!down) return;
      const dx = e.clientX - x0;
      if (Math.abs(dx) > 6) { moved = true; list.classList.add('is-drag'); }
      if (moved) list.scrollLeft = s0 - dx;
    });
    addEventListener('pointerup', () => { down = false; list.classList.remove('is-drag'); });
    list.addEventListener('click', e => { if (moved) { e.preventDefault(); moved = false; } }, true);
    if (REDUCE) list.classList.add('on'); else seen.observe(list);
  });

  /* ───────── Industries: pick one, the panel swaps. It cycles by itself until you choose ───────── */
  $$('.ind').forEach(list => {
    const items = $$(':scope > li', list);
    if (items.length < 2) return;
    list.classList.add('ind--pick');
    list.style.setProperty('--n', items.length);
    let cur = -1, auto = !REDUCE, onScreen = false;
    const set = i => { cur = i; items.forEach((li, j) => li.classList.toggle('is-on', j === i)); };
    items.forEach((li, i) => {
      const h = $('h3', li);
      h.tabIndex = 0; h.setAttribute('role', 'button');
      const pick = () => { auto = false; set(i); };
      h.addEventListener('click', pick);
      if (WIDE) h.addEventListener('mouseenter', pick);
      h.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); pick(); } });
    });
    set(0);
    new IntersectionObserver(es => { onScreen = es[0].isIntersecting; }, { threshold: .3 }).observe(list);
    setInterval(() => { if (auto && onScreen && !document.hidden) set((cur + 1) % items.length); }, 3200);
  });

  /* ───────── Client logos: a marquee ───────── */
  $$('.logos:not(.logos--certs)').forEach(row => {
    const imgs = $$('img', row);
    if (imgs.length < 6 || REDUCE) return;
    const track = el('div', 'mq__track');
    imgs.forEach(i => { i.loading = 'eager'; track.append(i); });
    imgs.forEach(i => { const c = i.cloneNode(true); c.alt = ''; c.setAttribute('aria-hidden', 'true'); track.append(c); });
    row.classList.add('logos--mq'); row.append(track);
    track.style.setProperty('--dur', `${imgs.length * 2.6}s`);
  });

  /* ───────── Hero: the numbers count up ───────── */
  if (!REDUCE) $$('.hero__stats b').forEach((b, k) => {
    const m = b.textContent.trim().match(/^(\d+)(\D*)$/);
    if (!m) return;
    const to = +m[1], t0 = performance.now() + 500 + k * 120;
    b.textContent = `0${m[2]}`;
    const tick = now => {
      const t = clamp((now - t0) / 1300), e = 1 - Math.pow(1 - t, 3);
      b.textContent = `${Math.round(to * e)}${m[2]}`;
      if (t < 1) requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  });

  /* ───────── One pass per frame while the page moves ───────── */
  let queued = false;
  const pass = () => { queued = false; for (const j of jobs) j(); };
  const ask = () => { if (!queued) { queued = true; requestAnimationFrame(pass); } };
  addEventListener('scroll', ask, { passive: true });
  addEventListener('resize', ask);
  addEventListener('load', ask);
  pass();
})();
