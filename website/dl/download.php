<?php
// 下载跳板：计数（flock 并发安全）后 302 跳转到真实下载地址。
// 访问方式：dl/download.php?id=<渠道名>（渠道定义见同目录 config.php）
$config = require __DIR__ . '/config.php';

$id = isset($_GET['id']) ? preg_replace('/[^a-z0-9_]/', '', strtolower((string)$_GET['id'])) : '';
$channels = $config['channels'];

function htmlExit($code, $title, $msg) {
    http_response_code($code);
    header('Content-Type: text/html; charset=utf-8');
    echo '<!doctype html><meta charset="utf-8"><title>' . $title . '</title>' .
         '<body style="font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0;color:#1b1f27;background:#f5f7fa">' .
         '<div style="text-align:center"><p style="font-size:44px;margin:0">' . ($code === 503 ? '⏳' : '404') . '</p>' .
         '<p style="font-size:16px;color:#5b6472">' . $msg . '</p>' .
         '<p style="font-size:13px"><a href="/" style="color:#0067c0">← 返回电脑开荒官网</a></p></div></body>';
    exit;
}

if ($id === '' || !isset($channels[$id])) {
    htmlExit(404, '渠道不存在', '下载渠道不存在。');
}
$ch = $channels[$id];
if (empty($ch['url'])) {
    htmlExit(503, '暂未开放', '「' . $ch['label'] . '」渠道暂未开放，请先使用本站直连下载。');
}

// ---- 计数（flock 独占锁，并发安全）----
$dir = __DIR__ . '/data';
if (!is_dir($dir)) { @mkdir($dir, 0755, true); }
$file = $dir . '/stats.json';
$fp = @fopen($file, 'c+');
if ($fp) {
    @flock($fp, LOCK_EX);
    $stats = json_decode((string)@stream_get_contents($fp), true);
    if (!is_array($stats)) { $stats = []; }

    $today = date('Y-m-d');
    $stats['total'] = (int)($stats['total'] ?? 0) + 1;
    $stats['channels'] = $stats['channels'] ?? [];
    $stats['channels'][$id] = (int)($stats['channels'][$id] ?? 0) + 1;
    $stats['daily'] = $stats['daily'] ?? [];
    $stats['daily'][$today] = $stats['daily'][$today] ?? [];
    $stats['daily'][$today]['_all'] = (int)($stats['daily'][$today]['_all'] ?? 0) + 1;
    $stats['daily'][$today][$id] = (int)($stats['daily'][$today][$id] ?? 0) + 1;
    $stats['updated'] = date('c');

    // 按保留天数清理旧日志
    $keepDays = max(30, (int)($config['keepDays'] ?? 180));
    $cutoff = date('Y-m-d', strtotime("-{$keepDays} days"));
    foreach (array_keys($stats['daily']) as $d) {
        if ($d !== '' && strcmp($d, $cutoff) < 0) { unset($stats['daily'][$d]); }
    }

    @ftruncate($fp, 0);
    @rewind($fp);
    @fwrite($fp, json_encode($stats, JSON_UNESCAPED_UNICODE));
    @flock($fp, LOCK_UN);
    @fclose($fp);
}

// ---- 跳转（302；直连时由 nginx 继续提供文件本体，支持断点续传）----
header('Location: ' . $ch['url']);
exit;
