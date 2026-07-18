# 稳定性与性能基线

## 测试环境

- 日期：2026-07-18
- 操作系统：Windows 11 家庭中文版
- CPU：AMD Ryzen 7 6800H with Radeon Graphics
- 内存：约15.2 GB
- .NET SDK：6.0.401
- 构建配置：Release
- 设备条件：无真实相机和PLC，使用模拟相机与本机TCP/Modbus回环

## 当前实测结果

| 项目 | 工作量 | 实测结果 | 自动失败阈值 |
|---|---:|---:|---:|
| 视觉处理 | 300帧，640×480 | 平均约1.896 ms/帧 | 平均小于25 ms/帧 |
| 视觉内存 | 20帧预热后处理300帧 | 私有内存增长约1.76 MB | 增长小于128 MB |
| TCP/Modbus | 400次本机请求 | 平均约0.124 ms/次 | 平均小于20 ms/次 |
| SQLite | 写入500条并分页查询、统计 | 总耗时约533 ms | 总耗时小于10秒 |
| 相机生命周期 | 连续启动/停止10轮 | 全部成功 | 不允许失败或残留运行状态 |
| TCP故障注入 | 连续服务端中断10轮 | 全部识别断线 | 每轮2秒内识别 |

## 运行方式

### 快速自动化基线

```powershell
powershell -ExecutionPolicy Bypass -File scripts\run-stability-tests.ps1
```

或者直接执行：

```powershell
dotnet test tests\IndustrialVisionHost.Tests\IndustrialVisionHost.Tests.csproj -c Release --filter "Category=Stability"
```

### 独立长时间运行

默认运行10分钟，每5秒采样，并定期模拟相机、文本PLC和Modbus故障：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\run-long-stability.ps1
```

例如运行8小时：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\run-long-stability.ps1 -DurationMinutes 480
```

每次运行会在`artifacts\stability\时间戳`目录生成：

- `samples.csv`：每个采样时刻的内存、线程、句柄、耗时和业务计数，可用Excel绘制趋势图。
- `summary.json`：本次开始/结束时间、总循环、峰值、故障恢复和最终通过状态。
- `stability-history.db`：本次运行实际写入的独立SQLite检测历史，不污染正式生产数据。

短时程序链路验收结果：7秒完成312个循环，文本通信成功319次、Modbus成功638次、数据库写入31次；注入21次故障并全部恢复，非预期错误为0，`Passed=true`。首帧包含OpenCV和网络预热，因此最大视觉耗时不作为稳定态平均耗时。

## 如何理解这些数据

- 这些结果用于比较同一电脑上后续代码修改前后的变化，不代表真实工业相机或PLC的现场性能。
- 自动失败阈值故意明显宽于当前实测值，用于发现数量级退化，而不是把正常系统抖动当作故障。
- 内存测试在预热和强制GC后比较进程私有内存，主要发现未释放`Mat`等明显泄漏；它不能替代数小时的真实进程监控。
- 长稳运行器用于补足数小时进程监控，并保留逐点CSV；判断泄漏应关注预热后的持续上升趋势，而不是只比较第一个与最后一个采样点。
- 本机回环通信不包含交换机、现场电磁干扰和PLC扫描周期，真实设备测试必须重新记录网络基线。

## 后续真实设备基线

接入工业相机或PLC后，应追加记录：设备型号、SDK/固件版本、曝光和帧率、图像分辨率、网络拓扑、PLC扫描周期、连续运行时间、最大/平均耗时、内存峰值及断线恢复次数。
