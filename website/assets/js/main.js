/* 电脑开荒官网交互：导航阴影 / 截图 Tab / 大图查看 / 复制按钮 */
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
