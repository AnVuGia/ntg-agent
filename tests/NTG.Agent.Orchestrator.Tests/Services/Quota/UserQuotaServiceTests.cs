using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NTG.Agent.Common.Dtos.TokenUsage;
using NTG.Agent.Orchestrator.Models.Quota;
using NTG.Agent.Orchestrator.Services.Quota;
using NTG.Agent.Orchestrator.Services.TokenTracking;

namespace NTG.Agent.Orchestrator.Tests.Services.Quota;

[TestFixture]
public class UserQuotaServiceTests
{
    private Mock<ITokenTrackingService> _mockTokenService;
    private QuotaSettings _defaultSettings;

    [SetUp]
    public void Setup()
    {
        _mockTokenService = new Mock<ITokenTrackingService>();
        
        _defaultSettings = new QuotaSettings
        {
            IsEnabled = true,
            MaxTokensAuth = 50000,
            MaxTokensAnonymous = 10000,
            ResetPeriodHours = 12
        };
    }

    private UserQuotaService CreateService(QuotaSettings settings)
    {
        var optionsMock = new Mock<IOptions<QuotaSettings>>();
        optionsMock.Setup(o => o.Value).Returns(settings);
        return new UserQuotaService(_mockTokenService.Object, optionsMock.Object);
    }

    [Test]
    public async Task CheckQuotaAsync_FeatureDisabled_ReturnsAlwaysAllowed()
    {
        // Arrange
        var settings = new QuotaSettings { IsEnabled = false };
        var service = CreateService(settings);
        
        // Act
        var result = await service.CheckQuotaAsync(Guid.NewGuid(), null, "A very long prompt.");

        // Assert
        Assert.That(result.IsAllowed, Is.True);
        Assert.That(result.RemainingTokens, Is.EqualTo(long.MaxValue));
        Assert.That(result.BlockReason, Is.Null);
        
        _mockTokenService.Verify(s => s.GetUsageStatsAsync(
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), 
            Times.Never);
    }

    [Test]
    public async Task CheckQuotaAsync_AuthUserUnderQuota_ReturnsAllowed()
    {
        // Arrange
        var service = CreateService(_defaultSettings);
        var userId = Guid.NewGuid();
        var promptText = "Hello world"; 

        var fakeStats = new TokenUsageStatsDto(0, 0, 0, TotalTokens: 10000, 0, 0, 0, 0, null, null, null);
        
        _mockTokenService
            .Setup(s => s.GetUsageStatsAsync(userId, null, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeStats);

        // Act
        var result = await service.CheckQuotaAsync(userId, null, promptText);

        // Assert
        Assert.That(result.IsAllowed, Is.True);
        Assert.That(result.RemainingTokens, Is.EqualTo(40000));
        Assert.That(result.BlockReason, Is.Null);
    }

    [Test]
    public async Task CheckQuotaAsync_AnonymousUserOverQuota_ReturnsBlocked()
    {
        // Arrange
        var service = CreateService(_defaultSettings);
        var sessionId = Guid.NewGuid();
        var promptText = "Hello, how are you?"; 
        
        var fakeStats = new TokenUsageStatsDto(0, 0, 0, TotalTokens: 9998, 0, 0, 0, 0, null!, null!, null!);
        
        _mockTokenService
            .Setup(s => s.GetUsageStatsAsync(null, sessionId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeStats);

        // Act
        var result = await service.CheckQuotaAsync(null, sessionId, promptText);

        // Assert
        Assert.That(result.IsAllowed, Is.False);
        Assert.That(result.RemainingTokens, Is.EqualTo(2)); 
        Assert.That(result.EstimatedTokens, Is.EqualTo(6));
        Assert.That(result.BlockReason, Is.EqualTo("quota_exhausted"));
    }

    [Test]
    public async Task CheckQuotaAsync_CallsTrackingServiceWithCorrectRollingWindow()
    {
        // Arrange
        var service = CreateService(_defaultSettings);
        var userId = Guid.NewGuid();
        var fakeStats = new TokenUsageStatsDto(0, 0, 0, TotalTokens: 0, 0, 0, 0, 0, null, null, null);
        DateTime? capturedFromDate = null;

        _mockTokenService
            .Setup(s => s.GetUsageStatsAsync(userId, null, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid?, Guid?, DateTime?, DateTime?, CancellationToken>((u, sid, from, to, ct) => 
            {
                capturedFromDate = from;
            })
            .ReturnsAsync(fakeStats);

        // Act
        await service.CheckQuotaAsync(userId, null, "Test");

        // Assert
        Assert.That(capturedFromDate, Is.Not.Null);
        var expectedTime = DateTime.UtcNow.AddHours(-_defaultSettings.ResetPeriodHours);
        var difference = Math.Abs((capturedFromDate.Value - expectedTime).TotalSeconds);
        Assert.That(difference, Is.LessThan(2.0));
    }
}