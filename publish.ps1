param(
    # version from git tag, e.g. 1.7.0 or 1.7.0.1; empty = use csproj version
    [string]$Version
)

# 关闭回显
$ErrorActionPreference = "SilentlyContinue"

$main = "src/STranslate/STranslate.csproj"
$mainProfile = "src/STranslate/Properties/PublishProfiles/FolderProfile.pubxml"

# AssemblyVersion/FileVersion 只接受 "数字.数字.数字.数字"(最多4段), 不符合格式(如带 v 前缀或预发布后缀)则忽略, 回退到 csproj 里的值
$versionArgs = @()
if ($Version) {
    $normalizedVersion = $Version.TrimStart("v", "V")
    if ($normalizedVersion -match '^\d+(\.\d+){0,3}$') {
        Write-Host ""
        Write-Host "========================================"
        Write-Host "[Version] Using version from tag: $normalizedVersion"
        Write-Host "========================================"
        $versionArgs = @("-p:Version=$normalizedVersion", "-p:AssemblyVersion=$normalizedVersion", "-p:FileVersion=$normalizedVersion")
    } else {
        Write-Host ""
        Write-Host "========================================"
        Write-Host "[Version] '$Version' is not a plain numeric version, falling back to csproj version."
        Write-Host "========================================"
    }
}

# 检查并删除发布目录
function CheckAndDeletePublishDir($publishDir) {
    if (Test-Path $publishDir) {
		Write-Host ""
		Write-Host "[Cache] Starting clear."
		Write-Host "========================================"
		Write-Host "[Cache] Deleting existing publish directory $publishDir..."
		Write-Host "========================================"
        try {
            Remove-Item -Path $publishDir -Recurse -Force
			Write-Host ""
			Write-Host "========================================"
            Write-Host "[Cache] Deleted $publishDir successfully."
			Write-Host "========================================"
        } catch {
			Write-Host ""
			Write-Host "========================================"
            Write-Host "[Cache] Failed to delete $publishDir. Stopping script."
			Write-Host "========================================"
            exit
        }
    } else {
		Write-Host ""
		Write-Host "========================================"
		Write-Host "[Cache] $publishDir not existing."
		Write-Host "========================================"
	}
}

# 将相对发布目录路径转换为绝对路径
$publishDirAbsolute = [System.IO.Path]::GetFullPath("publish")

CheckAndDeletePublishDir $publishDirAbsolute

Write-Host ""
Write-Host "Publishing started."
Write-Host "========================================"
Write-Host "Publishing $main..."
Write-Host "========================================"
dotnet publish $main -c Release -p:PublishProfile=$mainProfile @versionArgs

Write-Host ""
Write-Host "========================================"
Write-Host "Publishing completed."
Write-Host "========================================"
# 暂停
# Read-Host -Prompt "Press Enter to continue"