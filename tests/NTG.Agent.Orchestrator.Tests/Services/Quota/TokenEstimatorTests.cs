using NUnit.Framework;
using NTG.Agent.Orchestrator.Services.Quota;

namespace NTG.Agent.Orchestrator.Tests.Services.Quota;

[TestFixture]
public class TokenEstimatorTests
{
    [TestCase("")]
    [TestCase(" ")]
    [TestCase(null)]
    public void EstimateTokens_EmptyOrNullInput_ReturnsZero(string? input)
    {
        // Act
        var result = TokenEstimator.EstimateTokens(input);

        // Assert
        Assert.That(result, Is.EqualTo(0));
    }

    [Test]
    public void EstimateTokens_StandardText_ReturnsAccurateCount()
    {
        // Arrange
        string text = "Hello, how are you?";

        // Act
        var result = TokenEstimator.EstimateTokens(text);

        // Assert
        Assert.That(result, Is.EqualTo(6));
    }
}