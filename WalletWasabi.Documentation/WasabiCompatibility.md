# Abstract

This document lists all the officially supported software and devices by Wasabi Wallet. This means that Wasabi is tested on those systems, and we put all the efforts to make it work and maintain compatibility. One of our main goals is to not break the user-space, so we have to set up boundaries that we can responsibly maintain. This does not necessarily mean that systems that are not listed will not work - they might work, but we do not officially support them. There are a lot of potentially supported systems out there and more to come, but we can only promise support and stability on platforms that our dependencies support, too.

# Officially Supported Operating Systems

- Windows 10 1607+
- Windows 11 22000+
- macOS 12.0+
- Ubuntu 20.04+
- Fedora 37+
- Debian 11+

# Officially Supported Hardware Wallets

- BitBox02-BtcOnly<sup><sup>1*</sup></sup>
- Blockstream Jade
- ColdCard MK1
- ColdCard MK2
- ColdCard MK3
- ColdCard MK4
- Ledger Nano S
- Ledger Nano S Plus
- Ledger Nano X
- Trezor Model T
- Trezor Safe 3

<sup><sup>1*</sup> The device by default asks for a "Pairing code", currently, there is no such function in Wasabi. Therefore, either disable the feature or unlock the device with BitBoxApp or hwi-qt before using it with Wasabi.</sup>

# Officially Supported Architectures

- x64 (Windows, Linux, macOS)
- arm64 (macOS)

# FAQ

## What are the bottlenecks of officially supporting Operating Systems?

Wasabi dependencies are:
- .NET 8.0 [reqs](https://github.com/dotnet/core/blob/main/release-notes/8.0/supported-os.md).
- Avalonia [reqs](https://github.com/AvaloniaUI/Avalonia/wiki/Runtime-Requirements).
- NBitcoin dependencies and requirements are the same as .NET 8.0.
- Bitcoin Knots (same requirements as Bitcoin Core) [reqs](https://bitcoin.org/en/bitcoin-core/features/requirements#system-requirements).

## What are the bottlenecks of officially supporting Hardware Wallets?

Wasabi dependencies are:
- [HWI](https://github.com/bitcoin-core/HWI), check the [device support](https://github.com/bitcoin-core/HWI#device-support) list there. Some hardware wallets supported by HWI are still not compatible with Wasabi Wallet because they implemented custom workflows.

## What about Tails and Whonix?

It is currently not possible to use Wasabi on Tails or Whonix.
That is because Wasabi uses the Tor control port, which is not exposed in these operating systems.
