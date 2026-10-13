<?php
// 下载渠道配置 —— 填上网盘分享链接即可在前端自动显示该渠道按钮。
// url 留空 = 渠道暂未开放（前端自动隐藏）。
// 'direct' 为本站直连，请勿删除；url 使用站点根相对路径。
return [
    // stats.php 统计页访问密钥（URL 带 ?key= 此值才可查看）
    'statsKey' => 'dnkhw-7f3a9c2e51b8d406',

    // 是否在官网公开展示「累计下载 N 次」（仅聚合数字，不含任何个人信息）
    'showPublicStats' => true,

    // 日志保留天数（每日计数按此自动清理）
    'keepDays' => 180,

    'channels' => [
        'direct' => ['label' => '本站直连',   'type' => 'file', 'url' => '/download/releases/v1.0.5/Dnkhw.exe'],
        'lanzou' => ['label' => '蓝奏云',     'type' => 'link', 'url' => ''],
        'baidu'  => ['label' => '百度网盘',   'type' => 'link', 'url' => ''],
        'quark'  => ['label' => '夸克网盘',   'type' => 'link', 'url' => ''],
    ],
];
