using System.Globalization;

namespace PolyCopyTrader.Service.Analytics;

public static class BtcOrderBookPredictionStudyAnalyzer
{
    private const int MarketSeconds = 300;
    private static readonly string[] BookFeatureNames =
    [
        nameof(BtcOrderBookPredictionFeatureRow.LastImbalance),
        nameof(BtcOrderBookPredictionFeatureRow.TimeWeightedImbalance),
        nameof(BtcOrderBookPredictionFeatureRow.ImbalanceSlopePerSecond),
        nameof(BtcOrderBookPredictionFeatureRow.LastMicropriceOffsetBps),
        nameof(BtcOrderBookPredictionFeatureRow.TimeWeightedMicropriceOffsetBps),
        nameof(BtcOrderBookPredictionFeatureRow.ObservedL1OfiNormalized)
    ];

    public static (DateTimeOffset First, DateTimeOffset Last)? GetReceivedBounds(
        IEnumerable<BtcOrderBookPredictionRawEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        DateTimeOffset? first = null;
        DateTimeOffset? last = null;
        foreach (var item in events)
        {
            first ??= item.ReceivedUtc;
            last = item.ReceivedUtc;
        }

        return first is null || last is null ? null : (first.Value, last.Value);
    }

    public static IReadOnlyList<BtcOrderBookPredictionFeatureRow> BuildFeatureRows(
        IEnumerable<BtcOrderBookPredictionRawEvent> events,
        DateTimeOffset firstReceivedUtc,
        DateTimeOffset lastReceivedUtc,
        IReadOnlyCollection<int> decisionLeadSeconds,
        IReadOnlyCollection<int> featureWindowSeconds,
        IReadOnlyDictionary<DateTimeOffset, BtcOrderBookPredictionGammaLabel> labels,
        int maximumQuoteAgeMilliseconds,
        decimal minimumQuoteCoverageRatio)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(decisionLeadSeconds);
        ArgumentNullException.ThrowIfNull(featureWindowSeconds);
        ArgumentNullException.ThrowIfNull(labels);
        if (decisionLeadSeconds.Count != 1)
        {
            throw new ArgumentException(
                "Exactly one decision lead is allowed per prospective analysis run.",
                nameof(decisionLeadSeconds));
        }

        if (featureWindowSeconds.Count == 0)
        {
            throw new ArgumentException("At least one feature window is required.", nameof(featureWindowSeconds));
        }

        if (lastReceivedUtc < firstReceivedUtc)
        {
            return [];
        }

        int maximumWindowSeconds = featureWindowSeconds.Max();
        var schedules = BuildSchedules(
                firstReceivedUtc,
                lastReceivedUtc,
                decisionLeadSeconds,
                featureWindowSeconds)
            .OrderBy(item => item.DecisionUtc)
            .ThenBy(item => item.MarketStartUtc)
            .ThenBy(item => item.DecisionLeadSeconds)
            .ThenBy(item => item.FeatureWindowSeconds)
            .ToArray();
        if (schedules.Length == 0)
        {
            return [];
        }

        var books = new LinkedList<BtcOrderBookPredictionRawEvent>();
        var trades = new LinkedList<BtcOrderBookPredictionRawEvent>();
        var qualityEvents = new LinkedList<BtcOrderBookPredictionRawEvent>();
        var rows = new List<BtcOrderBookPredictionFeatureRow>(schedules.Length);
        var marketStarts = schedules.Select(item => item.MarketStartUtc).Distinct().Order().ToArray();
        var boundaryTimes = marketStarts
            .SelectMany(start => new[] { start, start.AddSeconds(MarketSeconds) })
            .Distinct()
            .Order()
            .ToArray();
        var boundaryPrices = new Dictionary<DateTimeOffset, decimal?>();
        int scheduleIndex = 0;
        int boundaryIndex = 0;
        decimal? lastReceivedTradePrice = null;
        DateTimeOffset? previousReceivedUtc = null;
        long? previousLogicalSequence = null;

        foreach (var item in events)
        {
            if (previousReceivedUtc is { } previousUtc && item.ReceivedUtc < previousUtc)
            {
                throw new InvalidDataException("Raw events are not ordered by reconstructed receive UTC.");
            }

            if (previousLogicalSequence is { } previousSequence && item.LogicalSequence <= previousSequence)
            {
                throw new InvalidDataException("Raw event logical sequences are not strictly increasing.");
            }

            previousReceivedUtc = item.ReceivedUtc;
            previousLogicalSequence = item.LogicalSequence;
            while (scheduleIndex < schedules.Length && schedules[scheduleIndex].DecisionUtc <= item.ReceivedUtc)
            {
                rows.Add(BuildFeatureRow(
                    schedules[scheduleIndex],
                    books,
                    trades,
                    qualityEvents,
                    labels,
                    maximumQuoteAgeMilliseconds,
                    minimumQuoteCoverageRatio));
                scheduleIndex++;
            }

            while (boundaryIndex < boundaryTimes.Length && boundaryTimes[boundaryIndex] <= item.ReceivedUtc)
            {
                boundaryPrices[boundaryTimes[boundaryIndex]] = lastReceivedTradePrice;
                boundaryIndex++;
            }

            switch (item.EventType)
            {
                case BtcOrderBookPredictionEventType.Book:
                    books.AddLast(item);
                    break;
                case BtcOrderBookPredictionEventType.Trade:
                    trades.AddLast(item);
                    if (item.TradePrice is { } tradePrice)
                    {
                        lastReceivedTradePrice = tradePrice;
                    }

                    break;
                case BtcOrderBookPredictionEventType.Control when IsQualityControl(item.Status):
                    qualityEvents.AddLast(item);
                    break;
            }

            DateTimeOffset retentionCutoff = item.ReceivedUtc.AddSeconds(-maximumWindowSeconds - MarketSeconds);
            TrimBooks(books, retentionCutoff);
            TrimAll(trades, retentionCutoff);
        }

        while (scheduleIndex < schedules.Length && schedules[scheduleIndex].DecisionUtc <= lastReceivedUtc)
        {
            rows.Add(BuildFeatureRow(
                schedules[scheduleIndex],
                books,
                trades,
                qualityEvents,
                labels,
                maximumQuoteAgeMilliseconds,
                minimumQuoteCoverageRatio));
            scheduleIndex++;
        }

        while (boundaryIndex < boundaryTimes.Length && boundaryTimes[boundaryIndex] <= lastReceivedUtc)
        {
            boundaryPrices[boundaryTimes[boundaryIndex]] = lastReceivedTradePrice;
            boundaryIndex++;
        }

        return rows.Select(row =>
        {
            boundaryPrices.TryGetValue(row.MarketStartUtc, out decimal? startPrice);
            boundaryPrices.TryGetValue(row.MarketEndUtc, out decimal? endPrice);
            string? proxyOutcome = startPrice is null || endPrice is null || endPrice == startPrice
                ? null
                : endPrice > startPrice ? "Up" : "Down";
            return row with
            {
                BinanceStartPrice = startPrice,
                BinanceEndPrice = endPrice,
                BinanceProxyOutcome = proxyOutcome
            };
        }).ToArray();
    }

    public static BtcOrderBookPredictionAnalysisResult Analyze(
        IReadOnlyCollection<BtcOrderBookPredictionFeatureRow> featureRows,
        int minimumLabeledMarkets,
        int minimumDistinctUtcDays,
        int minimumMarketsPerClass,
        decimal trainFraction,
        decimal validationFraction,
        decimal testFraction)
    {
        ArgumentNullException.ThrowIfNull(featureRows);
        DateTimeOffset analyzedAtUtc = DateTimeOffset.UtcNow;
        int distinctDecisionLeads = featureRows.Select(row => row.DecisionLeadSeconds).Distinct().Count();
        if (distinctDecisionLeads > 1)
        {
            throw new ArgumentException(
                "Exactly one decision lead is allowed per prospective analysis run.",
                nameof(featureRows));
        }

        var exclusionReasons = featureRows
            .Where(row => !row.IsValid || !HasOfficialGammaLabel(row))
            .GroupBy(row => !row.IsValid
                ? row.InvalidReason ?? "invalid_feature_window"
                : "label_" + row.GammaLabelStatus)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key + "=" + group.Count().ToString(CultureInfo.InvariantCulture))
            .ToList();
        var configurations = featureRows
            .Select(row => new ConfigurationKey(row.DecisionLeadSeconds, row.FeatureWindowSeconds))
            .Distinct()
            .OrderBy(key => key.DecisionLeadSeconds)
            .ThenBy(key => key.FeatureWindowSeconds)
            .ToArray();
        var validRows = featureRows
            .Where(row => row.IsValid && HasOfficialGammaLabel(row))
            .ToArray();
        var validMarketGroups = validRows.GroupBy(row => row.MarketStartUtc).ToArray();
        int conflictingLabelMarkets = validMarketGroups.Count(group =>
            group.Select(row => row.GammaOutcome).Distinct(StringComparer.Ordinal).Count() != 1);
        int duplicateConfigurationMarkets = validMarketGroups.Count(group =>
            group.GroupBy(row => new ConfigurationKey(row.DecisionLeadSeconds, row.FeatureWindowSeconds))
                .Any(configurationGroup => configurationGroup.Count() != 1));
        if (conflictingLabelMarkets > 0)
        {
            exclusionReasons.Add("conflicting_gamma_outcome=" + conflictingLabelMarkets.ToString(CultureInfo.InvariantCulture));
        }

        if (duplicateConfigurationMarkets > 0)
        {
            exclusionReasons.Add("duplicate_configuration=" + duplicateConfigurationMarkets.ToString(CultureInfo.InvariantCulture));
        }

        var commonMarkets = validMarketGroups
            .Where(group =>
                group.Select(row => new ConfigurationKey(row.DecisionLeadSeconds, row.FeatureWindowSeconds)).Distinct().Count() == configurations.Length &&
                group.Select(row => row.GammaOutcome).Distinct(StringComparer.Ordinal).Count() == 1 &&
                group.GroupBy(row => new ConfigurationKey(row.DecisionLeadSeconds, row.FeatureWindowSeconds))
                    .All(configurationGroup => configurationGroup.Count() == 1))
            .Select(group => group.Key)
            .Order()
            .ToArray();
        var marketOutcome = validRows
            .Where(row => commonMarkets.Contains(row.MarketStartUtc))
            .GroupBy(row => row.MarketStartUtc)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.GammaOutcome!).Distinct(StringComparer.Ordinal).Single());
        int upCount = marketOutcome.Values.Count(value => value == "Up");
        int downCount = marketOutcome.Values.Count(value => value == "Down");
        int distinctUtcDays = commonMarkets.Select(value => value.UtcDateTime.Date).Distinct().Count();
        decimal? gammaBinanceAgreement = CalculateGammaBinanceAgreement(validRows);

        if (commonMarkets.Length < minimumLabeledMarkets ||
            distinctUtcDays < minimumDistinctUtcDays ||
            upCount < minimumMarketsPerClass ||
            downCount < minimumMarketsPerClass)
        {
            string insufficientConclusion =
                $"Insufficient prospective data: common labeled markets={commonMarkets.Length}, UTC days={distinctUtcDays}, Up={upCount}, Down={downCount}. " +
                $"Required: markets>={minimumLabeledMarkets}, days>={minimumDistinctUtcDays}, each class>={minimumMarketsPerClass}.";
            return CreateIncompleteResult(
                "InsufficientData",
                analyzedAtUtc,
                featureRows.Count,
                featureRows.Select(row => row.MarketStartUtc).Distinct().Count(),
                validRows.Select(row => row.MarketStartUtc).Distinct().Count(),
                commonMarkets.Length,
                distinctUtcDays,
                minimumLabeledMarkets,
                minimumDistinctUtcDays,
                minimumMarketsPerClass,
                trainFraction,
                validationFraction,
                testFraction,
                gammaBinanceAgreement,
                exclusionReasons,
                insufficientConclusion);
        }

        int maximumWindowSeconds = configurations.Max(key => key.FeatureWindowSeconds);
        int maximumDecisionLeadSeconds = configurations.Max(key => key.DecisionLeadSeconds);
        BtcOrderBookPredictionSplit split = BuildChronologicalSplit(
            commonMarkets,
            trainFraction,
            validationFraction,
            maximumWindowSeconds,
            maximumDecisionLeadSeconds);
        if (split.TrainMarkets.Count == 0 || split.ValidationMarkets.Count == 0 || split.TestMarkets.Count == 0)
        {
            return CreateIncompleteResult(
                "InsufficientData",
                analyzedAtUtc,
                featureRows.Count,
                featureRows.Select(row => row.MarketStartUtc).Distinct().Count(),
                validRows.Select(row => row.MarketStartUtc).Distinct().Count(),
                commonMarkets.Length,
                distinctUtcDays,
                minimumLabeledMarkets,
                minimumDistinctUtcDays,
                minimumMarketsPerClass,
                trainFraction,
                validationFraction,
                testFraction,
                gammaBinanceAgreement,
                exclusionReasons,
                "Chronological train/validation/test split is empty after the configured embargo.");
        }

        int minimumTrainPerClass = Math.Max(1, (int)decimal.Floor(minimumMarketsPerClass * trainFraction));
        int minimumValidationPerClass = Math.Max(1, (int)decimal.Floor(minimumMarketsPerClass * validationFraction));
        int minimumTestPerClass = Math.Max(1, (int)decimal.Floor(minimumMarketsPerClass * testFraction));
        if (!HasMinimumClassCounts(split.TrainMarkets, marketOutcome, minimumTrainPerClass) ||
            !HasMinimumClassCounts(split.ValidationMarkets, marketOutcome, minimumValidationPerClass) ||
            !HasMinimumClassCounts(split.TestMarkets, marketOutcome, minimumTestPerClass))
        {
            return CreateIncompleteResult(
                "InsufficientData",
                analyzedAtUtc,
                featureRows.Count,
                featureRows.Select(row => row.MarketStartUtc).Distinct().Count(),
                validRows.Select(row => row.MarketStartUtc).Distinct().Count(),
                commonMarkets.Length,
                distinctUtcDays,
                minimumLabeledMarkets,
                minimumDistinctUtcDays,
                minimumMarketsPerClass,
                trainFraction,
                validationFraction,
                testFraction,
                gammaBinanceAgreement,
                exclusionReasons,
                "At least one chronological split does not contain its configured minimum Up and Down markets after embargo.");
        }

        var rowsByConfiguration = validRows
            .Where(row => commonMarkets.Contains(row.MarketStartUtc))
            .GroupBy(row => new ConfigurationKey(row.DecisionLeadSeconds, row.FeatureWindowSeconds))
            .ToDictionary(group => group.Key, group => group.ToDictionary(row => row.MarketStartUtc));
        var fittedRules = new List<BtcOrderBookPredictionRule>();
        foreach (var configuration in configurations)
        {
            IReadOnlyDictionary<DateTimeOffset, BtcOrderBookPredictionFeatureRow> rows = rowsByConfiguration[configuration];
            foreach (string featureName in BookFeatureNames)
            {
                var fitted = FitRule(
                    configuration,
                    featureName,
                    split.TrainMarkets,
                    split.ValidationMarkets,
                    rows,
                    marketOutcome);
                if (fitted is not null)
                {
                    fittedRules.Add(fitted);
                }
            }
        }

        BtcOrderBookPredictionRule? selectedRule = fittedRules
            .OrderByDescending(rule => rule.ValidationBalancedAccuracy)
            .ThenByDescending(rule => rule.TrainBalancedAccuracy)
            .ThenBy(rule => rule.DecisionLeadSeconds)
            .ThenBy(rule => rule.FeatureWindowSeconds)
            .ThenBy(rule => rule.FeatureName, StringComparer.Ordinal)
            .FirstOrDefault();
        if (selectedRule is null)
        {
            return CreateIncompleteResult(
                "InsufficientData",
                analyzedAtUtc,
                featureRows.Count,
                featureRows.Select(row => row.MarketStartUtc).Distinct().Count(),
                validRows.Select(row => row.MarketStartUtc).Distinct().Count(),
                commonMarkets.Length,
                distinctUtcDays,
                minimumLabeledMarkets,
                minimumDistinctUtcDays,
                minimumMarketsPerClass,
                trainFraction,
                validationFraction,
                testFraction,
                gammaBinanceAgreement,
                exclusionReasons,
                "No book feature had enough finite train and validation values.");
        }

        var selectedRows = rowsByConfiguration[new ConfigurationKey(selectedRule.DecisionLeadSeconds, selectedRule.FeatureWindowSeconds)];
        string majorityPrediction = MajorityOutcome(split.TrainMarkets.Select(market => marketOutcome[market]));
        var predictions = new List<BtcOrderBookPredictionMarketPrediction>();
        foreach (DateTimeOffset market in split.TestMarkets)
        {
            BtcOrderBookPredictionFeatureRow row = selectedRows[market];
            decimal? value = GetFeature(row, selectedRule.FeatureName);
            if (value is null)
            {
                continue;
            }

            string prediction = Predict(value.Value, selectedRule.Threshold, selectedRule.GreaterOrEqualPredictsUp);
            string actual = marketOutcome[market];
            predictions.Add(new BtcOrderBookPredictionMarketPrediction(
                market,
                actual,
                prediction,
                value.Value,
                majorityPrediction,
                prediction == actual,
                majorityPrediction == actual));
        }

        if (predictions.Count != split.TestMarkets.Count)
        {
            return CreateIncompleteResult(
                "InsufficientData",
                analyzedAtUtc,
                featureRows.Count,
                featureRows.Select(row => row.MarketStartUtc).Distinct().Count(),
                validRows.Select(row => row.MarketStartUtc).Distinct().Count(),
                commonMarkets.Length,
                distinctUtcDays,
                minimumLabeledMarkets,
                minimumDistinctUtcDays,
                minimumMarketsPerClass,
                trainFraction,
                validationFraction,
                testFraction,
                gammaBinanceAgreement,
                exclusionReasons,
                "The selected feature is missing in one or more untouched test markets; partial-test scoring is prohibited.");
        }

        BtcOrderBookPredictionMetrics testMetrics = CalculateMetrics(
            predictions.Select(item => (item.ActualOutcome, item.PredictedOutcome)));
        BtcOrderBookPredictionMetrics majorityMetrics = CalculateMetrics(
            predictions.Select(item => (item.ActualOutcome, item.BaselinePrediction)));
        BtcOrderBookPredictionMetrics momentumMetrics = CalculateMetrics(
            predictions.Select(item =>
            {
                BtcOrderBookPredictionFeatureRow row = selectedRows[item.MarketStartUtc];
                string prediction = row.TradeEventCount >= 2 && row.PremarketTradeReturnBps is { } momentum
                    ? momentum >= 0m ? "Up" : "Down"
                    : majorityPrediction;
                return (item.ActualOutcome, prediction);
            }));
        int momentumAvailableMarkets = predictions.Count(item =>
            selectedRows[item.MarketStartUtc].TradeEventCount >= 2 &&
            selectedRows[item.MarketStartUtc].PremarketTradeReturnBps is not null);
        decimal accuracyLift = testMetrics.Accuracy - majorityMetrics.Accuracy;
        decimal balancedLift = testMetrics.BalancedAccuracy - majorityMetrics.BalancedAccuracy;
        decimal accuracyLiftVsMomentum = testMetrics.Accuracy - momentumMetrics.Accuracy;
        decimal balancedLiftVsMomentum = testMetrics.BalancedAccuracy - momentumMetrics.BalancedAccuracy;
        bool beatsMajority = accuracyLift > 0m && balancedLift > 0m;
        bool momentumComplete = momentumAvailableMarkets == predictions.Count;
        bool beatsMomentum = accuracyLiftVsMomentum > 0m && balancedLiftVsMomentum > 0m;
        string status = beatsMajority && momentumComplete && beatsMomentum
            ? "ExploratoryPointEstimateLiftVsBothBaselines"
            : beatsMajority
                ? "ExploratoryPointEstimateLiftVsMajorityOnly"
                : "NoObservedPointEstimateLift";
        string conclusion = status switch
        {
            "ExploratoryPointEstimateLiftVsBothBaselines" =>
                "On the common-valid subset of one untouched chronological test segment, the selected book rule has higher point accuracy and balanced accuracy than both the train-majority and complete descriptive premarket-momentum rules. This does not establish statistical persistence or incremental information in the book; it permits only continued Paper research.",
            "ExploratoryPointEstimateLiftVsMajorityOnly" =>
                "On the common-valid subset of one untouched chronological test segment, the selected book rule beats the train-majority rule, but not a complete descriptive premarket-momentum rule on both point metrics. Incremental value from the book is not demonstrated.",
            _ =>
                "On the common-valid subset of the untouched chronological test segment, the selected book-only rule did not beat the train-majority rule on both point accuracy and balanced accuracy. No predictive edge is demonstrated."
        };

        return new BtcOrderBookPredictionAnalysisResult(
            status,
            analyzedAtUtc,
            featureRows.Count,
            featureRows.Select(row => row.MarketStartUtc).Distinct().Count(),
            validRows.Select(row => row.MarketStartUtc).Distinct().Count(),
            commonMarkets.Length,
            distinctUtcDays,
            minimumLabeledMarkets,
            minimumDistinctUtcDays,
            minimumMarketsPerClass,
            trainFraction,
            validationFraction,
            testFraction,
            split,
            selectedRule,
            testMetrics,
            majorityMetrics,
            momentumMetrics,
            accuracyLift,
            balancedLift,
            accuracyLiftVsMomentum,
            balancedLiftVsMomentum,
            momentumAvailableMarkets,
            gammaBinanceAgreement,
            exclusionReasons,
            predictions,
            conclusion);
    }

    public static DateTimeOffset FloorToFiveMinuteUtc(DateTimeOffset value)
    {
        long unixSeconds = value.ToUniversalTime().ToUnixTimeSeconds();
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds / MarketSeconds * MarketSeconds);
    }

    public static DateTimeOffset CeilingToFiveMinuteUtc(DateTimeOffset value)
    {
        DateTimeOffset floor = FloorToFiveMinuteUtc(value);
        return floor == value.ToUniversalTime() ? floor : floor.AddSeconds(MarketSeconds);
    }

    private static IReadOnlyList<FeatureSchedule> BuildSchedules(
        DateTimeOffset firstReceivedUtc,
        DateTimeOffset lastReceivedUtc,
        IReadOnlyCollection<int> decisionLeadSeconds,
        IReadOnlyCollection<int> featureWindowSeconds)
    {
        var schedules = new List<FeatureSchedule>();
        DateTimeOffset firstMarket = FloorToFiveMinuteUtc(firstReceivedUtc).AddSeconds(MarketSeconds);
        DateTimeOffset finalMarket = CeilingToFiveMinuteUtc(lastReceivedUtc.AddSeconds(decisionLeadSeconds.Max()));
        for (DateTimeOffset marketStart = firstMarket; marketStart <= finalMarket; marketStart = marketStart.AddSeconds(MarketSeconds))
        {
            foreach (int leadSeconds in decisionLeadSeconds.Order())
            {
                DateTimeOffset decisionUtc = marketStart.AddSeconds(-leadSeconds);
                foreach (int windowSeconds in featureWindowSeconds.Order())
                {
                    DateTimeOffset windowStartUtc = decisionUtc.AddSeconds(-windowSeconds);
                    if (windowStartUtc < firstReceivedUtc || decisionUtc > lastReceivedUtc)
                    {
                        continue;
                    }

                    schedules.Add(new FeatureSchedule(
                        marketStart,
                        marketStart.AddSeconds(MarketSeconds),
                        decisionUtc,
                        leadSeconds,
                        windowSeconds,
                        windowStartUtc));
                }
            }
        }

        return schedules;
    }

    private static BtcOrderBookPredictionFeatureRow BuildFeatureRow(
        FeatureSchedule schedule,
        LinkedList<BtcOrderBookPredictionRawEvent> books,
        LinkedList<BtcOrderBookPredictionRawEvent> trades,
        LinkedList<BtcOrderBookPredictionRawEvent> qualityEvents,
        IReadOnlyDictionary<DateTimeOffset, BtcOrderBookPredictionGammaLabel> labels,
        int maximumQuoteAgeMilliseconds,
        decimal minimumQuoteCoverageRatio)
    {
        labels.TryGetValue(schedule.MarketStartUtc, out var label);
        BtcOrderBookPredictionRawEvent? initialBook = null;
        var windowBooks = new List<BtcOrderBookPredictionRawEvent>();
        foreach (var book in books)
        {
            if (book.ReceivedUtc < schedule.FeatureWindowStartUtc)
            {
                initialBook = book;
                continue;
            }

            if (book.ReceivedUtc >= schedule.DecisionUtc)
            {
                break;
            }

            windowBooks.Add(book);
        }

        var windowTrades = trades
            .Where(item => item.ReceivedUtc >= schedule.FeatureWindowStartUtc && item.ReceivedUtc < schedule.DecisionUtc)
            .ToArray();
        bool hasQualityGap = HasQualityGap(
            qualityEvents,
            schedule.FeatureWindowStartUtc,
            schedule.DecisionUtc);
        BtcOrderBookPredictionRawEvent? lastBook = windowBooks.LastOrDefault() ?? initialBook;
        decimal? lastQuoteAgeMs = lastBook is null
            ? null
            : (decimal)(schedule.DecisionUtc - lastBook.ReceivedUtc).TotalMilliseconds;
        var states = new List<(DateTimeOffset Timestamp, decimal Imbalance, decimal MicroOffsetBps)>();
        if (initialBook is not null && TryBookFeatures(initialBook, out decimal initialImbalance, out decimal initialMicroOffset, out _))
        {
            states.Add((schedule.FeatureWindowStartUtc, initialImbalance, initialMicroOffset));
        }

        foreach (var book in windowBooks)
        {
            if (TryBookFeatures(book, out decimal imbalance, out decimal stateMicroOffset, out _))
            {
                states.Add((book.ReceivedUtc, imbalance, stateMicroOffset));
            }
        }

        decimal coveredSeconds = 0m;
        decimal weightedImbalance = 0m;
        decimal weightedMicroOffset = 0m;
        BtcOrderBookPredictionRawEvent? activeBook = initialBook;
        DateTimeOffset cursor = schedule.FeatureWindowStartUtc;
        decimal? activeImbalance = null;
        decimal? activeMicroOffset = null;
        if (initialBook is not null &&
            TryBookFeatures(initialBook, out decimal startImbalance, out decimal startMicroOffset, out _))
        {
            activeImbalance = startImbalance;
            activeMicroOffset = startMicroOffset;
        }
        foreach (var book in windowBooks)
        {
            if (activeBook is not null && activeImbalance is { } currentImbalance && activeMicroOffset is { } currentMicroOffset)
            {
                DateTimeOffset segmentEnd = book.ReceivedUtc > schedule.DecisionUtc ? schedule.DecisionUtc : book.ReceivedUtc;
                DateTimeOffset quoteFreshUntil = activeBook.ReceivedUtc.AddMilliseconds(maximumQuoteAgeMilliseconds);
                if (quoteFreshUntil < segmentEnd)
                {
                    segmentEnd = quoteFreshUntil;
                }

                if (segmentEnd > cursor)
                {
                    decimal seconds = (decimal)(segmentEnd - cursor).TotalSeconds;
                    coveredSeconds += seconds;
                    weightedImbalance += currentImbalance * seconds;
                    weightedMicroOffset += currentMicroOffset * seconds;
                }
            }

            activeBook = book;
            cursor = book.ReceivedUtc < schedule.FeatureWindowStartUtc ? schedule.FeatureWindowStartUtc : book.ReceivedUtc;
            if (TryBookFeatures(book, out decimal nextImbalance, out decimal nextMicroOffset, out _))
            {
                activeImbalance = nextImbalance;
                activeMicroOffset = nextMicroOffset;
            }
            else
            {
                activeImbalance = null;
                activeMicroOffset = null;
            }
        }

        if (activeBook is not null && activeImbalance is { } tailImbalance && activeMicroOffset is { } tailMicroOffset && schedule.DecisionUtc > cursor)
        {
            DateTimeOffset segmentEnd = activeBook.ReceivedUtc.AddMilliseconds(maximumQuoteAgeMilliseconds);
            if (segmentEnd > schedule.DecisionUtc)
            {
                segmentEnd = schedule.DecisionUtc;
            }

            if (segmentEnd > cursor)
            {
                decimal seconds = (decimal)(segmentEnd - cursor).TotalSeconds;
                coveredSeconds += seconds;
                weightedImbalance += tailImbalance * seconds;
                weightedMicroOffset += tailMicroOffset * seconds;
            }
        }

        decimal windowSeconds = schedule.FeatureWindowSeconds;
        decimal quoteCoverage = windowSeconds <= 0m ? 0m : Math.Clamp(coveredSeconds / windowSeconds, 0m, 1m);
        decimal? timeWeightedImbalance = coveredSeconds > 0m ? weightedImbalance / coveredSeconds : null;
        decimal? timeWeightedMicroOffset = coveredSeconds > 0m ? weightedMicroOffset / coveredSeconds : null;
        decimal? lastImbalance = lastBook is not null && TryBookFeatures(lastBook, out decimal lastImbalanceValue, out decimal lastMicroOffset, out decimal lastSpread)
            ? lastImbalanceValue
            : null;
        decimal? minimumImbalance = states.Count == 0 ? null : states.Min(item => item.Imbalance);
        decimal? maximumImbalance = states.Count == 0 ? null : states.Max(item => item.Imbalance);
        decimal? slope = states.Count < 2
            ? null
            : (states[^1].Imbalance - states[0].Imbalance) /
              Math.Max(0.001m, (decimal)(states[^1].Timestamp - states[0].Timestamp).TotalSeconds);
        (decimal ofi, decimal denominator) = CalculateObservedL1Ofi(initialBook, windowBooks);
        decimal? normalizedOfi = denominator > 0m ? ofi / denominator : null;
        decimal signedTradeQuantity = windowTrades.Sum(item => item.TradeQty is { } quantity
            ? item.IsBuyerMaker == true ? -quantity : quantity
            : 0m);
        decimal totalTradeQuantity = windowTrades.Sum(item => item.TradeQty ?? 0m);
        decimal? tradeFlowImbalance = totalTradeQuantity > 0m ? signedTradeQuantity / totalTradeQuantity : null;
        decimal? premarketReturnBps = windowTrades.Length >= 2 &&
                                       windowTrades.FirstOrDefault()?.TradePrice is { } firstPrice &&
                                       windowTrades.LastOrDefault()?.TradePrice is { } finalPrice &&
                                       firstPrice > 0m
            ? (finalPrice - firstPrice) / firstPrice * 10_000m
            : null;

        string? invalidReason = null;
        if (hasQualityGap)
        {
            invalidReason = "quality_gap";
        }
        else if (lastBook is null || lastImbalance is null)
        {
            invalidReason = "missing_valid_quote";
        }
        else if (lastQuoteAgeMs is null || lastQuoteAgeMs > maximumQuoteAgeMilliseconds)
        {
            invalidReason = "stale_quote";
        }
        else if (quoteCoverage < minimumQuoteCoverageRatio)
        {
            invalidReason = "insufficient_quote_coverage";
        }

        return new BtcOrderBookPredictionFeatureRow(
            schedule.MarketStartUtc,
            schedule.MarketEndUtc,
            schedule.DecisionUtc,
            schedule.DecisionLeadSeconds,
            schedule.FeatureWindowSeconds,
            schedule.FeatureWindowStartUtc,
            label?.Outcome,
            label?.Status ?? "label_not_requested",
            null,
            null,
            null,
            windowBooks.Count,
            windowTrades.Length,
            quoteCoverage,
            lastQuoteAgeMs,
            lastBook?.Bid,
            lastBook?.Ask,
            lastBook?.BidQty,
            lastBook?.AskQty,
            lastBook is null ? null : TryBookFeatures(lastBook, out _, out _, out decimal spread) ? spread : null,
            lastImbalance,
            timeWeightedImbalance,
            minimumImbalance,
            maximumImbalance,
            slope,
            lastBook is null ? null : TryBookFeatures(lastBook, out _, out decimal microOffset, out _) ? microOffset : null,
            timeWeightedMicroOffset,
            ofi,
            normalizedOfi,
            signedTradeQuantity,
            totalTradeQuantity,
            tradeFlowImbalance,
            premarketReturnBps,
            hasQualityGap,
            invalidReason is null,
            invalidReason);
    }

    private static bool TryBookFeatures(
        BtcOrderBookPredictionRawEvent book,
        out decimal imbalance,
        out decimal micropriceOffsetBps,
        out decimal spreadBps)
    {
        imbalance = 0m;
        micropriceOffsetBps = 0m;
        spreadBps = 0m;
        if (book.Bid is not { } bid || book.Ask is not { } ask ||
            book.BidQty is not { } bidQty || book.AskQty is not { } askQty ||
            bid <= 0m || ask <= 0m || ask < bid || bidQty < 0m || askQty < 0m)
        {
            return false;
        }

        decimal quantityTotal = bidQty + askQty;
        decimal mid = (bid + ask) / 2m;
        if (quantityTotal <= 0m || mid <= 0m)
        {
            return false;
        }

        imbalance = (bidQty - askQty) / quantityTotal;
        decimal microprice = (ask * bidQty + bid * askQty) / quantityTotal;
        micropriceOffsetBps = (microprice - mid) / mid * 10_000m;
        spreadBps = (ask - bid) / mid * 10_000m;
        return true;
    }

    private static (decimal Ofi, decimal Denominator) CalculateObservedL1Ofi(
        BtcOrderBookPredictionRawEvent? initialBook,
        IReadOnlyList<BtcOrderBookPredictionRawEvent> windowBooks)
    {
        decimal ofi = 0m;
        decimal denominator = 0m;
        BtcOrderBookPredictionRawEvent? previous = initialBook;
        foreach (var current in windowBooks)
        {
            if (previous is null ||
                !TryBookFeatures(previous, out _, out _, out _) ||
                !TryBookFeatures(current, out _, out _, out _) ||
                previous.Bid is not { } previousBid || previous.Ask is not { } previousAsk ||
                previous.BidQty is not { } previousBidQty || previous.AskQty is not { } previousAskQty ||
                current.Bid is not { } bid || current.Ask is not { } ask ||
                current.BidQty is not { } bidQty || current.AskQty is not { } askQty)
            {
                previous = current;
                continue;
            }

            decimal contribution = 0m;
            if (bid >= previousBid)
            {
                contribution += bidQty;
            }

            if (bid <= previousBid)
            {
                contribution -= previousBidQty;
            }

            if (ask <= previousAsk)
            {
                contribution -= askQty;
            }

            if (ask >= previousAsk)
            {
                contribution += previousAskQty;
            }

            ofi += contribution;
            denominator += previousBidQty + previousAskQty + bidQty + askQty;
            previous = current;
        }

        return (ofi, denominator);
    }

    private static BtcOrderBookPredictionRule? FitRule(
        ConfigurationKey configuration,
        string featureName,
        IReadOnlyList<DateTimeOffset> trainMarkets,
        IReadOnlyList<DateTimeOffset> validationMarkets,
        IReadOnlyDictionary<DateTimeOffset, BtcOrderBookPredictionFeatureRow> rows,
        IReadOnlyDictionary<DateTimeOffset, string> outcomes)
    {
        var train = trainMarkets
            .Select(market => (Market: market, Value: GetFeature(rows[market], featureName), Outcome: outcomes[market]))
            .Where(item => item.Value is not null)
            .Select(item => (item.Market, Value: item.Value!.Value, item.Outcome))
            .ToArray();
        var validation = validationMarkets
            .Select(market => (Market: market, Value: GetFeature(rows[market], featureName), Outcome: outcomes[market]))
            .Where(item => item.Value is not null)
            .Select(item => (item.Market, Value: item.Value!.Value, item.Outcome))
            .ToArray();
        if (train.Length != trainMarkets.Count || validation.Length != validationMarkets.Count ||
            train.Length < 10 || validation.Length < 5)
        {
            return null;
        }

        decimal[] sortedValues = train.Select(item => item.Value).Order().ToArray();
        decimal[] thresholds = new[] { 0.10m, 0.25m, 0.50m, 0.75m, 0.90m }
            .Select(quantile => Quantile(sortedValues, quantile))
            .Append(0m)
            .Distinct()
            .ToArray();
        BtcOrderBookPredictionRule? best = null;
        foreach (decimal threshold in thresholds)
        {
            foreach (bool greaterPredictsUp in new[] { true, false })
            {
                decimal trainBalanced = CalculateMetrics(train.Select(item =>
                    (item.Outcome, Predict(item.Value, threshold, greaterPredictsUp)))).BalancedAccuracy;
                decimal validationBalanced = CalculateMetrics(validation.Select(item =>
                    (item.Outcome, Predict(item.Value, threshold, greaterPredictsUp)))).BalancedAccuracy;
                var candidate = new BtcOrderBookPredictionRule(
                    configuration.DecisionLeadSeconds,
                    configuration.FeatureWindowSeconds,
                    featureName,
                    threshold,
                    greaterPredictsUp,
                    trainBalanced,
                    validationBalanced);
                if (best is null ||
                    candidate.ValidationBalancedAccuracy > best.ValidationBalancedAccuracy ||
                    candidate.ValidationBalancedAccuracy == best.ValidationBalancedAccuracy &&
                    candidate.TrainBalancedAccuracy > best.TrainBalancedAccuracy)
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    private static BtcOrderBookPredictionSplit BuildChronologicalSplit(
        IReadOnlyList<DateTimeOffset> markets,
        decimal trainFraction,
        decimal validationFraction,
        int maximumWindowSeconds,
        int maximumDecisionLeadSeconds)
    {
        int trainEnd = Math.Clamp((int)decimal.Floor(markets.Count * trainFraction), 1, markets.Count);
        int validationEnd = Math.Clamp(
            trainEnd + (int)decimal.Floor(markets.Count * validationFraction),
            trainEnd,
            markets.Count);
        // Purge enough markets that every later feature window starts after the
        // preceding split's final market has finished.  The decision lead is
        // part of that distance: window start = market start - lead - window.
        int embargoMarkets = Math.Max(
            1,
            (int)Math.Ceiling((maximumWindowSeconds + maximumDecisionLeadSeconds) / (double)MarketSeconds));
        var train = markets.Take(trainEnd).ToArray();
        var validation = markets.Skip(Math.Min(markets.Count, trainEnd + embargoMarkets))
            .Take(Math.Max(0, validationEnd - trainEnd - embargoMarkets))
            .ToArray();
        var test = markets.Skip(Math.Min(markets.Count, validationEnd + embargoMarkets)).ToArray();
        return new BtcOrderBookPredictionSplit(train, validation, test, embargoMarkets);
    }

    private static BtcOrderBookPredictionMetrics CalculateMetrics(
        IEnumerable<(string Actual, string Predicted)> observations)
    {
        var rows = observations.ToArray();
        int trueUp = rows.Count(item => item.Actual == "Up" && item.Predicted == "Up");
        int falseUp = rows.Count(item => item.Actual == "Down" && item.Predicted == "Up");
        int trueDown = rows.Count(item => item.Actual == "Down" && item.Predicted == "Down");
        int falseDown = rows.Count(item => item.Actual == "Up" && item.Predicted == "Down");
        int upCount = trueUp + falseDown;
        int downCount = trueDown + falseUp;
        decimal accuracy = rows.Length == 0 ? 0m : (decimal)(trueUp + trueDown) / rows.Length;
        decimal upRecall = upCount == 0 ? 0m : (decimal)trueUp / upCount;
        decimal downRecall = downCount == 0 ? 0m : (decimal)trueDown / downCount;
        decimal balancedAccuracy = (upRecall + downRecall) / 2m;
        decimal upPrecision = trueUp + falseUp == 0 ? 0m : (decimal)trueUp / (trueUp + falseUp);
        decimal brier = rows.Length == 0 ? 0m : (decimal)(falseUp + falseDown) / rows.Length;
        return new BtcOrderBookPredictionMetrics(
            rows.Length,
            upCount,
            downCount,
            trueUp,
            falseUp,
            trueDown,
            falseDown,
            accuracy,
            balancedAccuracy,
            upPrecision,
            upRecall,
            downRecall,
            brier);
    }

    private static decimal? CalculateGammaBinanceAgreement(
        IReadOnlyCollection<BtcOrderBookPredictionFeatureRow> rows)
    {
        var comparable = rows
            .Where(row => IsKnownOutcome(row.GammaOutcome) && IsKnownOutcome(row.BinanceProxyOutcome))
            .GroupBy(row => row.MarketStartUtc)
            .Select(group => group.First())
            .ToArray();
        return comparable.Length == 0
            ? null
            : (decimal)comparable.Count(row => row.GammaOutcome == row.BinanceProxyOutcome) / comparable.Length;
    }

    private static BtcOrderBookPredictionAnalysisResult CreateIncompleteResult(
        string status,
        DateTimeOffset analyzedAtUtc,
        int featureRows,
        int uniqueMarkets,
        int labeledMarkets,
        int validCommonMarkets,
        int distinctUtcDays,
        int minimumLabeledMarkets,
        int minimumDistinctUtcDays,
        int minimumMarketsPerClass,
        decimal trainFraction,
        decimal validationFraction,
        decimal testFraction,
        decimal? gammaBinanceAgreement,
        IReadOnlyList<string> exclusionReasons,
        string conclusion)
    {
        return new BtcOrderBookPredictionAnalysisResult(
            status,
            analyzedAtUtc,
            featureRows,
            uniqueMarkets,
            labeledMarkets,
            validCommonMarkets,
            distinctUtcDays,
            minimumLabeledMarkets,
            minimumDistinctUtcDays,
            minimumMarketsPerClass,
            trainFraction,
            validationFraction,
            testFraction,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            0,
            gammaBinanceAgreement,
            exclusionReasons,
            [],
            conclusion);
    }

    private static decimal? GetFeature(BtcOrderBookPredictionFeatureRow row, string featureName)
    {
        return featureName switch
        {
            nameof(BtcOrderBookPredictionFeatureRow.LastImbalance) => row.LastImbalance,
            nameof(BtcOrderBookPredictionFeatureRow.TimeWeightedImbalance) => row.TimeWeightedImbalance,
            nameof(BtcOrderBookPredictionFeatureRow.ImbalanceSlopePerSecond) => row.ImbalanceSlopePerSecond,
            nameof(BtcOrderBookPredictionFeatureRow.LastMicropriceOffsetBps) => row.LastMicropriceOffsetBps,
            nameof(BtcOrderBookPredictionFeatureRow.TimeWeightedMicropriceOffsetBps) => row.TimeWeightedMicropriceOffsetBps,
            nameof(BtcOrderBookPredictionFeatureRow.ObservedL1OfiNormalized) => row.ObservedL1OfiNormalized,
            _ => null
        };
    }

    private static decimal Quantile(IReadOnlyList<decimal> sorted, decimal quantile)
    {
        if (sorted.Count == 0)
        {
            return 0m;
        }

        decimal position = quantile * (sorted.Count - 1);
        int lower = (int)decimal.Floor(position);
        int upper = (int)decimal.Ceiling(position);
        if (lower == upper)
        {
            return sorted[lower];
        }

        decimal fraction = position - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }

    private static string Predict(decimal value, decimal threshold, bool greaterOrEqualPredictsUp)
    {
        bool greater = value >= threshold;
        return greater == greaterOrEqualPredictsUp ? "Up" : "Down";
    }

    private static string MajorityOutcome(IEnumerable<string> outcomes)
    {
        int up = outcomes.Count(value => value == "Up");
        int down = outcomes.Count(value => value == "Down");
        return up >= down ? "Up" : "Down";
    }

    private static bool IsKnownOutcome(string? value)
    {
        return string.Equals(value, "Up", StringComparison.Ordinal) ||
            string.Equals(value, "Down", StringComparison.Ordinal);
    }

    private static bool HasOfficialGammaLabel(BtcOrderBookPredictionFeatureRow row)
    {
        return IsKnownOutcome(row.GammaOutcome) &&
            string.Equals(row.GammaLabelStatus, "gamma_closed", StringComparison.Ordinal);
    }

    private static bool HasMinimumClassCounts(
        IReadOnlyCollection<DateTimeOffset> markets,
        IReadOnlyDictionary<DateTimeOffset, string> outcomes,
        int minimumPerClass)
    {
        int up = markets.Count(market => outcomes[market] == "Up");
        int down = markets.Count(market => outcomes[market] == "Down");
        return up >= minimumPerClass && down >= minimumPerClass;
    }

    private static bool HasQualityGap(
        IEnumerable<BtcOrderBookPredictionRawEvent> controlEvents,
        DateTimeOffset windowStartUtc,
        DateTimeOffset decisionUtc)
    {
        DateTimeOffset? persistentGapStart = null;
        foreach (var item in controlEvents)
        {
            if (item.ReceivedUtc >= decisionUtc)
            {
                break;
            }

            if (item.Status == "connected")
            {
                if (persistentGapStart is { } gapStart &&
                    gapStart < decisionUtc && item.ReceivedUtc > windowStartUtc)
                {
                    return true;
                }

                persistentGapStart = null;
                continue;
            }

            if (IsPersistentQualityGapStart(item.Status))
            {
                persistentGapStart ??= item.ReceivedUtc;
                continue;
            }

            if (IsQualityIssue(item.Status) &&
                item.ReceivedUtc >= windowStartUtc && item.ReceivedUtc < decisionUtc)
            {
                return true;
            }
        }

        return persistentGapStart is { } activeStart &&
            activeStart < decisionUtc && decisionUtc > windowStartUtc;
    }

    private static bool IsQualityControl(string status)
    {
        return status == "connected" || IsQualityIssue(status);
    }

    private static bool IsPersistentQualityGapStart(string status)
    {
        return status is "connection_error" or "disconnected" or "server_shutdown" or
            "collector_failed" or "local_queue_overflow";
    }

    private static bool IsQualityIssue(string status)
    {
        return status is "connection_error" or "disconnected" or "decode_error" or "parse_error" or
            "unexpected_frame_type" or "clock_regression" or "collector_failed" or "local_queue_overflow" or
            "local_backpressure" or "server_shutdown" or "symbol_mismatch";
    }

    private static void TrimBooks(LinkedList<BtcOrderBookPredictionRawEvent> items, DateTimeOffset cutoff)
    {
        while (items.First?.Next is not null && items.First.Next.Value.ReceivedUtc < cutoff)
        {
            items.RemoveFirst();
        }
    }

    private static void TrimAll(LinkedList<BtcOrderBookPredictionRawEvent> items, DateTimeOffset cutoff)
    {
        while (items.First is not null && items.First.Value.ReceivedUtc < cutoff)
        {
            items.RemoveFirst();
        }
    }

    private readonly record struct FeatureSchedule(
        DateTimeOffset MarketStartUtc,
        DateTimeOffset MarketEndUtc,
        DateTimeOffset DecisionUtc,
        int DecisionLeadSeconds,
        int FeatureWindowSeconds,
        DateTimeOffset FeatureWindowStartUtc);

    private readonly record struct ConfigurationKey(int DecisionLeadSeconds, int FeatureWindowSeconds);
}
