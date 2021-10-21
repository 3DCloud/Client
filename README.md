# 3DCloud Client
[![GitHub Workflow Status](https://img.shields.io/github/workflow/status/3DCloud/Client/Build%20%26%20Test?style=flat-square)](https://github.com/3DCloud/Client/actions/workflows/build-and-test.yml)
[![Codecov](https://img.shields.io/codecov/c/github/3DCloud/Client?style=flat-square)](https://codecov.io/gh/3DCloud/Client)
[![License](https://img.shields.io/github/license/3DCloud/Client?style=flat-square)](https://github.com/3DCloud/Client/blob/main/LICENSE)

This solution contains multiple projects.

- **[Print3DCloud.Client](Print3DCloud.Client)** &ndash; The client program itself.
- **[ActionCableSharp](ActionCableSharp)** &ndash; A .NET library for interacting with [Action Cable](https://guides.rubyonrails.org/action_cable_overview.html) (Ruby on Rails' layer on top of the WebSocket protocol).

## Contributing
### Getting Started
If you don't have Visual Studio already, download [Visual Studio 2022 Community](https://visualstudio.microsoft.com/fr/vs/preview/). When prompted in the Visual Studio Installer, make sure to select at least "Desktop .NET Development."

You must also download the [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0) (currently in preview).

This project uses custom NuGet packages via GitHub's NuGet package repository. To set it up, first [generate a personal access token (PAT) with at least the `read:packages` permission](https://github.com/settings/tokens/new?scopes=read:packages&description=NuGet%20(read-only)). Then, simply run the following command, swapping `github-username` and `github-token` for your actual username and the token you just generated:
```bash
dotnet nuget add source --username github-username --password github-token --name 3DCloud "https://nuget.pkg.github.com/3DCloud/index.json"
```
Note that you may need to use the `--store-password-in-clear-text` flag depending on your platform.

You should then be able to open the `Print3DCloud.sln` solution in Visual Studio and build the project. Before running, create a file called `config.json` in the build output folder with the following contents:

```json
{
  "ServerHost": "ip address/domain name and port of the server"
}
```

### Generating test coverage locally
The `generate-coverage-report.ps1` PowerShell script can be used to generate an HTML coverage report locally. You can download PowerShell (cross-platform) [from the latest GitHub release](https://github.com/PowerShell/PowerShell/releases/latest). The script will install [ReportGenerator](https://github.com/danielpalme/ReportGenerator) as a global .NET tool if you do not have it already.

To use it, simply open a PowerShell prompt at the root directory of the repository and run

```
./generate-coverage-report.ps1
```

An HTML report will be created in a folder called `coverage`.