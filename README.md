<p align="center">
  <a href="https://wasabiwallet.io">
    <img src="https://user-images.githubusercontent.com/51679301/169354261-c894bac0-27f2-4a29-8470-a7519963a4b5.jpg">
  </a>
</p>

<h3 align="center">
    An open-source, non-custodial, privacy-focused Bitcoin wallet for desktop.
</h3>

<h3 align="center">
  <a href="https://wasabiwallet.io">
    Website
  </a>
  <span> | </span>
  <a href="https://docs.wasabiwallet.io/">
    Documentation
  </a>
  <span> | </span>
  <a href="https://wasabiwallet.io/swagger/index.html">
    API
  </a>
  <span> | </span>
  <a href="https://github.com/zkSNACKs/WalletWasabi/discussions/5185">
    Support
  </a>
  <span> | </span>
  <a href="https://www.youtube.com/c/WasabiWallet">
    YouTube
  </a>
  <span> | </span>
  <a href="https://github.com/zkSNACKs/WalletWasabi/blob/master/PGP.txt">
    PGP
  </a>
</h3>

<br>
<br>

| Code Quality           | Windows Tests           | Linux Tests             | macOS Tests             | Continuous Delivery       | Deterministic builds      | License                   |
| :----------------------| :-----------------------| :-----------------------| :-----------------------| :-------------------------| :-------------------------| :-------------------------|
| [![CodeFactor][9]][10] | [![Build Status][1]][2] | [![Build Status][3]][4] | [![Build Status][5]][6] | [![Build Status][11]][12] | [![Build Status][13]][14] | [![GitHub license][7]][8] |

[1]: https://dev.azure.com/zkSNACKs/Wasabi/_apis/build/status/Wasabi.Windows?branchName=master
[2]: https://dev.azure.com/zkSNACKs/Wasabi/_build?definitionId=3
[3]: https://dev.azure.com/zkSNACKs/Wasabi/_apis/build/status/Wasabi.Linux?branchName=master
[4]: https://dev.azure.com/zkSNACKs/Wasabi/_build?definitionId=1
[5]: https://dev.azure.com/zkSNACKs/Wasabi/_apis/build/status/Wasabi.Osx?branchName=master
[6]: https://dev.azure.com/zkSNACKs/Wasabi/_build?definitionId=2
[7]: https://img.shields.io/github/license/zkSNACKs/WalletWasabi.svg
[8]: https://github.com/zkSNACKs/WalletWasabi/blob/master/LICENSE.md
[9]: https://www.codefactor.io/repository/github/zksnacks/walletwasabi/badge
[10]: https://www.codefactor.io/repository/github/zksnacks/walletwasabi
[11]: https://dev.azure.com/zkSNACKs/Wasabi/_apis/build/status/Wasabi.ContinuousDelivery?branchName=master
[12]: https://dev.azure.com/zkSNACKs/Wasabi/_build/latest?definitionId=12&branchName=master
[13]: https://dev.azure.com/zkSNACKs/Wasabi/_apis/build/status/Wasabi.DeterministicBuild?branchName=master
[14]: https://dev.azure.com/zkSNACKs/Wasabi/_build/latest?definitionId=13&branchName=master

<br>

# [Download Wasabi](https://github.com/zkSNACKs/WalletWasabi/releases)

<br>

![](/ui-ww.png)

# Build From Source Code

### Get The Requirements

1. Get Git: https://git-scm.com/downloads
2. Get .NET 8.0 SDK: https://dotnet.microsoft.com/download
3. Optionally disable .NET's telemetry by executing in the terminal `export DOTNET_CLI_TELEMETRY_OPTOUT=1` on Linux and macOS or `setx DOTNET_CLI_TELEMETRY_OPTOUT 1` on Windows.

### Get Wasabi

Clone & Restore & Build

```sh
git clone --depth=1 --single-branch --branch=master https://github.com/zkSNACKs/WalletWasabi.git
cd WalletWasabi/WalletWasabi.Fluent.Desktop
dotnet build
```

### Run Wasabi

Run Wasabi with `dotnet run` from the `WalletWasabi.Fluent.Desktop` folder.

### Update Wasabi

```sh
git pull
```
