﻿using System.Net;
using BotLooter.Resources;
using BotLooter.Steam;
using BotLooter.Steam.Contracts;
using BotLooter.Steam.Contracts.Responses;
using Polly;
using Polly.Retry;
using RestSharp;
using Serilog;

namespace BotLooter.Looting;

public class LootClient
{
    public SteamAccountCredentials Credentials { get; }
    
    private readonly SteamUserSession _steamSession;
    private readonly SteamWeb _steamWeb;

    private readonly AsyncRetryPolicy<RestResponse<SendTradeOfferResponse>> _sendTradeOfferPolicy;

    public LootClient(SteamAccountCredentials credentials, SteamUserSession steamSession, SteamWeb steamWeb)
    {
        Credentials = credentials;
        _steamSession = steamSession;
        _steamWeb = steamWeb;

        _sendTradeOfferPolicy = Policy
            .HandleResult<RestResponse<SendTradeOfferResponse>>(res => 
            {
                if (res.StatusCode != HttpStatusCode.InternalServerError) 
                {
                    return false;
                }

                return res.Content?.ToLower().Contains("please try again later") ?? false;
            })
            .WaitAndRetryAsync(2, _ => TimeSpan.FromSeconds(10));
    }

    public async Task<LootResult> TryLoot(TradeOfferUrl tradeOfferUrl, Configuration config)
    {
        var (isSession, ensureSessionMessage) = await _steamSession.TryEnsureSession();

        if (!isSession)
        {
            return new LootResult(false, ensureSessionMessage);
        }
        
        Log.Logger.Debug("{Login} : {SessionType}", Credentials.Login, ensureSessionMessage);

        var (assets, getAssetsMessage) = await GetAssetsToSend(config);

        if (assets is null)
        {
            return new LootResult(false, getAssetsMessage);
        }

        if (assets.Count < 1)
        {
            return new LootResult(false, "Empty inventories");
        }

        var tradeOffer = new JsonTradeOffer
        {
            NewVersion = true,
            Version = 4
        };

        foreach (var inventoryAsset in assets)
        {
            if (!int.TryParse(inventoryAsset.Amount, out var amount))
            {
                amount = 1;
            }
            
            var asset = new TradeOfferAsset
            {
                AppId = $"{inventoryAsset.Appid}",
                ContextId = $"{inventoryAsset.Contextid}",
                Amount = amount,
                AssetId = inventoryAsset.Assetid
            };

            tradeOffer.Me.Assets.Add(asset);
        }

        var (tradeOfferId, sendTradeOfferMessage) = await SendTradeOffer(tradeOfferUrl, tradeOffer);

        if (tradeOfferId is null)
        {
            return new LootResult(false, sendTradeOfferMessage);
        }

        var confirmationResult = await _steamSession.AcceptConfirmation(tradeOfferId.Value);

        if (!confirmationResult)
        {
            return new LootResult(false, "Could not confirm the trade");
        }

        return new LootResult(true, $"Looted! Items: {tradeOffer.Me.Assets.Count}", tradeOffer.Me.Assets.Count);
    }

    private async Task<(List<Asset>? Assets, string message)> GetAssetsToSend(Configuration config)
    {
        var filteredOut = new HashSet<string>();
        
        var assets = new List<Asset>();

        var index = 0;
        
        foreach (var inventory in config.Inventories)
        {
            var split = inventory.Split('/');

            if (split.Length != 2)
            {
                continue;
            }

            var inventoryId = split[0];
            var contextId = split[1];
            
            var inventoryResponse = await _steamWeb.LoadInventory(inventoryId, contextId);

            if (inventoryResponse is null || inventoryResponse is not {} inventoryData)
            {
                return (null, $"Could not retrieve inventory {inventory}.");
            }
            
            foreach (var description in inventoryData.Descriptions.Where(d => !d.Tradable))
            {
                filteredOut.Add(description.Classid);
            }

            if (config.IgnoreNotMarketable)
            {
                foreach (var description in inventoryData.Descriptions.Where(d => !d.Marketable))
                {
                    filteredOut.Add(description.Classid);
                }
            }

            if (config.IgnoreMarketable)
            {
                foreach (var description in inventoryData.Descriptions.Where(d => d.Marketable))
                {
                    filteredOut.Add(description.Classid);
                }
            }

            if (config.LootOnlyItemsWithNames.Count > 0)
            {
                foreach (var description in inventoryData.Descriptions.Where(d => !config.LootOnlyItemsWithNames.Contains(d.MarketName)))
                {
                    filteredOut.Add(description.Classid);
                }
            }

            if (config.IgnoreItemsWithNames.Count > 0)
            {
                foreach (var description in inventoryData.Descriptions.Where(d => config.IgnoreItemsWithNames.Contains(d.MarketName)))
                {
                    filteredOut.Add(description.Classid);
                }
            }

            if (config.LootOnlyItemsWithAppids.Count > 0)
            {
                foreach (var description in inventoryData.Descriptions.Where(d => d.Appid == 753 && !config.LootOnlyItemsWithAppids.Contains(d.MarketFeeApp)))
                {
                    filteredOut.Add(description.Classid);
                }
            }

            if (config.IgnoreItemsWithAppids.Count > 0)
            {
                foreach (var description in inventoryData.Descriptions.Where(d => d.Appid == 753 &&  config.IgnoreItemsWithAppids.Contains(d.MarketFeeApp)))
                {
                    filteredOut.Add(description.Classid);
                }
            }

            if (config.LootOnlyItemsWithTags.Count > 0)
            {
                foreach (var description in inventoryData.Descriptions.Where(d => d.Tags is not null && !d.Tags.Any(t => config.LootOnlyItemsWithTags.Contains(t.LocalizedTagName))))
                {
                    filteredOut.Add(description.Classid);
                }
            }

            if (config.IgnoreItemsWithTags.Count > 0)
            {
                foreach (var description in inventoryData.Descriptions.Where(d => d.Tags is not null && d.Tags.Any(t => config.IgnoreItemsWithTags.Contains(t.LocalizedTagName))))
                {
                    filteredOut.Add(description.Classid);
                }
            }

            var notFilteredOutAssets = inventoryData.Assets.Where(a => !filteredOut.Contains(a.Classid));
            
            assets.AddRange(notFilteredOutAssets);

            var isLast = index == config.Inventories.Count - 1;
            
            if (!isLast)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
            
            index++;
        }

        assets = assets
            .Take(config.MaxItemsPerTrade)
            .ToList();
        
        return (assets, "");
    }

    private async Task<(ulong? TradeOfferId, string Message)> SendTradeOffer(TradeOfferUrl tradeOfferUrl, JsonTradeOffer tradeOffer)
    {
        var sendTradeOfferResponse = await _sendTradeOfferPolicy.ExecuteAsync(() => _steamWeb.SendTradeOffer(tradeOfferUrl, tradeOffer));

        if (sendTradeOfferResponse.Data is not { } sendTradeOfferData)
        {
            return (null, $"Could not send trade - {sendTradeOfferResponse.StatusCode} {sendTradeOfferResponse.Content}");
        }

        if (!string.IsNullOrWhiteSpace(sendTradeOfferData.Error))
        {
            if (sendTradeOfferData.Error.Contains("Steam Guard enabled for at least 15 days"))
            {
                var cantTradeTime = await _steamWeb.GetHelpWhyCantITradeTime();

                return (null, $"Trade will be available in {cantTradeTime}");
            }
            
            return (null, $"Could not send trade - {sendTradeOfferData.Error}");
        }

        if (!ulong.TryParse(sendTradeOfferData.TradeofferId, out var tradeOfferId))
        {
            return (null, $"Could not parse trade ID - {sendTradeOfferResponse.StatusCode} {sendTradeOfferResponse.Content}");
        }

        return (tradeOfferId, "");
    }
}
