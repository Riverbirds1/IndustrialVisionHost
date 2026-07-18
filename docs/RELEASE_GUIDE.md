# Release 发布说明

## 发布目标

项目采用 Windows x64 自包含文件夹发布。目标电脑不需要预先安装.NET 6运行时，发布目录会同时携带WPF程序、OpenCV、SQLite和.NET运行文件。

采用文件夹发布而不是单文件发布，是为了让OpenCV和SQLite本地动态库的加载路径更直观，也便于出现问题时检查缺失文件。

## 当前版本

- 产品版本：1.0.0
- 目标系统：64位Windows 10/11
- 目标框架：.NET 6 Windows
- 发布配置：Release、win-x64、自包含
- 主程序：`IndustrialVisionHost.exe`

## 生成交付目录

在项目根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish-release.ps1
```

默认输出位置：

```text
artifacts\release\IndustrialVisionHost-v1.0.0-win-x64
```

发布脚本会完成以下工作：

1. 执行Release自包含发布。
2. 检查主程序、OpenCV和SQLite必要文件是否存在。
3. 写入`VERSION.txt`版本说明。
4. 生成`SHA256SUMS.txt`文件校验清单。
5. 输出文件数量和目录总大小。

## 运行与数据位置

双击`IndustrialVisionHost.exe`启动。程序第一次运行会在当前用户目录初始化默认账号、系统设置和数据库。

运行数据不保存在发布目录，而位于：

```text
%LOCALAPPDATA%\IndustrialVisionHost
```

这样重新覆盖发布目录升级程序时，不会直接删除配方、历史记录、日志、报警和用户数据。

## 发布前验收

正式对外提供版本前应依次确认：

1. Debug和Release构建均为0警告、0错误。
2. 全套自动化测试通过。
3. 在发布目录中启动程序并完成登录。
4. 打开模拟相机，执行一次OK和一次NG检测。
5. 启动PLC模拟器并验证文本PLC、Modbus连接。
6. 关闭程序后确认进程退出，再重新启动验证数据仍可读取。
7. 最好复制到一台未安装Visual Studio的64位Windows电脑再次验证。

## 版本号规则

版本号采用`主版本.次版本.修订号`：

- 不兼容的大改动提升主版本，例如2.0.0。
- 增加兼容功能提升次版本，例如1.1.0。
- 修复错误提升修订号，例如1.0.1。

GitHub首个完整演示版本计划使用`v1.0.0`标签。
