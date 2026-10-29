using System.Runtime.InteropServices;

namespace LXLPCL.Core;

public static class MemoryInfo
{
    [StructLayout(LayoutKind.Sequential)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    private const double BytesPerMb = 1048576d;

    public static (long TotalMb, long AvailableMb) Read()
    {
        try
        {
            var status = new MemoryStatusEx();
            if (GlobalMemoryStatusEx(status))
                return ((long)(status.TotalPhys / BytesPerMb), (long)(status.AvailPhys / BytesPerMb));
        }
        catch (Exception ex)
        {
            Logger.Error("读取系统内存失败: " + ex.Message);
        }
        return (0, 0);
    }

    public static long TotalMb => Read().TotalMb;

    public static long AvailableMb => Read().AvailableMb;

    public static long UsedMb
    {
        get
        {
            var (total, available) = Read();
            return Math.Max(0, total - available);
        }
    }

    /// <summary>
    /// 自动分配的内存：按系统总内存比例推荐，空闲内存越多分配越保守。
    /// </summary>
    public static int AutoMb
    {
        get
        {
            var total = TotalMb;
            if (total <= 0) return 2048;
            var ratio = total switch
            {
                < 4096 => 0.50,
                < 8192 => 0.45,
                < 16384 => 0.40,
                _ => 0.35
            };
            var value = (int)Math.Round(total * ratio / 256) * 256;
            return Math.Clamp(value, 1024, 32768);
        }
    }

    /// <summary>版本单独设置优先，其次自动配置，最后使用手动值。</summary>
    public static int Resolve(int? overrideMb = null)
    {
        if (overrideMb is > 0) return overrideMb.Value;
        return AppState.Config.AutoMemory
            ? AutoMb
            : Math.Max(512, AppState.Config.MemoryMb);
    }

    public static string FormatGb(long mb) => $"{mb / 1024.0:0.#} GB";
}
