using LlamaLink;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LlamaLink.Tests;

[TestClass]
public sealed class CostEstimatorTests
{
    [TestMethod]
    public void CalculatesEnergyAndCurrencyFromObservedRun()
    {
        var estimate = CostEstimator.Calculate(100, 500, 60, 120, 0.20);

        Assert.AreEqual(2.0, estimate.WattHours, 0.0001);
        Assert.AreEqual(0.0004, estimate.Currency, 0.000001);
        StringAssert.Contains(CostEstimator.Format(estimate), "600 tok");
    }

    [TestMethod]
    public void ForecastUsesTokensPerSecondForLongResponses()
    {
        var estimate = CostEstimator.Forecast(200, 1200, 20, 150, 0.18);

        Assert.AreEqual(60, estimate.ElapsedSeconds, 0.0001);
        Assert.AreEqual(2.5, estimate.WattHours, 0.0001);
    }
}
