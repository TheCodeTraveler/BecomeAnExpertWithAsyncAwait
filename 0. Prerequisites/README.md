# 0. Install Prerequisites

Complete this setup before the workshop starts. The goal is to prove that your machine can build the samples, not just that the tools appear to be installed.

## 1. Install an IDE

Use one of these options:

1. Windows: install [Visual Studio 2026](https://visualstudio.microsoft.com/downloads/) or later.
2. macOS: install [JetBrains Rider](https://www.jetbrains.com/rider/download/#section=mac) or another IDE that supports .NET 10 and .NET MAUI.
3. Locked-down laptop: use any approved IDE or editor your company allows, then use the command-line steps below for verification.

## 2. Get the Workshop Files

Use Git if it is available:

```console
git clone https://github.com/TheCodeTraveler/BecomeAnExpertWithAsyncAwait.git
cd BecomeAnExpertWithAsyncAwait
```

If Git is blocked by your company policy, download the repository as a ZIP file from GitHub, extract it, and open a terminal in the extracted folder.

## 3. Install .NET 10

Use one of these installation paths.

### Visual Studio Installer on Windows

1. Open **Visual Studio Installer**.
2. Select **Modify** on your Visual Studio installation.
3. Install the **.NET desktop development** workload.
4. Install the **Mobile development with .NET** workload if you plan to build the MAUI samples from the IDE.
5. Apply the changes and restart Visual Studio.

### Manual Installer on Windows or macOS

1. Open the [.NET 10 download page](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Download the latest .NET 10 SDK installer for your operating system and CPU architecture.
3. Run the installer.
4. Open a new terminal after the installer completes.

### Command-Line Install Without Admin Rights

Use this path if your company does not allow system-wide installers.

Windows PowerShell:

```powershell
$installDir = "$env:USERPROFILE\.dotnet"
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile dotnet-install.ps1
.\dotnet-install.ps1 -Channel 10.0 -InstallDir $installDir
$env:PATH = "$installDir;$installDir\tools;$env:PATH"
dotnet --version
```

macOS Terminal:

```console
mkdir -p "$HOME/.dotnet"
curl -L https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
dotnet --version
```

If this works, add the same PATH update to your shell profile so future terminals can find `dotnet`.

## 4. Install the .NET MAUI Workload

The HackerNews samples use .NET MAUI. Install or update the MAUI workload after .NET 10 is available.

Windows Command Prompt or PowerShell:

```console
dotnet workload install maui
dotnet workload update
```

macOS Terminal:

```console
sudo dotnet workload install maui
sudo dotnet workload update
```

If you installed .NET into your user profile without admin rights, try the same commands without `sudo`.

## 5. Verify the SDK and Workloads

From the repository root, run:

```console
dotnet --version
dotnet --list-sdks
dotnet workload list
```

You should see a .NET 10 SDK and installed MAUI workloads.

## 6. Build the Console Samples

These samples do not require device emulators, so they are the quickest environment check:

```console
dotnet build "3. Creating Custom Implementation of Task/1. Start/CreatingTaskFromScratch.slnx"
dotnet build "4. .NET Internals/3. ExecutionContext/ExecutionContextExample.slnx"
```

Both builds should succeed.

## 7. Build and Run the HackerNews Sample

Open **2. Correcting Common Async Await Mistakes/2. Finish/HackerNews.slnx** in your IDE.

On Windows:

1. Select the Windows target framework.
2. Start debugging.
3. Confirm that the **HackerNews** app launches.

On macOS:

1. Install Rider Android Support if Rider prompts for it.
2. Select an Android emulator, Mac Catalyst, or iOS target that is available on your machine.
3. Start debugging.
4. Confirm that the **HackerNews** app launches.

If your organization blocks emulator installation, use any approved physical device or ask your instructor which shared target to use during the workshop.

## 8. Setup Verification Challenge

Recommended time: 20 to 30 minutes.

> **Note:** Please avoid letting AI Agents solve the challenges for you. You're smart. You got this. Use AI Agents to interpret setup errors or understand what a command is checking, but run the verification steps yourself and record the actual result from your machine.

Before continuing to the workshop exercises, prove your machine is ready:

1. Open a fresh terminal.
2. Navigate to the workshop repository root.
3. Run `dotnet --version` and confirm it reports .NET 10.
4. Run `dotnet workload list` and confirm the MAUI workload appears.
5. Build the two console samples from step 6.
6. Build or run the HackerNews sample from step 7.
7. Write down any blocker, exact error message, operating system, IDE, and install path you used.

If every check passes, you are ready for the workshop.

## 9. Troubleshooting

If `dotnet` is not found, close and reopen your terminal. If it still fails, confirm your .NET install folder is on PATH.

If your installed SDK is not .NET 10, install the latest .NET 10 SDK and rerun `dotnet --version` from a new terminal.

If `dotnet workload install maui` fails on a locked-down laptop, use the user-local .NET install path first. If workload installation is still blocked, contact your IT team before the workshop and include the exact workload command and error message.

If Android emulator setup is blocked, you can still complete the console and web internals samples. You will need an approved Windows, Android, iOS, or Mac Catalyst target to run the MAUI HackerNews samples yourself.

If the repository path contains unusual characters or is inside a synchronized folder, move it to a simple local path such as `C:\src\BecomeAnExpertWithAsyncAwait` on Windows or `~/src/BecomeAnExpertWithAsyncAwait` on macOS.
