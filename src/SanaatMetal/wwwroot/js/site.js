/* Sanaat Metal — public site scripts (no inline scripts; CSP friendly) */
(function () {
  'use strict';
  const body = document.body;
  const LANG = body.dataset.lang || 'fa';
  const CUR = body.dataset.currency || '';
  const T = JSON.parse(document.getElementById('sm-texts')?.textContent || '{}');
  const nf = new Intl.NumberFormat(LANG === 'fa' ? 'fa-IR' : 'en-US');
  const fmt = n => nf.format(Math.round(n));
  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  /* ---------- Theme ---------- */
  document.querySelectorAll('.theme-toggle').forEach(btn => btn.addEventListener('click', () => {
    const next = document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-bs-theme', next);
    try { localStorage.setItem('sm-theme', next); } catch (e) { }
  }));

  /* ---------- Navbar shadow & back to top ---------- */
  const nav = document.querySelector('.sm-nav');
  const backTop = document.querySelector('.back-top');
  const onScroll = () => {
    const y = window.scrollY;
    nav && nav.classList.toggle('scrolled', y > 10);
    backTop && backTop.classList.toggle('show', y > 600);
  };
  window.addEventListener('scroll', onScroll, { passive: true });
  onScroll();
  backTop && backTop.addEventListener('click', () => window.scrollTo({ top: 0 }));

  /* ---------- Reveal on scroll ---------- */
  if ('IntersectionObserver' in window) {
    const io = new IntersectionObserver(entries => entries.forEach(e => {
      if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); }
    }), { threshold: .12 });
    document.querySelectorAll('.reveal').forEach((el, i) => { el.style.transitionDelay = (i % 4) * 80 + 'ms'; io.observe(el); });
  } else document.querySelectorAll('.reveal').forEach(el => el.classList.add('in'));

  /* ---------- Counters ---------- */
  document.querySelectorAll('[data-count]').forEach(el => {
    const target = +el.dataset.count || 0;
    const run = () => {
      const start = performance.now(), dur = 1400;
      const step = t => {
        const p = Math.min(1, (t - start) / dur);
        el.textContent = fmt(target * (1 - Math.pow(1 - p, 3))) + (el.dataset.suffix || '');
        if (p < 1) requestAnimationFrame(step);
      };
      requestAnimationFrame(step);
    };
    if ('IntersectionObserver' in window) {
      const io = new IntersectionObserver(es => { if (es[0].isIntersecting) { run(); io.disconnect(); } });
      io.observe(el);
    } else run();
  });

  /* ---------- Sliders ---------- */
  if (window.Swiper) {
    const hero = document.querySelector('.sm-hero .swiper');
    if (hero) new Swiper(hero, {
      loop: hero.querySelectorAll('.swiper-slide').length > 1, speed: 900, effect: 'fade', fadeEffect: { crossFade: true },
      autoplay: { delay: 6000, disableOnInteraction: false },
      pagination: { el: hero.querySelector('.swiper-pagination'), clickable: true },
      navigation: { nextEl: hero.querySelector('.swiper-button-next'), prevEl: hero.querySelector('.swiper-button-prev') },
    });
    const clients = document.querySelector('.sm-clients .swiper');
    if (clients) new Swiper(clients, {
      loop: true, speed: 4000, allowTouchMove: true, spaceBetween: 18,
      autoplay: { delay: 0, disableOnInteraction: false, pauseOnMouseEnter: true },
      slidesPerView: 2, breakpoints: { 576: { slidesPerView: 3 }, 768: { slidesPerView: 4 }, 1200: { slidesPerView: 6 } },
    });
    const featured = document.querySelector('.sm-featured .swiper');
    if (featured) new Swiper(featured, {
      spaceBetween: 20, slidesPerView: 1.15, grabCursor: true,
      navigation: { nextEl: '.sm-featured .f-next', prevEl: '.sm-featured .f-prev' },
      breakpoints: { 576: { slidesPerView: 2 }, 992: { slidesPerView: 3 }, 1200: { slidesPerView: 4 } },
    });
    const thumbsEl = document.querySelector('.sm-gallery .thumbs');
    const mainEl = document.querySelector('.sm-gallery .main');
    if (mainEl) {
      const thumbs = thumbsEl ? new Swiper(thumbsEl, { spaceBetween: 8, slidesPerView: 4, watchSlidesProgress: true }) : null;
      new Swiper(mainEl, { spaceBetween: 10, thumbs: thumbs ? { swiper: thumbs } : undefined, pagination: { el: mainEl.querySelector('.swiper-pagination'), clickable: true } });
    }
  }

  /* ---------- Quantity boxes ---------- */
  document.addEventListener('click', e => {
    const b = e.target.closest('[data-qty]');
    if (!b) return;
    const input = b.parentElement.querySelector('input');
    const v = Math.max(1, (parseFloat(input.value) || 1) + parseFloat(b.dataset.qty));
    input.value = v;
    input.dispatchEvent(new Event('change', { bubbles: true }));
  });

  /* ---------- Cart (localStorage) ---------- */
  const KEY = 'sm-cart';
  const read = () => { try { return JSON.parse(localStorage.getItem(KEY) || '[]').filter(x => x && x.v > 0 && x.q > 0); } catch (e) { return []; } };
  const write = items => { try { localStorage.setItem(KEY, JSON.stringify(items)); } catch (e) { } updateBadge(true); };
  function updateBadge(bump) {
    const n = read().length;
    document.querySelectorAll('.sm-cart-badge').forEach(b => {
      b.textContent = nf.format(n);
      b.classList.toggle('d-none', n === 0);
      if (bump) { b.classList.remove('bump'); void b.offsetWidth; b.classList.add('bump'); }
    });
  }
  updateBadge(false);

  function add(variant, qty) {
    const items = read();
    const it = items.find(x => x.v === variant);
    if (it) it.q = Math.min(100000, it.q + qty); else items.push({ v: variant, q: qty });
    write(items);
    toast(T.added || 'Added');
  }

  document.addEventListener('click', e => {
    const b = e.target.closest('[data-add-cart]');
    if (!b) return;
    e.preventDefault();
    let variant = +b.dataset.addCart;
    const row = b.closest('[data-row]');
    if (!variant && row) { const sel = row.querySelector('select[data-variant-select]'); variant = sel ? +sel.value : 0; }
    if (!variant) return;
    const q = row ? parseFloat(row.querySelector('.qty-box input')?.value) || 1 : 1;
    add(variant, q);
    b.classList.add('disabled');
    const old = b.innerHTML;
    b.innerHTML = '<i class="bi bi-check2-circle"></i>';
    setTimeout(() => { b.classList.remove('disabled'); b.innerHTML = old; }, 1100);
  });

  function toast(msg) {
    const el = document.getElementById('sm-toast');
    if (!el || !window.bootstrap) return;
    el.querySelector('.toast-body span').textContent = msg;
    bootstrap.Toast.getOrCreateInstance(el, { delay: 2600 }).show();
  }

  async function fetchItems() {
    const items = read();
    if (!items.length) return [];
    const res = await fetch('/api/cart?ids=' + items.map(x => x.v).join(','), { headers: { 'Accept': 'application/json' } });
    if (!res.ok) return [];
    const data = await res.json();
    // حذف اقلامی که دیگر وجود ندارند
    const valid = items.filter(x => data.some(d => d.id === x.v));
    if (valid.length !== items.length) write(valid);
    return valid.map(x => Object.assign({ q: x.q }, data.find(d => d.id === x.v)));
  }

  const priceHtml = it => it.callForPrice
    ? `<span class="sm-call"><i class="bi bi-telephone"></i>${esc(T.call)}</span>`
    : `<span class="sm-price">${fmt(it.finalPrice)} <small>${esc(CUR)}</small></span>` +
      (it.discount > 0 ? ` <span class="sm-price-old">${fmt(it.price)}</span>` : '');

  function itemHtml(it, editable) {
    return `<div class="sm-cart-item" data-id="${it.id}">
      <a href="${esc(it.url)}"><img src="${esc(it.image)}" alt="" loading="lazy"></a>
      <div class="flex-grow-1 min-w-0">
        <a href="${esc(it.url)}" class="fw-bold d-block text-truncate" style="color:var(--sm-text)">${esc(it.name)}</a>
        <div class="small text-muted mb-1">${esc(it.variant)} · ${esc(it.unit)}</div>
        <div>${priceHtml(it)}</div>
      </div>
      <div class="d-flex flex-column align-items-end gap-2">
        ${editable ? `<div class="qty-box"><button type="button" data-qty="1">+</button><input type="number" min="1" step="1" value="${it.q}" data-cart-qty="${it.id}" aria-label="qty"><button type="button" data-qty="-1">−</button></div>`
                   : `<span class="small text-muted">× ${fmt(it.q)}</span>`}
        <button type="button" class="btn btn-sm text-danger p-0" data-cart-remove="${it.id}"><i class="bi bi-trash3"></i> ${editable ? esc(T.remove) : ''}</button>
      </div></div>`;
  }

  const total = list => list.reduce((s, it) => s + (it.callForPrice ? 0 : it.finalPrice * it.q), 0);
  const emptyHtml = `<div class="sm-empty"><i class="bi bi-cart-x d-block mb-3"></i><p>${esc(T.empty)}</p><a href="/products" class="btn btn-sm-primary">${esc(T.continue)}</a></div>`;

  async function renderMini() {
    const box = document.getElementById('mini-cart-items');
    if (!box) return;
    box.innerHTML = '<div class="text-center p-5"><div class="spinner-border text-primary"></div></div>';
    const list = await fetchItems();
    box.innerHTML = list.length ? list.map(it => itemHtml(it, false)).join('') : emptyHtml;
    const tot = document.getElementById('mini-cart-total');
    if (tot) tot.textContent = fmt(total(list)) + ' ' + CUR;
    document.getElementById('mini-cart-foot')?.classList.toggle('d-none', !list.length);
  }
  document.getElementById('miniCart')?.addEventListener('show.bs.offcanvas', renderMini);

  const page = document.getElementById('cart-page');
  async function renderPage() {
    if (!page) return;
    const list = await fetchItems();
    const box = document.getElementById('cart-items');
    box.innerHTML = list.length ? list.map(it => itemHtml(it, true)).join('') : emptyHtml;
    document.getElementById('cart-summary').classList.toggle('d-none', !list.length);
    document.getElementById('cart-total').textContent = fmt(total(list));
    document.getElementById('cart-count').textContent = nf.format(list.length);
    document.getElementById('cart-json').value = JSON.stringify(read());
  }
  if (page) {
    renderPage();
    page.addEventListener('change', e => {
      const inp = e.target.closest('[data-cart-qty]');
      if (!inp) return;
      const items = read();
      const it = items.find(x => x.v === +inp.dataset.cartQty);
      if (it) { it.q = Math.max(1, Math.min(100000, parseFloat(inp.value) || 1)); write(items); renderPage(); }
    });
    document.getElementById('checkout-form')?.addEventListener('submit', () => {
      document.getElementById('cart-json').value = JSON.stringify(read());
    });
  }

  document.addEventListener('click', e => {
    const b = e.target.closest('[data-cart-remove]');
    if (!b) return;
    write(read().filter(x => x.v !== +b.dataset.cartRemove));
    if (page) renderPage(); else renderMini();
  });

  if (document.getElementById('order-success')) { write([]); }

  /* ---------- Bootstrap validation ---------- */
  document.querySelectorAll('form.needs-validation').forEach(f => f.addEventListener('submit', e => {
    if (!f.checkValidity()) { e.preventDefault(); e.stopPropagation(); }
    f.classList.add('was-validated');
  }));

  /* ---------- Auto submit filters ---------- */
  document.querySelectorAll('[data-autosubmit]').forEach(el => el.addEventListener('change', () => el.form.submit()));
})();
