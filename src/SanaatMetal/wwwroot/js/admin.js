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

    // جستجوی محصول
    let timer = null;
    body.addEventListener('input', e => {
      const input = e.target.closest('[data-f="Description"]');
      if (!input) return;
      const row = input.closest('tr');
      row.querySelector('[data-f="VariantId"]').value = '';
      if (!canPrice) lockRow(row, false);
      const menu = row.querySelector('.inv-suggest');
      clearTimeout(timer);
      const q = input.value.trim();
      if (q.length < 2) { menu.classList.remove('show'); return; }
      timer = setTimeout(async () => {
        const res = await fetch('/admin/invoices/variants?q=' + encodeURIComponent(q), { headers: { Accept: 'application/json' } });
        if (!res.ok) return;
        const list = await res.json();
        menu.innerHTML = list.length ? list.map(x => `<button type="button" class="dropdown-item d-flex justify-content-between gap-3" data-pick='${esc(JSON.stringify(x))}'>
            <span class="text-truncate">${esc(x.text)} <small class="text-muted">${esc(x.code)}</small></span><span class="num text-muted small">${x.price ? fmt(x.price) : ''}</span></button>`).join('')
          : '<span class="dropdown-item-text text-muted small">کالایی یافت نشد — به‌صورت آزاد ثبت می‌شود</span>';
        menu.classList.add('show');
      }, 250);
    });
    body.addEventListener('click', e => {
      const b = e.target.closest('[data-pick]');
      if (!b) return;
      const x = JSON.parse(b.dataset.pick);
      const row = b.closest('tr');
      const set = (f, v) => { row.querySelector(`[data-f="${f}"]`).value = v; };
      set('VariantId', x.id); set('Description', x.text); set('Unit', x.unit || '');
      set('UnitPrice', Number(x.price || 0).toLocaleString('en-US')); set('DiscountPercent', x.discount || 0);
      if (!canPrice) lockRow(row, true);
      row.querySelector('.inv-suggest').classList.remove('show');
      row.querySelector('[data-f="Quantity"]').focus();
      calc();
    });
    document.addEventListener('click', e => { if (!e.target.closest('.inv-suggest') && !e.target.closest('[data-f="Description"]')) $$('.inv-suggest.show').forEach(m => m.classList.remove('show')); });

    // نام‌گذاری مجدد فیلدها قبل از ارسال
    inv.addEventListener('submit', () => {
      $$('.inv-row', body).forEach((r, i) => $$('[data-f]', r).forEach(el => { el.name = `Items[${i}].${el.dataset.f}`; }));
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
      grad.addColorStop(0, 'rgba(37,99,235,.35)'); grad.addColorStop(1, 'rgba(37,99,235,0)');
      const ds = [{ label: 'فروش', data: d.sales, borderColor: '#2563eb', backgroundColor: grad, fill: true, tension: .4, borderWidth: 3, pointRadius: 4, pointBackgroundColor: '#2563eb' }];
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
        data: { labels: ['نقدی', 'چکی'], datasets: [{ data: [cash, cheque], backgroundColor: ['#10b981', '#f97316'], borderWidth: 0, hoverOffset: 10 }] },
        options: { cutout: '68%', animation: { animateRotate: true, duration: 1400 }, plugins: { legend: { position: 'bottom' }, tooltip: { rtl: true, callbacks: { label: c => ` ${c.label}: ${fmt(c.raw)}` } } } },
      }));
    };
    draw();
    document.addEventListener('themechange', draw);
  }
})();
