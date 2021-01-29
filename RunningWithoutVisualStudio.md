# Running Without Visual Studio

These steps should install everything you need.

## 1. Install PowerShell Core

Download it from here: https://github.com/PowerShell/PowerShell/releases

> Current tested version: https://github.com/PowerShell/PowerShell/releases/download/v7.0.4/PowerShell-7.0.4-win-x64.zip

## 2. Setup Powershell Build Console

The CsWin32 project contains an `init.ps1` script that installs the .NET SDK and configures the current powershell to use it.  Download it and run it in your powershell to setup the build environment.

```
git clone https://github.com/microsoft/CsWin32 && git -C CsWin32 checkout f9c10a34bdf538c6ea3dde0f81557fddc1b2dd05 -b release
.\CsWin32\init.ps1
```

> NOTE: it shouldn't matter what directory you are in when you run `init.ps1`.

## 3. Building/Running

```
# from the Powershell console that was setup above

# cd to the project you want to build and/or run (i.e. Generator or Validator)

# only build with
dotnet build

# build and run with
dotnet run

# after running, check the JSON files in the win32json repository you cloned above
```
