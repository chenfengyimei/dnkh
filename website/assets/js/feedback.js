/* 反馈页：类型切换 / 实时生成 / 提交反馈中心 / 复制 / 邮件 */
(function () {
  'use strict';

  var EMAIL = '2079937150@qq.com';

  var tabBug = document.getElementById('tabBug');
  var tabApp = document.getElementById('tabApp');
  var formBug = document.getElementById('formBug');
  var formApp = document.getElementById('formApp');
  var output = document.getElementById('fbOutput');
  var msg = document.getElementById('fbMsg');
  var csrfToken = '';

  if (!tabBug || !tabApp || !output) return;

  var mode = 'bug';
  var REQUIRED = { bug: ['fDesc'], app: ['fName', 'fSite'] };

  function v(id) {
    var el = document.getElementById(id);
    return el ? el.value.replace(/\s+$/, '') : '';
  }

  function line(label, value, fallback) {
    return label + '：' + (value || fallback || '（未填写）') + '\n';
  }

  function collectBug() {
    var t = '【电脑开荒 · Bug 反馈】\n';
    t += line('版本', v('fVersion'));
    t += line('系统环境', v('fOs'));
    t += line('所属模块', v('fModule'));
    t += '—\n问题描述：\n' + (v('fDesc') || '（未填写）') + '\n';
    if (v('fSteps')) t += '\n复现步骤：\n' + v('fSteps') + '\n';
    if (v('fExpect')) t += '\n期望结果：\n' + v('fExpect') + '\n';
    return t;
  }

  function collectApp() {
    var checks = [];
    ['c1', 'c2', 'c3', 'c4'].forEach(function (id) {
      var el = document.getElementById(id);
      if (el && el.checked) checks.push(el.parentNode.textContent.trim());
    });
    var t = '【电脑开荒 · 申请收录软件】\n';
    t += line('软件名称', v('fName'));
    t += line('官网地址', v('fSite'));
    t += line('winget ID', v('fWinget'), '无 / 未知');
    t += line('类目', v('fCat'));
    t += line('纯净自评', checks.join(' · '), '未勾选');
    if (v('fReason')) t += '—\n推荐理由：\n' + v('fReason') + '\n';
    return t;
  }

  function checks() {
    var result = [];
    ['c1', 'c2', 'c3', 'c4'].forEach(function (id) {
      var el = document.getElementById(id);
      if (el && el.checked) result.push(el.parentNode.textContent.trim());
    });
    return result;
  }

  function payload() {
    var base = { type: mode, website: v('fbWebsite') };
    if (mode === 'bug') {
      base.version = v('fVersion');
      base.os = v('fOs');
      base.module = v('fModule');
      base.description = v('fDesc');
      base.steps = v('fSteps');
      base.expectation = v('fExpect');
    } else {
      base.name = v('fName');
      base.site = v('fSite');
      base.winget = v('fWinget');
      base.category = v('fCat');
      base.checks = checks();
      base.reason = v('fReason');
    }
    return base;
  }

  function subject() {
    return mode === 'bug'
      ? '【电脑开荒】Bug 反馈 · ' + v('fModule')
      : '【电脑开荒】申请收录 · ' + v('fName');
  }

  function generate() {
    output.value = mode === 'bug' ? collectBug() : collectApp();
  }

  function clearInvalid() {
    var bad = document.querySelectorAll('.fb-invalid');
    Array.prototype.forEach.call(bad, function (el) { el.classList.remove('fb-invalid'); });
    if (msg) msg.textContent = '';
  }

  function validate() {
    var missing = [];
    (REQUIRED[mode] || []).forEach(function (id) {
      var el = document.getElementById(id);
      var empty = !el.value.trim();
      el.closest('.fb-field').classList.toggle('fb-invalid', empty);
      if (empty) missing.push(el);
    });
    if (missing.length) {
      if (msg) msg.textContent = '请先填写标注 * 的必填项';
      missing[0].focus();
      return false;
    }
    if (msg) msg.textContent = '';
    return true;
  }

  function showMessage(text, kind) {
    if (!msg) return;
    msg.textContent = text;
    msg.classList.toggle('fb-msg--success', kind === 'success');
  }

  function setMode(m) {
    mode = m;
    var bug = m === 'bug';
    tabBug.classList.toggle('active', bug);
    tabApp.classList.toggle('active', !bug);
    tabBug.setAttribute('aria-selected', bug ? 'true' : 'false');
    tabApp.setAttribute('aria-selected', bug ? 'true' : 'false');
    formBug.classList.toggle('visible', bug);
    formApp.classList.toggle('visible', !bug);
    clearInvalid();
    generate();
  }

  tabBug.addEventListener('click', function () { setMode('bug'); });
  tabApp.addEventListener('click', function () { setMode('app'); });

  [formBug, formApp].forEach(function (f) {
    ['input', 'change'].forEach(function (ev) {
      f.addEventListener(ev, function (e) {
        var field = e.target && e.target.closest ? e.target.closest('.fb-field') : null;
        if (field) field.classList.remove('fb-invalid');
        generate();
      });
    });
  });

  /* 复制（file:// 下回退 execCommand） */
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

  var btnCopy = document.getElementById('btnCopy');
  btnCopy.addEventListener('click', function () {
    var original = btnCopy.innerHTML;
    copyText(output.value).then(
      function () { btnCopy.textContent = '✓ 已复制'; },
      function () { btnCopy.textContent = '复制失败'; }
    );
    window.setTimeout(function () { btnCopy.innerHTML = original; }, 1800);
  });

  document.getElementById('btnMail').addEventListener('click', function () {
    if (!validate()) return;
    generate();
    window.location.href = 'mailto:' + EMAIL +
      '?subject=' + encodeURIComponent(subject()) +
      '&body=' + encodeURIComponent(output.value);
  });

  function getToken() {
    return fetch('feedback/token.php', {
      credentials: 'same-origin',
      cache: 'no-store'
    }).then(function (response) {
      if (!response.ok) throw new Error('token');
      return response.json();
    }).then(function (data) {
      csrfToken = data && data.ok && typeof data.token === 'string' ? data.token : '';
      return csrfToken;
    }).catch(function () {
      csrfToken = '';
      return '';
    });
  }

  var btnSubmit = document.getElementById('btnSubmit');
  if (btnSubmit) {
    btnSubmit.addEventListener('click', function () {
      if (!validate()) return;
      if (!csrfToken) {
        showMessage('反馈中心暂时不可用，请稍后重试或使用邮件发送。');
        getToken();
        return;
      }

      var original = btnSubmit.textContent;
      btnSubmit.disabled = true;
      btnSubmit.textContent = '正在提交…';
      showMessage('');
      fetch('feedback/submit.php', {
        method: 'POST',
        credentials: 'same-origin',
        headers: {
          'Content-Type': 'application/json',
          'X-CSRF-Token': csrfToken
        },
        body: JSON.stringify(payload())
      }).then(function (response) {
        return response.json().catch(function () { return {}; }).then(function (data) {
          if (!response.ok || !data.ok) throw new Error(data.message || '提交失败，请稍后重试。');
          return data;
        });
      }).then(function (data) {
        showMessage((data.message || '已收到，感谢你的反馈。') + ' 处理编号：' + data.id, 'success');
        btnSubmit.textContent = '✓ 已提交';
      }).catch(function (error) {
        showMessage(error && error.message ? error.message : '提交失败，请稍后重试或使用邮件发送。');
        btnSubmit.textContent = original;
      }).finally(function () {
        btnSubmit.disabled = false;
      });
    });
  }

  setMode('bug');
  /* 支持 ?mode=app 深链直达收录表单 */
  if (window.location.search.indexOf('mode=app') > -1) setMode('app');
  getToken();
})();
