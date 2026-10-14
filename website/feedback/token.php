<?php
declare(strict_types=1);
require __DIR__ . '/bootstrap.php';

if (($_SERVER['REQUEST_METHOD'] ?? 'GET') !== 'GET') {
    feedbackJson(405, ['ok' => false, 'message' => 'Method not allowed']);
}

feedbackStartSession('dnkhw_feedback_form');
$_SESSION['feedback_form_issued_at'] = time();
feedbackJson(200, ['ok' => true, 'token' => feedbackCsrfToken()]);
