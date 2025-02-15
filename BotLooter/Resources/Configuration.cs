using BotLooter.Steam.Contracts;
using BotLooter.Steam.Exceptions;
using Newtonsoft.Json;

namespace BotLooter.Resources;

public class Configuration
{
    public string? LootTradeOfferUrl { get; set; } = null;
    public List<string>? LootTradeOfferUrls { get; set; } = null;
    
    public string SecretsDirectoryPath { get; set; } = "";
    public string AccountsFilePath { get; set; } = "";
    public string SteamSessionsDirectoryPath { get; set; } = "";
    public string IgnoreAccountsFilePath { get; set; } = "";
    
    public string ProxiesFilePath { get; set; } = "proxies.txt";
    
    public string SuccessfulLootsExportFilePath { get; set; } = "";
    
    public int DelayBetweenAccountsSeconds { get; set; } = 30;
    public int DelayInventoryEmptySeconds { get; set; } = 10;
    
    public bool AskForApproval { get; set; } = true;
    public bool ExitOnFinish { get; set; } = false;
    
    public int LootThreadCount { get; set; } = 1;
    public List<string> Inventories { get; set; } = new();

    public int MaxItemsPerTrade { get; set; } = 8192;

    public int MaxItemsPerAllTrades { get; set; } = int.MaxValue;
    
    public bool IgnoreNotMarketable { get; set; } = false;
    public bool IgnoreMarketable { get; set; } = false;

    public List<string> LootOnlyItemsWithNames { get; set; } = new();
    public List<string> IgnoreItemsWithNames { get; set; } = new(); 
    public List<int> LootOnlyItemsWithAppids { get; set; } = new();
    public List<int> IgnoreItemsWithAppids { get; set; } = new();

    public List<string> LootOnlyItemsWithTags { get; set; } = new();
    public List<string> IgnoreItemsWithTags { get; set; } = new();

    public static async Task<(Configuration? Config, string Message)> TryLoadFromFile(string? filePath = null)
    {
        filePath ??= "BotLooter.Config.json";

        if (!File.Exists(filePath))
        {
            return (null, $"Configuration file '{filePath}' not found");
        }

        var contents = await File.ReadAllTextAsync(filePath);

        Configuration config;
        
        var errors = new List<string>();
        
        try
        {
            var deserialized = JsonConvert.DeserializeObject<Configuration>(contents, new JsonSerializerSettings
            {
                Error = (_, args) =>
                {
                    errors.Add(args.ErrorContext.Error.Message);
                }
            });

            if (deserialized is null)
            {
                return (null, "The config has an invalid format, details:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
            }

            config = deserialized;
        }
        catch
        {
            return (null, "The config has an invalid format, details:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        if (config.LootTradeOfferUrl is not null)
        {
            try
            {
                _ = new TradeOfferUrl(config.LootTradeOfferUrl);
            }
            catch (InvalidTradeOfferUrlException)
            {
                return (null, "Config parameter 'LootTradeOfferUrl' is either empty or incorrectly filled.");
            }
        }

        if (config.LootTradeOfferUrls is not null)
        {
            foreach (var tradeOfferUrl in config.LootTradeOfferUrls)
            {
                try
                {
                    _ = new TradeOfferUrl(tradeOfferUrl);
                }
                catch (InvalidTradeOfferUrlException)
                {
                    return (null, "Config parameter 'LootTradeOfferUrls' contains invalid trade offer links.");
                }
            }
        }

        if (config.LootTradeOfferUrl is null && config.LootTradeOfferUrls is null)
        {
            return (null, "Either 'LootTradeOfferUrl' or 'LootTradeOfferUrls' must be filled.");
        }

        if (config.Inventories?.Count == 0)
        {
            return (null, """
            The config parameter 'Inventories' does not contain any inventories for looting.
            Format: appId/contextId
            Example with a CS:GO inventory:
            ...
            "Inventories": [
                "730/2"
            ],
            ...
            """);
        }

        if (config.MaxItemsPerTrade > 8192)
        {
            return (null, $"The config parameter 'MaxItemsPerTrade' must not exceed 8192, current value: {config.MaxItemsPerTrade}");
        }
            
        return (config, "");
    }
}
