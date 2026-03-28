param(
    [switch]$Publish,
    [switch]$SelfContained = $true,
    [string]$Runtime = "win-x64"
)

$project = Join-Path $PSScriptRoot "src\AppMigrator.UI\AppMigrator.UI.csproj"

dotnet restore $project
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($Publish) {
    $selfContainedValue = if ($SelfContained) { "true" } else { "false" }
    dotnet publish $project -c Release -r $Runtime --self-contained $selfContainedValue -p:PublishSingleFile=true
    exit $LASTEXITCODE
}
