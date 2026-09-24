# 免编译包 · 直接跑

这个目录是**当前版本的免编译包**，随每次发版由 `TimePlanner/release.ps1` 自动刷新。
下载下来就能用，不用装 SDK、也不用编译（仓库最新一版是 **sp1 特别版**，经典版那条线最新是 1.5.2）。

| 文件 | 作用 |
| --- | --- |
| `TimePlanner.exe` | 主程序：规划今日与本周，平时缩在托盘里 |
| `TimePlanner.Widget.exe` | 桌面插件：摆在桌面上显示任务、勾选完成 |
| `启动时间规划.cmd` | 双击启动（等同双击主程序） |
| `使用说明.txt` | 上手说明与版本更新记录 |

## 两点注意

- 两个 exe **必须放在同一个文件夹里**，插件由主程序按同目录查找并启动。
- 需要 .NET Framework 4.8，Windows 10 / 11 已经自带，不用另外装。

数据放在 `%APPDATA%\TimePlanner\data.json`，主程序与插件共用；删掉它就是完全重置。
想自己编译或者看源码，见 [../TimePlanner/README.md](../TimePlanner/README.md)。
