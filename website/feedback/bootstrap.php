<?php
declare(strict_types=1);

/** @return array{adminUser:string,adminPasswordHash:string} */
function feedbackConfig(): array
{
    static $config = null;
    if (is_array($config)) {
        return $config;
    }

    $localFile = __DIR__ . '/config.local.php';
    $local = is_file($localFile) ? require $localFile : [];
    $local = is_array($local) ? $local : [];

    $config = [
        'adminUser' => isset($local['adminUser']) && is_string($local['adminUser'])
            ? $local['adminUser']
            : 'admin',
        'adminPasswordHash' => isset($local['adminPasswordHash']) && is_string($local['adminPasswordHash'])
            ? $local['adminPasswordHash']
            : '',
    ];

    return $config;
}

function feedbackSendSecurityHeaders(bool $private = false): void
{
    header('X-Content-Type-Options: nosniff');
    header('Referrer-Policy: no-referrer');
    header('X-Frame-Options: DENY');
    if ($private) {
        header('Cache-Control: no-store, no-cache, must-revalidate, max-age=0');
        header('Pragma: no-cache');
        header("Content-Security-Policy: default-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'none'; img-src 'self'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'");
    }
}

function feedbackJson(int $status, array $body): never
{
    feedbackSendSecurityHeaders();
    http_response_code($status);
    header('Content-Type: application/json; charset=utf-8');
    header('Cache-Control: no-store, no-cache, must-revalidate, max-age=0');
    echo json_encode($body, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    exit;
}

function feedbackStartSession(string $name): void
{
    if (session_status() === PHP_SESSION_ACTIVE) {
        return;
    }

    session_name($name);
    session_set_cookie_params([
        'lifetime' => 0,
        'path' => '/feedback/',
        'secure' => true,
        'httponly' => true,
        'samesite' => 'Strict',
    ]);
    ini_set('session.use_strict_mode', '1');
    ini_set('session.cookie_httponly', '1');
    session_start();
}

function feedbackCsrfToken(): string
{
    if (empty($_SESSION['feedback_csrf']) || !is_string($_SESSION['feedback_csrf'])) {
        $_SESSION['feedback_csrf'] = bin2hex(random_bytes(32));
    }
    return $_SESSION['feedback_csrf'];
}

function feedbackValidCsrf(?string $provided): bool
{
    $expected = $_SESSION['feedback_csrf'] ?? '';
    return is_string($expected) && is_string($provided) && hash_equals($expected, $provided);
}

function feedbackDataDirectory(): string
{
    return __DIR__ . '/data';
}

function feedbackStoragePath(): string
{
    return feedbackDataDirectory() . '/feedbacks.json';
}

/** @return list<array<string, mixed>> */
function feedbackReadAll(): array
{
    $file = feedbackStoragePath();
    if (!is_file($file)) {
        return [];
    }

    $raw = @file_get_contents($file);
    $records = is_string($raw) ? json_decode($raw, true) : [];
    if (!is_array($records)) {
        return [];
    }
    return array_values(array_filter($records, 'is_array'));
}

/** @param callable(list<array<string, mixed>>):list<array<string, mixed>> $mutator */
function feedbackMutate(callable $mutator): void
{
    $directory = feedbackDataDirectory();
    if (!is_dir($directory) && !@mkdir($directory, 0770, true) && !is_dir($directory)) {
        throw new RuntimeException('无法创建反馈存储目录。');
    }

    $lock = @fopen($directory . '/feedbacks.lock', 'c');
    if ($lock === false || !@flock($lock, LOCK_EX)) {
        throw new RuntimeException('暂时无法锁定反馈存储。');
    }

    try {
        $file = feedbackStoragePath();
        $raw = is_file($file) ? @file_get_contents($file) : '';
        $records = is_string($raw) ? json_decode($raw, true) : [];
        $records = is_array($records) ? array_values($records) : [];
        $records = $mutator($records);

        $encoded = json_encode($records, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR);
        $temporary = $file . '.' . bin2hex(random_bytes(8)) . '.tmp';
        if (@file_put_contents($temporary, $encoded, LOCK_EX) === false || !@rename($temporary, $file)) {
            @unlink($temporary);
            throw new RuntimeException('无法保存反馈。');
        }
        @chmod($file, 0640);
    } finally {
        @flock($lock, LOCK_UN);
        @fclose($lock);
    }
}

function feedbackClean(mixed $value, int $maxLength): string
{
    if (!is_string($value)) {
        return '';
    }

    $value = trim($value);
    $value = preg_replace('/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]/u', '', $value) ?? '';
    if (function_exists('mb_substr')) {
        return mb_substr($value, 0, $maxLength, 'UTF-8');
    }
    return substr($value, 0, $maxLength);
}

/** @param array<string, mixed> $payload */
function feedbackCreateRecord(array $payload): array
{
    $type = $payload['type'] ?? '';
    if (!in_array($type, ['bug', 'app'], true)) {
        throw new InvalidArgumentException('反馈类型无效。');
    }

    $record = [
        'id' => bin2hex(random_bytes(10)),
        'type' => $type,
        'status' => 'new',
        'createdAt' => date(DATE_ATOM),
        'updatedAt' => date(DATE_ATOM),
        'adminNote' => '',
        'data' => [],
    ];

    if ($type === 'bug') {
        $description = feedbackClean($payload['description'] ?? '', 4000);
        if ($description === '') {
            throw new InvalidArgumentException('请填写问题描述。');
        }
        $record['data'] = [
            'version' => feedbackClean($payload['version'] ?? '', 64),
            'os' => feedbackClean($payload['os'] ?? '', 160),
            'module' => feedbackClean($payload['module'] ?? '', 64),
            'description' => $description,
            'steps' => feedbackClean($payload['steps'] ?? '', 4000),
            'expectation' => feedbackClean($payload['expectation'] ?? '', 1000),
        ];
    } else {
        $name = feedbackClean($payload['name'] ?? '', 160);
        $site = feedbackClean($payload['site'] ?? '', 2048);
        $url = filter_var($site, FILTER_VALIDATE_URL);
        $scheme = is_string($url) ? strtolower((string)parse_url($url, PHP_URL_SCHEME)) : '';
        if ($name === '' || $url === false || !in_array($scheme, ['https', 'http'], true)) {
            throw new InvalidArgumentException('请填写软件名称和有效的官网地址。');
        }

        $checks = is_array($payload['checks'] ?? null) ? $payload['checks'] : [];
        $allowedChecks = ['官方渠道分发', '安装器无捆绑', '无弹窗广告', '免费或开源'];
        $checks = array_values(array_intersect($allowedChecks, array_map(static fn($item) => feedbackClean($item, 32), $checks)));
        $record['data'] = [
            'name' => $name,
            'site' => $url,
            'winget' => preg_replace('/[^A-Za-z0-9._-]/', '', feedbackClean($payload['winget'] ?? '', 160)) ?? '',
            'category' => feedbackClean($payload['category'] ?? '', 64),
            'checks' => $checks,
            'reason' => feedbackClean($payload['reason'] ?? '', 4000),
        ];
    }

    return $record;
}

function feedbackEscape(mixed $value): string
{
    return htmlspecialchars(is_scalar($value) ? (string)$value : '', ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8');
}
