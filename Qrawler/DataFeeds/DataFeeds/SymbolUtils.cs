using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuantConnect.Configuration;

namespace QuantConnect.Lean.Engine.DataFeeds.Qrawler
{
    public class SymbolTranslator
    {
        private readonly Dictionary<string, string> _feedMap;
        private readonly Dictionary<string, string> _feedReverseMap;

        public SymbolTranslator()
        {
            _feedMap = ReadConfigExchangeMap();
            _feedReverseMap = _feedMap.ToDictionary(x => x.Value, x => x.Key);
        }

        private static Dictionary<string, string> ReadConfigExchangeMap()
        {
            return Config.GetValue<Dictionary<string, string>>("qrawler.feed-map");
        }

        /// <summary>
        /// Split a Lean symbol string into qrawler symbol and feed name
        /// </summary>
        /// <param name="symbol"></param>
        /// <param name="qsymbol"></param>
        /// <param name="qfeed"></param>
        /// <exception cref="ArgumentException"></exception>
        public void Translate(Symbol symbol, out string qsymbol, out string qfeed)
        {
            string[] split = SplitSymbol(symbol);
            if (split.Length != 2)
                throw new ArgumentException($"Unexpected number of colons in symbol", nameof(symbol));

            qsymbol = split[1];
            if (!_feedMap.TryGetValue(split[0], out qfeed))
            {
                throw new ArgumentException($"Exchange not found in exchange map: {split[0]}", nameof(symbol));
            }
        }

        public string TranslateBack(string qsymbol, string qfeed)
        {
            return $"{_feedReverseMap[qfeed]}:{qsymbol}";
            
        }

        private static string[] SplitSymbol(Symbol symbol)
        {
            return symbol.Value.Split(':');
        }

        public static string TranslateResolution(Resolution res)
        {
            switch (res)
            {
                case Resolution.Hour:
                    return "H1";
                case Resolution.Daily:
                    return "D1";
                case Resolution.Minute:
                    return "M1";
                default:
                    throw new ArgumentException("Unsupported resolution.", nameof(res));
            }
        }
    }
}
