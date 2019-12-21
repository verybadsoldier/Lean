using System.Collections.Generic;
using QuantConnect.Data;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Interfaces;

namespace QuantConnect.Qrawler.DataFeeds
{
    class HistoricalDataEnumeratorFactory : ISubscriptionEnumeratorFactory
    {
        public IEnumerator<BaseData> CreateEnumerator(SubscriptionRequest request, IDataProvider dataProvider)
        {
            return new HistoricalStreamer(request);
        }
    }
}
