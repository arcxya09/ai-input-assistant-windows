# AI 输入助手 v1.0

独立 Windows AI 输入助手。WinUI 3 设置界面，DeepSeek 官方文本／图像接口。输入停顿时生成短续写，用户逐条确认插入。

## 获取程序

从 [GitHub Release v1.0.0](https://github.com/arcxya09/ai-input-assistant-windows/releases/tag/v1.0.0) 下载：

- [安装版 EXE](https://github.com/arcxya09/ai-input-assistant-windows/releases/download/v1.0.0/AiInputAssistant-1.0.0-Setup-x64.exe)：每用户安装程序。
- [便携版 ZIP](https://github.com/arcxya09/ai-input-assistant-windows/releases/download/v1.0.0/AiInputAssistant-1.0.0-win-x64.zip)：完整解压后运行 `AiInputAssistant.exe`。
- [SHA256SUMS.txt](https://github.com/arcxya09/ai-input-assistant-windows/releases/download/v1.0.0/SHA256SUMS.txt)：校验值。

程序已包含所需运行时。不要单独复制主程序 EXE；它需要同目录的运行时、PRI 和 ContextHost。

## 功能

- Windows 10/11 x64，WinUI 3 设置页与不抢焦点的建议浮窗。
- 只填 DeepSeek API Key，内置 deepseek-flash。
- 停顿 1.5 秒触发，默认取光标前后各 300 字符。
- 每次一条短续写，逐条采纳；插入确认后继续下一条。
- Ctrl+Alt+S 单次截取当前输入窗口所在显示器，图片上传后清理。
- 启动、解锁和恢复后暂停；排除密码输入目标。
- 浮窗位置、尺寸、字号、背景透明度及快捷键可保存。
- 密钥用 Windows 当前用户保护机制加密；诊断日志不含正文和截图。
- 不改写系统剪贴板；上下文访问在独立进程中运行。

| 默认快捷键 | 功能 |
| --- | --- |
| Ctrl+Alt+Space | 启用／暂停 |
| Ctrl+Alt+S | 截图并单次生成 |
| Ctrl+Alt+Enter | 采纳当前建议 |

## 兼容性边界

支持取决于目标应用的 UI Automation 能力。没有可靠光标、可编辑属性或中文组合输入检测的控件会拒绝处理。管理员窗口、系统安全桌面、部分自绘控件和远程桌面内部输入框暂不支持。

Windows Server 构建机的测试不等同于 Windows 10/11 全应用验收。真实 DeepSeek 调用需要用户的 API Key；不会将测试 Key 放入仓库或安装包。详见验证记录。

## 构建

Windows 上安装 .NET SDK 10.0.401、Windows SDK 和 Inno Setup 6，运行：

```powershell
./build.ps1
```

脚本依次运行核心测试、发布主程序和 ContextHost、执行真实控件集成测试、启动 WinUI 冒烟测试，再生成安装 EXE、便携 ZIP 和校验值。依赖锁文件随构建记录保存。

## 文档

- [使用说明](docs/user-guide.md)
- [需求基线](docs/requirements.md)
- [软件工程技术方案](docs/architecture.md)
- [验收矩阵](docs/acceptance.md)
- [开发路线](docs/roadmap.md)
- [第三方组件](THIRD-PARTY-NOTICES.md)

项目自身许可证尚未选定。
