<?php
declare(strict_types=1);
require __DIR__ . '/bootstrap.php';

feedbackSendSecurityHeaders(true);
feedbackStartSession('dnkhw_feedback_admin');

/** @return never */
function adminPage(int $status, string $title, string $message): never
{
    http_response_code($status);
    header('Content-Type: text/html; charset=utf-8');
    echo '<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">'
        . '<title>' . feedbackEscape($title) . '</title><style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#f4f7fb;color:#142033;font:16px/1.55 system-ui,"Microsoft YaHei UI",sans-serif}.box{max-width:520px;margin:24px;padding:28px;border:1px solid #dce5ef;border-radius:16px;background:#fff;box-shadow:0 12px 36px #1e385018}a{color:#0067c0}</style>'
        . '<main class="box"><h1>' . feedbackEscape($title) . '</h1><p>' . feedbackEscape($message) . '</p><p><a href="/index.html">返回官网</a></p></main>';
    exit;
}

function adminConfigured(): bool
{
    $hash = feedbackConfig()['adminPasswordHash'];
    return $hash !== '' && password_get_info($hash)['algo'] !== null;
}

function adminLoggedIn(): bool
{
    return ($_SESSION['feedback_admin'] ?? false) === true;
}

function adminStatusLabel(string $status): string
{
    return match ($status) {
        'new' => '待处理',
        'in_progress' => '处理中',
        'resolved' => '已解决',
        'closed' => '已关闭',
        default => '未知状态',
    };
}

function adminTypeLabel(string $type): string
{
    return $type === 'app' ? '收录申请' : 'Bug 反馈';
}

if (!adminConfigured()) {
    adminPage(503, '后台尚未初始化', '请在服务器的 feedback/config.local.php 配置管理员密码哈希后再访问。');
}

$error = '';
if (($_SERVER['REQUEST_METHOD'] ?? 'GET') === 'POST' && ($_POST['action'] ?? '') === 'login') {
    if (!feedbackValidCsrf($_POST['csrf'] ?? null)) {
        $error = '登录页已过期，请刷新后重试。';
    } elseif ((int)($_SESSION['feedback_login_locked_until'] ?? 0) > time()) {
        $error = '尝试次数过多，请稍后再试。';
    } else {
        $user = feedbackClean($_POST['username'] ?? '', 80);
        $password = is_string($_POST['password'] ?? null) ? $_POST['password'] : '';
        $config = feedbackConfig();
        if (hash_equals($config['adminUser'], $user) && password_verify($password, $config['adminPasswordHash'])) {
            session_regenerate_id(true);
            $_SESSION['feedback_admin'] = true;
            $_SESSION['feedback_login_attempts'] = 0;
            header('Location: admin.php', true, 303);
            exit;
        }

        $attempts = (int)($_SESSION['feedback_login_attempts'] ?? 0) + 1;
        $_SESSION['feedback_login_attempts'] = $attempts;
        if ($attempts >= 5) {
            $_SESSION['feedback_login_locked_until'] = time() + 900;
        }
        $error = '账号或密码不正确。';
    }
}

if (!adminLoggedIn()) {
    $csrf = feedbackCsrfToken();
    header('Content-Type: text/html; charset=utf-8');
    ?>
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>反馈后台登录 · 电脑开荒</title>
  <style>
    :root{color-scheme:light dark}*{box-sizing:border-box}body{margin:0;min-height:100vh;display:grid;place-items:center;padding:20px;background:radial-gradient(circle at 75% 0,#dceeff,transparent 34%),#f4f7fb;color:#142033;font:15px/1.55 system-ui,"Microsoft YaHei UI",sans-serif}.login{width:min(100%,410px);padding:32px;border:1px solid #dce5ef;border-radius:18px;background:#fff;box-shadow:0 18px 50px #193a5e1c}h1{margin:0;font-size:25px}.muted{color:#5d6b7d}label{display:block;margin-top:18px;font-weight:650}input{width:100%;margin-top:6px;padding:11px 12px;border:1px solid #b8c5d4;border-radius:9px;font:inherit}input:focus{outline:3px solid #0067c033;border-color:#0067c0}.btn{width:100%;margin-top:24px;padding:11px 16px;border:0;border-radius:9px;background:#0067c0;color:#fff;font:inherit;font-weight:700;cursor:pointer}.error{margin:16px 0 0;padding:10px 12px;border-radius:8px;background:#fff0ef;color:#a4262c}@media (prefers-color-scheme:dark){body{background:#0b1320;color:#f4f7fb}.login{background:#121d2c;border-color:#314159}.muted{color:#b5c0d0}input{background:#0b1320;color:#fff;border-color:#52637b}}
  </style>
</head>
<body>
  <main class="login">
    <p class="muted">电脑开荒</p>
    <h1>反馈处理后台</h1>
    <p class="muted">仅管理员可查看用户提交的反馈与收录申请。</p>
    <?php if ($error !== ''): ?><p class="error" role="alert"><?= feedbackEscape($error) ?></p><?php endif; ?>
    <form method="post" action="admin.php" autocomplete="off">
      <input type="hidden" name="action" value="login">
      <input type="hidden" name="csrf" value="<?= feedbackEscape($csrf) ?>">
      <label>账号<input name="username" required maxlength="80" autofocus></label>
      <label>密码<input name="password" type="password" required maxlength="512"></label>
      <button class="btn" type="submit">安全登录</button>
    </form>
  </main>
</body>
</html>
<?php
    exit;
}

if (($_SERVER['REQUEST_METHOD'] ?? 'GET') === 'POST') {
    if (!feedbackValidCsrf($_POST['csrf'] ?? null)) {
        adminPage(403, '请求已拒绝', '页面令牌无效，请返回后台后重试。');
    }

    $action = (string)($_POST['action'] ?? '');
    if ($action === 'logout') {
        $_SESSION = [];
        session_destroy();
        header('Location: admin.php', true, 303);
        exit;
    }

    if ($action === 'update') {
        $id = feedbackClean($_POST['id'] ?? '', 32);
        $status = feedbackClean($_POST['status'] ?? '', 32);
        $note = feedbackClean($_POST['adminNote'] ?? '', 4000);
        $allowedStatuses = ['new', 'in_progress', 'resolved', 'closed'];
        if (!preg_match('/^[a-f0-9]{20}$/', $id) || !in_array($status, $allowedStatuses, true)) {
            adminPage(422, '无法保存', '反馈编号或状态无效。');
        }

        $updated = false;
        try {
            feedbackMutate(static function (array $records) use ($id, $status, $note, &$updated): array {
                foreach ($records as &$record) {
                    if (($record['id'] ?? '') === $id) {
                        $record['status'] = $status;
                        $record['adminNote'] = $note;
                        $record['updatedAt'] = date(DATE_ATOM);
                        $updated = true;
                        break;
                    }
                }
                unset($record);
                return $records;
            });
        } catch (Throwable $exception) {
            error_log('feedback admin update failed: ' . $exception->getMessage());
            adminPage(503, '无法保存', '服务器暂时无法写入处理结果。');
        }

        header('Location: admin.php?' . http_build_query([
            'status' => $_POST['filterStatus'] ?? 'all',
            'type' => $_POST['filterType'] ?? 'all',
            'saved' => $updated ? '1' : '0',
        ]), true, 303);
        exit;
    }
}

$allowedStatuses = ['all', 'new', 'in_progress', 'resolved', 'closed'];
$allowedTypes = ['all', 'bug', 'app'];
$statusFilter = (string)($_GET['status'] ?? 'all');
$typeFilter = (string)($_GET['type'] ?? 'all');
if (!in_array($statusFilter, $allowedStatuses, true)) $statusFilter = 'all';
if (!in_array($typeFilter, $allowedTypes, true)) $typeFilter = 'all';

$records = feedbackReadAll();
$counts = ['all' => count($records), 'new' => 0, 'in_progress' => 0, 'resolved' => 0, 'closed' => 0];
foreach ($records as $record) {
    $status = (string)($record['status'] ?? 'new');
    if (isset($counts[$status])) $counts[$status]++;
}
$records = array_values(array_filter($records, static function (array $record) use ($statusFilter, $typeFilter): bool {
    return ($statusFilter === 'all' || ($record['status'] ?? '') === $statusFilter)
        && ($typeFilter === 'all' || ($record['type'] ?? '') === $typeFilter);
}));

$csrf = feedbackCsrfToken();
header('Content-Type: text/html; charset=utf-8');
?>
<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>反馈后台 · 电脑开荒</title>
  <style>
    :root{color-scheme:light dark;--ink:#142033;--muted:#637186;--line:#d9e2ec;--bg:#f3f6fa;--card:#fff;--accent:#0067c0}*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.55 system-ui,"Microsoft YaHei UI",sans-serif}.bar{background:#0d1b2c;color:#fff}.barin,.wrap{max-width:1180px;margin:0 auto;padding-left:22px;padding-right:22px}.barin{min-height:66px;display:flex;align-items:center;justify-content:space-between;gap:16px}.brand{font-weight:750;font-size:17px}.sub{color:#b8c8da;font-size:13px}.logout{border:1px solid #7691ad;background:transparent;color:#fff;border-radius:7px;padding:7px 10px;cursor:pointer}.wrap{padding-top:28px;padding-bottom:64px}.stats{display:grid;grid-template-columns:repeat(5,1fr);gap:12px;margin:0 0 22px}.stat{display:block;padding:15px;border:1px solid var(--line);border-radius:12px;background:var(--card);color:inherit;text-decoration:none}.stat b{display:block;font-size:25px}.stat span{color:var(--muted);font-size:12px}.stat.on{border-color:var(--accent);box-shadow:0 0 0 2px #0067c020}.filters{display:flex;flex-wrap:wrap;gap:9px;margin:16px 0 22px}.filter{padding:7px 10px;border:1px solid var(--line);border-radius:999px;text-decoration:none;color:var(--ink);background:var(--card)}.filter.on{color:#fff;background:var(--accent);border-color:var(--accent)}.notice{padding:10px 12px;border-radius:9px;background:#e8f5ee;color:#16763a;margin-bottom:16px}.empty{padding:42px;text-align:center;border:1px dashed #b8c5d4;border-radius:12px;color:var(--muted);background:var(--card)}.entry{margin:16px 0;padding:22px;border:1px solid var(--line);border-radius:14px;background:var(--card);box-shadow:0 1px 2px #20364a0d}.head{display:flex;gap:10px;align-items:center;flex-wrap:wrap}.head h2{margin:0;font-size:18px}.pill{display:inline-block;padding:3px 8px;border-radius:999px;background:#e8f0fa;color:#205d98;font-size:12px;font-weight:650}.status-new{background:#fff3cd;color:#7a5700}.status-in_progress{background:#e0f0ff;color:#075ca2}.status-resolved{background:#dff4e6;color:#197440}.status-closed{background:#edf0f3;color:#596777}.meta{margin:8px 0 16px;color:var(--muted);font-size:12px}.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px 18px}.field{min-width:0}.field b{display:block;font-size:12px;color:var(--muted)}.field span,.content{white-space:pre-wrap;overflow-wrap:anywhere}.content{margin:13px 0;padding:12px;border-radius:8px;background:#f6f8fb}.entry form{display:grid;grid-template-columns:170px 1fr;gap:12px;margin-top:16px;padding-top:16px;border-top:1px solid var(--line)}select,textarea{width:100%;padding:9px 10px;border:1px solid #b7c5d5;border-radius:8px;background:#fff;color:#142033;font:inherit}textarea{min-height:76px;resize:vertical}.save{align-self:end;padding:9px 13px;border:0;border-radius:8px;background:var(--accent);color:#fff;font:inherit;font-weight:700;cursor:pointer}.form-label{font-size:12px;color:var(--muted);font-weight:650}@media(max-width:780px){.stats{grid-template-columns:repeat(2,1fr)}.grid,.entry form{grid-template-columns:1fr}.barin{align-items:flex-start;padding-top:15px;padding-bottom:15px}}@media(prefers-color-scheme:dark){:root{--ink:#f1f5fa;--muted:#b1c0d1;--line:#33445a;--bg:#0b1421;--card:#122033}.content{background:#0c1725}select,textarea{background:#0b1420;color:#fff;border-color:#4d6078}.status-closed{background:#2d3948;color:#d5dce5}}
  </style>
</head>
<body>
  <header class="bar"><div class="barin"><div><div class="brand">电脑开荒 · 反馈处理后台</div><div class="sub">仅查看用户主动提交的反馈；请勿导出或转发其中的个人信息。</div></div><form method="post"><input type="hidden" name="action" value="logout"><input type="hidden" name="csrf" value="<?= feedbackEscape($csrf) ?>"><button class="logout" type="submit">退出登录</button></form></div></header>
  <main class="wrap">
    <?php if (($_GET['saved'] ?? '') === '1'): ?><div class="notice" role="status">处理状态已保存。</div><?php endif; ?>
    <section class="stats" aria-label="反馈统计">
      <?php foreach (['all' => '全部', 'new' => '待处理', 'in_progress' => '处理中', 'resolved' => '已解决', 'closed' => '已关闭'] as $key => $label): ?>
        <a class="stat<?= $statusFilter === $key ? ' on' : '' ?>" href="?<?= feedbackEscape(http_build_query(['status' => $key, 'type' => $typeFilter])) ?>"><b><?= (int)($counts[$key] ?? 0) ?></b><span><?= feedbackEscape($label) ?></span></a>
      <?php endforeach; ?>
    </section>
    <nav class="filters" aria-label="按类型筛选">
      <?php foreach (['all' => '全部类型', 'bug' => 'Bug 反馈', 'app' => '收录申请'] as $key => $label): ?>
        <a class="filter<?= $typeFilter === $key ? ' on' : '' ?>" href="?<?= feedbackEscape(http_build_query(['status' => $statusFilter, 'type' => $key])) ?>"><?= feedbackEscape($label) ?></a>
      <?php endforeach; ?>
    </nav>

    <?php if (!$records): ?>
      <p class="empty">当前筛选条件下暂无反馈。</p>
    <?php endif; ?>

    <?php foreach ($records as $record):
        $data = is_array($record['data'] ?? null) ? $record['data'] : [];
        $type = (string)($record['type'] ?? 'bug');
        $status = (string)($record['status'] ?? 'new');
        $title = $type === 'app' ? (string)($data['name'] ?? '未命名软件') : (string)($data['module'] ?? 'Bug 反馈');
    ?>
      <article class="entry">
        <div class="head"><span class="pill"><?= feedbackEscape(adminTypeLabel($type)) ?></span><span class="pill status-<?= feedbackEscape($status) ?>"><?= feedbackEscape(adminStatusLabel($status)) ?></span><h2><?= feedbackEscape($title) ?></h2></div>
        <p class="meta">编号 <?= feedbackEscape($record['id'] ?? '') ?> · 提交于 <?= feedbackEscape($record['createdAt'] ?? '') ?> · 更新于 <?= feedbackEscape($record['updatedAt'] ?? '') ?></p>
        <?php if ($type === 'bug'): ?>
          <div class="grid"><div class="field"><b>版本</b><span><?= feedbackEscape($data['version'] ?? '未填写') ?></span></div><div class="field"><b>系统环境</b><span><?= feedbackEscape($data['os'] ?? '未填写') ?></span></div><div class="field"><b>所属模块</b><span><?= feedbackEscape($data['module'] ?? '未填写') ?></span></div></div>
          <div class="content"><b>问题描述</b><br><?= feedbackEscape($data['description'] ?? '') ?></div>
          <?php if (($data['steps'] ?? '') !== ''): ?><div class="content"><b>复现步骤</b><br><?= feedbackEscape($data['steps']) ?></div><?php endif; ?>
          <?php if (($data['expectation'] ?? '') !== ''): ?><div class="content"><b>期望结果</b><br><?= feedbackEscape($data['expectation']) ?></div><?php endif; ?>
        <?php else: ?>
          <div class="grid"><div class="field"><b>官网地址</b><span><a href="<?= feedbackEscape($data['site'] ?? '') ?>" target="_blank" rel="noopener noreferrer"><?= feedbackEscape($data['site'] ?? '') ?></a></span></div><div class="field"><b>winget ID</b><span><?= feedbackEscape($data['winget'] ?? '未填写') ?></span></div><div class="field"><b>类目</b><span><?= feedbackEscape($data['category'] ?? '未填写') ?></span></div><div class="field"><b>纯净自评</b><span><?= feedbackEscape(implode(' · ', is_array($data['checks'] ?? null) ? $data['checks'] : [])) ?></span></div></div>
          <?php if (($data['reason'] ?? '') !== ''): ?><div class="content"><b>推荐理由</b><br><?= feedbackEscape($data['reason']) ?></div><?php endif; ?>
        <?php endif; ?>
        <form method="post" action="admin.php">
          <input type="hidden" name="action" value="update"><input type="hidden" name="csrf" value="<?= feedbackEscape($csrf) ?>"><input type="hidden" name="id" value="<?= feedbackEscape($record['id'] ?? '') ?>"><input type="hidden" name="filterStatus" value="<?= feedbackEscape($statusFilter) ?>"><input type="hidden" name="filterType" value="<?= feedbackEscape($typeFilter) ?>">
          <label class="form-label">处理状态<select name="status"><?php foreach (['new', 'in_progress', 'resolved', 'closed'] as $choice): ?><option value="<?= $choice ?>"<?= $choice === $status ? ' selected' : '' ?>><?= feedbackEscape(adminStatusLabel($choice)) ?></option><?php endforeach; ?></select></label>
          <label class="form-label">管理员备注（仅后台可见）<textarea name="adminNote" maxlength="4000" placeholder="记录排查结论、后续动作或回复要点"><?= feedbackEscape($record['adminNote'] ?? '') ?></textarea></label>
          <button class="save" type="submit">保存处理结果</button>
        </form>
      </article>
    <?php endforeach; ?>
  </main>
</body>
</html>
