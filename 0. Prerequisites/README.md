# 0. Install Prerequisites

This workshop requires the following tools:

* Visual Studio (Windows) or Jet Brains Rider (macOS)
* .NET 10
* .NET MAUI

## 1a. Install IDE (Windows)

1. On a Windows PC, open a browser and navigate to [https://visualstudio.microsoft.com/downloads/](https://visualstudio.microsoft.com/downloads/)
2. In the browser, download Visual Studio Community (or install Professional/Enterprise if you have a license)

## 1b. Install IDE (macOS)

1. On a Mac, open a browser and navigate to [https://www.jetbrains.com/rider/download/?section=mac](https://www.jetbrains.com/rider/download/?section=mac)
2. In the browser, download Jet Brains Rider

## 2a. Update IDE (Winodws)

1. On a Windows PC, press the Winodws button to launch the Start Menu
2. In the Start Menu, at the top, locate the Search Bar
3. In the Search Bar, type `Visual Studio Installer`
4. In the Start Menu, in the search results, open the app by selecting **Visual Studio Installer**
![Visual Studio Installer](https://github.com/user-attachments/assets/35717cbc-ea79-42de-9589-d313273c1dc5)

5. In the **Visual Studio Installer** app, select the **Update All** button

> **Note**: If the **Update All** button does not appear, Visual Studio is up-to-date. You may continue to Step 3
 ![Update All](https://github.com/user-attachments/assets/c2dc0051-a09a-4ca3-b530-0ee9f23c9998)

1. Stand by until Visual Studio has finished updating

## 3. Install the Latest Version of .NET 10

1. Open a browser and nvigate to [https://dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0)
2. In the browser, locate the `Installers` column for the latest release of .NET:
![Install .NET](https://github.com/user-attachments/assets/babbbb15-f801-48ae-b431-8e20eb4b2911)

3. Under the `Installers` column, click the link that corresponds to your computer's Operating System and CPU Architecture
4. Stand by while the `dotnet-sdk` downlods
5. Once the `dotnet-sdk` download has completed, launch the downloaded file
6. In the `Install Microsoft .NET` window, click continue and follow the prompts until the installation has successfully completd
![Install .NET](https://github.com/user-attachments/assets/99ca5ce7-3c4d-4628-97e5-d3e2d244b283)

## 4a. Install the Latest Version of .NET MAUI (Windows)

1. Open the [Command Prompt](https://learn.microsoft.com/answers/questions/5637237/how-to-open-command-prompt-in-windows-11)
2. In the Command Prompt, type `dotnet workload install maui && dotnet workload update`
3. On the Windows Command Prompt, hit **Enter**
4. Stand by while the latest version of .NET MAUI is being installed

## 4b. Install the Latest Version of .NET MAUI (macOS)

1. On macOS, open the [Terminal](https://support.apple.com/guide/terminal/open-or-quit-terminal-apd5265185d-f365-44cb-8b09-71a064a42125/mac)
2. In the Terminal, type `sudo dotnet workload install maui; sudo dotnet workload update`
3. In the Terminal, hit **Enter**
4. In the Terminal, enter your macOS login Password
5. Stand by while the latest version of .NET MAUI is being installed

## 5. Ensure .NET MAUI App Builds + Runs Successfully

1. Using File Explorer (Windows) / Finder (macOS), navigate to **BecomeAnExpertWithAsyncAwait/2. Correcting Common Async Await Mistakes/2. Finish**
2. In the **2. Finish** folder, open **HackerNews.slnx** in your IDE (Visual Studio on Windows or Jet Brains Rider on macOS)

<img width="1032" height="674" alt="Screenshot 2026-01-22 at 2 51 36 PM" src="https://github.com/user-attachments/assets/8a714e01-2bb6-40cc-83ff-5b389566499b" />

### 5a. Build/Run the App on Windows (Windows)

1. In **Visual Studio**, in the toolbar, select the **HackerNews** dropdown menu
2. In the **HackerNews** dropdown menu, select **Framework (net10.0-windows10.0.19041.0)**
3. In the **Framework (net10.0-windows10.0.19041.0)** menu, select **net10.0-windows10.0.19041.0**
<img width="1116" height="242" alt="Screenshot 2026-01-25 at 4 15 44 PM" src="https://github.com/user-attachments/assets/113d87d8-a22c-42a6-83b8-afe9071e8830" />

4. In **Visual Studio**, select the play button next the the **HackerNews Drop Down menu** to build + run the app
<img width="663" height="64" alt="Screenshot 2026-01-25 at 4 18 51 PM" src="https://github.com/user-attachments/assets/c01c5f7e-019b-4f26-b993-c6902339fcb4" />

5. Verify that the **HackerNews** app launches on Windows

<img width="1291" height="762" alt="Screenshot 2026-01-25 at 4 39 38 PM" src="https://github.com/user-attachments/assets/8d9b59da-0d18-48d4-8cb8-3ecc890b94fc" />


### 5b. Build/Run the App on Android (macOS)

1. In **Jet Brains Rider**, using the macOS Menu Bar, navigate to **JetBrains Rider -> Settings**

<img width="376" height="302" alt="image" src="https://github.com/user-attachments/assets/f06c3819-fe72-46dc-bd7c-cf9fd38d75a7" />

2. In the Jet Brains Rider **Settings Menu**, on the left-hand menu, select **Plugins**
3. In the **Plugins** window, at the top of the window, select **Marketplace**

<img width="1462" height="1162" alt="Screenshot 2026-01-22 at 3 11 56 PM" src="https://github.com/user-attachments/assets/662c025c-8559-4b1f-ab42-59d706e10f97" />

4. In the **Plugins** window, in the **search bar**, type `Rider Android Support`
5. In the **Plugins** window, in the search results, locate the **Rider Android Support** plugin
6. On the **Rider Android Support** plugin, click **Install**

> **Note:** If **Rider Android Support** is already installed, skip this step

<img width="1462" height="1162" alt="Screenshot 2026-01-22 at 3 14 54 PM" src="https://github.com/user-attachments/assets/ee5fe44f-f405-423a-a9fa-e43477e539f1" />

7. Stand by while the **Rider Android Support** plugin is installed
8. After the **Rider Android Support** has installed, click **Restart IDE**
9. Stand by until Jet Brains Rider restarts
10. After Jet Brains Rider has restarted, open **HackerNews.slnx**
11. In Jet Brains Rider, on the top-right corner of the toolbar, click the **HackerNews** startup project drop-down menu

<img width="633" height="280" alt="image" src="https://github.com/user-attachments/assets/7ef7075c-978a-49f7-b0b8-b09913cec8b0" />

12. In the **HackerNews** startup project drop-down menu, select the Android icon

> **Note**: Alternatively, you may select the macOS or iOS icon if you have [Xcode](https://developer.apple.com/xcode/) installed

13. In Jet Brains Rider, on the top-center of the toolbar, click the Android Device drop-down menu

<img width="1212" height="357" alt="image" src="https://github.com/user-attachments/assets/dc4463ce-5e88-4741-ba66-9683f8a2dfe7" />

14. In the Android device drop-down menu, select an Android simulator targeting Android API 25 or higher

15. In Jet Brains Rider, on the top-right corner of the toolbar, click **Debug**

<img width="417" height="242" alt="image" src="https://github.com/user-attachments/assets/b1af904a-5190-4513-8277-beaa5a1d9592" />

16. Confirm the app succesfully builds, launches, and runs

<img width="737" height="1083" alt="image" src="https://github.com/user-attachments/assets/2d07f935-055e-4e21-b9aa-fb1c54fc2558" />
