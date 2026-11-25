// قبل از رندر صفحه اجرا می‌شود تا تم بدون پرش اعمال شود
// تم پیش‌فرض (data-default-theme روی html) از پنل: ظاهر سایت تعیین می‌شود؛ انتخاب خود بازدیدکننده اولویت دارد
(function () {
  var html = document.documentElement;
  html.classList.add('js');
  try {
    var t = localStorage.getItem('sm-theme');
    var def = html.getAttribute('data-default-theme');
    if (!t) t = def === 'light' || def === 'dark' ? def : (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
    html.setAttribute('data-bs-theme', t);
  } catch (e) { html.setAttribute('data-bs-theme', 'light'); }
})();
