# Agents Anywhere UWP：手机 UI 重做交接



更新日期：2026-10-10（Asia/Shanghai）

工作目录：`H:\项目\UWP\Agents Anywhere`

## 用户当前要求 用户原先要求完成原生 Win10 Mobile App、在桌面测试，并通过 Windows Device Portal 部署至 `192.168.3.159`。用户明确选择使用官方官网服务。



UI 重做已完成并通过桌面验证：设计稿迭代了 v1–v4，用户确认采用 **v3（Windows 10 Mobile 风格）**，见 `design/mobile-v3/index.html` 与 `design/mobile-v3/preview.png`。v4（Metro 全景稿）被评价为“用力过猛”，仅留档。重做前的旧实现备份在 `backup/ui-before-v3/`。



当前状态：1.0.0.6（2026-10-18）。第四轮修复新建对话发送消息提示 session-lock（见 TEST-REPORT.md 顶部）；x86 Debug / ARM Release 编译通过，协议测试 16 / 16、原生自测 13 / 13 通过。1.0.0.6 已签名（CN=AgentsAnywhere，CurrentUser\My 存储证书）并经 Device Portal 部署到手机（192.168.3.159），启动后官网登录 200、dashboard / 会话 WebSocket 正常；界面与功能验收待用户在真机完成。



第二轮修复要点（2026-10-10）：

- **关键坑：`Application.Resources` 在 App 构造期间抛 E_UNEXPECTED**（桌面与手机 15254 均复现）。任何启动早期读 Resources 的代码必须整体 try/catch，并在 `Window.Current.Activate()` 之后重试。`AccentLight1/2/3` 在 Windows 10 Mobile 不受支持，浅色阶需本地计算（`App.Lighten`）。

- `Pivot.Title` 在 UWP 默认模板不渲染，标题用独立行实现。

- UWP CommandBar: ClosedDisplayMode=Minimal 关闭时会把主命令 IsEnabled 强制为 False, 且主命令被折叠; 页面级唯一操作用普通按钮, 不要放进这种 CommandBar (1.0.0.5 进入对话按钮即因此重做)。

- 新增：文件列表行内下载、Markdown 增强（标题 / 引用 / 任务列表 / 表格 / 链接 / 斜体）、对话页“回到底部”浮动按钮、崩溃同步落盘 `LocalState\crash.log`（完整堆栈，未脱敏，仅调试用）。



本文件已替换早期骨架交接，下面描述当前实际状态。

## 当前可运行版本与验证边界

- 包名 / 命名空间：`AgentsAnywhere.Mobile`。

- 手机安装版本：**1.0.0.1**，ARM Release，.NET Native。

- 手机系统：Windows 10 Mobile `15254.603.armfre.feature2_rs3svc.200106-1633`。

- 手机 Portal：`http://192.168.3.159`，当前环境无需 Portal 身份验证。此前成功安装、更新并启动本应用。

- 手机包完整名：`AgentsAnywhere.Mobile_1.0.0.1_arm__8bryj0aeywnda`。

- 应用激活 ID：`AgentsAnywhere.Mobile_8bryj0aeywnda!App`。

- 桌面 x86 Debug 1.0.0.1 已注册；测试结束后已恢复正常官方云启动。

- 协议测试 **9 / 9** 通过；真实 Windows UWP 自测 **9 / 9** 通过。实时重连修改后原生测试再次通过。

- 手机真实脱敏日志确认官网密码登录成功，`auth/me`、设备、项目、会话列表请求均为 200；更新后重新启动直接验证保存的 Token，仍返回 200。

- 手机 Dashboard WebSocket 实际收到 54517 字节快照，但仍记录过 `0x80072EFE` / 关闭码 `1006`。已验证自动重连，根因尚未确定。用户“好了”的反馈不能替代所有功能逐项验收，也不能据此宣称实时连接长期稳定。

- 浏览器 OAuth、复杂审批 schema、所有真实 Connector 文件 / 终端 / 任务流程尚未逐项验收。合成测试服务不执行真实 Agent 或 shell。



详细记录见 `TEST-REPORT.md`；机器可读结果在 `TestResults/protocol-tests.json`、`TestResults/native-test-results.json`。

## 代码地图

| 文件 | 当前职责 / 重做 UI 时的关系 |
| --- | --- |
| `AgentsAnywhere.Mobile/App.xaml` | v3 视觉系统资源：黑底、系统强调色画刷（启动时读 `UISettings` 系统色）、控件样式、图标字体 |
| `App.xaml.cs` | Frame 导航、激活、挂起 / 恢复、启动 / 未处理异常日志、强调色应用；桌面默认窗口 400x760 |
| `MainPage.xaml` | 全部视图：登录、根 Pivot（会话 / 设备 / 我的）、会话 Pivot（对话 / 文件 / 终端）、会话设置页、文件预览页；CommandBar、时间线各类型模板 |
| `MainPage.xaml.cs` | 登录、工作区加载、筛选、会话选择、生命周期、WebSocket、cursor 恢复、渲染、视图与返回键导航、状态栏进度、临时提示横幅 |
| `MainPage.Actions.cs` | 发送 / 追加 / 打断、附件（草稿态本地内联 + 真实会话上传）、项目 / 会话创建、审批 ContentDialog（SchemaForm）、模型 / 权限（选中立即提交）、命令、会话管理 |
| `MainPage.Files.cs` | 文件目录（面包屑 / 返回上一级）、文本预览 / 编辑整页视图、并发写入保护、上传、下载和完整性校验 |
| `MainPage.Terminal.cs` | 远程终端创建 / 关闭（关闭前确认）、replay、彩色行渲染（ListView + Runs 差量更新）、输入（输入框 + 输出表面键盘）、控制键和按字体度量计算的尺寸 |
| `MainPage.Tests.cs` | 仅 Debug 的原生运行时测试；已适配新行模型；`--test-ui --loop` 打开多轮循环合成会话 |
| `Models/RemoteModels.cs` | JObject 适配模型、TimelineState、cursor、时间线修订、能力状态 |
| `Models/TimelineProjection.cs` | 时间线投影：把 timeline items 按轮分组为用户消息 / 过程轨道（思考、工具、中间说明）/ 最终输出 / 系统 / 错误 / 审批行；纯 C#，被协议测试直接覆盖 |
| `Models/SessionCards.cs` | 会话卡片分组（需要你处理 / 今天 / 更早）、设备卡片、附件项模型 |
| `Ui/TimelineUi.cs` | 行 / 步骤的视觉映射转换器、DataTemplateSelector、Markdown 子集渲染（段落、列表、围栏代码、行内代码） |
| `Ui/TerminalView.cs` | 终端行 / run 视图模型与 ANSI 调色板；把缓冲行投影为可绑定 Runs（含光标块） |
| `Services/ApiClient.cs` | API 地址规范化、HTTP / JSON / multipart、401 事件、请求诊断、下载限制、缺失文件错误分类 |
| `Services/AuthService.cs` | 官网盐值 / 校验值登录、PasswordVault、启动恢复、浏览器 PKCE |
| `Services/RealtimeSocket.cs` | MessageWebSocket、串行接收、重连、连续短连接失败的递增退避 |
| `Services/DiagnosticLog.cs` | LocalState 持久脱敏日志、轮换、读取 |
| `Services/SchemaForm.cs` | 常见 JSON schema 转原生输入控件及验证 |
| `Services/TerminalBuffer.cs` | 完整终端模拟：单元格网格 + SGR 颜色（16 / 256 / 真彩、加粗 / 斜体 / 反显）、块状光标、备用屏幕、插入 / 删除行列、延迟换行；`Text` 保持旧语义 |
| `Services/CommandArguments.cs` | 引号命令参数解析 |
| `Package.appxmanifest` | 包身份、版本、14393 最低系统、能力、协议、PhoneIdentity |
| `Properties/Default.rd.xml` | .NET Native 反射支持配置 |
| `THIRD-PARTY-NOTICES.txt` | 上游与 Newtonsoft.Json 的 MIT 声明 |
| 目前业务和控件状态耦合在 MainPage 的几个 partial 文件中，并不是已经分离好的 MVVM。直接删掉 XAML 控件会让 code-behind 编译失败。若拆成页面 / ViewModel / 服务，请同步迁移回调、取消令牌、状态更新、测试入口和生命周期；不要只改 XAML 或只让编译通过。

## 手机 UI 重做重点 当前界面偏桌面：窄屏下 SplitView 从 Inline 改 Overlay，其他区域仍大量使用横向按钮、下拉框和 Pivot。手机上只是“能显示”，尚未完成完整的手机体验。 建议优先重新组织以下流程，具体视觉和导航可自行设计：

1. 登录和启动恢复：清晰区分加载、登录失败、网络失败，保留官方云默认值、自托管入口和调试日志入口。

2. 工作区浏览：设备 / 项目 / 会话层级、搜索、在线 / 离线、空列表与创建入口。

3. 会话阅读和输入：消息布局、长回复、加载历史、滚动行为、附件、键盘弹出后的输入区、发送 / 追加 / 打断。

4. 审批和输入请求：手机友好的完整阅读、选择 / 填写 / 提交，保留 schema 验证和请求状态。

5. 会话设置和管理：模型 / 推理强度 / 权限、命令、重命名、归档、接管等避免挤在横向一排。

6. 文件和终端：路径操作、预览 / 编辑 / 下载状态、终端控制键、横竖屏和返回行为。

已知布局风险：附件出现时“清除附件”会让底部横向按钮更拥挤；文件和终端工具栏可能溢出；审批占据会话顶部较多空间；设置页长表单偏桌面；消息目前是简单 TextBlock 文本，没有完整 Markdown / 代码呈现。重做应在约 320–430 有效像素窄屏、软键盘、长中文 / 英文内容、空 / 错误 / 忙碌状态下检查。

仍需原生 C# + UWP XAML，不要以抓取网页 DOM、WebView 包官网作为主界面。桌面仍应可以运行以便验证。

## 必须保留的协议细节

### 官网登录（已踩坑并修复）

默认 origin：`https://web.agents-anywhere.com`，所有 API 前缀 `/api/v2`。

1. `POST /auth/password-salt`，body `{email}`，邮箱 trim + lowercase。

2. 返回的 `salt` **按 UTF-8 字符串本身**使用，不做 base64 解码。

3. 密码 UTF-8；PBKDF2-SHA256，120000 次，32 字节；结果 base64url（去 `=`，替换 `+` / `/`）。Windows KeyDerivation API 的结果已用 Python 独立中文 / emoji 向量验证。

4. `POST /auth/login`，body `{email,passwordVerifier}`。不要改回 `{email,password}`，这曾导致官网账号被拒绝。

5. 设置 accessToken 后 `GET /auth/me` 验证，随后保存凭据。

Token 使用 PasswordVault，resource 为 `AgentsAnywhere.UWP.Token.` + origin，用户名 `access`；LocalSettings 只保存 `ServerOrigin`。不要明文存密码、Token 或派生校验值。启动恢复调用 `auth/me`；当前没有确认可用的 refresh 路由，不要凭空实现。

登录端点的 401 表示登录失败，不是“已经登录的凭证过期”。只有携带当前非空 Token 的认证请求收到 401，才触发 Unauthorized。保留 `ApiException.Operation` 的错误上下文。

浏览器 OAuth 使用 WebAuthenticationBroker + PKCE，官网 `/#/mobile-oauth`，callback `agents-anywhere://oauth/callback`，交换 `/oauth/token`；流程尚未用真实账号验收。

### Timeline 与实时恢复

- `/connectors`、`/projects`、`/sessions/list`；快照 `/sessions/{id}/snapshot`。

- `/ws-ticket` 为 dashboard 或 session scope 获取 ticket；重连时获取新 ticket，不能复用旧 ticket。

- `/dashboard/ws?ticket=...`；`/sessions/{id}/ws?ticket=...`。

- 断线 / 恢复使用 `/sessions/{id}/events?after=seq:N`，必要时 snapshot。

- **同一个 sequence / cursor 可以有多个不同投影事件；同 cursor 的能力和状态更新必须处理。sequence 也不保证连续。** 不能按数字 sequence 全局去重、不能遇到数字跳跃就认定丢事件。

- Timeline 按 item ID / revision 合并；recovery 使用服务端 `nextCursor` 推进。

- 重连时重新读取当前状态、notices、capabilities、catalogs；已有早期历史不能被当前短快照抹掉。`RefreshProjection` 是有意实现的行为。

- 保留挂起 / 恢复、旧会话取消令牌、工作区版本检查，避免异步请求把旧会话结果写到新会话。

- 按 effective capabilities 决定发送 / 打断 / 追加 / 附件 / 设置等操作是否可用，不要因新按钮设计而无条件开放操作。

- HTTP 降级恢复定时器为 25 秒；连接连续短暂失败时逐步延长重试至 30 秒，稳定连接超过 30 秒后重置退避。

### 操作与文件 / 终端

- 发消息 `/sessions/{id}/runtime/messages`：`content`、`clientMessageId`、attachments 中的 `fileId`；发送失败重试复用同一 clientMessageId，避免重复任务。

- 追加 `/runtime/steer`；打断 `/runtime/interrupt`；请求回复 `/runtime/notices/{noticeId}/respond`：`{actionId,input}`。

- 模型 / 权限 PATCH `/runtime/selections`：`{selections:{model,permission}}`。推理强度 item 的 `selectionId` 写入 **model**，不能猜测单独 effort 字段。

- 工作目录取项目 workspace 或会话 cwd。文件列表 `/fs/list` 带 root / path；读文本 `/fs/readText?root=...`；写 `/fs/write?root=...`，`encoding:"utf8"` 与 `ifMatch:sha256`；412 必须提示冲突，不能强行覆盖。

- 二进制下载 `/fs/read` 返回下载地址；仅允许同源下载，限制 25 MB，校验大小 / SHA256。文本上传最多 4 MB；会话附件 multipart 字段 `files`，单个最多 25 MB，最多 10 个。

- 终端 `/connectors/{id}/terminals-v2`，WS stream query 为 `fromSeq` / token；input 与 output / replay 数据用 base64，尺寸为 cols / rows。日志不能包含 token 查询参数。

- TerminalBuffer 现在是完整屏幕模拟（颜色 / 光标 / 备用屏幕），但不等同于成熟终端（无 scroll region、无鼠标上报、下划线 UWP Run 不支持）；SchemaForm 也未覆盖所有高级 JSON schema。

## 上游参考 仓库：`https://github.com/anywhere-labs/Agents-Anywhere`（MIT）。本轮分析的 commit：`7df5b31f3fe23d4be92ccd5ae5e0aeb6a3744c0e`。 本机临时副本：`$env:TEMP\Agents-Anywhere-review`，是否仍存在请先检查。关键文件：

- `web-next/src/features/auth/api.ts`、`password-verifier.ts`

- `server/agent_server/api/auth.py`、`dashboard_stream.py`、`sessions.py`

- `server/agent_server/api/connector_files.py`、`connector_terminal.py`

- `android/app/src/main/java/com/agentsanywhere/app/api/RealtimeApi.kt`

- `contracts/` 及 Android 对应功能实现 以当前代码和上游实际 payload 为准，不要使用原始交接里未验证的 refresh 或 turn.start 等猜测。保留 MIT 声明。

## 构建与部署（已验证可用） 项目：`AgentsAnywhere.Mobile.sln`；工具链使用 **VS 2017 Community MSBuild 15** + Windows SDK 19041，C# 7.3，UWP 5.4.7、Newtonsoft.Json 13.0.3。不要随意升级 UWP 依赖、最低系统版本或使用 WinUI 3 / Windows App SDK，它们不适用于此 Mobile 目标。

```powershell $msbuild='C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe' & $msbuild AgentsAnywhere.Mobile\AgentsAnywhere.Mobile.csproj /t:Restore,Build /p:Configuration=Debug /p:Platform=x86 /p:VisualStudioVersion=15.0 /v:minimal /nologo & .\scripts\Deploy-Desktop.ps1 & .\scripts\Launch-Desktop.ps1 & $msbuild AgentsAnywhere.Mobile\AgentsAnywhere.Mobile.csproj /t:Restore,Build /p:Configuration=Release /p:Platform=ARM /p:VisualStudioVersion=15.0 /v:minimal /nologo

``` 当前 ARM 包路径：`AgentsAnywhere.Mobile\AppPackages\AgentsAnywhere.Mobile_1.0.0.5_ARM_Test\AgentsAnywhere.Mobile_1.0.0.5_ARM.appx`（未签名）。 构建默认不签名，需要显式签名。本机 `Cert:\CurrentUser\My` 已存在 `CN=AgentsAnywhere` 的开发代码签名证书，私钥不可导出；复用现有证书，不提交私钥、不删除别的证书、不因自签名证书校验提示而擅自修改系统信任：

```powershell $signingCert=Get-ChildItem Cert:\CurrentUser\My | Where-Object {$_.Subject -eq 'CN=AgentsAnywhere' -and $_.HasPrivateKey} | Sort-Object NotAfter -Descending | Select-Object -First 1 $packagePath='AgentsAnywhere.Mobile\AppPackages\AgentsAnywhere.Mobile_1.0.0.1_ARM_Test\AgentsAnywhere.Mobile_1.0.0.1_ARM.appx' & 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe' sign /fd SHA256 /sha1 $signingCert.Thumbprint $packagePath & .\scripts\Deploy-Phone.ps1 -DeviceOrigin http://192.168.3.159 -PackagePath $packagePath & .\scripts\Launch-Phone.ps1 -DeviceOrigin http://192.168.3.159

``` 发布新版时递增 manifest 的包版本，保持 Name / Publisher / PhoneIdentity，保留用户登录数据；同时更新 Deploy-Phone 默认路径与 Deploy-Desktop 依赖目录（当前脚本版本路径是 1.0.0.5）。拆出新 XAML 页面时必须在旧格式 csproj 中补上 Page 项，C# 文件已有 wildcard。 Portal 注意事项：

- 手机亮屏、Wi-Fi 和 Portal 开启；可能间歇超时。脚本绕过系统 HTTP 代理直连，PowerShell 7 推荐。

- 旧 Mobile multipart 不接受 PowerShell `-Form` 生成的 quoted boundary，所以部署使用 curl。

- 先关闭本应用进程再更新，已解决本轮 `0x80070005 Access denied`；不要直接卸载，避免丢失数据。

- 脚本同时上传包旁 Dependencies/ARM 的 .NET Native Framework / Runtime。

- Mobile 返回的 PackageFamilyName 可能只有 `AgentsAnywhere.Mobile`；以 PackageFullName / PackageRelativeId 精确识别应用。

- 激活接口参数需要 UTF-8 base64，再 URL 转义；curl POST 激活需 `Content-Length: 0`。

## 日志与测试复现 登录页和设置页提供“查看调试日志”及复制。日志存于本应用 `LocalState/diagnostics.log`，自动轮换。记录 UTC 时间、阶段、请求路径 / 状态 / 耗时、socket 路径 / 消息长度、异常类型 / HRESULT。 **不记录账号、密码、Token、passwordVerifier、请求 / 响应正文、HTTP 头、URL query 或异常 message。** 新 UI 的日志入口应继续可用，登录失败时也能进入。日志不自动上传。

```powershell & .\scripts\Read-PhoneDiagnostics.ps1 -DeviceOrigin http://192.168.3.159

``` Portal App File Explorer 的目录 query 必须是 `%5CLocalState`，没有前导反斜线会得到空目录 / 404；脚本已修正。只读取本应用诊断，不抓取凭据或其他应用数据。 协议测试：在独立隐藏后台进程运行 `python tests/mock_server.py`，然后：

```powershell dotnet run --project tests\ProtocolTests\ProtocolTests.csproj

``` 测试项目为 net10.0，链接实际 ApiClient / 模型 / TerminalBuffer / CommandArguments；Newtonsoft 引用本机 NuGet 缓存。模拟服务仅监听 `127.0.0.1:18743`，合成账号、模拟终端，不会执行真实 shell。 原生测试：x86 Debug 构建后安装，关闭已有本应用实例，再执行：

```powershell CheckNetIsolation.exe LoopbackExempt -a '-n=AgentsAnywhere.Mobile_8bryj0aeywnda' & .\scripts\Launch-Desktop.ps1 -Arguments '--self-test'

# 等待应用底部报告 9 / 9，并检查本应用 LocalState/native-test-results.json CheckNetIsolation.exe LoopbackExempt -d '-n=AgentsAnywhere.Mobile_8bryj0aeywnda'

``` `--test-ui` 可加载合成工作区供 UI 检查；这两个入口只在 Debug 编译。上一轮测试服务已停止、临时 loopback 豁免已移除。UI 重构后要同步更新 MainPage.Tests.cs，而不是删掉验收。测试不应写入真实账号凭据；模拟登录不持久化 Token，PasswordVault 测试使用隔离资源且清理。

## 下一位 agent 的执行顺序与验收

1. 阅读本文件、README、TEST-REPORT，运行 / 查看当前 UI；确认旧工具链和手机连接。

2. 设计手机页面与导航，识别 code-behind 控件耦合并安排重构。保留日志与所有已实现功能入口。

3. 实现手机界面，验证窄屏、软键盘、长内容、返回、旋转、后台恢复和错误状态，同时维持桌面可运行。

4. 构建并运行协议 / 原生回归；针对新 UI 检查真实界面，不能只看测试 JSON。

5. 升级版本、签名、部署同一台手机，由用户操作真实登录 / 会话；读取脱敏日志确认，重点检查长消息、审批、发送 / 追加 / 打断、附件、文件与终端。

6. 继续定位手机 WebSocket 间歇断开，区分网络、系统 / 服务器与客户端原因；不要把自动重连或 HTTP 降级当成稳定实时连接已经修复的证据。 当前环境默认 shell 为 PowerShell。普通 exec 有时因 sandbox setup refresh 失败；本轮使用带理由的 require_escalated 执行获自动审查允许的读取、构建、部署等操作。不要把环境错误误判为编译失败。工作区之外写入仍遵循运行环境权限。Windows UI 操作按当时可用 computer-use skill 执行；真实登录让用户输入，不读取或输出密码。 

