namespace StageZero.Application.Areas.DnsConfig;

public class AddCloudflareProviderResult
{
    public string Name { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public string ZoneId { get; set; } = string.Empty;
    public string ZoneName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

