using System.IO;
using System.Runtime.InteropServices;

namespace ThermalGuard.Services;

public sealed class SystemInfoService
{
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;

    public SystemSnapshot Read()
    {
        var cpu = ReadCpuUsage();
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        GlobalMemoryStatusEx(ref memory);
        var usedMemory = memory.TotalPhysical - memory.AvailablePhysical;
        var root = Path.GetPathRoot(AppContext.BaseDirectory)!;
        var drive = new DriveInfo(root);
        var memoryPercent = memory.TotalPhysical == 0 ? 0 : usedMemory * 100d / memory.TotalPhysical;
        var diskPercent = drive.TotalSize == 0 ? 0 : (drive.TotalSize - drive.AvailableFreeSpace) * 100d / drive.TotalSize;
        return new(cpu, memoryPercent, diskPercent,
            $"{ToGb(usedMemory):F1} / {ToGb(memory.TotalPhysical):F1} GB",
            $"{root} 可用 {ToGb((ulong)drive.AvailableFreeSpace):F1} GB");
    }

    private double ReadCpuUsage()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime)) return 0;
        var idle = ToUInt64(idleTime);
        var kernel = ToUInt64(kernelTime);
        var user = ToUInt64(userTime);
        var idleDelta = idle - _previousIdle;
        var totalDelta = kernel - _previousKernel + user - _previousUser;
        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;
        return totalDelta == 0 ? 0 : Math.Clamp((totalDelta - idleDelta) * 100d / totalDelta, 0, 100);
    }

    private static double ToGb(ulong bytes) => bytes / 1024d / 1024 / 1024;
    private static ulong ToUInt64(FileTime time) => ((ulong)time.High << 32) | time.Low;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }
}

public sealed record SystemSnapshot(double CpuPercent, double MemoryPercent,
    double DiskPercent, string MemoryText, string DiskText);
