using QrawlerEngine.Models;
using QuantConnect.Data.Market;

namespace QuantConnect.Qrawler.DataFeeds
{
    static class DataConverter
    {
        public static TradeBar ToTradeBar(this Ohlc ohlc, Symbol symbol)
        {
            // member intialization order is important here! (Time -> EndTime)
            var tb = new TradeBar()
            {
                Symbol = symbol,
                Open = ohlc.Open,
                High = ohlc.High,
                Low = ohlc.Low,
                Close = ohlc.Close,
                Volume = ohlc.Volume ?? 0,
                Time = ohlc.StartTime.ToDateTimeUtc(),
                EndTime = ohlc.Time.ToDateTimeUtc(),
            };
            return tb;
        }
    }
}
