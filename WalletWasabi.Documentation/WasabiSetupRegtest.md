# Wasabi Setup on RegTest

## Why do this?

RegTest is a local testing environment in which developers can almost instantly generate blocks on demand for testing events, and create private satoshis with no real-world value. Running Wasabi Backend on RegTest allows you to emulate network events and observe how the Backend and the Client react on that.
You do not need to download the blockchain for this setup!

## Setup Bitcoin Knots with RegTest

Currently, the latest version(25.1) of Bitcoin Knots is not tested, so the preferred version is [23.0](https://github.com/bitcoinknots/bitcoin/releases/tag/v23.0.knots20220529).

Todo:

1. Install [Bitcoin Knots 23.0](https://bitcoinknots.org/files/23.x/23.0.knots20220529/) on your computer. Verify the PGP signatures. Check the security notice [here](https://bitcoinknots.org/).
2. Start Bitcoin Knots with: `bitcoin-qt.exe -regtest` then quit immediately. In this way the data directory and the config files will be generated. If you use the `-datadir` parameter, make sure the directory exists.

     Windows x64:
    ```
    "C:\Program Files\Bitcoin\bitcoin-qt.exe" -regtest -blockfilterindex -txindex -datadir=c:\Bitcoin
    ```        
    macOS: 
    ```
    "/Applications/Bitcoin-Qt.app/Contents/MacOS/Bitcoin-Qt" -regtest -blockfilterindex -txindex -datadir=$HOME/Library/Application Support/Bitcoin"
    ```
    Linux: 
    ```
     ~/bitcoin-[version number]/bin/bitcoin-qt -regtest -blockfilterindex -txindex -datadir=$HOME/.bitcoin/
    ```
4. Go to Bitcoin Knots data directory. If the directory is missing run core bitcoin-qt, then quit immediately. In this way the data directory and the config files will be generated.
    
    There may be differences if you used the "-datadir" parameter before.
    
    Defaults:
    Windows
    ```
    %APPDATA%\Bitcoin\
    ```
    macOS
    ```
    $HOME/Library/Application Support/Bitcoin/
    ```
    Linux
    ```
    $HOME/.bitcoin/
    ```
4. Add a file called **bitcoin.conf** and add these lines:
    ```C#
    regtest.server = 1
    regtest.listen = 1
    regtest.txindex = 1
    regtest.whitebind = 127.0.0.1:18444
    regtest.rpchost = 127.0.0.1
    regtest.rpcport = 18443
    regtest.rpcuser = 7c9b6473600fbc9be1120ae79f1622f42c32e5c78d
    regtest.rpcpassword = 309bc9961d01f388aed28b630ae834379296a8c8e3
    regtest.disablewallet = 0
    regtest.softwareexpiry = 0
    regtest.listenonion = 0
    ```
5. Save it.
6. Start Bitcoin Knots with: bitcoin-qt.exe -regtest.
7. Do not worry about "Syncing Headers" just press the Hide button. Because you run on Regtest, no Mainnet blocks will be downloaded.
8. Go to menu *File / Create* wallet and create a wallet with the name you prefer. Use the default options.
9. Go to menu *Window / Console*.
10. Generate a new address with:
`getnewaddress`
11. Generate the first 101 blocks with:
`generatetoaddress 101 <replace_new_address_here>`
12. Now you have your own Bitcoin blockchain and you are a God there - try to resist the insurmountable temptation to start your own shit coin, remember there is only one true coin. You can create transactions with the Send button and confirm with:
`generatetoaddress 1 <replace_new_address_here>`

You can force rebuilding the txindex with the `-reindex` command line argument.

## Setup Wasabi Backend

Here you will have to build from source, follow [these instructions here](https://github.com/zkSNACKs/WalletWasabi#build-from-source-code).

Todo:
1. Go to `WalletWasabi\WalletWasabi.Backend` folder.
2. Open the command line and enter:
`dotnet run`
3. You will get some errors, but the data directory will be created. Stop the backend if it is still running with CTRL-C.
4. Go to the Backend folder:
    ```
    Windows: "C:\Users\{your username}\AppData\Roaming\WalletWasabi\Backend"
    macOS: "/Users/{your username}/.walletwasabi/backend"
    Linux: "/home/{your username}/.walletwasabi/backend"
    ```
5. Edit `Config.json` file by replacing everything with:
    ```json
    {
      "Network": "RegTest",
      "BitcoinRpcConnectionString": "7c9b6473600fbc9be1120ae79f1622f42c32e5c78d:309bc9961d01f388aed28b630ae834379296a8c8e3",
      "MainNetBitcoinP2pEndPoint": "127.0.0.1:8333",
      "TestNetBitcoinP2pEndPoint": "127.0.0.1:18333",
      "RegTestBitcoinP2pEndPoint": "127.0.0.1:18444",
      "MainNetBitcoinCoreRpcEndPoint": "127.0.0.1:8332",
      "TestNetBitcoinCoreRpcEndPoint": "127.0.0.1:18332",
      "RegTestBitcoinCoreRpcEndPoint": "127.0.0.1:18443"
    }
    ```
6. Edit some lines in `WabiSabiConfig.json`. For example, make the `InputRegistrationPhase` faster and allow rounds to have between 2 and 100 inputs:
    ```
    "StandardInputRegistrationTimeout": "0d 0h 2m 0s",
    "MaxInputCountByRound": 100,
    "MinInputCountByRoundMultiplier": 0.02,
    ```
7. Start Bitcoin Knots in RegTest (command to run is explained above).
8. Go to WalletWasabi folder
9. Open the command line and enter. This will build all the projects under this directory. 
`dotnet build`
10. Go to WalletWasabi\WalletWasabi.Backend folder.
`dotnet run --no-build`
11. Now the Backend is generating the filters and it is running. (You can quit with CTRL-C any time)

## Setup Wasabi Client

Todo:

1. Go to `WalletWasabi\WalletWasabi.Fluent.Desktop` folder.
2. Open the command line and run the Wasabi Client with:
`dotnet run --no-build`
3. Go to Settings/Bitcoin and set the network to RegTest
4. Close Wasabi and restart it with:
`dotnet run --no-build`
5. Generate a wallet in Wasabi named: R1.
6. Generate a receive address in Wasabi, now go to Bitcoin Knots to the Send tab.
7. Send BTC 1 to that address.
8. Generate a wallet in Wasabi named: R2.
9. Generate a receive address in Wasabi, now go to Bitcoin Knots to the Send tab.
10. Send BTC 1 to that address.
11. Now let the coinjoin happen automatically in both wallets.
12. If you see `Waiting for confirmed funds` in the music box you can generate a block in Bitcoin Knots to continue coinjoining.
    - You can do it with the console command `generatetoaddress 1 <replace_with_your_address_here>`

Happy coinjoin!
