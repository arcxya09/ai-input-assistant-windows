# v1.0 实现与验证记录

## 实现范围

已实现 WinUI 3 设置页、托盘、可调整的无焦点浮窗、全局快捷键、停顿续写、DeepSeek SSE、逐条采纳、采纳后继续生成、单次截图、API Key 加密存储和无正文日志导出。

ContextHost 使用独立进程和当前用户限定的随机命名管道。UIA 调用阻塞或取消时终止旧子进程；旧请求不能向新的上下文提供建议。文本插入通过 Unicode 输入并回读确认，不修改剪贴板。

## 与设计文档的具体差异

- Contracts 合并到 AiInput.Core；Windows 系统功能合并到 AiInput.Windows，避免首版空壳工程。
- 上下文复核以 250 ms 周期加键鼠活动失效机制实现；读取期间的文本／选区变化由 ContextHost 的 UIA 事件校验。
- ContextHost RPC 总超时为 2 秒，首次启动为 8 秒；设计中的 500 ms 软超时尚未单独实现。阻塞不会发生在 UI 线程。
- 截图采用 PNG 并逐步降低分辨率以满足 8 MiB 上限，不做 JPEG 回退。
- 浮窗使用原生分层窗口进行独立背景透明绘制，WinUI 3 用于设置页；位置按屏幕像素保存并限制在可见工作区，尚未实现相对显示器的 DIP 布局迁移。
- SDK 和直接 NuGet 包版本固定；构建生成的依赖锁文件另存为构建记录。当前恢复命令没有开启 locked-mode。
- IPC 使用随机管道名、CurrentUserOnly 和父进程生命周期约束；没有单独的 nonce 握手。
- 未实现自动更新和开机自启动；它们不属于本项目确认的 v1.0 需求。

这些差异不改变 1.5 秒、300/300 上下文、逐条确认、启动／解锁暂停和截图一次性使用的产品默认值。

## 尚需用户桌面验证

Windows 10/11 实际机器、中文输入法候选状态、Word／WPS／微信等实际版本、多显示器不同 DPI、长期驻留性能、真实 DeepSeek 文本与截图请求。当前未使用真实 API Key，未产生付费生成请求。

缺少可靠 UIA 文本／光标能力或 CJK 组合输入能力的控件会返回不支持。管理员窗口及安全桌面不提供插入能力。软件不会把测试机上的局部成功描述为全应用兼容。

## 本次自动验证

- 源码提交：`aaa5ec16e84ea6c15fa02eb8e814cbdf4767ae6e`。
- [成功构建记录](https://github.com/arcxya09/ai-input-assistant-windows/actions/runs/35183963567)。
- 构建系统：GitHub Windows Server 2025 x64（build 26100）。
- .NET SDK：10.0.401；Windows App SDK：2.5.1。
- 25 项核心测试通过。
- 5 项真实 Win32 Edit 控件测试通过：光标前后文读取、Unicode 插入回读、重复 token 拒绝、原有后文保留、密码框排除。
- WinUI 设置界面、托盘、输入钩子及 ContextHost IPC 启动检查通过。
- 自包含 ZIP 和 Inno Setup 安装 EXE 编译成功。
- 尚未执行安装后的卸载回归、Windows 10/11 真机、主要第三方应用和真实 DeepSeek 调用测试。

该构建可供用户试用；上述未执行项目保留为兼容性验证任务。
