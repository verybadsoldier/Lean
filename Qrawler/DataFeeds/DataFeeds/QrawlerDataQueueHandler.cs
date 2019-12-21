using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Qrawler.Api;
using Qrawler.Interfaces.WebSocket;
using QuantConnect.Brokerages.GDAX.Messages;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Interfaces;
using QuantConnect.Packets;
using QSymbol = QrawlerEngine.Models.Symbol;
using Tick = QuantConnect.Data.Market.Tick;

namespace QuantConnect.Lean.Engine.DataFeeds.Qrawler
{
    class QrawlerDataQueueHandler : IDataQueueHandler
    {
        private readonly LiveData _qlive;
        private readonly HashSet<Symbol> _symbols = new HashSet<Symbol>();
        private readonly SymbolTranslator _symbolTranslator = new SymbolTranslator();

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public QrawlerDataQueueHandler()
        {
            _qlive = new LiveData(new WebSocket4NetFactory(), Config.GetValue<string>("qrawler.url_live"));
            _qlive.Start();
        }

        public IEnumerable<BaseData> GetNextTicks()
        {
            foreach (QrawlerEngine.Models.Tick tickIn in _qlive.GetTicks())
            {

                Symbol s = _symbols.First(x => x.Value == _symbolTranslator.TranslateBack(tickIn.Symbol, tickIn.Feed));

                Logger.Debug("Received Tick");

                yield return new Tick
                {
                    Time = tickIn.Time.ToDateTimeUtc(),
                    Symbol = s,
                    Value = tickIn.Last,
                    TickType = TickType.Trade,
                    Quantity = tickIn.Volume.GetValueOrDefault(0),
                };
            }
        }

        public void Subscribe(LiveNodePacket job, IEnumerable<Symbol> symbols)
        {
            foreach(Symbol s in symbols)
            {
                _symbolTranslator.Translate(s, out string qsymbol, out string qfeed);
                _qlive.SubscribeSymbols(symbols.Select(x => new QSymbol(qsymbol, "*", qfeed)).ToList());

                _symbols.Add(s);
            }
        }

        public void Unsubscribe(LiveNodePacket job, IEnumerable<Symbol> symbols)
        {
            foreach (Symbol s in symbols)
            {
                _symbols.Remove(s);
            }

            _qlive.UnubscribeSymbols(symbols.Select(x => new QSymbol(x.Value)).ToList());
        }
    }
}
