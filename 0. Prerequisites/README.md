# 0. Install Prerequisites

This workshop is designed to run on Windows or macOS with no mobile workloads, no emulators, and no platform SDKs. The UI samples are Blazor Web Apps and run in a browser.

By the end of this setup, every attendee should be able to run these commands from the repository root:

```console
dotnet --version
dotnet build "2. Correcting Common Async Await Mistakes/2. Finish/HackerNews.slnx"
dotnet run --project "2. Correcting Common Async Await Mistakes/2. Finish/HackerNews/HackerNews.csproj"
```

## 1. Install or Open an Editor

Use any editor that can open C# files:

* [Visual Studio](https://visualstudio.microsoft.com/downloads/) on Windows
* [JetBrains Rider](https://www.jetbrains.com/rider/download/) on Windows or macOS
* [Visual Studio Code](https://code.visualstudio.com/) with the [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) extension

If your work laptop blocks installing IDEs, you can still complete the workshop from a terminal and any editor that is already approved by your organization.

## 2. Install .NET 10 SDK

You need the .NET SDK, not just the runtime. Choose one install path below.

After installing, open a new terminal and verify:

```console
dotnet --list-sdks
```

Confirm that the output includes `10.0.100` or later. If `dotnet` is not found, close and reopen your terminal. If it is still not found, use the no-admin install steps below and run the PATH commands shown there.

## 2a. Windows: Install with Visual Studio Installer

Use this path if you already use Visual Studio or your company manages installs through Visual Studio Installer.

1. Open **Visual Studio Installer**.
2. Select **Modify** on your Visual Studio installation.
3. Select the **ASP.NET and web development** workload.
4. Open **Individual components** and confirm that **.NET 10 SDK** is selected.
5. Select **Modify** or **Install**.
6. Open a new PowerShell window and run:

```powershell
dotnet --list-sdks
```

## 2b. Windows: Install with the .NET SDK Installer

Use this path if you can download and run installers.

1. Open [https://dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Download the **SDK** installer for Windows x64 or Windows Arm64, matching your laptop.
3. Run the installer.
4. Open a new PowerShell window and run:

```powershell
dotnet --list-sdks
```

## 2c. Windows: Install from the Command Line

Use this path if your organization allows command-line package installation.

```powershell
winget install Microsoft.DotNet.SDK.10
```

Then open a new PowerShell window and verify:

```powershell
dotnet --list-sdks
```

## 2d. Windows: No-Admin or Locked-Down Install

Use this path if installers require admin approval or are blocked by IT. It installs .NET into your user profile.

```powershell
mkdir $env:USERPROFILE\.dotnet -Force
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $env:TEMP\dotnet-install.ps1
powershell -ExecutionPolicy Bypass -File $env:TEMP\dotnet-install.ps1 -Channel 10.0 -InstallDir $env:USERPROFILE\.dotnet
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:DOTNET_ROOT\tools;$env:PATH"
dotnet --list-sdks
```

Those PATH changes apply to the current PowerShell window. If IT blocks changing your permanent user PATH, run the `$env:DOTNET_ROOT` and `$env:PATH` lines again each time you open a new terminal for the workshop.

## 2e. macOS: Install with the .NET SDK Installer

Use this path if you can install `.pkg` files.

1. Open [https://dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Download the **SDK** installer for macOS Arm64 on Apple Silicon, or macOS x64 on Intel.
3. Run the installer.
4. Open a new Terminal window and run:

```console
dotnet --list-sdks
```

## 2f. macOS: Install from the Command Line

Use this path if your organization allows Homebrew.

```console
brew install --cask dotnet-sdk
dotnet --list-sdks
```

If Homebrew is not approved, use the no-admin install steps below.

## 2g. macOS: No-Admin or Locked-Down Install

Use this path if `.pkg` installers require admin approval or are blocked by IT. It installs .NET into your home folder.

```console
mkdir -p "$HOME/.dotnet"
curl -L https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
chmod +x /tmp/dotnet-install.sh
/tmp/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
dotnet --list-sdks
```

Those PATH changes apply to the current Terminal window. If IT blocks changing your shell profile, run the `export DOTNET_ROOT=...` and `export PATH=...` lines again each time you open a new terminal for the workshop.

## 3. Get the Workshop Code

If Git is installed, clone the repository:

```console
git clone https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait.git
cd BecomeAnExpertWithAsyncAwait
```

If Git is blocked or not installed:

1. Open the repository in a browser.
2. Select **Code**.
3. Select **Download ZIP**.
4. Extract the ZIP file.
5. Open a terminal in the extracted **BecomeAnExpertWithAsyncAwait** folder.

## 4. Verify the Workshop Build

From the repository root, run:

```console
dotnet build "2. Correcting Common Async Await Mistakes/2. Finish/HackerNews.slnx"
```

The build should end with `Build succeeded`.

Then run the Blazor sample:

```console
dotnet run --project "2. Correcting Common Async Await Mistakes/2. Finish/HackerNews/HackerNews.csproj"
```

Open [http://localhost:5002](http://localhost:5002). Confirm that the **HackerNews** Blazor app loads and displays top stories.

To stop the app, return to the terminal and press **Ctrl+C**.

## 5. Setup Verification Challenge

Recommended time: 20 to 30 minutes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use AI Agents to interpret setup errors or understand what a command is checking, but run the verification steps yourself and record the actual result from your machine.

Before continuing to the workshop exercises, prove your machine is ready:

1. Open a fresh terminal.
2. Navigate to the workshop repository root.
3. Run `dotnet --version` and confirm it reports .NET 10.
4. Build the completed HackerNews sample from step 4.
5. Run the completed HackerNews sample from step 4.
6. Open the local URL and confirm that the app displays top stories.
7. Write down any blocker, exact error message, operating system, editor, and install path you used.

If every check passes, you are ready for the workshop.

## 6. Troubleshooting Checklist

If `dotnet` is not found:

* Close and reopen the terminal.
* Run `dotnet --list-sdks` again.
* If you used the no-admin install, rerun the PATH commands from that section in the same terminal window.

If the build says the .NET SDK is missing:

* Run `dotnet --list-sdks`.
* Confirm that `10.0.100` or later is listed.
* Confirm that you installed the SDK, not only the runtime.

If `localhost:5002` does not load:

* Confirm the `dotnet run` command is still running.
* Read the terminal output and open the URL it prints.
* If port `5002` is already in use, run:

```console
dotnet run --project "2. Correcting Common Async Await Mistakes/2. Finish/HackerNews/HackerNews.csproj" --urls http://localhost:5012
```

Then open [http://localhost:5012](http://localhost:5012).

If your company blocks access to Hacker News:

* The app can still build.
* The live story list may show a refresh error until you are on a network that allows access to `https://hacker-news.firebaseio.com`.

## 7. Maintainer Build Check

The repository includes a script that formats and builds every solution:

```powershell
./BuildAllSolutions.ps1
```

On macOS or Linux, run it with PowerShell 7:

```console
pwsh ./BuildAllSolutions.ps1
```

If PowerShell 7 is not installed, you can still participate in the workshop. The maintainer script is only needed to validate every solution in the repository at once.
