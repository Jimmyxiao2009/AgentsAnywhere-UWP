# Agents Anywhere UWP

原生 C# / XAML 客户端，支持 Windows 10 Mobile（最低 14393）和 Windows 桌面。默认连接官方云 https://web.agents-anywhere.com，也支持 HTTPS 自托管服务器。

已实现邮箱登录、Windows 凭据保险箱持久化、设备 / 项目 / 会话列表、实时 Timeline、断线 cursor 恢复、审批和输入回复、发送 / 追加 / 打断、模型和权限设置、会话管理、先选设置再建会话的新建对话流程、远程新建项目目录、附件、远程文件以及基础终端。

工作设备必须运行官方客户端或 Connector，并登录同一账号。这个 UWP 客户端通过 `/api/v2` 和 WebSocket 连接服务器，不会在手机上运行 Agent。

## 构建与部署

使用 VS 2017 UWP 工具链、Windows SDK 19041、Microsoft.NETCore.UniversalWindowsPlatform 5.4.7 和 Newtonsoft.Json 13.0.3：

```powershell
& 'C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe' AgentsAnywhere.Mobile\AgentsAnywhere.Mobile.csproj /t:Restore,Build /p:Configuration=Debug /p:Platform=x86 /p:VisualStudioVersion=15.0 /v:minimal
& .\scripts\Deploy-Desktop.ps1
& .\scripts\Launch-Desktop.ps1
```

手机使用 `Configuration=Release`、`Platform=ARM` 构建，输出为 `.NET Native` 包。部署前需用与 manifest 发布者 `CN=AgentsAnywhere` 一致的代码签名证书签名。不要提交证书私钥。

```powershell
& .\scripts\Deploy-Phone.ps1 -DeviceOrigin http://192.168.3.159
& .\scripts\Launch-Phone.ps1 -DeviceOrigin http://192.168.3.159
```

当前版本 1.0.0.5。部署脚本会先关闭本应用进程，保留应用数据。适用于当前无需身份验证的 Device Portal；若手机开启 Portal 身份验证，需要补充凭据支持。手机需保持亮屏和 Wi-Fi 连接。脚本使用 curl multipart 兼容旧 Mobile Portal，并绕过系统 HTTP 代理直连设备。PowerShell 7 推荐用于部署。

## 调试日志

登录页和设置页都有“查看调试日志”，可查看或复制。日志在应用 `LocalState/diagnostics.log` 中自动轮换，记录 UTC 时间、HTTP 路径 / 方法 / 状态码 / 耗时、登录阶段、WebSocket 状态、异常类型和 HRESULT。

不记录邮箱、密码、派生校验值、Token、请求 / 响应正文、HTTP 头、URL 查询参数或异常消息。没有远程日志上传。

```powershell
& .\scripts\Read-PhoneDiagnostics.ps1 -DeviceOrigin http://192.168.3.159
```

读取脚本只下载本应用的诊断文件；API 依据 [Microsoft Device Portal 文档](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/device-portal-api-core#app-file-explorer)。

## 验证

协议验证：先在独立进程运行 `python tests/mock_server.py`，再运行 `dotnet run --project tests/ProtocolTests/ProtocolTests.csproj`。

原生验证：仅 Debug 构建支持 `scripts/Launch-Desktop.ps1 -Arguments '--self-test'`。若应用已运行，需先关闭本项目应用。合成服务仅监听 127.0.0.1:18743，不执行真实 Agent 或 shell。UWP 桌面测试需要临时为本包添加 loopback 豁免，测试后移除：

```powershell
CheckNetIsolation.exe LoopbackExempt -a '-n=AgentsAnywhere.Mobile_8bryj0aeywnda'
& .\scripts\Launch-Desktop.ps1 -Arguments '--self-test'
CheckNetIsolation.exe LoopbackExempt -d '-n=AgentsAnywhere.Mobile_8bryj0aeywnda'
```

结果位于项目 `TestResults/`；原生程序写入自己的 `LocalState/native-test-results.json`。Release 不包含测试服务入口。详见 [TEST-REPORT.md](TEST-REPORT.md)。

终端为完整屏幕模拟：SGR 16 / 256 / 真彩色、加粗 / 斜体 / 反显、块状光标、备用屏幕（vim / htop 等全屏应用不污染回滚）、插入 / 删除行列，输出表面支持选中复制，桌面硬件键盘可直接输入；文件编辑 / 上传限 UTF-8 文本，二进制文件可下载或作为会话附件上传，新建对话也支持附件（随 create-and-start 内联提交）。浏览器 OAuth 已实现但尚未用真实官网账号验证。
