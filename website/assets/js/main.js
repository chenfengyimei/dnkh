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

  /* ---- 多渠道下载：下载前先选择渠道 ----
     无 PHP 环境（本地 file:// 预览）时 fetch 失败，自动回退为静态直连链接 */
  var dlMain = document.getElementById('dlMain');
  if (dlMain) {
    var heroDownload = document.getElementById('dlDirect');
    var downloadDialog = document.getElementById('downloadDialog');
    var downloadDialogClose = document.getElementById('downloadDialogClose');
    var downloadChoices = document.getElementById('downloadChoices');
    var downloadTriggers = [dlMain, heroDownload].filter(Boolean);
    var previousDownloadFocus = null;
    var downloadReady = false;

    function closeDownloadDialog() {
      if (!downloadDialog || downloadDialog.hidden) { return; }
      downloadDialog.hidden = true;
      document.body.classList.remove('dialog-open');
      if (previousDownloadFocus) { previousDownloadFocus.focus(); }
    }

    function openDownloadDialog(trigger) {
      if (!downloadReady || !downloadDialog) { return; }
      previousDownloadFocus = trigger;
      downloadDialog.hidden = false;
      document.body.classList.add('dialog-open');
      window.setTimeout(function () {
        if (downloadDialogClose) { downloadDialogClose.focus(); }
      }, 0);
    }

    function channelDescription(channel) {
      if (channel.id === 'direct') {
        return '官方网站高速直连，下载完成后可直接核对 SHA256。';
      }
      return '将在新标签页跳转至网盘下载页面。';
    }

    function addDownloadChoice(channel) {
      if (!downloadChoices || !channel.enabled || typeof channel.url !== 'string' || !/^dl\/download\.php\?id=[\w-]+$/.test(channel.url)) {
        return;
      }
      var link = document.createElement('a');
      var text = document.createElement('span');
      var title = document.createElement('strong');
      var description = document.createElement('small');
      var arrow = document.createElement('span');

      link.className = 'download-choice';
      link.href = channel.url;
      if (channel.id !== 'direct') {
        link.target = '_blank';
        link.rel = 'noopener';
      }
      text.className = 'download-choice-text';
      title.textContent = channel.label;
      description.textContent = channelDescription(channel);
      arrow.className = 'download-choice-arrow';
      arrow.setAttribute('aria-hidden', 'true');
      arrow.textContent = '→';
      text.appendChild(title);
      text.appendChild(description);
      link.appendChild(text);
      link.appendChild(arrow);
      link.addEventListener('click', closeDownloadDialog);
      downloadChoices.appendChild(link);
    }

    if (downloadDialog) {
      downloadDialog.addEventListener('click', function (event) {
        if (event.target === downloadDialog) { closeDownloadDialog(); }
      });
    }
    if (downloadDialogClose) { downloadDialogClose.addEventListener('click', closeDownloadDialog); }
    window.addEventListener('keydown', function (event) {
      if (event.key === 'Escape') { closeDownloadDialog(); }
    });

    fetch('dl/status.php').then(function (r) { return r.json(); }).then(function (s) {
      if (!s || !s.channels) { return; }
      s.channels.forEach(function (c) {
        if (c.id === 'direct') {
          /* 直连走跳板计数（302 到文件本体，nginx 继续提供下载） */
          dlMain.href = c.url;
          if (heroDownload) { heroDownload.href = c.url; }
        }
        addDownloadChoice(c);
      });
      if (downloadChoices && downloadChoices.children.length > 0) {
        downloadReady = true;
        downloadTriggers.forEach(function (trigger) {
          trigger.addEventListener('click', function (event) {
            event.preventDefault();
            openDownloadDialog(trigger);
          });
        });
      }
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
