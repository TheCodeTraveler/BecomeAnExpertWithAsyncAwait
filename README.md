# Become An Expert With Async Await in C#

Outline
* [0. Install Prerequisites](#0-install-prerequisites)
* 1. Presentation
    * Teach thread switching
    * Show compiled code using Sharplab.io
    * (Basically, the first part of Correcting Common Mistakes talk)
* 2. Code
    * Use asyncawaitbestpractices example
* 3. Creating Custum Immplementation of Task
* 4. .NET Internals Presentation
* 5. .NET Internals Code

## 0. Install Prerequisites

This workshop requires the following tools:
* Visual Studio (Windows) or Jet Brains Rider (macOS)
* .NET 10
* .NET MAUI

### 1a. Install IDE (Windows)
1. On a Windows PC, open a browser and navigate to [https://visualstudio.microsoft.com/downloads/](https://visualstudio.microsoft.com/downloads/)
2. In the browser, download Visual Studio Community (or install Professional/Enterprise if you have a license)

### 1b. Install IDE (macOS)
1. On a Mac, open a browser and navigate to [https://www.jetbrains.com/rider/download/?section=mac](https://www.jetbrains.com/rider/download/?section=mac)
2. In the browser, download Jet Brains Rider

### 2a. Update IDE (Winodws)
1. On a Windows PC, press the Winodws button to launch the Start Menu
2. In the Start Menu, at the top, locate the Search Bar
3. In the Search Bar, type `Visual Studio Installer`
4. In the Start Menu, in the search results, open the app by selecting **Visual Studio Installer**
<img width="770" height="263" alt="vs-installer" src="https://github.com/user-attachments/assets/35717cbc-ea79-42de-9589-d313273c1dc5" />

5. In the **Visual Studio Installer** app, select the **Update All** button
> **Note**: If the **Update All** button does not appear, Visual Studio is up-to-date. You may continue to Step 3
 <img width="2808" height="999" alt="Picture1" src="https://github.com/user-attachments/assets/c2dc0051-a09a-4ca3-b530-0ee9f23c9998" />

6. Stand by until Visual Studio has finished updating

### 3. Install the Latest Version of .NET 10
1. Open a browser and nvigate to [https://dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0)
2. In the browser, locate the `Installers` column for the latest release of .NET:
<img width="459" height="319" alt="Screenshot 2026-01-19 at 12 19 16 PM" src="https://github.com/user-attachments/assets/babbbb15-f801-48ae-b431-8e20eb4b2911" />

3. Under the `Installers` column, click the link that corresponds to your computer's Operating System and CPU Architecture
4. Stand by while the `dotnet-sdk` downlods
5. Once the `dotnet-sdk` download has completed, launch the downloaded file
6. In the `Install Microsoft .NET` window, click continue and follow the prompts until the installation has successfully completd
<img width="732" height="560" alt="Screenshot 2026-01-19 at 12 23 45 PM" src="https://github.com/user-attachments/assets/99ca5ce7-3c4d-4628-97e5-d3e2d244b283" />

### 4a. Install the Latest Version of .NET MAUI (Windows)
1. Open the [Command Prompt](https://learn.microsoft.com/answers/questions/5637237/how-to-open-command-prompt-in-windows-11)
2. In the Command Prompt, type `dotnet workload install maui && dotnet workload update`
3. On the Windows Command Prompt, hit **Enter**
4. Stand by while the latest version of .NET MAUI is being installed

### 4b. Install the Latest Version of .NET MAUI (macOS)
1. On macOS, open the [Terminal](https://support.apple.com/guide/terminal/open-or-quit-terminal-apd5265185d-f365-44cb-8b09-71a064a42125/mac)
2. In the Terminal, type `sudo dotnet workload install maui; sudo dotnet workload update`
3. In the Terminal, hit **Enter**
4. In the Terminal, enter your macOS login Password
5. Stand by while the latest version of .NET MAUI is being installed


