# KeyTempo 构建与发布

## 本地开发

需要 Windows 10 / 11 x64 和 .NET 8 SDK。SDK 安装后重新打开终端，以刷新路径。运行 `dotnet --list-sdks` 应列出 8.x SDK。

```powershell
dotnet restore .\AutoPilotInput\AutoPilotInput.csproj
dotnet build .\AutoPilotInput\AutoPilotInput.csproj -c Release
dotnet run --project .\AutoPilotInput\AutoPilotInput.csproj
```

测试项目位于仓库中以 `Tests` 结尾的目录；按照该项目实际类型使用 `dotnet test`，或者使用 `dotnet run --project <测试项目>` 运行独立测试入口。GitHub 工作流会检测这些项目并执行。

## 便携版

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Version 1.0.0 -SkipInstaller
```

脚本从自身所在目录定位工程，所以工作目录可以包含中文或空格。输出：

- `artifacts/keytempo-portable/KeyTempo.exe`：包含运行时的 Windows x64 单文件。
- `artifacts/keytempo-release/KeyTempo.exe`：供 GitHub Releases 直接下载运行的同一程序。
- `artifacts/keytempo-release/KeyTempo-1.0.0-win-x64-portable.zip`：程序、许可证和使用指南。
- `artifacts/keytempo-release/KeyTempo-1.0.0-source.zip`：源码、测试、构建脚本、文档和许可证，不含本地构建目录。
- `artifacts/keytempo-release/SHA256SUMS.txt`：发布文件校验值。

构建产物、下载的构建工具、用户配置不应提交到 Git。便携程序不要求用户安装 .NET；单文件中的本机运行库可在首次启动时自解压。

## 安装版

安装 [Inno Setup 6](https://jrsoftware.org/isdl.php)，然后运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Version 1.0.0 -RequireInstaller
```

非标准位置可以通过 `-InnoCompiler 'D:\Tools\Inno Setup 6\ISCC.exe'` 指定。脚本也能使用本地 `.tools/innosetup-6.4.3/tools/ISCC.exe`。编译器只用于构建，不随应用分发。

安装版输出为 `artifacts/keytempo-release/KeyTempo-1.0.0-win-x64-setup.exe`，默认安装到当前用户的 `%LOCALAPPDATA%\Programs\KeyTempo`，不请求管理员权限。卸载移除安装目录与快捷方式，保留独立存放的用户配置。

脚本默认在找到 Inno Setup 时构建安装包；找不到时仍生成便携版并提示。`-RequireInstaller` 用于发布环境，缺少编译器则失败，防止遗漏安装包。

## GitHub

工作流响应 pull request、`v*` 标签和手动运行。它安装 .NET SDK、检查项目、执行测试入口并构建直接下载的 `KeyTempo.exe`、便携 ZIP、安装 EXE、源码 ZIP 和校验文件，随后把文件保存为 Actions 构建产物。

发布标签采用 `v1.0.0` 这样的数字版本号。创建并推送标签后，工作流为该标签创建 GitHub Release 并上传构建文件；本地运行脚本不会上传任何内容。

```powershell
git tag v1.0.0
git push origin v1.0.0
```

官方仓库为 https://github.com/Yunis3056/KeyTempo。程序内的手动检查更新默认使用 `Yunis3056/KeyTempo`，衍生版本可在设置中更换仓库。

当前构建未配置代码签名。维护者正式分发时可在工作流中接入自己的签名证书；签名凭据应保存在 GitHub Secrets 中。

