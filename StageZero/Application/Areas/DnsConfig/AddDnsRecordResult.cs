namespace StageZero.Application.Areas.DnsConfig;

public class AddDnsRecordResult
{
    public string RecordName { get; set; } = string.Empty;
    public string RecordType { get; set; } = string.Empty;
    public bool AutoUpdate { get; set; } = true;
    public string? RecordId { get; set; }
    public string? Content { get; set; }
}

