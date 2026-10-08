using OeeSystem.Models;

namespace OeeSystem.Services;

public record OeeResult(
    double Availability,
    double Performance,
    double Quality,
    double Oee);

public interface IOeeService
{
    /// <summary>
    /// Menghitung OEE sesuai formula standar:
    /// - Availability = (Loading Time - Down Time) / Loading Time × 100
    /// - Performance = (CT Standar × Product Output) / Operating Time × 100
    /// - Quality = (Product Unit Processed - Defect Unit) / Product Unit Processed × 100
    /// - OEE = Availability × Performance × Quality
    /// </summary>
    /// <param name="loadingTime">Total Shift Time (Loading Time)</param>
    /// <param name="downTime">Total Downtime (Planned + Unplanned)</param>
    /// <param name="totalCount">Total Product Output (Good + Reject)</param>
    /// <param name="goodCount">Good Product Output</param>
    /// <param name="standarCycleTime">Standar Cycle Time dalam detik</param>
    OeeResult CalculateOee(
        TimeSpan loadingTime,
        TimeSpan downTime,
        int totalCount,
        int goodCount,
        double standarCycleTime);

    MachineStatus GetRealTimeStatus(Machine machine, JobRun? activeJobRun, bool hasOpenDowntime);
}


