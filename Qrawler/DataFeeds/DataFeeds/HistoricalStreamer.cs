using QuantConnect.Data;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Qrawler.Api;
using QrawlerEngine.Models;
using QuantConnect.Configuration;
using QuantConnect.Data.Market;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.DataFeeds.Enumerators;
using QuantConnect.Notifications;

namespace QuantConnect.Lean.Engine.DataFeeds.Qrawler
{
    class HistoricalStreamer : IEnumerator<BaseData>, ITradableDatesNotifier, IDataProviderEvents
    {
        class QHistoricalRequest
        {
            public QHistoricalRequest(SubscriptionRequest r)
            {
                Symbol = r.Configuration.Symbol;
                TickType = r.Configuration.TickType;
                Resolution = r.Configuration.Resolution;
                StartTimeUtc = r.StartTimeUtc;
                EndTimeUtc = r.EndTimeUtc;
            }

            public QHistoricalRequest(HistoryRequest r)
            {
                Symbol = r.Symbol;
                TickType = r.TickType;
                Resolution = r.Resolution;
                StartTimeUtc = r.StartTimeUtc;
                EndTimeUtc = r.EndTimeUtc;
            }

            public Symbol Symbol { get; set; }
            public TickType TickType { get; set; }
            public Resolution Resolution { get; set; }
            public DateTime StartTimeUtc { get; set; }
            public DateTime EndTimeUtc {get; set; }
        }

        public BaseData Current { get; set;  }

        private readonly global::Qrawler.Api.HistoricalData _hist;
        private readonly QHistoricalRequest _request;
        private readonly SymbolTranslator _symbolTranslator = new SymbolTranslator();
        private bool _isStarted;
        private IEnumerator<Ohlc> _bars;

        object IEnumerator.Current => Current;

        public HistoricalStreamer(SubscriptionRequest request) : this(new QHistoricalRequest(request))
        {
        }

        public HistoricalStreamer(HistoryRequest request) : this(new QHistoricalRequest(request))
        {
        }

        private HistoricalStreamer(QHistoricalRequest request)
        {
            _request = request;

            string uri = Config.Get("qrawler.url_historical");
            _hist = new global::Qrawler.Api.HistoricalData(uri);
        }

        public void FetchBars()
        {
            if (_request.Resolution == Resolution.Tick && _request.TickType != TickType.Trade)
                throw new Exception("Only trade tick data supported by QrawlerHistorical");

            _symbolTranslator.Translate(_request.Symbol, out string qsymbol, out string qfeed);

            _bars = _hist.GetHistoricalOhlcsCsv(
                qsymbol,
                "*",
                qfeed,
                _request.StartTimeUtc,
                _request.EndTimeUtc,
                SymbolTranslator.TranslateResolution(_request.Resolution)
            ).GetEnumerator();
        }

        public bool MoveNext()
        {
            if (!_isStarted)
            {
                FetchBars();
                _isStarted = true;
            }

            if (!_bars.MoveNext())
                return false;

            Current = _bars.Current.ToTradeBar(_request.Symbol);

            return true;
        }

        public void Reset()
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
        }

        public event EventHandler<NewTradableDateEventArgs> NewTradableDate;
        public event EventHandler<InvalidConfigurationDetectedEventArgs> InvalidConfigurationDetected;
        public event EventHandler<NumericalPrecisionLimitedEventArgs> NumericalPrecisionLimited;
        public event EventHandler<DownloadFailedEventArgs> DownloadFailed;
        public event EventHandler<ReaderErrorDetectedEventArgs> ReaderErrorDetected;
        public event EventHandler<StartDateLimitedEventArgs> StartDateLimited;
    }
}
