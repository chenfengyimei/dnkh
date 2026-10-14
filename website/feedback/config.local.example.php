<?php
// 复制为 config.local.php 后，仅在服务器上设置管理员账号与密码哈希。
// 生成哈希：php -r "echo password_hash(trim(fgets(STDIN)), PASSWORD_DEFAULT), PHP_EOL;"
// 将实际文件保留在服务器，绝不可提交到 Git。
return [
    'adminUser' => 'admin',
    'adminPasswordHash' => 'replace-with-password_hash-output',
];
