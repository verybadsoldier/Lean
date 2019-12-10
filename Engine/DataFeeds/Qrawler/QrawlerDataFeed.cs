using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.DataFeeds.Enumerators;
using QuantConnect.Lean.Engine.DataFeeds.Enumerators.Factories;
using QuantConnect.Lean.Engine.Results;
using QuantConnect.Logging;
using QuantConnect.Packets;
using QuantConnect.Securities;
using QuantConnect.Util;

namespace QuantConnect.Lean.Engine.DataFeeds.Qrawler
{
    /// <summary>
    /// Historical datafeed stream reader for processing files on a local disk.
    /// </summary>
    /// <remarks>Filesystem datafeeds are incredibly fast</remarks>
    public class QrawlerDataFeed : IDataFeed, IDataQueueHandler
    {
        private IAlgorithm _algorithm;
        private IResultHandler _resultHandler;
        private IMapFileProvider _mapFileProvider;
        private IFactorFileProvider _factorFileProvider;
        private IDataProvider _dataProvider;
        private LiveNodePacket _job;
        private SubscriptionCollection _subscriptions;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private UniverseSelection _universeSelection;
        private SubscriptionDataReaderSubscriptionEnumeratorFactory _subscriptionFactory;

        private List<Symbol> _subscribedSymbols = new List<Symbol>();

        /// <summary>
        /// Flag indicating the hander thread is completely finished and ready to dispose.
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Initializes the data feed for the specified job and algorithm
        /// </summary>
        public void Initialize(IAlgorithm algorithm,
            AlgorithmNodePacket job,
            IResultHandler resultHandler,
            IMapFileProvider mapFileProvider,
            IFactorFileProvider factorFileProvider,
            IDataProvider dataProvider,
            IDataFeedSubscriptionManager subscriptionManager,
            IDataFeedTimeProvider dataFeedTimeProvider)
        {
            _algorithm = algorithm;
            _job = (LiveNodePacket)job;
            _resultHandler = resultHandler;
            _mapFileProvider = mapFileProvider;
            _factorFileProvider = factorFileProvider;
            _dataProvider = dataProvider;
            _subscriptions = subscriptionManager.DataFeedSubscriptions;
            _universeSelection = subscriptionManager.UniverseSelection;
            _cancellationTokenSource = new CancellationTokenSource();
            _subscriptionFactory = new SubscriptionDataReaderSubscriptionEnumeratorFactory(
                _resultHandler,
                _mapFileProvider,
                _factorFileProvider,
                _dataProvider,
                includeAuxiliaryData: true);

            IsActive = true;
            var threadCount = Math.Max(1, Math.Min(4, Environment.ProcessorCount - 3));
        }

        private Subscription CreateDataSubscription(SubscriptionRequest request)
        {
            var enqueueable = new EnqueueableEnumerator<SubscriptionData>(true);
            var exchangeHours = request.Security.Exchange.Hours;

            var timeZoneOffsetProvider = new TimeZoneOffsetProvider(request.Security.Exchange.TimeZone, request.StartTimeUtc, request.EndTimeUtc);
            var subscription = new Subscription(request, enqueueable, timeZoneOffsetProvider);

            
            var t = new TradeBar(new DateTime(2012, 1, 1), request.Security.Symbol, 1, 5, 6, 5, 4);
            var subscriptionData = SubscriptionData.Create(subscription.Configuration, exchangeHours, subscription.OffsetProvider, t);
            enqueueable.Enqueue(subscriptionData);

            var t2 = new TradeBar(new DateTime(2012, 1, 2), request.Security.Symbol, 1, 5, 6, 5, 4);
            var subscriptionData2 = SubscriptionData.Create(subscription.Configuration, exchangeHours, subscription.OffsetProvider, t);
            enqueueable.Enqueue(subscriptionData2);

            enqueueable.Stop();


            return subscription;
            // ReSharper disable once PossibleMultipleEnumeration
            if (!request.TradableDays.Any())
            {
                _algorithm.Error(
                    $"No data loaded for {request.Security.Symbol} because there were no tradeable dates for this security."
                );
                return null;
            }

            // ReSharper disable once PossibleMultipleEnumeration
            var enumeratorFactory = GetEnumeratorFactory(request);
            var enumerator = enumeratorFactory.CreateEnumerator(request, _dataProvider);
            enumerator = ConfigureEnumerator(request, false, enumerator);

            return SubscriptionUtils.CreateAndScheduleWorker(request, enumerator);
        }

        /// <summary>
        /// Creates a new subscription to provide data for the specified security.
        /// </summary>
        /// <param name="request">Defines the subscription to be added, including start/end times the universe and security</param>
        /// <returns>The created <see cref="Subscription"/> if successful, null otherwise</returns>
        public Subscription CreateSubscription(SubscriptionRequest request)
        {
            Subscribe(_job, new[] { request.Security.Symbol });
            return CreateDataSubscription(request);
            /*            return request.IsUniverseSubscription
                            ? CreateUniverseSubscription(request)
                            : CreateDataSubscription(request);*/

        }

        /// <summary>
        /// Removes the subscription from the data feed, if it exists
        /// </summary>
        /// <param name="subscription">The subscription to remove</param>
        public void RemoveSubscription(Subscription subscription)
        {
        }

        /// <summary>
        /// Adds a new subscription for universe selection
        /// </summary>
        /// <param name="request">The subscription request</param>
        private Subscription CreateUniverseSubscription(SubscriptionRequest request)
        {
            throw new NotImplementedException("Universe not supported");
        }

        /// <summary>
        /// Creates the correct enumerator factory for the given request
        /// </summary>
        private ISubscriptionEnumeratorFactory GetEnumeratorFactory(SubscriptionRequest request)
        {
            if (request.IsUniverseSubscription)
            {
                if (request.Universe is ITimeTriggeredUniverse)
                {
                    var universe = request.Universe as UserDefinedUniverse;
                    if (universe != null)
                    {
                        // Trigger universe selection when security added/removed after Initialize
                        universe.CollectionChanged += (sender, args) =>
                        {
                            var items =
                                args.Action == NotifyCollectionChangedAction.Add ? args.NewItems :
                                args.Action == NotifyCollectionChangedAction.Remove ? args.OldItems : null;

                            if (items == null) return;

                            var symbol = items.OfType<Symbol>().FirstOrDefault();
                            if (symbol == null) return;

                            var collection = new BaseDataCollection(_algorithm.UtcTime, symbol);
                            var changes = _universeSelection.ApplyUniverseSelection(universe, _algorithm.UtcTime, collection);
                            _algorithm.OnSecuritiesChanged(changes);
                        };
                    }

                    return new TimeTriggeredUniverseSubscriptionEnumeratorFactory(request.Universe as ITimeTriggeredUniverse, MarketHoursDatabase.FromDataFolder());
                }
                if (request.Configuration.Type == typeof (CoarseFundamental))
                {
                    return new BaseDataCollectionSubscriptionEnumeratorFactory();
                }
                if (request.Universe is OptionChainUniverse)
                {
                    return new OptionChainUniverseSubscriptionEnumeratorFactory((req, e) => ConfigureEnumerator(req, true, e),
                        _mapFileProvider.Get(request.Security.Symbol.ID.Market), _factorFileProvider);
                }
                if (request.Universe is FuturesChainUniverse)
                {
                    return new FuturesChainUniverseSubscriptionEnumeratorFactory((req, e) => ConfigureEnumerator(req, true, e));
                }
            }

            return _subscriptionFactory;
        }

        /// <summary>
        /// Send an exit signal to the thread.
        /// </summary>
        public void Exit()
        {
            if (IsActive)
            {
                IsActive = false;
                Log.Trace("QarwlerDataFeed.Exit(): Start. Setting cancellation token...");
                _cancellationTokenSource.Cancel();
                _subscriptionFactory?.DisposeSafely();
                Log.Trace("QarwlerDataFeed.Exit(): Exit Finished.");
            }
        }

        /// <summary>
        /// Configure the enumerator with aggregation/fill-forward/filter behaviors. Returns new instance if re-configured
        /// </summary>
        private IEnumerator<BaseData> ConfigureEnumerator(SubscriptionRequest request, bool aggregate, IEnumerator<BaseData> enumerator)
        {
            if (aggregate)
            {
                enumerator = new BaseDataCollectionAggregatorEnumerator(enumerator, request.Configuration.Symbol);
            }

            // optionally apply fill forward logic, but never for tick data
            if (request.Configuration.FillDataForward && request.Configuration.Resolution != Resolution.Tick)
            {
                // copy forward Bid/Ask bars for QuoteBars
                if (request.Configuration.Type == typeof(QuoteBar))
                {
                    enumerator = new QuoteBarFillForwardEnumerator(enumerator);
                }

                var fillForwardResolution = _subscriptions.UpdateAndGetFillForwardResolution(request.Configuration);

                enumerator = new FillForwardEnumerator(enumerator, request.Security.Exchange, fillForwardResolution,
                    request.Security.IsExtendedMarketHours, request.EndTimeLocal, request.Configuration.Resolution.ToTimeSpan(), request.Configuration.DataTimeZone, request.StartTimeLocal);
            }

            // optionally apply exchange/user filters
            if (request.Configuration.IsFilteredSubscription)
            {
                enumerator = SubscriptionFilterEnumerator.WrapForDataFeed(_resultHandler, enumerator, request.Security, request.EndTimeLocal);
            }

            return enumerator;
        }

        public IEnumerable<BaseData> GetNextTicks()
        {
            Decimal d = 123;
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                foreach (var s in _subscribedSymbols)
                {
                    d += 3;
                    var t = new Tick(new DateTime(2012, 1, 1), s, d, d + 0.3m);
                    yield return t;
                }
            }
        }

        public void Subscribe(LiveNodePacket job, IEnumerable<Symbol> symbols)
        {
            _subscribedSymbols = symbols.ToList();
        }

        public void Unsubscribe(LiveNodePacket job, IEnumerable<Symbol> symbols)
        {
            _subscribedSymbols.Clear();
        }
    }
}
