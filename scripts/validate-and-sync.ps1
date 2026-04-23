[CmdletBinding()]
param(
    [string]$CommitMessage,
    [switch]$SkipBuild,
    [switch]$SkipCommit,
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

if ($remoteHead -eq $localHead) {
    Write-Step "GitHub main already matches the local HEAD"
    & git status --short --branch
    Assert-LastExitCode "git status --short --branch"
    return
}

Write-Step "Trying a normal git push first"
& git push origin main
if ($LASTEXITCODE -eq 0) {
    Write-Step "git push succeeded"
    & git status --short --branch
    Assert-LastExitCode "git status --short --branch"
    return
}

Write-Warning "git push failed. Falling back to a GitHub API fast-forward update."

$parentSha = (& git rev-parse HEAD^).Trim()
Assert-LastExitCode "git rev-parse HEAD^"

if ($remoteHead -ne $parentSha) {
    throw "GitHub main ($remoteHead) is not the parent of local HEAD ($parentSha). Aborting API fallback to avoid overwriting remote history."
}

$baseTreeSha = (& gh api "repos/$Repository/git/commits/$remoteHead" --jq ".tree.sha").Trim()
Assert-LastExitCode "gh api git/commits/$remoteHead"

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
    parents = @($parentSha)
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
if ($remoteCommitSha -ne $localHead) {
    Write-Warning "Local HEAD remains $localHead while GitHub main now points at $remoteCommitSha. Run git fetch once github.com connectivity is healthy to fully reconcile local history."
}

$confirmedRemoteHead = (& gh api "repos/$Repository/git/ref/heads/main" --jq ".object.sha").Trim()
Assert-LastExitCode "gh api confirm git/ref/heads/main"
Write-Host "Confirmed GitHub main: $confirmedRemoteHead" -ForegroundColor Green