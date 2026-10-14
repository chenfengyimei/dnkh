<?php
declare(strict_types=1);
require __DIR__ . '/bootstrap.php';

if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') {
    feedbackJson(405, ['ok' => false, 'message' => 'Method not allowed']);
}

feedbackStartSession('dnkhw_feedback_form');

$origin = (string)($_SERVER['HTTP_ORIGIN'] ?? '');
if ($origin !== '') {
    $originHost = parse_url($origin, PHP_URL_HOST);
    $originScheme = parse_url($origin, PHP_URL_SCHEME);
    if ($originHost !== 'app.cyhaoka.vip' || $originScheme !== 'https') {
        feedbackJson(403, ['ok' => false, 'message' => '请求来源无效。']);
    }
}

if (!feedbackValidCsrf($_SERVER['HTTP_X_CSRF_TOKEN'] ?? null)) {
    feedbackJson(403, ['ok' => false, 'message' => '表单已过期，请刷新页面后重试。']);
}

$issuedAt = (int)($_SESSION['feedback_form_issued_at'] ?? 0);
if ($issuedAt === 0 || time() - $issuedAt < 3) {
    feedbackJson(429, ['ok' => false, 'message' => '请稍等片刻后再提交。']);
}

$lastSubmitAt = (int)($_SESSION['feedback_last_submit_at'] ?? 0);
if ($lastSubmitAt > 0 && time() - $lastSubmitAt < 30) {
    feedbackJson(429, ['ok' => false, 'message' => '提交过于频繁，请稍后再试。']);
}

$raw = file_get_contents('php://input');
$payload = is_string($raw) ? json_decode($raw, true) : null;
if (!is_array($payload)) {
    feedbackJson(400, ['ok' => false, 'message' => '提交内容格式无效。']);
}

if (feedbackClean($payload['website'] ?? '', 128) !== '') {
    feedbackJson(400, ['ok' => false, 'message' => '提交未通过校验。']);
}

try {
    $record = feedbackCreateRecord($payload);
    feedbackMutate(static function (array $records) use ($record): array {
        array_unshift($records, $record);
        return array_slice($records, 0, 1000);
    });
    $_SESSION['feedback_last_submit_at'] = time();
    feedbackJson(201, ['ok' => true, 'id' => $record['id'], 'message' => '已收到，感谢你的反馈。']);
} catch (InvalidArgumentException $exception) {
    feedbackJson(422, ['ok' => false, 'message' => $exception->getMessage()]);
} catch (Throwable $exception) {
    error_log('feedback submit failed: ' . $exception->getMessage());
    feedbackJson(503, ['ok' => false, 'message' => '服务器暂时无法保存反馈，请稍后重试或使用邮件发送。']);
}
