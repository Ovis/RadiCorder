using System.Diagnostics;
using ZLogger;

namespace RadiCorder.Application
{
    public class MemoryUsageMiddleware(RequestDelegate next, ILogger<MemoryUsageMiddleware> logger)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            var process = Process.GetCurrentProcess();
            var memoryUsageBefore = process.WorkingSet64;
            var privateMemoryBefore = process.PrivateMemorySize64;
            var managedMemoryBefore = GC.GetTotalMemory(false);
            var totalAllocatedBefore = GC.GetTotalAllocatedBytes(false);
            var gcMemoryInfoBefore = GC.GetGCMemoryInfo();

            await next(context);

            var memoryUsageAfter = process.WorkingSet64;
            var privateMemoryAfter = process.PrivateMemorySize64;
            var managedMemoryAfter = GC.GetTotalMemory(false);
            var totalAllocatedAfter = GC.GetTotalAllocatedBytes(false);
            var gcMemoryInfoAfter = GC.GetGCMemoryInfo();

            logger.ZLogDebug($"Memory Usage Before: {memoryUsageBefore / 1024 / 1024} MB");
            logger.ZLogDebug($"Memory Usage After: {memoryUsageAfter / 1024 / 1024} MB");
            logger.ZLogDebug($"Private Memory Before: {privateMemoryBefore / 1024 / 1024} MB");
            logger.ZLogDebug($"Private Memory After: {privateMemoryAfter / 1024 / 1024} MB");
            logger.ZLogDebug($"Managed Memory Before: {managedMemoryBefore / 1024 / 1024} MB");
            logger.ZLogDebug($"Managed Memory After: {managedMemoryAfter / 1024 / 1024} MB");
            logger.ZLogDebug($"Managed Allocated Before: {totalAllocatedBefore / 1024 / 1024} MB");
            logger.ZLogDebug($"Managed Allocated After: {totalAllocatedAfter / 1024 / 1024} MB");
            logger.ZLogDebug($"Managed Allocated Delta: {(totalAllocatedAfter - totalAllocatedBefore) / 1024 / 1024} MB");
            logger.ZLogDebug($"GC Heap Size Before: {gcMemoryInfoBefore.HeapSizeBytes / 1024 / 1024} MB");
            logger.ZLogDebug($"GC Heap Size After: {gcMemoryInfoAfter.HeapSizeBytes / 1024 / 1024} MB");
            logger.ZLogDebug($"GC Total Committed Before: {gcMemoryInfoBefore.TotalCommittedBytes / 1024 / 1024} MB");
            logger.ZLogDebug($"GC Total Committed After: {gcMemoryInfoAfter.TotalCommittedBytes / 1024 / 1024} MB");
            logger.ZLogDebug($"GC Fragmented Before: {gcMemoryInfoBefore.FragmentedBytes / 1024 / 1024} MB");
            logger.ZLogDebug($"GC Fragmented After: {gcMemoryInfoAfter.FragmentedBytes / 1024 / 1024} MB");
        }
    }

    // ミドルウェアの登録
    public static class MemoryUsageMiddlewareExtensions
    {
        public static IApplicationBuilder UseMemoryUsageMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<MemoryUsageMiddleware>();
        }
    }
}
