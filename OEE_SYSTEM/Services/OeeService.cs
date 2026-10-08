using OeeSystem.Models;

namespace OeeSystem.Services;

public class OeeService : IOeeService
{
    /// <summary>
    /// Menghitung OEE sesuai formula standar dari gambar:
    /// - Availability = (Loading Time - Down Time) / Loading Time × 100
    /// - Performance = (CT Standar × Product Output) / Operating Time × 100
    /// - Quality = (Product Unit Processed - Defect Unit) / Product Unit Processed × 100
    /// - OEE = Availability × Performance × Quality
    /// </summary>
    public OeeResult CalculateOee(
        TimeSpan loadingTime,
        TimeSpan downTime,
        int totalCount,
        int goodCount,
        double standarCycleTime)
    {
        double loadingSeconds = loadingTime.TotalSeconds;
        double downSeconds = downTime.TotalSeconds;
        
        // Operating Time = Loading Time - Down Time
        double operatingSeconds = Math.Max(0, loadingSeconds - downSeconds);

        // ✅ FORMULA SESUAI GAMBAR: Availability = (Loading Time - Down Time) / Loading Time × 100
        double availability = loadingSeconds <= 0
            ? 0
            : Math.Min(100.0, (loadingSeconds - downSeconds) / loadingSeconds * 100.0);

        // ✅ FORMULA SESUAI GAMBAR: Performance = (CT Standar × Product Output) / Operating Time × 100
        // Ideal Output = Operating Time / Standar Cycle Time
        // Performance = Actual Output / Ideal Output × 100
        double performance = (operatingSeconds <= 0 || standarCycleTime <= 0)
            ? 0
            : Math.Min(100.0, (standarCycleTime * totalCount) / operatingSeconds * 100.0);

        // ✅ FORMULA SESUAI GAMBAR: Quality = (Product Unit Processed - Defect Unit) / Product Unit Processed × 100
        // Defect Unit = Total Count - Good Count = Reject Count
        // Quality = Good Count / Total Count × 100
        double quality = totalCount <= 0
            ? 0
            : Math.Min(100.0, (double)goodCount / totalCount * 100.0);

        // OEE = Availability × Performance × Quality
        double oee = (availability / 100.0) * (performance / 100.0) * (quality / 100.0) * 100.0;

        return new OeeResult(
            Math.Round(availability, 2),
            Math.Round(performance, 2),
            Math.Round(quality, 2),
            Math.Round(oee, 2));
    }

    public MachineStatus GetRealTimeStatus(Machine machine, JobRun? activeJobRun, bool hasOpenDowntime)
    {
        if (hasOpenDowntime)
        {
            return MachineStatus.TidakAktif;
        }

        if (activeJobRun != null && activeJobRun.EndTime == null)
        {
            return MachineStatus.Aktif;
        }

        return MachineStatus.TidakAktif; // Idle menjadi Tidak Aktif
    }
}


