using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Qrawler.Api;
using Qrawler.Interfaces.WebSocket;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Interfaces;
using QuantConnect.Packets;
using QuantConnect.Util;
using QSymbol = QrawlerEngine.Models.Symbol;

namespace QuantConnect.Lean.Engine.DataFeeds.Qrawler
{
    class QrawlerDataQueueHandler : IDataQueueHandler
    {
        private LiveData _qlive;
        private readonly HashSet<Symbol> _symbols = new HashSet<Symbol>();

        public QrawlerDataQueueHandler()
        {
            var wsfactory = new WebSocket4NetFactory();
            _qlive = new LiveData(wsfactory,"ws://127.0.0.1:7772/live");
        }
        public IEnumerable<BaseData> GetNextTicks()
        {
            foreach (QrawlerEngine.Models.Tick VARIABLE in _qlive.GetTicks())
            {
                Symbol s = _symbols.First(x => x.Value == VARIABLE.Symbol);
                yield return new Tick(VARIABLE.Time.ToDateTimeUtc(), s, VARIABLE.Bid, VARIABLE.Ask);
            }
        }

        public void Subscribe(LiveNodePacket job, IEnumerable<Symbol> symbols)
        {
            _qlive.SubscribeSymbols(symbols.Select(x => new QSymbol(x.Value)).ToList());

            foreach (var s in symbols)
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
