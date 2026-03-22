using SIV.Application.DTOs;
using SIV.Application.Interfaces;
using SIV.Domain.Games;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SIV.Infrastructure.Pricing;

public sealed class SteamMarketPriceProvider : IPriceProvider
{
    private readonly HttpClient _http;
    private readonly uint _appId;
    public string SourceName => "steam_market";

    public SteamMarketPriceProvider(HttpClient http, IGameDefinition gameDefinition)
    {
        _http = http;
        _appId = gameDefinition.AppId;
    }

    public async Task<IReadOnlyList<PriceResult>> GetPricesAsync(IReadOnlyList<string> marketHashNames, CancellationToken ct = default)
    {
        var results = new List<PriceResult>();

        foreach (var name in marketHashNames)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var encoded = Uri.EscapeDataString(name);
                var response = await _http.GetAsync(
                    $"https://steamcommunity.com/market/priceoverview/?appid={_appId}&currency=1&market_hash_name={encoded}", ct);

                response.EnsureSuccessStatusCode();

                var data = await response.Content.ReadFromJsonAsync<SteamPriceResponse>(ct);
                if (data?.Success == true && data.LowestPrice is not null)
                {
                    var price = ParsePrice(data.LowestPrice);
                    results.Add(new PriceResult(name, price, SourceName));
                }
                else
                {
                    results.Add(new PriceResult(name, null, null));
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                throw;
            }
            catch
            {
                results.Add(new PriceResult(name, null, null));
            }
        }

        return results;
    }

    private static decimal? ParsePrice(string raw)
    {
        var cleaned = raw.Replace("$", "").Replace(",", "").Trim();
        return decimal.TryParse(cleaned, System.Globalization.CultureInfo.InvariantCulture, out var val) ? val : null;
    }

    private sealed class SteamPriceResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("lowest_price")]
        public string? LowestPrice { get; set; }

        [JsonPropertyName("median_price")]
        public string? MedianPrice { get; set; }
    }
}
