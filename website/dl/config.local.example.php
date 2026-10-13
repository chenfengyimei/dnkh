<?php
// 复制为 config.local.php 后，在服务器上填入用 openssl rand -hex 32 生成的随机值。
// 该实际文件被 .gitignore 排除，绝不可提交。
return [
    'statsKey' => 'replace-with-a-random-server-only-value',
];
