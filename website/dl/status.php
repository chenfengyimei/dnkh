<?php
// 前端渠道状态接口（同源 fetch）：返回启用渠道与累计下载数（仅聚合数字）。
$config = require __DIR__ . '/config.php';

function readStats(string $file): array {
    $fp = @fopen($file, 'r');
    if (!$fp) { return []; }
    @flock($fp, LOCK_SH);
    $stats = json_decode((string)@stream_get_contents($fp), true);
    @flock($fp, LOCK_UN);
    @fclose($fp);
    return is_array($stats) ? $stats : [];
}

$stats = readStats(__DIR__ . '/data/stats.json');
$today = date('Y-m-d');

$out = [
    'total' => (int)($stats['total'] ?? 0),
    'today' => (int)($stats['daily'][$today]['_all'] ?? 0),
    'showPublicStats' => (bool)($config['showPublicStats'] ?? true),
    'channels' => [],
];

foreach ($config['channels'] as $id => $ch) {
    $enabled = $ch['type'] === 'file' || !empty($ch['url']);
    $out['channels'][] = [
        'id' => $id,
        'label' => $ch['label'],
        'type' => $ch['type'],
        'enabled' => $enabled,
        'url' => 'dl/download.php?id=' . rawurlencode($id),
        'count' => (int)($stats['channels'][$id] ?? 0),
    ];
}

header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store');
echo json_encode($out, JSON_UNESCAPED_UNICODE);
