# win32jsongen

Generates the JSON Win32 metadata files for https://github.com/marlersoft/win32json

# How to Generate the JSON files

## 1. Clone the win32json repository

Before running the Generator, you'll need a place to store the JSON files. The generator will look for a directory named "win32json" in any of its parent directories (relative to CWD).  You can create this directory by cloning the `win32json` repository to a subdirectory inside this repository or any parent directory.

```
git clone https://github.com/marlersoft/win32json
```

## 2. Generate the JSON files

Running `Generator/JsonWin32Generator.sln" will generate the JSON files with "Visual Studio 2019", otherwise, see [RunningWithoutVisualStudio.md](RunningWithoutVisualStudio.md).
