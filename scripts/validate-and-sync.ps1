[CmdletBinding()]
param(
    [string]$CommitMessage,
    [switch]$SkipBuild,
    [switch]$SkipCommit,
    [switch]$TryGitPush,
    [string]$Repository = "qurikuduo/kiwix_converter"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-LastExitCode {
    param(
        [string]$Operation
    )

    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

function Write-Step {
    param(
        [string]$Message
    )

    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Test-GitHubGitConnectivity {
    param(
        [int]$TimeoutMilliseconds = 3000
    )

    $client = New-Object System.Net.Sockets.TcpClient

    try {
        $asyncResult = $client.BeginConnect("github.com", 443, $null, $null)
        if (-not $asyncResult.AsyncWaitHandle.WaitOne($TimeoutMilliseconds, $false)) {
            return $false
        }

        $client.EndConnect($asyncResult)
        return $true
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

function Get-RemoteGitCommit {
    param(
        [string]$RepositoryName,
        [string]$CommitSha
    )

    $remoteCommitJson = & gh api "repos/$RepositoryName/git/commits/$CommitSha"
    Assert-LastExitCode "gh api git/commits/$CommitSha"
    return $remoteCommitJson | ConvertFrom-Json
}

function Get-LocalCommitMetadata {
    param(
        [string]$CommitSha
    )

    $payload = (& git show -s --format='%T%x1f%P%x1f%an%x1f%ae%x1f%aI%x1f%cn%x1f%ce%x1f%cI%x1f%B' $CommitSha)
    Assert-LastExitCode "git show metadata for $CommitSha"

    $parts = $payload -split [char]31, 9
    return [pscustomobject]@{
        TreeSha = $parts[0]
        ParentShas = if ([string]::IsNullOrWhiteSpace($parts[1])) { @() } else { @($parts[1] -split ' ') }
        AuthorName = $parts[2]
        AuthorEmail = $parts[3]
        AuthorDate = [DateTimeOffset]::Parse($parts[4])
        CommitterName = $parts[5]
        CommitterEmail = $parts[6]
        CommitterDate = [DateTimeOffset]::Parse($parts[7])
        Message = $parts[8].TrimEnd("`r", "`n")
    }
}

function Test-CommitEquivalence {
    param(
        [string]$RepositoryName,
        [string]$LocalCommitSha,
        [object]$RemoteCommit,
        [int]$Depth = 0
    )

    $localCommit = Get-LocalCommitMetadata -CommitSha $LocalCommitSha
    return $localCommit.TreeSha -eq $RemoteCommit.tree.sha
}

function Update-EquivalentTrackingRefs {
    param(
        [string]$LocalCommitSha,
        [string]$ActualRemoteSha
    )

    & git update-ref refs/remotes/origin/main $LocalCommitSha
    Assert-LastExitCode "git update-ref refs/remotes/origin/main"

    Write-Host "Recorded GitHub main SHA $ActualRemoteSha and aligned local origin/main to content-equivalent commit $LocalCommitSha." -ForegroundColor Green
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $repoRoot

foreach ($commandName in "git", "gh", "dotnet") {
    if (-not (Get-Command $commandName -ErrorAction SilentlyContinue)) {
        throw "Required command '$commandName' is not available on PATH."
    }
}

Write-Step "Checking GitHub CLI authentication"
& gh auth status
Assert-LastExitCode "gh auth status"

if (-not $SkipBuild) {
    Write-Step "Building the WinForms project in Release"
    & dotnet build "src/KiwixConverter.WinForms/KiwixConverter.WinForms.csproj" --configuration Release
    Assert-LastExitCode "dotnet build"
}

$workingTreeStatus = & git status --porcelain
Assert-LastExitCode "git status --porcelain"

if ($SkipCommit -and $workingTreeStatus) {
    throw "-SkipCommit requires a clean working tree. Commit or stash local changes before using the sync-only path."
}

if (-not $SkipCommit -and $workingTreeStatus) {
    if ([string]::IsNullOrWhiteSpace($CommitMessage)) {
        throw "Provide -CommitMessage to commit the validated changes before syncing GitHub."
    }

    Write-Step "Creating a local git commit"
    & git add -A
    Assert-LastExitCode "git add -A"

    & git commit -m $CommitMessage
    Assert-LastExitCode "git commit"
}

$localHead = (& git rev-parse HEAD).Trim()
Assert-LastExitCode "git rev-parse HEAD"

$remoteHead = (& gh api "repos/$Repository/git/ref/heads/main" --jq ".object.sha").Trim()
Assert-LastExitCode "gh api git/ref/heads/main"

$remoteHeadCommit = Get-RemoteGitCommit -RepositoryName $Repository -CommitSha $remoteHead

if ($remoteHead -eq $localHead -or (Test-CommitEquivalence -RepositoryName $Repository -LocalCommitSha $localHead -RemoteCommit $remoteHeadCommit)) {
    Update-EquivalentTrackingRefs -LocalCommitSha $localHead -ActualRemoteSha $remoteHead
    Write-Step "GitHub main already matches or is content-equivalent to the local HEAD"
    & git status --short --branch
    Assert-LastExitCode "git status --short --branch"
    return
}

if ($TryGitPush -and (Test-GitHubGitConnectivity)) {
    Write-Step "Trying a normal git push first"
    $previousPromptSetting = $env:GIT_TERMINAL_PROMPT
    try {
        $env:GIT_TERMINAL_PROMPT = "0"
        & git push origin main
        if ($LASTEXITCODE -eq 0) {
            Update-EquivalentTrackingRefs -LocalCommitSha $localHead -ActualRemoteSha $localHead
            Write-Step "git push succeeded"
            & git status --short --branch
            Assert-LastExitCode "git status --short --branch"
            return
        }
    }
    finally {
        if ($null -eq $previousPromptSetting) {
            Remove-Item Env:GIT_TERMINAL_PROMPT -ErrorAction SilentlyContinue
        }
        else {
            $env:GIT_TERMINAL_PROMPT = $previousPromptSetting
        }
    }

    Write-Warning "git push failed with exit code $LASTEXITCODE. Falling back to a GitHub API fast-forward update."
}
elseif ($TryGitPush) {
    Write-Warning "Direct git connectivity to github.com:443 is unavailable. Falling back to a GitHub API fast-forward update."
}
else {
    Write-Warning "Skipping direct git push and using the GitHub API sync path. Pass -TryGitPush to try a normal git push first."
}

$parentSha = (& git rev-parse HEAD^).Trim()
Assert-LastExitCode "git rev-parse HEAD^"

if ($remoteHead -ne $parentSha) {
    if (-not (Test-CommitEquivalence -RepositoryName $Repository -LocalCommitSha $parentSha -RemoteCommit $remoteHeadCommit)) {
        throw "GitHub main ($remoteHead) is not equivalent to the parent of local HEAD ($parentSha). Aborting API fallback to avoid overwriting remote history."
    }

    Write-Warning "GitHub main differs by SHA from the local parent but is content-equivalent. Continuing with the GitHub main SHA as the remote parent."
}

$baseTreeSha = $remoteHeadCommit.tree.sha

$changes = & git diff-tree --no-commit-id --name-status --no-renames -r HEAD
Assert-LastExitCode "git diff-tree"

$treeEntries = @()
foreach ($line in $changes) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }

    $parts = $line -split "`t"
    if ($parts.Length -lt 2) {
        throw "Unexpected diff-tree output line: $line"
    }

    $statusCode = $parts[0]
    $path = $parts[1]
    $treePath = $path -replace "\\", "/"

    if ($statusCode -eq "D") {
        $deletedTreeEntry = (& git ls-tree $parentSha -- "$path").Trim()
        Assert-LastExitCode "git ls-tree $parentSha -- $path"
        if ([string]::IsNullOrWhiteSpace($deletedTreeEntry)) {
            throw "Unable to resolve deleted path '$path' in parent commit $parentSha."
        }

        $deletedParts = $deletedTreeEntry -split "\s+"
        $treeEntries += @{
            path = $treePath
            mode = $deletedParts[0]
            type = $deletedParts[1]
            sha = $null
        }
        continue
    }

    $treeEntry = (& git ls-tree HEAD -- "$path").Trim()
    Assert-LastExitCode "git ls-tree HEAD -- $path"
    if ([string]::IsNullOrWhiteSpace($treeEntry)) {
        throw "Unable to resolve path '$path' in HEAD."
    }

    $treeParts = $treeEntry -split "\s+"
    $fullPath = Join-Path $repoRoot $path
    $contentBase64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($fullPath))
    $blobBody = @{ content = $contentBase64; encoding = "base64" } | ConvertTo-Json -Compress
    $blobSha = ($blobBody | & gh api "repos/$Repository/git/blobs" -X POST --input - --jq ".sha").Trim()
    Assert-LastExitCode "gh api git/blobs"

    $treeEntries += @{
        path = $treePath
        mode = $treeParts[0]
        type = $treeParts[1]
        sha = $blobSha
    }
}

if ($treeEntries.Count -eq 0) {
    Write-Step "No new commit delta was detected after validation"
    return
}

$treeBody = @{ base_tree = $baseTreeSha; tree = $treeEntries } | ConvertTo-Json -Depth 10 -Compress
$createdTreeSha = ($treeBody | & gh api "repos/$Repository/git/trees" -X POST --input - --jq ".sha").Trim()
Assert-LastExitCode "gh api git/trees"

$authorName = (& git show -s --format=%an HEAD).Trim()
Assert-LastExitCode "git show author name"
$authorEmail = (& git show -s --format=%ae HEAD).Trim()
Assert-LastExitCode "git show author email"
$authorDate = (& git show -s --format=%aI HEAD).Trim()
Assert-LastExitCode "git show author date"
$committerName = (& git show -s --format=%cn HEAD).Trim()
Assert-LastExitCode "git show committer name"
$committerEmail = (& git show -s --format=%ce HEAD).Trim()
Assert-LastExitCode "git show committer email"
$committerDate = (& git show -s --format=%cI HEAD).Trim()
Assert-LastExitCode "git show committer date"
$commitMessageValue = ((& git log -1 --pretty=%B HEAD) | Out-String).TrimEnd()
Assert-LastExitCode "git log commit message"

$commitBody = @{
    message = $commitMessageValue
    tree = $createdTreeSha
    parents = @($remoteHead)
    author = @{
        name = $authorName
        email = $authorEmail
        date = $authorDate
    }
    committer = @{
        name = $committerName
        email = $committerEmail
        date = $committerDate
    }
} | ConvertTo-Json -Depth 10 -Compress

$remoteCommitSha = ($commitBody | & gh api "repos/$Repository/git/commits" -X POST --input - --jq ".sha").Trim()
Assert-LastExitCode "gh api git/commits"

$refBody = @{ sha = $remoteCommitSha; force = $false } | ConvertTo-Json -Compress
$null = $refBody | & gh api "repos/$Repository/git/refs/heads/main" -X PATCH --input -
Assert-LastExitCode "gh api git/refs/heads/main PATCH"

Write-Host "GitHub main updated to $remoteCommitSha via gh API fallback." -ForegroundColor Green
$confirmedRemoteHead = (& gh api "repos/$Repository/git/ref/heads/main" --jq ".object.sha").Trim()
Assert-LastExitCode "gh api confirm git/ref/heads/main"

$confirmedRemoteCommit = Get-RemoteGitCommit -RepositoryName $Repository -CommitSha $confirmedRemoteHead

if (Test-CommitEquivalence -RepositoryName $Repository -LocalCommitSha $localHead -RemoteCommit $confirmedRemoteCommit) {
    Update-EquivalentTrackingRefs -LocalCommitSha $localHead -ActualRemoteSha $confirmedRemoteHead
    Write-Host "Updated local tracking refs using content-equivalent GitHub main $confirmedRemoteHead." -ForegroundColor Green
}
else {
    Write-Warning "Local HEAD remains $localHead while GitHub main now points at $confirmedRemoteHead. Run git fetch once github.com connectivity is healthy to fully reconcile local history."
}

Write-Host "Confirmed GitHub main: $confirmedRemoteHead" -ForegroundColor Green