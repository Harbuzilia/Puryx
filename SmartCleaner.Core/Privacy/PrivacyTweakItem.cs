namespace SmartCleaner.Core.Privacy;

public enum PrivacyCategory
{
    Telemetry,
    Advertising,
    FeedbackAndErrors,
    TrackingAndSensors
}

public class PrivacyTweakItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PrivacyCategory Category { get; set; } = PrivacyCategory.Telemetry;
    public string CategoryName => Category switch
    {
        PrivacyCategory.Telemetry => "Телеметрия и диагностика",
        PrivacyCategory.Advertising => "Реклама и предложения",
        PrivacyCategory.FeedbackAndErrors => "Отчёты и отзывы",
        PrivacyCategory.TrackingAndSensors => "Слежка и датчики",
        _ => "Общее"
    };

    public bool IsApplied { get; set; }
    public bool IsRecommended { get; set; } = true;
    public string? RegistryRoot { get; set; } // "HKLM" or "HKCU"
    public string? SubKeyPath { get; set; }
    public string? ValueName { get; set; }
    public object? DisabledValue { get; set; }
    public object? DefaultValue { get; set; }
    public string? ServiceName { get; set; }
}
