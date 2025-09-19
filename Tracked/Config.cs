namespace Tracked;

public class Config
{
    public bool Debug { get; set; } = false;

    public int CoinEscapeMultiplier { get; set; } = 3;
    public string EndpointUrl { get; set; } = "https://example.com/upload";
    public string Apikey { get; set; } = "1234";
}