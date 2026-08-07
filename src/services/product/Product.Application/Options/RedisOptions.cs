namespace Product.Application.Options;

public sealed class RedisOptions
{
    public const string SectionName = "Redis";
    public string ConnectionString { get; set; } = "localhost:6379";
    public string KeyPrefix { get; set; } = "edcp";
}