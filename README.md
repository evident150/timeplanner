# 时间规划 TimePlanner

Windows 桌面上的时间规划小工具：一个**主程序**用来规划今日与本周的计划，一个**桌面挂件**常驻桌面显示任务、勾选完成。

原生 WPF（.NET Framework 4.8 / C# 5），**零第三方依赖**，界面全部由 C# 代码绘制（无 XAML、无资源字典），只用 Windows 自带的 `csc.exe` 就能编译。

![主程序 · 今日](outputs/screenshots/main-today.png)

## 特性

- **主程序四个页面**：今日（完成率进度条 + 待办/已完成分组 + 日期切换 + 未完成顺延）、本周（一周概览条 + 按天分组，任务可按住拖到别的日期，拖到列表上/下边缘会自动翻页）、已完成（按完成日期归档、可撤销、可清理）、设置（插件层级/透明度/大小、主题色、全屏自动隐藏、开机自启）。
- **桌面挂件**：卡片式任务面板，点圆圈勾选完成、拖标题栏移动、八向边缘拖拽缩放、周条跳到指定日期、深色自绘右键菜单（贴在桌面 / 始终置顶 / 普通窗口）。
- **完成任务的礼花筒**：勾选框处举起一个小纸筒，一束彩纸扇形喷出并飘一句鼓励的话；当天任务全部完成再补一筒更大的。只用位移/旋转/透明度动画，放完立刻回收，不常驻占 CPU。
- **输入语法**：`写完周报 !!` 紧急、`!` 重要、`#工作` 打标签。
- **不卡**：勾选、改设置立刻响应，写盘交给后台合并；全屏游戏/演示时挂件自动隐藏；内置诊断日志记录界面卡顿。
- **不跳回顶部**：拖动改期、勾选完成后列表停在你原来的位置，每个页面各记各的。
- **省内存**：托盘图标直接调 Windows 系统接口（不把 WinForms 拉进进程）、挂件走软件渲染，空闲时两个进程的工作集都会降回 1 MB 上下（详见 [TimePlanner/README.md](TimePlanner/README.md) 的「内存」一节）。
- 挂件不会出现在任务栏和 Alt+Tab 中。

## 使用

1. 双击 `TimePlanner.exe`（会同时启动桌面挂件）。
2. 点任务左侧圆圈 = 完成；底部输入框回车 = 新增任务。
3. 主程序点 ✕ 只是隐藏到托盘，托盘右键可退出。

数据放在 `%APPDATA%\TimePlanner\data.json`，主程序与挂件共用；删掉它就是完全重置。

## 从源码构建

```powershell
cd TimePlanner
powershell -ExecutionPolicy Bypass -File build.ps1      # 编译到 dist\
powershell -ExecutionPolicy Bypass -File release.ps1    # 一条命令出整套交付（并自动删掉上一版）
```

版本号只有一个来源：`TimePlanner/version.txt`。
详细的工程说明（源码结构、性能取舍、发版流程）见 [TimePlanner/README.md](TimePlanner/README.md)。

## 截图

| 本周 | 桌面挂件 · 右键菜单 | 完成任务 |
| --- | --- | --- |
| ![本周](outputs/screenshots/main-week.png) | ![菜单](outputs/screenshots/widget-menu.png) | ![礼花](outputs/screenshots/main-firework-2.png) |
