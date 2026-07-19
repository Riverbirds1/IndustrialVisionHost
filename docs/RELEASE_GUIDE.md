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

## v1.0.0公开发布包复验

2026-07-19从GitHub Release重新下载公开文件`IndustrialVisionHost-v1.0.0-win-x64.zip`，而不是复用源码目录中的本地发布结果，并完成以下检查：

- 下载文件大小为109,473,796字节。
- ZIP的SHA-256为`312C61A5DDA0280E38844E3007063D296AB5284D9F573A33F2B43F2452520C0C`，与Release附带校验文件完全一致。
- 解压后共513个文件，主程序、OpenCV、SQLite、版本说明和内部文件校验清单均存在。
- `IndustrialVisionHost.exe`产品版本为`1.0.0`，文件版本为`1.0.0.0`。
- 从系统临时目录启动成功，显示“登录 - 工业视觉检测上位机”，随后能够正常关闭。
- 启动路径不在源码目录或本地构建输出目录中，证明公开压缩包不依赖Visual Studio和项目源码运行。

当前电脑未启用Windows Sandbox，因此这次复验证明的是“公开下载包脱离源码启动”，不等同于全新Windows用户、不同电脑或真实设备环境验收。

## 版本号规则

版本号采用`主版本.次版本.修订号`：

- 不兼容的大改动提升主版本，例如2.0.0。
- 增加兼容功能提升次版本，例如1.1.0。
- 修复错误提升修订号，例如1.0.1。

GitHub首个完整演示版本计划使用`v1.0.0`标签。
