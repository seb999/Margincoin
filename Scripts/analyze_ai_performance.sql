-- ===================================================================
-- AI Model Performance Analysis Queries
-- ===================================================================
-- Run these queries to analyze your ML model's prediction accuracy
-- ===================================================================

-- 1. OVERALL ACCURACY: How often do "UP" predictions result in profit?
-- ===================================================================
SELECT
    'Overall Entry Accuracy' as Metric,
    COUNT(*) as TotalTrades,
    SUM(CASE WHEN AIPrediction = 'up' AND Profit > 0 THEN 1 ELSE 0 END) as CorrectPredictions,
    SUM(CASE WHEN AIPrediction = 'up' AND Profit <= 0 THEN 1 ELSE 0 END) as IncorrectPredictions,
    ROUND(CAST(SUM(CASE WHEN AIPrediction = 'up' AND Profit > 0 THEN 1 ELSE 0 END) AS FLOAT) / COUNT(*) * 100, 2) as AccuracyPercent
FROM "Order"
WHERE IsClosed = 1
  AND AIPrediction IS NOT NULL
  AND AIPrediction != '';

-- 2. PROFITABILITY BY PREDICTION
-- ===================================================================
SELECT
    AIPrediction as EntryPrediction,
    COUNT(*) as TradeCount,
    SUM(CASE WHEN Profit > 0 THEN 1 ELSE 0 END) as WinningTrades,
    ROUND(CAST(SUM(CASE WHEN Profit > 0 THEN 1 ELSE 0 END) AS FLOAT) / COUNT(*) * 100, 2) as WinRate,
    ROUND(AVG(Profit), 2) as AvgProfit,
    ROUND(SUM(Profit), 2) as TotalProfit,
    ROUND(AVG(AIScore), 3) as AvgConfidence
FROM "Order"
WHERE IsClosed = 1
  AND AIPrediction IS NOT NULL
  AND AIPrediction != ''
GROUP BY AIPrediction
ORDER BY TotalProfit DESC;

-- 3. CONFIDENCE ANALYSIS: Does higher confidence = better accuracy?
-- ===================================================================
SELECT
    CASE
        WHEN AIScore >= 0.8 THEN 'Very High (0.8-1.0)'
        WHEN AIScore >= 0.7 THEN 'High (0.7-0.8)'
        WHEN AIScore >= 0.6 THEN 'Medium (0.6-0.7)'
        WHEN AIScore >= 0.5 THEN 'Low (0.5-0.6)'
        ELSE 'Very Low (<0.5)'
    END as ConfidenceLevel,
    COUNT(*) as TradeCount,
    SUM(CASE WHEN Profit > 0 THEN 1 ELSE 0 END) as WinningTrades,
    ROUND(CAST(SUM(CASE WHEN Profit > 0 THEN 1 ELSE 0 END) AS FLOAT) / COUNT(*) * 100, 2) as WinRate,
    ROUND(AVG(Profit), 2) as AvgProfit,
    ROUND(SUM(Profit), 2) as TotalProfit
FROM "Order"
WHERE IsClosed = 1
  AND AIPrediction IS NOT NULL
  AND AIPrediction != ''
  AND AIScore > 0
GROUP BY
    CASE
        WHEN AIScore >= 0.8 THEN 'Very High (0.8-1.0)'
        WHEN AIScore >= 0.7 THEN 'High (0.7-0.8)'
        WHEN AIScore >= 0.6 THEN 'Medium (0.6-0.7)'
        WHEN AIScore >= 0.5 THEN 'Low (0.5-0.6)'
        ELSE 'Very Low (<0.5)'
    END
ORDER BY
    CASE
        WHEN AIScore >= 0.8 THEN 1
        WHEN AIScore >= 0.7 THEN 2
        WHEN AIScore >= 0.6 THEN 3
        WHEN AIScore >= 0.5 THEN 4
        ELSE 5
    END;

-- 4. EXIT PREDICTION EFFECTIVENESS
-- ===================================================================
SELECT
    'Exit Predictions' as Analysis,
    ExitAIPrediction,
    COUNT(*) as ExitCount,
    SUM(CASE WHEN Profit > 0 THEN 1 ELSE 0 END) as ProfitableExits,
    ROUND(AVG(Profit), 2) as AvgProfitAtExit,
    ROUND(AVG(ExitAIScore), 3) as AvgExitConfidence
FROM "Order"
WHERE IsClosed = 1
  AND ExitAIPrediction IS NOT NULL
  AND ExitAIPrediction != ''
GROUP BY ExitAIPrediction
ORDER BY ExitCount DESC;

-- 5. PREDICTION CHANGES: Entry vs Exit
-- ===================================================================
SELECT
    AIPrediction as EntryPrediction,
    ExitAIPrediction,
    COUNT(*) as OccurrenceCount,
    ROUND(AVG(Profit), 2) as AvgProfit,
    SUM(CASE WHEN Profit > 0 THEN 1 ELSE 0 END) as ProfitableCount,
    ROUND(CAST(SUM(CASE WHEN Profit > 0 THEN 1 ELSE 0 END) AS FLOAT) / COUNT(*) * 100, 2) as SuccessRate
FROM "Order"
WHERE IsClosed = 1
  AND AIPrediction IS NOT NULL
  AND AIPrediction != ''
  AND ExitAIPrediction IS NOT NULL
  AND ExitAIPrediction != ''
GROUP BY AIPrediction, ExitAIPrediction
ORDER BY OccurrenceCount DESC;

-- 6. SYMBOL-SPECIFIC PERFORMANCE
-- ===================================================================
SELECT
    Symbol,
    COUNT(*) as TotalTrades,
    SUM(CASE WHEN Profit > 0 THEN 1 ELSE 0 END) as WinningTrades,
    ROUND(CAST(SUM(CASE WHEN Profit > 0 THEN 1 ELSE 0 END) AS FLOAT) / COUNT(*) * 100, 2) as WinRate,
    ROUND(AVG(Profit), 2) as AvgProfit,
    ROUND(SUM(Profit), 2) as TotalProfit,
    ROUND(AVG(AIScore), 3) as AvgEntryConfidence,
    ROUND(AVG(CASE WHEN ExitAIScore > 0 THEN ExitAIScore ELSE NULL END), 3) as AvgExitConfidence
FROM "Order"
WHERE IsClosed = 1
  AND AIPrediction IS NOT NULL
  AND AIPrediction != ''
GROUP BY Symbol
HAVING COUNT(*) >= 3  -- At least 3 trades
ORDER BY TotalProfit DESC
LIMIT 15;

-- 7. RECENT PERFORMANCE TREND (Last 30 days by week)
-- ===================================================================
SELECT
    strftime('%Y-%W', CloseDate) as YearWeek,
    COUNT(*) as TradeCount,
    SUM(CASE WHEN Profit > 0 THEN 1 ELSE 0 END) as WinningTrades,
    ROUND(CAST(SUM(CASE WHEN Profit > 0 THEN 1 ELSE 0 END) AS FLOAT) / COUNT(*) * 100, 2) as WinRate,
    ROUND(AVG(Profit), 2) as AvgProfit,
    ROUND(AVG(AIScore), 3) as AvgConfidence
FROM "Order"
WHERE IsClosed = 1
  AND AIPrediction IS NOT NULL
  AND AIPrediction != ''
  AND CloseDate IS NOT NULL
GROUP BY strftime('%Y-%W', CloseDate)
ORDER BY YearWeek DESC
LIMIT 10;

-- 8. DETAILED LAST 20 TRADES WITH PREDICTIONS
-- ===================================================================
SELECT
    Id,
    Symbol,
    OpenDate,
    CloseDate,
    AIPrediction as EntryPred,
    ROUND(AIScore, 3) as EntryConf,
    ExitAIPrediction as ExitPred,
    ROUND(ExitAIScore, 3) as ExitConf,
    ROUND(Profit, 2) as Profit,
    ROUND((ClosePrice - OpenPrice) / OpenPrice * 100, 2) as ProfitPercent,
    Type as CloseReason,
    CASE
        WHEN AIPrediction = 'up' AND Profit > 0 THEN '✓ Correct'
        WHEN AIPrediction = 'up' AND Profit <= 0 THEN '✗ Wrong'
        ELSE '? Unknown'
    END as Accuracy
FROM "Order"
WHERE IsClosed = 1
  AND AIPrediction IS NOT NULL
  AND AIPrediction != ''
ORDER BY Id DESC
LIMIT 20;

-- 9. RECOMMENDATION QUERY: Should you use AI for trading?
-- ===================================================================
SELECT
    'RECOMMENDATION' as Analysis,
    COUNT(*) as TotalTrades,
    ROUND(CAST(SUM(CASE WHEN Profit > 0 THEN 1 ELSE 0 END) AS FLOAT) / COUNT(*) * 100, 2) as OverallWinRate,
    ROUND(AVG(Profit), 2) as AvgProfit,
    ROUND(SUM(Profit), 2) as TotalProfit,
    CASE
        WHEN CAST(SUM(CASE WHEN Profit > 0 THEN 1 ELSE 0 END) AS FLOAT) / COUNT(*) >= 0.60
            THEN '✅ GOOD - Consider enabling AI trading'
        WHEN CAST(SUM(CASE WHEN Profit > 0 THEN 1 ELSE 0 END) AS FLOAT) / COUNT(*) >= 0.50
            THEN '⚠️ MODERATE - Use with caution, high confidence only'
        ELSE '❌ POOR - More training needed, do NOT automate'
    END as Recommendation
FROM "Order"
WHERE IsClosed = 1
  AND AIPrediction IS NOT NULL
  AND AIPrediction != '';
