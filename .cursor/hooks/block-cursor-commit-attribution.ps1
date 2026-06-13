# Cursor が git commit に Co-authored-by / --trailer を付けないようブロックする
$raw = [Console]::In.ReadToEnd()
try {
    $obj = $raw | ConvertFrom-Json
    $command = [string]$obj.command
} catch {
    Write-Output '{"permission":"allow"}'
    exit 0
}

if ($command -match 'git\s+commit' -and (
        $command -match '--trailer' -or
        $command -match 'cursoragent@cursor\.com' -or
        $command -match 'Co-authored-by:\s*Cursor' -or
        $command -match 'Made-with:\s*Cursor')) {
    Write-Output (@{
        permission    = 'deny'
        user_message  = 'このリポジトリでは Cursor の Co-authored-by / Made-with / --trailer は禁止です。'
        agent_message = 'Never add Co-authored-by, Made-with, or --trailer to git commits in this repo.'
    } | ConvertTo-Json -Compress)
    exit 2
}

Write-Output '{"permission":"allow"}'
exit 0
