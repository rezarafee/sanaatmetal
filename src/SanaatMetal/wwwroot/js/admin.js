/* Sanaat Metal — admin panel scripts (CSP friendly; no inline JS) */
(function () {
  'use strict';
  const $ = (s, r = document) => r.querySelector(s);
  const $$ = (s, r = document) => Array.from(r.querySelectorAll(s));
  const nf = new Intl.NumberFormat('fa-IR');
  const toEn = s => String(s ?? '').replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d)).replace(/[٠-٩]/g, d => '٠١٢٣٤٥٦٧٨٩'.indexOf(d));
  const num = v => { const n = parseFloat(toEn(v).replace(/[,٬،\s]/g, '').replace('٫', '.')); return isNaN(n) ? 0 : n; };
  const fmt = n => nf.format(Math.round(n));
  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  /* ---------- Theme ---------- */
  $$('.theme-toggle').forEach(b => b.addEventListener('click', () => {
    const t = document.documentElement.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
    document.documentElement.setAttribute('data-bs-theme', t);
    try { localStorage.setItem('sm-theme', t); } catch (e) { }
    document.dispatchEvent(new Event('themechange'));
  }));

  /* ---------- Sidebar ---------- */
  try { if (localStorage.getItem('sm-side') === 'collapsed') document.body.classList.add('side-collapsed'); } catch (e) { }
  $$('[data-side-open]').forEach(b => b.addEventListener('click', () => document.body.classList.add('side-open')));
  $$('[data-side-close]').forEach(b => b.addEventListener('click', () => document.body.classList.remove('side-open')));
  $$('[data-side-collapse]').forEach(b => b.addEventListener('click', () => {
    document.body.classList.toggle('side-collapsed');
    try { localStorage.setItem('sm-side', document.body.classList.contains('side-collapsed') ? 'collapsed' : ''); } catch (e) { }
  }));
  $('.a-link.active')?.scrollIntoView({ block: 'center' });

  /* ---------- Modals ----------
     پنجره‌ها به body منتقل می‌شوند تا انیمیشن یا transform والدها آن‌ها را زیر پس‌زمینه تیره نبرد */
  $$('.modal').forEach(m => { if (m.parentElement !== document.body && !m.parentElement.closest('form')) document.body.appendChild(m); });

  /* ---------- پیش‌نمایش اندازه فونت پنل ---------- */
  $$('input[data-font-preview]').forEach(r => {
    const out = $(r.dataset.fontPreview);
    const apply = () => { document.documentElement.style.fontSize = r.value + 'px'; if (out) out.textContent = r.value + 'px'; };
    r.addEventListener('input', apply);
    apply();
  });

  /* ---------- Jalali date picker (همه فیلدهای data-jdp) ---------- */
  if (window.jalaliDatepicker) {
    jalaliDatepicker.startWatch({
      zIndex: 2000, time: false, persianDigits: false, autoReadOnlyInput: false, hideAfterChange: true,
      showTodayBtn: true, showEmptyBtn: true, showCloseBtn: true, useDropDownYears: true,
      separatorChars: { date: '/' },
    });
  }

  /* ---------- Confirm dialogs ---------- */
  const modalEl = $('#confirmModal');
  let pending = null;
  document.addEventListener('submit', e => {
    const f = e.target;
    const submitter = e.submitter;
    const msg = submitter?.dataset.confirm || f.dataset.confirm;
    if (!msg || f.dataset.confirmed === '1' || !modalEl || !window.bootstrap) return;
    e.preventDefault();
    pending = { form: f, submitter };
    $('#confirmText').textContent = msg;
    bootstrap.Modal.getOrCreateInstance(modalEl).show();
  }, true);
  $('#confirmYes')?.addEventListener('click', () => {
    if (!pending) return;
    const { form, submitter } = pending;
    form.dataset.confirmed = '1';
    bootstrap.Modal.getOrCreateInstance(modalEl).hide();
    if (submitter && submitter.formAction && submitter.getAttribute('formaction')) form.action = submitter.formAction;
    form.submit();
  });

  /* ---------- Modal fill (edit forms in modals) ---------- */
  document.addEventListener('show.bs.modal', e => {
    const trigger = e.relatedTarget;
    if (!trigger || !trigger.dataset.fill) return;
    const data = JSON.parse(trigger.dataset.fill);
    const form = e.target.querySelector('form') || e.target;
    if (form.tagName === 'FORM') form.reset();
    if (form.dataset.actionBase) form.action = form.dataset.actionBase + (data.id || 0);
    Object.entries(data).forEach(([k, v]) => {
      const el = form.querySelector(`[name="${k}"]`);
      if (!el) return;
      if (el.type === 'checkbox') el.checked = !!v;
      else el.value = v ?? '';
    });
    const isNew = !(data.Id || data.id);
    $$('[data-new-only]', form).forEach(x => x.classList.toggle('d-none', !isNew));
    $$('[data-money]', form).forEach(formatMoney);
  });

  /* ---------- Image preview ---------- */
  $$('[data-preview-for]').forEach(img => {
    const input = document.getElementById(img.dataset.previewFor);
    input?.addEventListener('change', () => {
      const f = input.files?.[0];
      if (f && f.type.startsWith('image/')) img.src = URL.createObjectURL(f);
    });
  });

  /* ---------- Money inputs ---------- */
  function formatMoney(el) {
    if (!el.value) return;
    const n = num(el.value);
    el.value = n ? n.toLocaleString('en-US') : el.value === '0' ? '0' : '';
  }
  document.addEventListener('input', e => {
    const el = e.target.closest('[data-money]');
    if (!el) return;
    const pos = el.value.length - el.selectionStart;
    formatMoney(el);
    try { el.setSelectionRange(el.value.length - pos, el.value.length - pos); } catch (err) { }
  });
  $$('[data-money]').forEach(formatMoney);

  /* ---------- Misc ---------- */
  $$('[data-autosubmit]').forEach(el => el.addEventListener('change', () => el.form.submit()));
  $$('[data-print]').forEach(b => b.addEventListener('click', () => window.print()));

  const pay = $('#payMethod');
  if (pay) {
    const names = { 1: 'Cash', 2: 'Cheque', 3: 'Mixed', 4: 'Credit' };
    const update = () => {
      const cur = names[pay.value] || pay.value;
      $$('[data-pay]').forEach(x => x.classList.toggle('d-none', !x.dataset.pay.split(' ').includes(cur)));
    };
    pay.addEventListener('change', update); update();
  }

  /* ---------- Pricing preview ---------- */
  const priceForm = $('#priceForm');
  if (priceForm) {
    const upd = () => {
      const buy = num(priceForm.PurchasePrice.value), sale = num(priceForm.SalePrice.value), disc = num(priceForm.DiscountPercent.value);
      const fin = sale * (100 - disc) / 100;
      $('#ppFinal').textContent = sale ? fmt(fin) + ' ریال' : '-';
      $('#ppProfit').textContent = sale && buy ? fmt(fin - buy) + ' ریال' : '-';
      const m = buy ? ((fin - buy) * 100 / buy) : null;
      const el = $('#ppMargin');
      el.textContent = m === null ? '-' : nf.format(Math.round(m * 10) / 10) + '٪';
      el.className = m === null ? '' : m >= 0 ? 'text-success' : 'text-danger';
    };
    priceForm.addEventListener('input', upd); upd();
  }

  /* ---------- Invoice editor ---------- */
  const inv = $('#invoiceForm');
  if (inv) {
    const body = $('#invRows');
    const tpl = $('#invRowTpl');
    const canPrice = inv.dataset.canPrice === '1';

    const lockRow = (row, locked) => {
      row.dataset.locked = locked ? '1' : '0';
      ['UnitPrice', 'DiscountPercent'].forEach(f => { const el = row.querySelector(`[data-f="${f}"]`); el.readOnly = locked; el.classList.toggle('bg-body-tertiary', locked); });
    };
    let grandTotal = 0, chequeHook = null;
    const calc = () => {
      let sub = 0, disc = 0;
      $$('.inv-row', body).forEach(r => {
        const g = (f) => num(r.querySelector(`[data-f="${f}"]`).value);
        const gross = Math.round(g('UnitPrice') * g('Quantity'));
        const d = Math.round(gross * Math.min(100, Math.max(0, g('DiscountPercent'))) / 100);
        sub += gross; disc += d;
        r.querySelector('[data-line]').textContent = fmt(gross - d);
      });
      const extra = Math.min(num(inv.ExtraDiscount.value), sub - disc);
      const taxable = sub - disc - extra;
      const tax = Math.round(taxable * num(inv.TaxPercent.value) / 100);
      const grand = taxable + tax + num(inv.ShippingCost.value);
      $('#tSub').textContent = fmt(sub);
      $('#tDisc').textContent = fmt(disc + extra);
      $('#tTax').textContent = fmt(tax);
      $('#tGrand').textContent = fmt(grand) + ' ریال';
      grandTotal = Math.round(grand);
      $$('.inv-row', body).forEach((r, i) => { const c = r.querySelector('[data-idx]'); if (c) c.textContent = nf.format(i + 1); });
      chequeHook?.();
    };
    const addRow = () => {
      const node = tpl.content.firstElementChild.cloneNode(true);
      body.appendChild(node);
      node.querySelector('[data-f="Description"]').focus();
      calc();
    };
    $('[data-inv-add]')?.addEventListener('click', addRow);
    body.addEventListener('click', e => {
      const b = e.target.closest('[data-inv-remove]');
      if (!b) return;
      b.closest('tr').remove();
      if (!$$('.inv-row', body).length) addRow();
      calc();
    });
    inv.addEventListener('input', calc);
    $$('.inv-row', body).forEach(r => { if (!canPrice && r.dataset.locked === '1') lockRow(r, true); });

    // جستجوی محصول: لیست پیشنهاد روی body و با موقعیت fixed نمایش داده می‌شود تا پشت جدول پنهان نشود
    const menu = $('#invSuggest');
    if (menu) document.body.appendChild(menu);
    let timer = null, activeInput = null, activeIdx = -1;
    const placeMenu = () => {
      if (!activeInput || !menu.classList.contains('show')) return;
      const r = activeInput.getBoundingClientRect();
      const below = window.innerHeight - r.bottom, w = Math.max(r.width, 420);
      menu.style.width = Math.min(w, window.innerWidth - 16) + 'px';
      menu.style.right = Math.max(8, window.innerWidth - r.right) + 'px';
      menu.style.left = 'auto';
      if (below < 260 && r.top > below) { menu.style.top = 'auto'; menu.style.bottom = (window.innerHeight - r.top + 4) + 'px'; menu.style.maxHeight = Math.min(360, r.top - 16) + 'px'; }
      else { menu.style.bottom = 'auto'; menu.style.top = (r.bottom + 4) + 'px'; menu.style.maxHeight = Math.min(360, below - 16) + 'px'; }
    };
    const hideMenu = () => { menu?.classList.remove('show'); activeIdx = -1; };
    const highlight = i => {
      const items = $$('[data-pick]', menu);
      if (!items.length) return;
      activeIdx = (i + items.length) % items.length;
      items.forEach((x, k) => x.classList.toggle('active', k === activeIdx));
      items[activeIdx].scrollIntoView({ block: 'nearest' });
    };
    window.addEventListener('scroll', placeMenu, true);
    window.addEventListener('resize', placeMenu);
    body.addEventListener('input', e => {
      const input = e.target.closest('[data-f="Description"]');
      if (!input || !menu) return;
      const row = input.closest('tr');
      row.querySelector('[data-f="VariantId"]').value = '';
      if (!canPrice) lockRow(row, false);
      activeInput = input;
      clearTimeout(timer);
      const q = input.value.trim();
      if (q.length < 2) { hideMenu(); return; }
      timer = setTimeout(async () => {
        const res = await fetch('/admin/invoices/variants?q=' + encodeURIComponent(q), { headers: { Accept: 'application/json' } });
        if (!res.ok || activeInput !== input) return;
        const list = await res.json();
        menu.innerHTML = list.length ? list.map(x => `<button type="button" class="dropdown-item" data-pick='${esc(JSON.stringify(x))}'>
            <span class="sug-title">${esc(x.text)}</span><span class="sug-meta"><small class="text-muted ltr">${esc(x.code)}</small><span class="num">${x.price ? fmt(x.price) + ' ریال' : 'تماس بگیرید'}</span></span></button>`).join('')
          : '<span class="dropdown-item-text text-muted small">کالایی یافت نشد؛ به‌صورت شرح آزاد ثبت می‌شود.</span>';
        activeIdx = -1;
        menu.classList.add('show');
        placeMenu();
      }, 250);
    });
    body.addEventListener('keydown', e => {
      if (!e.target.closest('[data-f="Description"]') || !menu?.classList.contains('show')) return;
      if (e.key === 'ArrowDown') { e.preventDefault(); highlight(activeIdx + 1); }
      else if (e.key === 'ArrowUp') { e.preventDefault(); highlight(activeIdx - 1); }
      else if (e.key === 'Enter' && activeIdx >= 0) { e.preventDefault(); $$('[data-pick]', menu)[activeIdx]?.click(); }
      else if (e.key === 'Escape') hideMenu();
    });
    menu?.addEventListener('mousedown', e => e.preventDefault());
    menu?.addEventListener('click', e => {
      const b = e.target.closest('[data-pick]');
      if (!b || !activeInput) return;
      const x = JSON.parse(b.dataset.pick);
      const row = activeInput.closest('tr');
      const set = (f, v) => { row.querySelector(`[data-f="${f}"]`).value = v; };
      set('VariantId', x.id); set('Description', x.text); set('Unit', x.unit || '');
      set('UnitPrice', Number(x.price || 0).toLocaleString('en-US')); set('DiscountPercent', x.discount || 0);
      if (!canPrice) lockRow(row, true);
      hideMenu();
      row.querySelector('[data-f="Quantity"]').focus();
      calc();
    });
    document.addEventListener('click', e => { if (!e.target.closest('#invSuggest') && !e.target.closest('[data-f="Description"]')) hideMenu(); });

    // چک‌ها
    const chequeBody = $('#chequeRows'), chequeTpl = $('#chequeRowTpl'), chequeCount = $('#chequeCount');
    const jToday = () => {
      const p = new Intl.DateTimeFormat('en-US-u-ca-persian-nu-latn', { year: 'numeric', month: '2-digit', day: '2-digit' }).formatToParts(new Date());
      const g = t => parseInt(p.find(x => x.type === t).value, 10);
      return [g('year'), g('month'), g('day')];
    };
    const jParse = v => { const m = toEn(v).match(/^(\d{4})[\/\-.](\d{1,2})[\/\-.](\d{1,2})$/); return m ? [+m[1], +m[2], +m[3]] : null; };
    const jFmt = ([y, m, d]) => `${y}/${String(m).padStart(2, '0')}/${String(d).padStart(2, '0')}`;
    const jAddMonth = ([y, m, d], n = 1) => {
      m += n; while (m > 12) { m -= 12; y++; }
      const max = m <= 6 ? 31 : m <= 11 ? 30 : 29;
      return [y, m, Math.min(d, max)];
    };
    const chequeTotal = () => {
      const method = $('#payMethod')?.value;
      if (method === '2') return grandTotal;
      if (method === '3') return Math.max(0, grandTotal - Math.min(num(inv.CashAmount.value), grandTotal));
      return 0;
    };
    const renumber = (tb) => $$('tr', tb).forEach((r, i) => { const c = r.querySelector('[data-idx]'); if (c) c.textContent = nf.format(i + 1); });
    const splitEqual = (force) => {
      const rows = $$('.cheque-row', chequeBody);
      if (!rows.length) return;
      if (!force && rows.some(r => r.dataset.manual === '1')) return;
      const total = chequeTotal(), n = rows.length;
      const part = Math.floor(total / n / 1000) * 1000;
      rows.forEach((r, i) => {
        const v = i === n - 1 ? total - part * (n - 1) : part;
        r.querySelector('[data-c="Amount"]').value = v.toLocaleString('en-US');
        if (force) delete r.dataset.manual;
      });
    };
    const updateChequeSum = () => {
      const el = $('#chequeSum');
      if (!el) return;
      const rows = $$('.cheque-row', chequeBody);
      $('#chequeEmpty')?.classList.toggle('d-none', rows.length > 0);
      if (!rows.length) { el.innerHTML = ''; return; }
      const sum = rows.reduce((a, r) => a + num(r.querySelector('[data-c="Amount"]').value), 0);
      const total = chequeTotal(), ok = sum === total;
      el.className = 'cheque-sum ' + (ok ? 'ok' : 'bad');
      el.innerHTML = `<i class="bi ${ok ? 'bi-check-circle-fill' : 'bi-exclamation-triangle-fill'} ic"></i>جمع چک‌ها: <b class="num">${fmt(sum)}</b> از <b class="num">${fmt(total)}</b> ریال`;
    };
    const setChequeCount = n => {
      n = Math.max(0, Math.min(48, n | 0));
      let rows = $$('.cheque-row', chequeBody);
      while (rows.length > n) rows.pop().remove();
      while (rows.length < n) {
        const node = chequeTpl.content.firstElementChild.cloneNode(true);
        const prev = rows.length ? jParse(rows[rows.length - 1].querySelector('[data-c="DueDate"]').value) : null;
        const base = prev || jParse($('#issueDate')?.value || '') || jToday();
        node.querySelector('[data-c="DueDate"]').value = jFmt(jAddMonth(base, 1));
        chequeBody.appendChild(node);
        rows.push(node);
      }
      renumber(chequeBody);
      splitEqual(false);
      updateChequeSum();
    };
    if (chequeBody) {
      chequeHook = () => { splitEqual(false); updateChequeSum(); };
      chequeCount.addEventListener('input', () => setChequeCount(num(chequeCount.value)));
      $('#chequeSplit')?.addEventListener('click', () => { splitEqual(true); updateChequeSum(); });
      chequeBody.addEventListener('input', e => { if (e.target.closest('[data-c="Amount"]')) e.target.closest('tr').dataset.manual = '1'; updateChequeSum(); });
      chequeBody.addEventListener('click', e => {
        const b = e.target.closest('[data-cheque-remove]');
        if (!b) return;
        b.closest('tr').remove();
        chequeCount.value = $$('.cheque-row', chequeBody).length;
        renumber(chequeBody); updateChequeSum();
      });
      $('#payMethod')?.addEventListener('change', () => {
        if (['2', '3'].includes($('#payMethod').value) && !$$('.cheque-row', chequeBody).length) { chequeCount.value = 1; setChequeCount(1); }
        splitEqual(false); updateChequeSum();
      });
    }

    // نام‌گذاری مجدد فیلدها قبل از ارسال
    inv.addEventListener('submit', () => {
      $$('.inv-row', body).forEach((r, i) => $$('[data-f]', r).forEach(el => { el.name = `Items[${i}].${el.dataset.f}`; }));
      const useCheques = ['2', '3'].includes($('#payMethod')?.value);
      $$('.cheque-row', chequeBody || document).forEach((r, i) => $$('[data-c]', r).forEach(el => { el.name = useCheques ? `Cheques[${i}].${el.dataset.c}` : ''; }));
    });
    calc();
  }

  /* ---------- Dashboard charts ---------- */
  const dataEl = $('#salesData');
  if (dataEl && window.Chart) {
    const d = JSON.parse(dataEl.textContent || '{}');
    const css = getComputedStyle(document.documentElement);
    Chart.defaults.font.family = 'Vazirmatn';
    const charts = [];
    const draw = () => {
      charts.forEach(c => c.destroy()); charts.length = 0;
      const muted = getComputedStyle(document.body).getPropertyValue('--a-muted').trim() || '#64748b';
      const grid = getComputedStyle(document.body).getPropertyValue('--a-border').trim() || '#e5e9f0';
      Chart.defaults.color = muted;
      const ctx = $('#salesChart').getContext('2d');
      const grad = ctx.createLinearGradient(0, 0, 0, 300);
      grad.addColorStop(0, 'rgba(74,95,208,.35)'); grad.addColorStop(1, 'rgba(74,95,208,0)');
      const ds = [{ label: 'فروش', data: d.sales, borderColor: '#4a5fd0', backgroundColor: grad, fill: true, tension: .4, borderWidth: 3, pointRadius: 4, pointBackgroundColor: '#f5b301' }];
      if (d.profit) ds.push({ label: 'سود', data: d.profit, borderColor: '#10b981', backgroundColor: 'transparent', tension: .4, borderWidth: 2, borderDash: [6, 4], pointRadius: 3 });
      charts.push(new Chart(ctx, {
        type: 'line', data: { labels: d.labels, datasets: ds },
        options: {
          responsive: true, interaction: { intersect: false, mode: 'index' },
          animation: { duration: 1200, easing: 'easeOutQuart' },
          plugins: { legend: { position: 'bottom' }, tooltip: { rtl: true, callbacks: { label: c => ` ${c.dataset.label}: ${fmt(c.raw)}` } } },
          scales: { y: { position: 'right', grid: { color: grid }, ticks: { callback: v => nf.format(v / 1e6) + ' م' } }, x: { reverse: true, grid: { display: false } } },
        },
      }));
      const cash = (d.cash || []).reduce((a, b) => a + b, 0), cheque = (d.cheque || []).reduce((a, b) => a + b, 0);
      charts.push(new Chart($('#payChart'), {
        type: 'doughnut',
        data: { labels: ['نقدی', 'چکی'], datasets: [{ data: [cash, cheque], backgroundColor: ['#4a5fd0', '#f5b301'], borderWidth: 0, hoverOffset: 10 }] },
        options: { cutout: '68%', animation: { animateRotate: true, duration: 1400 }, plugins: { legend: { position: 'bottom' }, tooltip: { rtl: true, callbacks: { label: c => ` ${c.label}: ${fmt(c.raw)}` } } } },
      }));
    };
    draw();
    document.addEventListener('themechange', draw);
  }
})();
