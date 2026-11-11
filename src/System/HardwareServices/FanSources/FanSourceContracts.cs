namespace LiteMonitor.src.SystemServices
{
    /// <summary>
    /// 风扇数据源的健康状态。所有适配器共用这一套语义，管理器据此决定回退。
    /// </summary>
    internal enum FanSourceStatus
    {
        /// <summary>还没拿到第一帧（或刚失败、等待重试；也用于「等待交叉校验」）。</summary>
        Waiting,
        /// <summary>可对外提供实时读数。</summary>
        Ok,
        /// <summary>查询成功但数据连续多帧不再变化（快照冻结，不能当实时值用）。</summary>
        Frozen,
        /// <summary>查询失败（权限、驱动、超时等），可恢复。</summary>
        Error,
        /// <summary>本机不适用该适配器，已停用。</summary>
        Unsupported
    }

    /// <summary>一次采样得到的 CPU / GPU 风扇转速（RPM）；null 表示该侧无效。</summary>
    internal readonly struct FanRpmPair
    {
        public FanRpmPair(float? cpu, float? gpu)
        {
            Cpu = cpu;
            Gpu = gpu;
        }

        public float? Cpu { get; }
        public float? Gpu { get; }
        public bool HasAny => Cpu.HasValue || Gpu.HasValue;
        public bool HasBoth => Cpu.HasValue && Gpu.HasValue;
    }

    /// <summary>
    /// 风扇转速数据源适配器。
    ///
    /// 只读：适配器不得写 EC 寄存器、不得调用厂商 WMI 方法、不得启停厂商服务。
    /// 每个适配器自行在后台采集并缓存，Reading / Status / DiagnosticText 必须能在
    /// UI 线程无阻塞读取。新增品牌只需实现本接口并往 FanProfileCatalog 里加一条规则。
    /// </summary>
    internal interface IFanSource
    {
        /// <summary>稳定标识（用于诊断与日志）。</summary>
        string Id { get; }

        /// <summary>给用户看的名称。</summary>
        string DisplayName { get; }

        FanSourceStatus Status { get; }

        FanRpmPair Reading { get; }

        /// <summary>一行诊断文本，供「系统硬件详情」展示。</summary>
        string DiagnosticText { get; }
    }
}
