# Dragon

Dragon is Chimera's game client launcher. This open-source version of the project excludes the auto-patch / launcher application, but includes the command-line code injector and three modules: a flash module that loads the ActiveX Flash plugin from the client's directory, a connect module for redirecting socket connections for private servers, and a websites module for redirecting / blocking website popups. 

Dragon uses code injection to change the behavior of the game client. It starts a new process for the game client in a suspended state. Then, queues asynchronous procedure calls on the main thread of the client using QueueUserAPC. Once injected, the process is resumed. Modules injected into the client will load before the entry point of the client is called.

## Getting Started

To compile and run Dragon from source, 

1. Download and install VC++22 using the Visual Studio 2026 installer.
2. In Dragon.Launch's project Debugging properties, find Command Arguments.
3. Set arguments for the client path, exe, and blacknull. Example: "G:\Conquer\5615" Conquer.exe blacknull.
4. Ensure the project is being built in the x86 configuration (Conquer Online is 32-bit) -> (Build > Configuration Manager > Platform > Win32)
5. Build and run the project.

* Project does not compile for standard g++ compiler. Use VS Community 2026 along with the project files provided. 
* Filesystem namespace by default may not work. You can resolve this by updating the project properties to use ISO C++ 20 (Project > ...Properties > General > C++ Language Standard)

## Projects

Dragon is made up of multiple projects:

* __Dragon__: Not included in this version of Dragon, but is usually the patching app.
* __Dragon.Launcher__: The code injector that accepts command-line arguments from the app.
* __Dragon.Module__: Library containing shared classes for the injected modules. 
* __Dragon.Module.Connect__: Injected DLL for redirecting socket connections for private servers.
* __Dragon.Module.Flash__: Injected DLL for loading ActiveX Flash from the client rather than the system.
* __Dragon.Module.Websites__: Injected DLL for redirecting / blocking website popups.

## Usage

To use Dragon.Launcher from your own Dragon app, call using runas. That way, your app doesn't need to run as admin and Windows will prompt the user when clicking Play. See the C# example below:

```csharp
Process? launch = Process.Start(new ProcessStartInfo {
    WorkingDirectory = Environment.CurrentDirectory,
    FileName = Resources.LaunchExecutableName, // Dragon.Launch.exe
    WindowStyle = ProcessWindowStyle.Hidden,
    UseShellExecute = true,
    Verb = "runas",
    Arguments = string.Format("{0} {1} {2}", 
        clientFolder, // Path to top directory
        Resources.ClientExecutableName, // Conquer.exe
        Resources.ClientExecutableArguments), // blacknull
    });
```

If you wish to use the program as-is without an external autopatcher, then you can create a shortcut to the Dragon.Launcher.exe executable in Windows and provide the shortcut's target with the following command-line arguments: "<client_path>", "Conquer.exe", "blacknull". In advanced, check "Run as administrator" (required so the program can start Conquer.exe and queue modules on its main thread).

## FAQs

* __Q: Why is Conquer.Loader.exe showing up as a virus in my anti-virus?__<br />
A: It uses code injection to load modules into the client. You can add this executable to your exclusions list.
* __Q: What is blacknull?__<br />
A: Blacknull is a command-line argument to the game client for bypassing the official auto-patch requirement.
