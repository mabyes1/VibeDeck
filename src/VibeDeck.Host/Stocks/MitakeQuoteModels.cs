using System;
using System.Collections.Generic;

namespace VibeDeck.Host.Stocks
{
    public sealed class StockQuoteSnapshot
    {
        public bool Connected { get; set; }

        public string Provider { get; set; } = "mitake-dde";

        public long Revision { get; set; }

        public DateTimeOffset? UpdatedAt { get; set; }

        public IReadOnlyList<StockMarketQuoteItem> Markets { get; set; } = Array.Empty<StockMarketQuoteItem>();

        public IReadOnlyList<StockQuoteItem> Quotes { get; set; } = Array.Empty<StockQuoteItem>();
    }

    public sealed class StockQuoteItem
    {
        public string Symbol { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public decimal Quantity { get; set; }

        public string Deal { get; set; } = string.Empty;

        public string DiffPercent { get; set; } = string.Empty;

        public string TotalVol { get; set; } = string.Empty;

        public string BidPrice { get; set; } = string.Empty;

        public DateTimeOffset? UpdatedAt { get; set; }
    }

    public sealed class StockMarketQuoteItem
    {
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Deal { get; set; } = string.Empty;
        public string Change { get; set; } = string.Empty;
        public string DiffPercent { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public DateTimeOffset? UpdatedAt { get; set; }
        public bool Stale { get; set; }
    }
}
