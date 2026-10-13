<?php
// 下载统计查看页（需带密钥）：dl/stats.php?key=<statsKey>
// 展示：累计总数 / 各渠道计数 / 近 30 天每日明细。
$config = require __DIR__ . '/config.php';
$key = (string)($_GET['key'] ?? '');
if (!hash_equals((string)$config['statsKey'], $key)) {
    http_response_code(403);
    header('Content-Type: text/plain; charset=utf-8');
    exit('403 Forbidden');
}

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
$channels = $config['channels'];
$daily = $stats['daily'] ?? [];
ksort($daily);
$rows = array_slice($daily, -30, null, true);
$rows = array_reverse($rows, true);
$today = date('Y-m-d');

header('Content-Type: text/html; charset=utf-8');
$fmt = fn(int $n) => number_format($n);
?>
<!doctype html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>下载统计 — 电脑开荒</title>
<style>
  body{font-family:system-ui,"Segoe UI","Microsoft YaHei UI",sans-serif;background:#f5f7fa;color:#1b1f27;margin:0;padding:32px 16px}
  .wrap{max-width:760px;margin:0 auto}
  h1{font-size:22px} h2{font-size:16px;margin:28px 0 10px;color:#0067c0}
  .cards{display:flex;gap:14px;flex-wrap:wrap}
  .card{background:#fff;border:1px solid rgba(28,47,74,.1);border-radius:10px;padding:16px 22px;box-shadow:0 1px 2px rgba(16,30,54,.06)}
  .card b{display:block;font-size:30px;line-height:1.2}
  .card span{font-size:13px;color:#5b6472}
  table{border-collapse:collapse;width:100%;background:#fff;border:1px solid rgba(28,47,74,.1);border-radius:8px;overflow:hidden}
  th,td{padding:8px 14px;border-bottom:1px solid rgba(28,47,74,.06);text-align:left;font-size:14px}
  th{background:#eef2f7;font-size:13px;color:#4e5665}
  td.n,th.n{text-align:right;font-variant-numeric:tabular-nums}
  .muted{color:#8a93a1;font-size:12.5px}
  .today{color:#0f7b0f;font-weight:600}
</style>
</head>
<body>
<div class="wrap">
  <h1>电脑开荒 · 下载统计</h1>
  <p class="muted">仅统计各渠道点击次数（聚合数据，不含任何个人信息）· 更新时间：<?= htmlspecialchars($stats['updated'] ?? '尚无数据') ?></p>

  <div class="cards">
    <div class="card"><b><?= $fmt((int)($stats['total'] ?? 0)) ?></b><span>累计下载（全部渠道）</span></div>
    <div class="card"><b><?= $fmt((int)($stats['daily'][$today]['_all'] ?? 0)) ?></b><span>今日下载</span></div>
    <div class="card"><b><?= count($daily) ?></b><span>有记录天数</span></div>
  </div>

  <h2>各渠道累计</h2>
  <table>
    <tr><th>渠道</th><th>状态</th><th class="n">累计点击</th></tr>
    <?php foreach ($channels as $id => $ch):
        $enabled = $ch['type'] === 'file' || !empty($ch['url']); ?>
    <tr>
      <td><?= htmlspecialchars($ch['label']) ?> <span class="muted">(<?= htmlspecialchars($id) ?>)</span></td>
      <td><?= $enabled ? '✅ 开放' : '⏳ 未配置链接' ?></td>
      <td class="n"><?= $fmt((int)($stats['channels'][$id] ?? 0)) ?></td>
    </tr>
    <?php endforeach; ?>
  </table>

  <h2>每日明细（近 30 天，新→旧）</h2>
  <?php if (!$rows): ?>
    <p class="muted">暂无每日数据。</p>
  <?php else: ?>
  <table>
    <tr><th>日期</th><th class="n">合计</th>
    <?php foreach (array_keys($channels) as $id): ?><th class="n"><?= htmlspecialchars($channels[$id]['label']) ?></th><?php endforeach; ?>
    </tr>
    <?php foreach ($rows as $d => $row): ?>
    <tr>
      <td<?= $d === $today ? ' class="today"' : '' ?>><?= htmlspecialchars($d) ?></td>
      <td class="n"><b><?= $fmt((int)($row['_all'] ?? 0)) ?></b></td>
      <?php foreach (array_keys($channels) as $id): ?>
      <td class="n"><?= $fmt((int)($row[$id] ?? 0)) ?></td>
      <?php endforeach; ?>
    </tr>
    <?php endforeach; ?>
  </table>
  <?php endif; ?>

  <p class="muted" style="margin-top:20px">说明：网盘渠道统计的是「点击跳转次数」，实际是否完成下载以网盘后台为准；本站直连为真实文件下载。</p>
</div>
</body>
</html>
