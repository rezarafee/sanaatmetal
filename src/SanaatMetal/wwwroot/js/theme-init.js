// قبل از رندر صفحه اجرا می‌شود تا تم بدون پرش اعمال شود
(function () {
  try {
    var t = localStorage.getItem('sm-theme');
    if (!t) t = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    document.documentElement.setAttribute('data-bs-theme', t);
  } catch (e) { document.documentElement.setAttribute('data-bs-theme', 'light'); }
})();
