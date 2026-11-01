# 验证记录 - 2026-10-12 (1.0.0.5)

# 验证记录 - 2026-10-18 (1.0.0.6)

## 修复: 新建对话里发送消息提示 session-lock

- 现象: 新建对话发送第一条消息、会话刚创建且 Agent 回合刚启动时, 在这个新会话里再发消息, 提示裸错误码 "session-lock"。
- 根因: 首条消息走 `POST /sessions/create-and-start`, 服务端创建会话并立即启动回合。云端在处理完连接器刚发布的回合状态前, 客户端绑定会话时抓取的快照仍携带回合开始前的能力(`session.send_message` available), 发送按钮因此保持可用。对不支持 steer 的 Runtime(Claude `steerTurn=False`, 只暴露禁用的发送按钮)用户一点发送, 运行时即因"已有活跃回合"拒绝, 云端返回 409 session-lock, 被 `FriendlyError` 原样透传。旧会话从列表打开时状态早已落定, 所以只有新建对话会复现。
- 修复:
  - `CreateDraftSessionAsync` 成功后把新会话标记为忙(`turnJustStarted`), 直到事件流证明回合结束才恢复; `SelectSessionAsync` 新增 `startingTurn` 参数, WebSocket 事件与断线恢复(`RecoverAsync`)应用到事件后清除该标记, 切会话 / 重新登录 / 进入新草稿时重置。
  - `SessionBusy` 补齐服务端 `has_active_turn` 使用的状态集合(`waiting`、`blocked` 原缺失), 与"可发送 ⟺ 无活跃回合"保持一致。
  - `UpdateCapabilities` 中发送按钮改为 `!SessionBusy && Can("session.send_message")`: 数据新鲜时等价, 快照能力陈旧时更安全。
  - `ApiClient.IsSessionLockedError` 识别该 409(裸码 / turn_active / already running 等), `FriendlyError` 提示"这个会话正在执行任务……或用「追加」把指令并入当前任务", 不再显示裸码。
- 新增原生用例(并入 "New session drafts locally and is created on first send"): 首条消息绑定后会话即为忙、发送被搁置; 事件证明回合结束后发送恢复。
- 新增协议用例: session-lock 与普通 409、本地权限提示、其它状态码的区分。

# 验证记录 - 2026-10-12 (1.0.0.5)

## 修复: 新建对话页唯一操作按钮不可用

- 现象: 选好项目 / Agent / 模型后看不到"进入对话"按钮, 只能看到"..." 溢出按钮; 展开溢出菜单后该按钮 IsEnabled 仍是 False。
- 根因: NewSessionStartButton 是 ClosedDisplayMode=Minimal 的 CommandBar 里的 AppBarButton。UWP 在该状态下把主命令的 IsEnabled 强制为 False(诊断探针验证: 赋值 true 后立刻读回仍是 False, CommandBar 展开后才恢复), 主命令同时被折叠。
- 修复: 改为页面底部的普通强调色按钮(与"连接终端"同款样式), 不再受 CommandBar 状态影响; 启用状态集中到 UpdateNewSessionStart(), 按当前下拉选择计算, 避免 ItemsSource 重填期间多次 SelectionChanged 互相覆盖。
- 新增原生用例 New conversation page keeps its continue button usable。

## 新增: 新建对话支持附件

- 草稿态原来禁用附件按钮, create-and-start 的 attachments 分支不可达, 且只发 {fileId}, 与真实服务端 InlineAttachmentRef(需要 name / mediaType / size / sha256 / contentBase64)不符。
- 修复: 草稿态可选本地文件, 读入内存并计算 SHA-256, 随 create-and-start 以 inline 附件提交(与 web / Android 同形); 真实会话仍走上传后按 {fileId} 引用。上限 10 个, 单个 25 MB。

## 修复: 上传文本误报 Connector 掉线

- 现象: 文件页上传文本到不存在的路径时提示"工作设备暂不可用, 请检查 Connector 并重试", 而 Connector 实际在线。
- 根因: 上传前的存在性预读 fs/readText 在目标不存在时, 真实服务端把 Connector 的 FileNotFoundError 原样转发为 502(detail 含 file not found), 客户端只吞 404, 于是 502 走通用映射变成 Connector 暂不可用。
- 修复: ApiClient.IsMissingFileError 统一识别目标不存在(404, 或 5xx 且 detail 含 not found / no such file); 上传预读据此放行, FriendlyError 提示"工作设备上找不到这个文件或目录", 超时 / 权限等真实故障仍按原样提示。
- 模拟服务改为与真实服务端一致(缺失文件返回 502), 协议测试新增分类用例; fixture integration 改用随机会话内容, 可对热服务重复运行。

## 重做: 终端彩色与完整屏幕模拟

- 原来 TerminalBuffer 忽略所有 SGR, 输出是纯文本 TextBox, 无法显示颜色; 单元格度量用固定 8.4 / 19 px 估算。
- 修复: TerminalBuffer 改为单元格网格加每格属性, 支持 SGR 0 / 1 / 3 / 4 / 7 / 22-27 / 30-37 / 40-47 / 90-97 / 100-107 / 38;5;n / 48;5;n / 38;2;r;g;b / 39 / 49, 块状光标, 备用屏幕(1049 / 1047 / 47), 插入删除行列与字符, 滚动区操作, 延迟换行; Text 语义保持不变。
- 渲染改为 ListView 加每行 Runs(Ui/TerminalView.cs): ANSI 16 色 / 256 色 / 真彩色映射为画刷, 加粗提亮, 反显与光标块; 只有版本戳变化的行会重建, ListView 负责虚拟化; 贴底自动滚动, 向上滚动不抢焦点; 输出可选中复制, 桌面硬件键盘可直接输入远程 shell。
- 模拟服务终端回放 fixture 加入彩色序列; 原生用例断言绿色 / 加粗红 / 256 色 run 与行集合同步。

## 验证

- x86 Debug 与 ARM Release 均编译通过。
- 协议测试 15 / 15 通过(新增 SGR 颜色与真彩色, 备用屏幕, 缺失文件分类三条)。
- 真实 Windows UWP 自测 13 / 13 通过(新增新建对话继续按钮可用用例, 终端用例新增颜色断言)。
- 桌面实际走查: 新建对话页按钮可见可用, 草稿态附件按钮可用, 发送后绑定真实会话; 终端连接后彩色 run 正常渲染, 输入命令回显正常。


# 验证记录 · 2026-10-10（1.0.0.4）

## 修复：提问 / 审批弹窗只显示默认外壳

- 现象：Agent 通过 AskUserQuestion 提问时，弹窗只有标题和“关闭”，看不到问题本身。
- 根因：这类 notice 没有 `message` 字段，问题文本只在 `action.input.uiSchema.questions` 与 `context.toolInput` 里；原实现只读 `message` 和审批用的 `context.command/path/reason/cwd`，并用通用 `SchemaForm` 渲染 `input.schema`——那是 answers → question → optionIds(enum 数组) 的嵌套对象，会被渲染成一串嵌套标签加“添加一项”，问题文本完全不显示。
- 修复：新增 `Services/InputRequestForm.cs`，识别 `uiSchema.component == "inputRequest"`，按问题渲染 header / prompt / 选项（单选 RadioButton、多选 CheckBox，带 description）和“其他”输入框，产出 `{"answers":{questionId:{"optionIds":[...],"customText":"..."}}}`；弹窗另外显示 `context.toolName` 与 `context.toolInput.questions` 的问题和选项；`input.schema` 兼容 `schema_` 别名；没有可操作动作时给出提示。

## 新增：新建对话改为“先选设置、后建会话”

- 点“新建会话”不再弹对话框，而是打开 `NewSessionView` 页面：选项目 → 加载设备上的 Agent → 选模型（`GET /connectors/{id}/runtimes/{runtimeId}/catalogs/model`，读取 `catalog.models`）。
- 点“进入对话”进入对话页草稿态：时间线为空并提示“发送第一条消息后才会真正创建会话”，发送按钮可用；文件 / 终端标签页以及重命名、归档、接管、设置等会话级操作在草稿态被禁用。
- 第一条消息才调用 `POST /sessions/create-and-start`，带上项目、runtime、cwd 与 `selections.model`，返回后绑定真实会话。这同时取消了“第一个任务必填”的限制。

## 修复：流式输出时对话页闪烁、上下跳动

- 根因：每个流式事件都会重新投影行。原来用 `timelineRows[i]=rows[i]` 替换对象，ListView 会重建容器；同时每次都 `ScrollIntoView(..., Leading)` 再跑 4 轮 60ms 滚动循环，多个循环叠加导致视口来回跳。
- 修复：`TimelineRow` / `ProcessStep` 实现 `INotifyPropertyChanged`，新增 `CopyFrom` 只对真正变化的属性发通知，行按 key 原地更新（仅当模板类型变化时才替换对象）；滚动改为只在“用户已在底部且内容增长”时触发、同一时刻只允许一个滚动循环、3 轮 90ms 直接 `ChangeView` 到底。

## 验证

- x86 Debug 与 ARM Release 均编译通过。
- 协议测试 12 / 12 通过。
- 真实 Windows UWP 自测 12 / 12 通过，新增两条用例：
  - `New session drafts locally and is created on first send` 覆盖草稿态、发送按钮可用性、首次发送创建会话与 `selections.model` 透传；
  - `AskUserQuestion notices render their questions and answers` 使用与真实 Claude notice 同形的 fixture（无 `message`、只有 `uiSchema.questions`）断言问题文本、选项数量、未作答校验与答案结构。
- 手机 ARM 包 `AgentsAnywhere.Mobile_1.0.0.4_ARM.appx` 已生成，尚未在手机上安装验证。

# 验证记录 · 2026-10-10（1.0.0.3）

## 修复：新建会话用错 API

- 根因：`NewSession_Click` 在“第一个任务”留空时回退到 `POST /api/v2/sessions`。服务端该路由只用于绑定已有 runtime 会话（必须带 `externalSessionId`），否则返回 `new sessions must use /sessions/create-and-start`，所以留空任务时新建一定失败。
- 修复：始终调用 `POST /api/v2/sessions/create-and-start`；“第一个任务”改为必填，项目 / Agent 就绪且任务非空时才允许提交。
- 依据：上游 `server/agent_server/services/session_run.py` 的 `create_session` 分支，以及 Android 客户端（只使用 `/sessions/create-and-start`，且 `content` 始终存在）。

## 新增：远程新建项目目录

- “新建项目”对话框新增“在设备上创建这个目录（含上级目录）”开关，默认开启。
- 通过 `POST /api/v2/connectors/{id}/shell/exec?root=父目录` 执行 `mkdir`：Windows 用 `if not exist ... mkdir ...`，POSIX 用 `mkdir -p ...`；非零退出码会抛出设备返回的 stderr。
- 服务端 `POST /projects` 只登记路径、不会在设备上建目录；Connector 要求 `cwd` 必须是已存在目录，因此 root 传父目录、cwd 传 `.`。

## 验证

- x86 Debug 与 ARM Release 均编译通过。
- 协议测试 12 / 12 通过（`tests/mock_server.py` 新增 `shell/exec` 桩，仍不执行真实 shell）。
- 真实 Windows UWP 自测 10 / 10 通过；新增用例 Remote project folder creation uses the connector shell RPC 覆盖 POSIX / Windows 命令构造、root+cwd 与退出码失败路径。
- 手机 ARM 包 `AgentsAnywhere.Mobile_1.0.0.3_ARM.appx` 已生成，尚未在手机上安装验证。

# 验证记录 · 2026-10-10

## v3 界面重做（1.0.0.2，第二轮修复后）

- x86 Debug 编译通过；桌面窗口默认 400×760（接近手机宽度）。
- 协议测试 12 / 12 通过（需在无应用实例连接模拟服务时运行，否则 25 秒降级恢复会干扰幂等计数）。结果：`TestResults/protocol-tests.json`。
- 真实 Windows UWP 测试 9 / 9 通过（断言已迁移到新行模型），结果：`TestResults/native-test-results.json`。
- 桌面截图检查：多轮 agentic 循环会话（`ses_loop`）、根 Pivot 会话列表、审批块 + ContentDialog、展开的 CommandBar、会话设置页均按 v3 设计稿显示。
- 交互检查：设置页修改权限立即发送 PATCH `/runtime/selections`，模拟服务确认收到；应用后下拉框回读为“工作区写入”。
- 模拟服务新增 `ses_loop` 合成多轮循环会话（仅用于 `--test-ui` / `--self-test`，不执行真实 shell）。`ses_demo` 快照仍为最后 5 条以保留历史加载路径。

第二轮修复与新增：

- 启动闪退根因：`Application.Resources` 在 App 构造期间抛 E_UNEXPECTED（桌面和手机 15254 均如此）。第一版因整段 try/catch 只是静默失败；重写 accent 逻辑时把 Resources 访问移出 try 导致崩溃。现恢复全段保护，并在 `Window.Activate` 后重试（桌面日志确认重试成功，强调色生效）。AccentLight2 在 Windows 10 Mobile 不受支持，改为本地计算浅色阶。
- `Pivot.Title` 在 UWP 默认模板中不渲染：根页面与会话页改为独立标题行（AGENTS ANYWHERE / 会话名 + 设备状态）。临时横幅覆盖标题属预期，手机上横幅 4 秒自动隐藏。
- 新建项目 / 新建会话 / 审批弹窗内容包进 ScrollViewer（MaxHeight 440），小屏和软键盘下按钮可达。
- 文件下载：文件列表每行新增下载按钮（目录不显示），与预览页下载共用同一实现（fs/read → downloadUrl → 大小 + SHA256 校验 → FileSavePicker）。
- Markdown 渲染增强：标题分级（#/##/###）、引用块、任务列表（☐/☑）、管道表格、分隔线、链接（仅 http/https）、斜体；原有段落、列表、围栏代码（带复制）、行内代码、加粗保留。
- 对话页新增“回到底部”浮动按钮：向上滚动超过约 240px 出现，点击平滑回底后自动隐藏。
- 诊断日志改为 ConfigureAwait(false) 并新增 Flush()；未处理异常现在同步落盘（LocalState\crash.log 记录完整堆栈，仅本地）。

设计稿：`design/mobile-v3/index.html`（确认版本）；v2 深色稿与 v4 Metro 稿保留在 `design/mobile-v2`、`design/mobile-v4` 供对比，未采用。

# 验证记录 · 2026-10-09

- x86 Debug 编译通过；已注册并实际运行原生 UWP。
- ARM Release 编译通过（.NET Native 1.7）；使用本机现有开发证书签名。
- 协议测试 9 / 9 通过，结果：`TestResults/protocol-tests.json`。
- 真实 Windows UWP 测试 9 / 9 通过，结果：`TestResults/native-test-results.json`。

原生测试覆盖：Windows PBKDF2-SHA256 与 Python 独立 UTF-8 测试向量一致（120000 次迭代，32 字节，base64url）；官网盐值 / 校验值登录流程与错误分类；设备 / 项目 / 会话和原生控件；MessageWebSocket 实时接收与消息幂等；断线重连和漏失事件恢复；文件预览、二进制下载校验；终端 replay 和中文输入输出；隔离测试凭据的 PasswordVault 往返；持久日志脱敏。

协议测试另外覆盖同 cursor 状态 / 能力投影、时间线修订、稀疏恢复与历史保留、拆分 UTF-8 / ANSI、引号参数、文件并发修改 412、multipart 附件和跨源下载拒绝。

此前已通过 Device Portal 在 `192.168.3.159` 部署并启动第一版。手机为 Windows 10 Mobile 15254.603 ARM。用户反馈第一版邮箱登录立即被错误地提示凭证过期。

本次修复：官网登录先请求 `/auth/password-salt`，再提交派生后的 `passwordVerifier`，不再提交明文 password 字段；登录端点 401 不触发已登录凭证失效事件，并显示登录失败；登录页与设置页增加可查看 / 复制的持久脱敏日志。

修复版 1.0.0.1 已签名并通过 Portal 成功部署，手机应用已启动。更新时需先关闭本应用进程；初次更新遇到 0x80070005，关闭后重试成功。Portal 曾间歇超时，恢复连接后完成部署。

已读取手机真实脱敏日志：用户重试后 `/auth/password-salt`、`/auth/login`、`/auth/me`、设备 / 项目 / 会话请求均返回 200；更新为 1.0.0.1 后启动直接使用保存的凭据，`/auth/me` 仍返回 200，证实更新和重新启动保留登录。

Dashboard WebSocket 已实际接收 54517 字节快照，但手机连接仍出现 0x80072EFE 和关闭码 1006。自动重新取 ticket 并连接已确认；新版对连续短连接失败采用递增退避，并保留 25 秒 HTTP 降级恢复。根因尚未确定，不能声称实时连接稳定。日志加入路径和消息字节数以便继续定位。

真实 Connector 端到端任务和浏览器 OAuth 尚待手机验收，不能用合成测试替代。新版本实时重连变更后，桌面 UWP 9 / 9 自测再次通过。

手机验收顺序：

1. 更新后登录默认官方云，确认进入设备 / 项目 / 会话列表。
2. 关闭并重新打开，确认保留登录状态。
3. 选择在线设备的会话，发送简单任务，确认工作设备执行、手机显示实时回复。
4. 锁屏 / 恢复或短暂断网，确认消息补齐且不会重复。
5. 在专用测试目录检查审批、输入回复、模型 / 权限、追加和打断，再验证文件和终端。
6. 若失败，在“查看调试日志”核对最后请求路径与状态码；也可通过 `Read-PhoneDiagnostics.ps1` 读取。
