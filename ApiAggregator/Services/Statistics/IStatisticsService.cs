using ApiAggregator.Models;

namespace ApiAggregator.Services.Statistics;

public interface IStatisticsService
{
    void Record(string apiName, double elapsedMs, bool failed);

    IReadOnlyList<ApiStatisticsSnapshot> GetSnapshots();

    ApiStatisticsSnapshot? GetSnapshot(string apiName);

    IReadOnlyList<AnomalyReport> DetectAnomalies();
}
