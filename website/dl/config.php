<?php
// 统计页密钥只从服务器本地 config.local.php 读取；该文件已在 .gitignore 中排除。
// 缺失密钥时 stats.php 会拒绝所有访问，绝不把空值当作可用口令。
$localConfig = __DIR__ . '/config.local.php';
$local = is_file($localConfig) ? require $localConfig : [];
$statsKey = is_array($local) && isset($local['statsKey']) && is_string($local['statsKey'])
    ? $local['statsKey']
    : '';

// 下载渠道配置 —— 填上网盘分享链接即可在前端自动显示该渠道按钮。
// url 留空 = 渠道暂未开放（前端自动隐藏）。
// 'direct' 为本站直连，请勿删除；url 使用站点根相对路径。
return [
    // stats.php 统计页访问密钥（服务器本地配置；不入库）
    'statsKey' => $statsKey,

    // 是否在官网公开展示「累计下载 N 次」（仅聚合数字，不含任何个人信息）
    'showPublicStats' => true,

    // 日志保留天数（每日计数按此自动清理）
    'keepDays' => 180,

    'channels' => [
        'direct' => ['label' => '本站直连',   'type' => 'file', 'url' => '/download/releases/v1.0.6/Dnkhw.exe'],
        'lanzou' => ['label' => '蓝奏云（提取码：81it）', 'type' => 'link', 'url' => 'https://wwbga.lanzoum.com/iN0Ap4by0lij'],
        'baidu'  => ['label' => '百度网盘',   'type' => 'link', 'url' => ''],
        'quark'  => ['label' => '夸克网盘',   'type' => 'link', 'url' => ''],
    ],
];
