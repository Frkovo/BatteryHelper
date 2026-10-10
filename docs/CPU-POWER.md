# CPU 功率扣除核显

## 已确认的包含关系

本机为 Intel Core Ultra X7 358H / Arc B390。旧版任务栏 CPU 来自 LibreHardwareMonitor 的 `CPU Package`，使用 RAPL `MSR_PKG_ENERGY_STATUS`（0x611）。核显对应 RAPL `MSR_PP1_ENERGY_STATUS`（0x641）。

Intel 的 [Core Ultra Series 3 功率管理寄存器文档](https://edc.intel.com/content/www/us/en/design/publications/core-ultra-series-3-processors-cfg-and-mem-registers/001/-package-rapl-limit-0-0-0-mchbar-pcu-offset-59a0/)明确列出封装功率预算中的 IA 和 GT 功率域。Intel 员工的 [RAPL 域说明](https://community.intel.com/t5/Software-Tuning-Performance/Xeon-E5-MSR-PP1-ENERGY-STATUS-read-write-Error/m-p/1056691)解释封装包含 CPU 核心、PP1 核显与其他封装组件。

项目固定的上游实现将 0x611 标为 `CPU Package`、0x641 标为 `CPU Graphics`；Windows 上原本隐藏后者，另在 Intel 集成 GPU 设备下将同一计数器标为 `GPU Power`。本次补丁在 CPU 设备上保留 PP1，使封装与核显在同一次 `IntelCpu.Update()` 中采样。

## 1.0.2 的显示口径

```text
任务栏 CPU = RAPL CPU Package − 同一 CPU 的 RAPL CPU Graphics（PP1）
本机任务栏 GPU = 同一份 RAPL CPU Graphics（PP1）
```

只扣除确认属于该 CPU 封装的核显域，不能使用“当前显示的 GPU”作为减数，因为它可能是独显。独显功率保留独立展示。IGCL 可用于 GPU 展示回退；没有确认同封装范围的 IGCL 值不会代替 PP1 做扣除。

扣除后的 CPU 数值仍可能包含封装内其他组件，不能称为“纯 CPU 核心功率”。CPU 核心域 PP0 不是本次采用的口径。不同设备的包含范围不能仅凭 `GPU` 名称判断；无法确认时保留原始测量范围并说明限制。

两个输入必须有效、未超过 5 秒且采样时间差不超过 250 毫秒。真实零值有效。若核显功率大于封装、计数器重置、读数缺失或过期，显示 `— W` 并说明原因，不用绝对值或钳位伪造零功率。

命名管道快照保留 `CpuPackagePower` 和 `IntegratedGpuPower` 原始读数；详情显示计算式与来源。新增字段可选，旧版快照仍可反序列化。数据切换和休眠恢复继续遵循原有基线与过期规则。
