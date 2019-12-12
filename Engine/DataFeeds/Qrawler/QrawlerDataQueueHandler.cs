using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Qrawler.Api;
using Qrawler.Interfaces.WebSocket;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Interfaces;
using QuantConnect.Packets;
using QSymbol = QrawlerEngine.Models.Symbol;

namespace QuantConnect.Lean.Engine.DataFeeds.Qrawler
{
    class QrawlerDataQueueHandler : IDataQueueHandler
    {
        private readonly LiveData _qlive;
        private readonly HashSet<Symbol> _symbols = new HashSet<Symbol>();

        public QrawlerDataQueueHandler()
        {
            _qlive = new LiveData(new WebSocket4NetFactory(), Config.GetValue<string>("qrawler.uri"));
            _qlive.Start();
        }

        public IEnumerable<BaseData> GetNextTicks()
        {
            foreach (QrawlerEngine.Models.Tick tickIn in _qlive.GetTicks())
            {
                Symbol s = _symbols.First(x => x.Value == tickIn.Symbol);
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
            _qlive.SubscribeSymbols(symbols.Select(x => new QSymbol(x.Value, "*", "GodmodeTrader")).ToList());

            foreach (Symbol s in symbols)
            {
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
