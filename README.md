# TcpDebugAssistant · TCP 调试助手

Windows 桌面 TCP 收发调试工具（C# / WinForms / .NET 8）。纯代码构建（无设计器文件），界面由本地设计系统引擎推导的暗色「工业实用风」主题驱动。

![模式](https://img.shields.io/badge/模式-客户端%20%2B%20服务端-blue)
![主题](https://img.shields.io/badge/主题-暗色%20Industrial%2FUtility-7675C7)
![对比度](https://img.shields.io/badge/对比度-16.2%3A1%20WCAG%20AA-success)

## 功能

### 双模式
| 模式 | 说明 |
|---|---|
| **客户端模式** | 连接远端 `地址:端口`，收发数据 |
| **服务端模式** | 监听本地端口，接受**多个**客户端，列表化管理（选中发送 / 全部发送 / 手动断开） |

### 收发
- **ASCII/UTF-8** 与 **HEX**（`01 A0 FF` / `01a0ff`，自动忽略空格/逗号/分隔符）两种编码发送
- 接收区 **时间戳**（精确到毫秒）与 **HEX 显示**（附带可打印字符预览）随时切换，历史记录即时重排
- **定时自动发送**：间隔 50ms–60s，可指定次数（0 = 无限）
- 收发字节数统计（B/KB/MB 自适应）

### 服务端发送零门槛
- 「发送到所有客户端」默认勾选（广播）
- 客户端接入自动选中，未选中发送时自动回退第一个客户端
- 每次发送在状态栏回显 `已发送 N 字节 → 目标`，杜绝「点了没反应」

### 视觉
- 暗色主题：调色板 #97 Calm Complementary（背景 `#12121C` / 主色 `#7675C7`），文字对比度 16.2:1
- 自绘标签页（选中态主色下划线）、暗色 ListView 表头、终端风格日志区
- 日志着色：紫=发送、绿=接收、灰=状态、琥珀=错误
- 日志超过 3000 条自动裁剪、超过 1.5MB 自动清屏，长期挂机不卡

## 构建运行

```bash
# 需要 .NET 8 SDK（或直接用 Visual Studio 2022 打开文件夹）
dotnet build -c Release
dotnet run -c Release
# 产物：bin/Release/net8.0-windows/TcpDebugAssistant.exe

# 发布免安装独立版（自包含单文件，无需目标机器安装 .NET）
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## 快速自测

1. 打开两个本程序实例
2. 实例 A：服务端模式，端口 `9000`，点「开始监听」
3. 实例 B：客户端模式，`127.0.0.1:9000`，点「连接」
4. 互发消息 / 切 HEX 发送 `48 65 6C 6C 6F`（= "Hello"）/ 开定时发送

## 项目结构

```
TcpDebugAssistant/
├── TcpDebugAssistant.csproj
├── Program.cs          # 入口
├── MainForm.cs         # 全部界面 + Theme 设计令牌（代码构建，无设计器文件）
└── NetController.cs    # TCP 客户端/服务端控制器 + 编解码工具
```

## 技术点

- `TcpClient` / `TcpListener` 全异步（async/await），接收循环 64KB 缓冲
- 线程安全：socket 事件经 `BeginInvoke` 切回 UI 线程，控制器无共享可变 UI 状态
- 服务端会话表带快照遍历，连接/断开与遍历并发安全
- 界面色彩集中在 `Theme` 静态类（设计令牌单点维护）

## License

MIT
