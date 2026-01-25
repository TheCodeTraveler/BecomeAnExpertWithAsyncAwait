# .NET Internals
In this section, we'll dive deep into the internal data structures that .NET uses to implement asynchronous programming

## 1. ThreadStatic

1. Using File Explorer (Windows) / Finder (macOS), navigate to **BecomeAnExpertWithAsyncAwait/4. .NET Internals/1. Thread Static/**
2. In the **1. Thread Static** folder, open **ThreadStaticExample.slnx** in your IDE (Visual Studio on Windows or Jet Brains Rider on macOS)

<img width="1071" height="667" alt="image" src="https://github.com/user-attachments/assets/387c6743-46f5-4814-9e0e-5c860c0b7402" />

2. In your IDE, open **Program.cs**
3. In **Program.cs**, note the **[ThreadStatic]** attribute on the field **static int _threadSpecificValue**
> **Note:** `[ThreadStatic]` will keep the value of **_threadSpecificValue** on its specific Thread

4. In **Program.cs**, note the local variables **Thread thread1** and **Thread thread2**
> **Note:** **thread1** and **thread2** will run **static void ThreadMethod()**, setting a thread-specific value to **_threadSpecificValue** when we call **Thread.Start()**
> **Note:** **Thread.Join()** blocks the calling Thread until it has completed

5. In your IDE, build + run **ThreadSpecificExample.csproj**
6. In your IDE, note the results in the **Console** output:
> Main thread - threadSpecificValue: 100
>
> Thread 5 - threadSpecificValue: 5 // A background thread with a different threadSpecificValue
>
> Thread 8 - threadSpecificValue: 47 // A second background thread with a second different threadSpecificValue
>
> Main thread - threadSpecificValue: 100 // Returning to the main thread to see it maintained its original threadSpecificValue

## 2. IPrincipal (aka "Security Context")

1. Using File Explorer (Windows) / Finder (macOS), navigate to **BecomeAnExpertWithAsyncAwait/4. .NET Internals/2. Principal/**
2. In the **2. Principal** folder, open **PrincipalExample.slnx** in your IDE (Visual Studio on Windows or Jet Brains Rider on macOS)

<img width="1080" height="678" alt="image" src="https://github.com/user-attachments/assets/b001faac-8a49-4f7c-b68b-b582031f4c16" />

3. In your IDE, open **Program.cs**
4. In **Program.cs**, note the Login Controller:

```cs
app.MapControllerRoute(
		name: "login",
		pattern: "{controller=Account}/{action=Login}/{id?}")
	.WithStaticAssets();
```

5. In your IDE, open **AccountController.cs**
6. In **AccountController.cs** set a Breakpoint on Line 24:
```cs
await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal).ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.None);`
```
> **Note:** We are using **.ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.None)** to ensure that we switch Threads and do not return to the calling Thread

7. In **AccountController.cs** set a Breakpoint on Line 26:
```cs
return RedirectToAction("Index", "Home");
```

8. In your IDE, Build + Debug **PrincipalExample.csproj**
> **Note:** Be sure to debug (not "run") the app, and be sure to use the Debug build configuration

9. In your browser, navigate to [http://localhost:5000/Account/Login](http://localhost:5000/Account/Login)
10. In your IDE, confirm the program pauses execution when it hits the Breakpoint on Line 24:
```cs
await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal).ConfigureAwait(ConfigureAwaitOptions.ForceYielding | ConfigureAwaitOptions.None);`
```
11. In your IDE, using the debugging tools, note the current Thread ID
12. In your IDE, using the debugging tools, note the Property values in **HttpContext**
13. In your IDE, using the debugging tools, note that the **User** object contains the two **Claims** we passed into our **ClaimsIdentity**
> **Warning:** Do not hard-code username and role **Claims** like this in production apps

14. In your IDE, using the debugging tools, resume execution of the program
15. In your IDE, confirm the code has hit the Breakpoint on Line 26:

```cs
return RedirectToAction("Index", "Home");
```

16. In your IDE, using the debugging tools, note the current Thread ID
> **Note:** The current Thread ID should be different than the Thread ID noted above in **Step 11**

17. In your IDE, using the debugging tools, note the Property values in **HttpContext**
> **Note:** The Property values in **HttpContext** should be the same as noted above on **Step 13**
> 
> **Warning:** In .NET Framework 4 and earlier, .NET did not preserve **HttpContext** when switching Threads

18. In your IDE, using the debugging tools, note that the **User** object contains the two **Claims** we passed into our **ClaimsIdentity**

## 3. ExecutionContext

1. Using File Explorer (Windows) / Finder (macOS), navigate to **BecomeAnExpertWithAsyncAwait/4. .NET Internals/3. ExecutionContext/**
2. In the **3. ExecutionContext** folder, open **ExecutionContextExample.slnx** in your IDE (Visual Studio on Windows or Jet Brains Rider on macOS)
<img width="1072" height="696" alt="image" src="https://github.com/user-attachments/assets/947cdc25-8015-407d-b665-509593686f67" />

3. In your IDE, open **Program.cs**
4. In **Program**, note the **AsyncLocal<T>** field on line 9
> **Note:** **AsyncLocal<T>** will pass along its value from Thread to Thread thanks to the **ExecutionContext** (unlike **[ThreadLocal]** which stays on the specific Thread)

5. In **Program**, set a Breakpoint on Line 22
6. In **Program**, set a Breakpoint on Line 33
7. In **Program**, set a Breakpoint on Line 37
8. In **Program**, set a Breakpoint on Line 50
9. In **Program**, set a Breakpoint on Line 54
10. In **Program**, set a Breakpoint on Line 63
11. In your IDE, Build + Debug **ExecutionContextExample.csproj**
> **Note:** Be sure to debug (not "run") the app, and be sure to use the Debug build configuration

12. In your IDE, confirm the program pauses execution when it hits the Breakpoint on Line 22
13. In your IDE, confirm the Console output:
> Thread ID: 1
>
> Culture: Spanish (Spain)
>
> Principal: System.Security.Claims.ClaimsPrincipal
>
> AsyncLocalData: Initial Value

14. In your IDE, using the debugging tools, resume execution of the program
15. In your IDE, confirm the code has hit the Breakpoint on Line 33
16. In your IDE, confirm the Console output:
> Thread ID: 6
>
> Culture: English (United Kingdom)
>
> Principal: ExecutionContext.CustomPrincipal
>
> AsyncLocalData: AsyncLocalData in Thread

17. In your IDE, using the debugging tools, resume execution of the program
18. In your IDE, confirm the code has hit the Breakpoint on Line 37
19. In your IDE, confirm the Console output:
> Thread ID: 6 // Same Background Thread as the previous breakpoint
>
> Culture: Spanish (Spain) // Same value as Thread 1 because we passed in captured **ExecutionContext**
>
> Principal: System.Security.Claims.ClaimsPrincipal // Same value as Thread 1 because we passed in captured **ExecutionContext**
>
> AsyncLocalData: Initial Value // Same value as Thread 1 because we passed in captured **ExecutionContext**

18. In your IDE, using the debugging tools, resume execution of the program
19. In your IDE, confirm the code has hit the Breakpoint on Line 50
20. In your IDE, confirm the Console output:
> Thread ID: 1
>
> Culture: Spanish (Spain)
>
> Principal: System.Security.Claims.ClaimsPrincipal
>
> AsyncLocalData: Initial Value

21. In your IDE, using the debugging tools, resume execution of the program
22. In your IDE, confirm the code has hit the Breakpoint on Line 54
23. In your IDE, confirm the Console output:
> Thread ID: 10 // Background thread
>
> Culture: Spanish (Spain) // Same value as Thread 1 because async/await automatically flows along the **ExecutionContext*
>
> Principal: System.Security.Claims.ClaimsPrincipal // Same value as Thread 1 because async/await automatically flows along the **ExecutionContext*
>
> AsyncLocalData: Initial Value // Same value as Thread 1 because async/await automatically flows along the **ExecutionContext*

24. In your IDE, using the debugging tools, resume execution of the program
25. In your IDE, confirm the code has hit the Breakpoint on Line 63
26. In your IDE, confirm the Console output:
> Thread ID: 6 // Background Thread
>
> Culture: English (United States) // Default value for your computer because we suppressed the flow of the **ExecutionContext**
>
> Principal: // Default value is empty because we suppressed the flow of the **ExecutionContext**
>
> AsyncLocalData: Initial Value // Default value is empty because we suppressed the flow of the **ExecutionContext**

## 4. SynchronizationContext

1. Using File Explorer (Windows) / Finder (macOS), navigate to **BecomeAnExpertWithAsyncAwait/4. .NET Internals/4. SynchronizationContext/**
2. In the **4. SynchronizationContext** folder, open **SynchronizationContext.slnx** in your IDE (Visual Studio on Windows or Jet Brains Rider on macOS)
3. In your IDE, open **NewsViewModel.cs**
4. In **NewsViewModel**, set a Breakpoint on Line 25
5. In **NewsViewModel**, set a Breakpoint on Line 35
6. In your IDE, Build + Debug **HackerNews.csproj**
> **Note:** Be sure to debug (not "run") the app, and be sure to use the Debug build configuration

7. In your IDE, confirm the program pauses execution when it hits the Breakpoint on Line 25
8. In your IDE, using the debugging tools, confirm **thread.ManagedThreadId** is **1**
9. In your IDE, using the debugging tools, confirm **thread._synchronizationContext** is not **null**
> **Note:** **thread._synchronizationContext** will be **UIKitSynchronizationContext** when running on iOS, **AndroidSynchronizationContext** when running on Android and **DispatcherQueueSynchronizationContext** when running on Windows

10. In your IDE, using the debugging tools, resume execution of the program
11. In your IDE, confirm the code has hit the Breakpoint on Line 35
12. In your IDE, using the debugging tools, confirm **threadAfterConfigureAwaitFalse._synchronizationContext** is **null**
> **Note:** **.ConfigureAwait(false)** and **.ConfigureAwait(ConfigureAwaitOptions.None)** set the **SynchronizationContext** to **null**

