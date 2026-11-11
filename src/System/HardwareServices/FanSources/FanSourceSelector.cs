using System;

namespace LiteMonitor.src.SystemServices
{
    /// <summary>
    /// 纯逻辑：按优先级挑选当前可用的风扇数据源。
    ///
    /// 规则很简单但很重要：只有 Status == Ok 且确实读到了值才对外供数；
    /// Frozen（数据块静止）/ Error / Unsupported / Waiting 一律跳过，
    /// 自动回退到下一优先级。高优先级恢复后下一帧自动切回，无需重启程序。
    /// </summary>
    internal static class FanSourceSelector
    {
        public static int Select(IFanSource[]? sources)
        {
            if (sources == null) return -1;

            for (int i = 0; i < sources.Length; i++)
            {
                IFanSource? source = sources[i];
                if (source == null) continue;
                if (source.Status != FanSourceStatus.Ok) continue;
                if (!source.Reading.HasAny) continue;
                return i;
            }

            return -1;
        }

        /// <summary>取指定监控项的转速；无可用源时返回 null（界面显示 "--"）。</summary>
        public static float? Read(IFanSource[]? sources, string key)
        {
            int index = Select(sources);
            if (index < 0) return null;

            FanRpmPair pair = sources![index].Reading;
            return key switch
            {
                "CPU.Fan" => pair.Cpu,
                "GPU.Fan" => pair.Gpu,
                _ => null
            };
        }

        /// <summary>是否至少有一个活的数据源；用于禁止把缓存里的旧转速兜回来。</summary>
        public static bool HasLiveSource(IFanSource[]? sources) => Select(sources) >= 0;
    }
}
