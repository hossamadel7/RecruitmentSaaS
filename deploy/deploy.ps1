param()

$ErrorActionPreference = 'Stop'

$vps = 'ubuntu@158.69.206.138'
$service = 'recruitmentsaas'
$repoDir = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$remoteTarball = '/home/ubuntu/recruitmentsaas-deploy.tgz'
$tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$taskTemp = Join-Path $tempBase ('recruitmentsaas-deploy-' + [Guid]::NewGuid().ToString('N'))
$publishDir = Join-Path $taskTemp 'publish'
$tarball = Join-Path $taskTemp 'recruitmentsaas-deploy.tgz'

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
$resolvedTaskTemp = (Resolve-Path $taskTemp).Path
if (-not $resolvedTaskTemp.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -or
    (Split-Path $resolvedTaskTemp -Leaf) -notlike 'recruitmentsaas-deploy-*') {
    throw "Unsafe temporary deployment path: $resolvedTaskTemp"
}

try {
    Write-Host '==> [1/4] Publishing (Release, linux-x64, self-contained)...'
    & dotnet publish (Join-Path $repoDir 'RecruitmentSaaS/RecruitmentSaaS.csproj') `
        -c Release -r linux-x64 --self-contained true -o $publishDir -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    $uploads = Join-Path $publishDir 'wwwroot/uploads'
    if (Test-Path -LiteralPath $uploads) { Remove-Item -LiteralPath $uploads -Recurse -Force }
    $developmentSettings = Join-Path $publishDir 'appsettings.Development.json'
    if (Test-Path -LiteralPath $developmentSettings) { Remove-Item -LiteralPath $developmentSettings -Force }

    Write-Host '==> [2/4] Packing...'
    & tar -czf $tarball -C $publishDir .
    if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE" }

    Write-Host "==> [3/4] Uploading to $vps ..."
    & scp -q $tarball "${vps}:$remoteTarball"
    if ($LASTEXITCODE -ne 0) { throw "scp failed with exit code $LASTEXITCODE" }

    Write-Host "==> [4/4] Installing and restarting $service ..."
    & ssh $vps 'sudo /usr/local/bin/recruitmentsaas-deploy.sh'
    if ($LASTEXITCODE -ne 0) { throw "remote deploy failed with exit code $LASTEXITCODE" }

    Write-Host '==> Health check'
    Start-Sleep -Seconds 3
    & ssh $vps 'curl -fsS http://127.0.0.1:8082/health'
    if ($LASTEXITCODE -ne 0) { throw "health check failed with exit code $LASTEXITCODE" }
    Write-Host ''
    Write-Host '==> Deployed.'
}
finally {
    if (Test-Path -LiteralPath $resolvedTaskTemp) {
        Remove-Item -LiteralPath $resolvedTaskTemp -Recurse -Force
    }
}
