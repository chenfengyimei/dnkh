<?php
declare(strict_types=1);

// 动态输出同目录 JSON：不让 CDN 缓存旧版本清单，应用内更新始终拿到当前发布信息。
header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store, no-cache, must-revalidate, max-age=0');
header('Pragma: no-cache');
header('Expires: 0');

$manifest = __DIR__ . '/version.json';
if (!is_file($manifest)) {
    http_response_code(500);
    echo '{"error":"release manifest unavailable"}';
    exit;
}

readfile($manifest);
