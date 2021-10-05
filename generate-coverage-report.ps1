$ErrorActionPreference = "Stop"

if (!(dotnet tool list --global | Select-String "dotnet-reportgenerator-globaltool" -quiet))
{
    dotnet tool install -g dotnet-reportgenerator-globaltool
}

if (Test-Path ActionCableSharp.Tests/TestResults)
{
    Remove-Item ActionCableSharp.Tests/TestResults -Recurse
}

if (Test-Path Print3DCloud.Client.Tests/TestResults)
{
    Remove-Item Print3DCloud.Client.Tests/TestResults -Recurse
}

dotnet test --collect:"XPlat Code Coverage"

$coverage_acs = Get-ChildItem ActionCableSharp.Tests/TestResults/*/*.xml | Sort-Object LastWriteTime -Desc | Select-Object -First 1
$coverage_p3d = Get-ChildItem Print3DCloud.Client.Tests/TestResults/*/*.xml | Sort-Object LastWriteTime -Desc | Select-Object -First 1

reportgenerator -reports:"$coverage_acs;$coverage_p3d" -targetdir:"coverage"
