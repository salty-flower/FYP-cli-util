namespace DataCollection.Application.Options;

public class ThresholdOptions
{
    public const string SectionName = "Thresholds";

    public double HighConfidenceThreshold { get; set; } = 0.8;
    public double MediumConfidenceThreshold { get; set; } = 0.6;
    public double LowConfidenceThreshold { get; set; } = 0.4;
    public double MinimumConfidenceThreshold { get; set; } = 0.2;
    public double DefaultMinimumConfidence { get; set; } = 0.5;
    public double QualityFilterMinConfidence { get; set; } = 0.7;
}
