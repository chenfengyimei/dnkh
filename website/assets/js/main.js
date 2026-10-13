/* 电脑开荒官网交互：导航阴影 / 截图 Tab / 大图查看 / 复制按钮 / 多渠道下载 */
(function () {
  'use strict';

  /* ---- 导航滚动阴影 ---- */
  var nav = document.querySelector('.nav');
  if (nav) {
    var onScroll = function () {
      nav.classList.toggle('scrolled', window.scrollY > 8);
    };
    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();
  }

  /* ---- 移动端汉堡菜单 ---- */
  var navToggle = document.querySelector('.nav-toggle');
  var navLinks = document.getElementById('navLinks');
  if (navToggle && navLinks) {
    navToggle.addEventListener('click', function () {
      var open = navLinks.classList.toggle('open');
      navToggle.setAttribute('aria-expanded', open ? 'true' : 'false');
      navToggle.setAttribute('aria-label', open ? '关闭导航菜单' : '打开导航菜单');
    });
    navLinks.addEventListener('click', function (e) {
      if (e.target && e.target.tagName === 'A') {
        navLinks.classList.remove('open');
        navToggle.setAttribute('aria-expanded', 'false');
      }
    });
  }

  /* ---- 截图 Tab 切换 ---- */
  var galleryImg = document.getElementById('galleryImg');
  var galleryCap = document.getElementById('galleryCap');
  var tabs = Array.prototype.slice.call(document.querySelectorAll('.gallery-tab'));

  if (tabs.length && galleryImg) {
    tabs.forEach(function (btn) {
      btn.addEventListener('click', function () {
        tabs.forEach(function (b) {
          var active = b === btn;
          b.classList.toggle('active', active);
          b.setAttribute('aria-selected', active ? 'true' : 'false');
        });
        galleryImg.src = btn.dataset.src;
        galleryImg.alt = btn.dataset.title + '页界面';
        galleryCap.textContent = btn.dataset.caption;
      });
      /* 预加载全部截图，切换无延迟 */
      var pre = new Image();
      pre.src = btn.dataset.src;
    });
  }

  /* ---- 大图查看 ---- */
  var lightbox = document.getElementById('lightbox');
  var lightboxImg = document.getElementById('lightboxImg');

  function closeLightbox() {
    lightbox.hidden = true;
  }
  if (lightbox && galleryImg) {
    galleryImg.addEventListener('click', function () {
      lightboxImg.src = galleryImg.src;
      lightboxImg.alt = galleryImg.alt;
      lightbox.hidden = false;
    });
    lightbox.addEventListener('click', closeLightbox);
    document.querySelector('.lightbox-close').addEventListener('click', closeLightbox);
    window.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') closeLightbox();
    });
  }

  /* ---- 回到顶部 ---- */
  var toTop = document.getElementById('toTop');
  if (toTop) {
    var toggleTop = function () {
      toTop.classList.toggle('show', window.scrollY > 600);
    };
    window.addEventListener('scroll', toggleTop, { passive: true });
    toggleTop();
    toTop.addEventListener('click', function () {
      window.scrollTo({ top: 0, behavior: 'smooth' });
    });
  }

  /* ---- 多渠道下载：dl/status.php 渲染渠道按钮与累计计数 ----
     无 PHP 环境（本地 file:// 预览）时 fetch 失败，自动回退为静态直连链接 */
  var dlMain = document.getElementById('dlMain');
  if (dlMain) {
    fetch('dl/status.php').then(function (r) { return r.json(); }).then(function (s) {
      if (!s || !s.channels) { return; }
      var row = document.getElementById('dlChannels');
      var shown = 0;
      s.channels.forEach(function (c) {
        if (c.id === 'direct') {
          /* 直连走跳板计数（302 到文件本体，nginx 继续提供下载） */
          dlMain.href = c.url;
          var hero = document.getElementById('dlDirect');
          if (hero) { hero.href = c.url; }
          return;
        }
        if (!c.enabled || !row) { return; }
        var a = document.createElement('a');
        a.className = 'btn btn-secondary btn-sm';
        a.href = c.url;
        a.target = '_blank';
        a.rel = 'noopener';
        a.textContent = '☁ ' + c.label;
        row.appendChild(a);
        shown++;
      });
      if (shown > 0) { document.getElementById('dlAlt').hidden = false; }
      var cnt = document.getElementById('dlCount');
      if (cnt && s.showPublicStats && s.total > 0) {
        cnt.querySelector('strong').textContent = String(s.total);
        cnt.hidden = false;
      }
    }).catch(function () { /* 本地预览无 PHP：保持静态直连 */ });
  }

  /* ---- 复制按钮（file:// 下回退 execCommand） ---- */
  function copyText(text) {
    if (navigator.clipboard && window.isSecureContext) {
      return navigator.clipboard.writeText(text);
    }
    return new Promise(function (resolve, reject) {
      var ta = document.createElement('textarea');
      ta.value = text;
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      try {
        document.execCommand('copy') ? resolve() : reject();
      } catch (e) {
        reject(e);
      } finally {
        ta.remove();
      }
    });
  }

  Array.prototype.forEach.call(document.querySelectorAll('[data-copy]'), function (btn) {
    btn.addEventListener('click', function () {
      var text = btn.getAttribute('data-copy');
      var original = btn.textContent;
      copyText(text).then(
        function () {
          btn.textContent = '✓ 已复制';
          btn.classList.add('copied');
        },
        function () {
          btn.textContent = '复制失败';
          btn.classList.add('failed');
        }
      );
      window.setTimeout(function () {
        btn.textContent = original;
        btn.classList.remove('copied', 'failed');
      }, 1800);
    });
  });
})();
