namespace Product.Application.Options;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";
    public int PollIntervalMs { get; set; } = 2000;
    public int BatchSize { get; set; } = 100;
}