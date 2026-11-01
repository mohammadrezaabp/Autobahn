using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Autobahn.Domain.Configuration;

namespace Autobahn.Application.Tests.Configuration;

public sealed class GatewayOptionsValidationTests
{
    [Fact]
    public void Validate_EmptyModelsConfigPath_ReturnsError()
    {
        var options = new GatewayOptions { ModelsConfigPath = "  " };

        var errors = GatewayOptionsValidation.Validate(options);

        errors.Should().Contain(e => e.Contains(nameof(GatewayOptions.ModelsConfigPath), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ReloadIntervalOutOfRange_ReturnsError()
    {
        var options = new GatewayOptions { ConfigReloadIntervalSeconds = 0 };

        var errors = GatewayOptionsValidation.Validate(options);

        errors.Should().Contain(e => e.Contains(nameof(GatewayOptions.ConfigReloadIntervalSeconds), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ZeroHealthCheckInterval_ReturnsError()
    {
        var options = new GatewayOptions { HealthCheckIntervalSeconds = 0 };

        var errors = GatewayOptionsValidation.Validate(options);

        errors.Should().Contain(e => e.Contains(nameof(GatewayOptions.HealthCheckIntervalSeconds), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ZeroHealthCheckUnhealthyThreshold_ReturnsError()
    {
        var options = new GatewayOptions { HealthCheckUnhealthyThreshold = 0 };

        var errors = GatewayOptionsValidation.Validate(options);

        errors.Should().Contain(e => e.Contains(nameof(GatewayOptions.HealthCheckUnhealthyThreshold), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ZeroHealthCheckTimeout_ReturnsError()
    {
        var options = new GatewayOptions { HealthCheckTimeoutSeconds = 0 };

        var errors = GatewayOptionsValidation.Validate(options);

        errors.Should().Contain(e => e.Contains(nameof(GatewayOptions.HealthCheckTimeoutSeconds), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ZeroCircuitBreakerHalfOpenProbeTimeout_ReturnsError()
    {
        var options = new GatewayOptions
        {
            Resilience = new GatewayResilienceOptions { CircuitBreakerHalfOpenProbeTimeoutSeconds = 0 },
        };

        var errors = GatewayOptionsValidation.Validate(options);

        errors.Should().Contain(e => e.Contains(
            nameof(GatewayResilienceOptions.CircuitBreakerHalfOpenProbeTimeoutSeconds),
            StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ZeroRequestBufferThreshold_ReturnsError()
    {
        var options = new GatewayOptions
        {
            Resilience = new GatewayResilienceOptions { RequestBufferThresholdBytes = 0 },
        };

        var errors = GatewayOptionsValidation.Validate(options);

        errors.Should().Contain(e => e.Contains(
            nameof(GatewayResilienceOptions.RequestBufferThresholdBytes),
            StringComparison.Ordinal));
    }

    /// <summary>
    /// There is deliberately no "off" value: the gateway holds an upstream connection, a bulkhead slot
    /// and a budget reservation for the whole of a write to the client.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_NonPositiveDownstreamWriteTimeout_ReturnsError(int seconds)
    {
        var options = new GatewayOptions
        {
            Resilience = new GatewayResilienceOptions { DownstreamWriteTimeoutSeconds = seconds },
        };

        var errors = GatewayOptionsValidation.Validate(options);

        errors.Should().ContainSingle(e => e.Contains(nameof(GatewayResilienceOptions.DownstreamWriteTimeoutSeconds), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NegativeMinRequestBodyRate_ReturnsError()
    {
        var options = new GatewayOptions
        {
            Resilience = new GatewayResilienceOptions { MinRequestBodyBytesPerSecond = -1 },
        };

        var errors = GatewayOptionsValidation.Validate(options);

        errors.Should().ContainSingle(e => e.Contains(nameof(GatewayResilienceOptions.MinRequestBodyBytesPerSecond), StringComparison.Ordinal));
    }

    /// <summary>Zero is the documented way to switch the check off, not a misconfiguration.</summary>
    [Fact]
    public void Validate_ZeroMinRequestBodyRate_IsAllowed()
    {
        var options = new GatewayOptions
        {
            Resilience = new GatewayResilienceOptions { MinRequestBodyBytesPerSecond = 0 },
        };

        GatewayOptionsValidation.Validate(options).Should().BeEmpty();
    }

    [Fact]
    public void Validate_ZeroMinRequestBodyGrace_ReturnsError()
    {
        var options = new GatewayOptions
        {
            Resilience = new GatewayResilienceOptions { MinRequestBodyDataRateGraceSeconds = 0 },
        };

        var errors = GatewayOptionsValidation.Validate(options);

        errors.Should().ContainSingle(e => e.Contains(nameof(GatewayResilienceOptions.MinRequestBodyDataRateGraceSeconds), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ValidOptions_ReturnsNoErrors()
    {
        var options = new GatewayOptions();

        var errors = GatewayOptionsValidation.Validate(options);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_InvalidResilienceOptions_ReturnsErrors()
    {
        var options = new GatewayOptions
        {
            Resilience = new GatewayResilienceOptions
            {
                ForwardTimeoutSeconds = 0,
                MaxRequestBodyBytes = 0,
                MaxConcurrentForwardsPerModel = 0,
                CircuitBreakerFailureThreshold = 0,
                CircuitBreakerBreakDurationSeconds = 0,
            },
        };

        var errors = GatewayOptionsValidation.Validate(options);

        errors.Should().HaveCountGreaterThanOrEqualTo(5);
    }

    /// <summary>
    /// The breaker window and ratio were never validated: a ratio typo such as 1.5 (or 50 meaning
    /// percent) can never be reached and silently disables the breaker; 0 makes it count-only.
    /// </summary>
    [Theory]
    [InlineData(1.5)]
    [InlineData(50)]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    public void Validate_CircuitBreakerFailureRatioOutOfRange_ReturnsError(double ratio)
    {
        var options = new GatewayOptions
        {
            Resilience = new GatewayResilienceOptions { CircuitBreakerFailureRatioThreshold = ratio },
        };

        GatewayOptionsValidation.Validate(options)
            .Should().ContainSingle(e => e.Contains(nameof(GatewayResilienceOptions.CircuitBreakerFailureRatioThreshold), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Validate_CircuitBreakerFailureRatioInRange_IsAccepted(double ratio)
    {
        var options = new GatewayOptions
        {
            Resilience = new GatewayResilienceOptions { CircuitBreakerFailureRatioThreshold = ratio },
        };

        GatewayOptionsValidation.Validate(options).Should().BeEmpty();
    }

    [Fact]
    public void Validate_CircuitBreakerMinFailingCallersBelowOne_ReturnsError()
    {
        var options = new GatewayOptions
        {
            Resilience = new GatewayResilienceOptions { CircuitBreakerMinFailingCallers = 0 },
        };

        GatewayOptionsValidation.Validate(options)
            .Should().ContainSingle(e => e.Contains(nameof(GatewayResilienceOptions.CircuitBreakerMinFailingCallers), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_TracingOff_IsAccepted()
    {
        new GatewayOptions().Tracing.IsEnabled.Should().BeFalse();
        GatewayOptionsValidation.Validate(new GatewayOptions()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("http://alloy:4317", GatewayTracingOptions.Grpc, 1.0)]
    [InlineData("https://collector.example/v1/traces", GatewayTracingOptions.HttpProtobuf, 0.0)]
    public void Validate_TracingConfigured_IsAccepted(string endpoint, string protocol, double ratio)
    {
        var options = new GatewayOptions
        {
            Tracing = new GatewayTracingOptions { OtlpEndpoint = endpoint, OtlpProtocol = protocol, SampleRatio = ratio },
        };

        options.Tracing.IsEnabled.Should().BeTrue();
        GatewayOptionsValidation.Validate(options).Should().BeEmpty();
    }

    [Theory]
    [InlineData("http://tempo:4318", GatewayTracingOptions.HttpProtobuf, "http://tempo:4318/v1/traces")]
    [InlineData("http://tempo:4318/", GatewayTracingOptions.HttpProtobuf, "http://tempo:4318/v1/traces")]
    [InlineData("https://collector.example/otlp/v1/traces", GatewayTracingOptions.HttpProtobuf, "https://collector.example/otlp/v1/traces")]
    [InlineData("http://tempo:4317", GatewayTracingOptions.Grpc, "http://tempo:4317/")]
    public void ResolveExportEndpoint_AddsTheTracesPathOnlyForHttpWithoutOne(string endpoint, string protocol, string expected)
    {
        var tracing = new GatewayTracingOptions { OtlpEndpoint = endpoint, OtlpProtocol = protocol };

        tracing.ResolveExportEndpoint().Should().Be(new Uri(expected));
    }

    [Fact]
    public void Tracing_DefaultsToHttp_WhichWorksWithoutHttp2()
    {
        new GatewayTracingOptions().OtlpProtocol.Should().Be(GatewayTracingOptions.HttpProtobuf);
        new GatewayTracingOptions { OtlpEndpoint = "not a url" }.ResolveExportEndpoint().Should().BeNull();
    }

    [Theory]
    [InlineData("alloy:4317", GatewayTracingOptions.Grpc, 1.0, nameof(GatewayTracingOptions.OtlpEndpoint))]
    [InlineData("ftp://collector", GatewayTracingOptions.Grpc, 1.0, nameof(GatewayTracingOptions.OtlpEndpoint))]
    [InlineData("http://alloy:4317", "thrift", 1.0, nameof(GatewayTracingOptions.OtlpProtocol))]
    [InlineData("http://alloy:4317", GatewayTracingOptions.Grpc, 1.5, nameof(GatewayTracingOptions.SampleRatio))]
    [InlineData("http://alloy:4317", GatewayTracingOptions.Grpc, double.NaN, nameof(GatewayTracingOptions.SampleRatio))]
    public void Validate_TracingMisconfigured_ReturnsError(string endpoint, string protocol, double ratio, string setting)
    {
        var options = new GatewayOptions
        {
            Tracing = new GatewayTracingOptions { OtlpEndpoint = endpoint, OtlpProtocol = protocol, SampleRatio = ratio },
        };

        GatewayOptionsValidation.Validate(options)
            .Should().ContainSingle(e => e.Contains(setting, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_CircuitBreakerSamplingWindowBelowOneSecond_ReturnsError()
    {
        var options = new GatewayOptions
        {
            Resilience = new GatewayResilienceOptions { CircuitBreakerSamplingWindowSeconds = 0 },
        };

        GatewayOptionsValidation.Validate(options)
            .Should().ContainSingle(e => e.Contains(nameof(GatewayResilienceOptions.CircuitBreakerSamplingWindowSeconds), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_NegativeShutdownDrain_ReturnsError()
    {
        var options = new GatewayOptions
        {
            Resilience = new GatewayResilienceOptions { ShutdownDrainSeconds = -1 },
        };

        GatewayOptionsValidation.Validate(options)
            .Should().ContainSingle(e => e.Contains(nameof(GatewayResilienceOptions.ShutdownDrainSeconds), StringComparison.Ordinal));
    }

    [Fact]
    public void Bind_FromConfigurationDictionary_BindsGatewaySection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:ModelsConfigPath"] = "/etc/Autobahn/models.json",
                ["Gateway:ConfigReloadIntervalSeconds"] = "60",
            })
            .Build();

        var options = new GatewayOptions();
        configuration.GetSection(GatewayOptions.SectionName).Bind(options);

        options.ModelsConfigPath.Should().Be("/etc/Autobahn/models.json");
        options.ConfigReloadIntervalSeconds.Should().Be(60);
    }
}
