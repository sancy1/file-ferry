// ============================================================================
// FILE:     src/FileHarvester.Tests/BootstrapSmokeTests.cs
// PURPOSE:  End-to-end smoke test for the Portfolio.Resilience pipeline in
//           the File-Ferry host. Proves the resilience wiring from Phase 0.3
//           works before any feature code is built on top of it.
// RELATION: Consumes TestHarness.CreateProviderWithPolicy() from
//           Infrastructure. Each circuit test uses a unique policy name to
//           isolate circuit and metric state (Portfolio.Resilience
//           troubleshooting trap 24.13).
// ============================================================================

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Portfolio.Resilience.Abstractions;
using Portfolio.Resilience.Correlation;
using Portfolio.Resilience.Errors;
using FileHarvester.Tests.Infrastructure;
using Xunit;

namespace FileHarvester.Tests;

/// <summary>
/// Smoke tests for the Portfolio.Resilience pipeline in the File-Ferry host.
/// Each test uses a unique policy name to isolate circuit state.
/// </summary>
public sealed class BootstrapSmokeTests : IClassFixture<TestHarness>
{
    private readonly TestHarness _harness;

    public BootstrapSmokeTests(TestHarness harness)
    {
        _harness = harness;
    }

    private static string NewPolicyName() => $"smoke-{Guid.NewGuid():N}";

    // ---------------------------------------------------------------------
    // TEST 1 — Happy path
    // ---------------------------------------------------------------------
    [Fact]
    public async Task HappyPathReadsRealFileSucceeds()
    {
        var policyName = NewPolicyName();
        var path = _harness.WriteFile("test-input.txt", "hello from File-Ferry");

        using var provider = TestHarness.CreateProviderWithPolicy(
            policyName, failureThreshold: 10);

        var executor = provider.GetRequiredService<IResilienceExecutor>();

        var content = await executor.ExecuteAsync(
            policyName: policyName,
            operation: token => File.ReadAllTextAsync(path, token),
            ct: CancellationToken.None);

        content.Should().Be("hello from File-Ferry");
    }

    // ---------------------------------------------------------------------
    // TEST 2 — Circuit opens after threshold
    // ---------------------------------------------------------------------
    [Fact]
    public async Task CircuitOpensAfterThreshold()
    {
        var policyName = NewPolicyName();
        const int failureThreshold = 5;

        using var provider = TestHarness.CreateProviderWithPolicy(
            policyName, failureThreshold: failureThreshold);

        var executor = provider.GetRequiredService<IResilienceExecutor>();
        var monitor = provider.GetRequiredService<ICircuitBreakerMonitor>();

        for (var i = 0; i < failureThreshold + 2; i++)
        {
            try
            {
                await executor.ExecuteAsync(
                    policyName: policyName,
                    operation: _ => throw new IOException("simulated failure"),
                    ct: CancellationToken.None);
            }
            catch (ResilienceException) { /* expected */ }
        }

        var snapshot = monitor.Snapshot()
            .FirstOrDefault(c => c.PolicyName == policyName);

        snapshot.Should().NotBeNull(
            "the circuit monitor should have a snapshot for the policy");

        snapshot!.State.Should().Be(
            CircuitState.Open,
            "the circuit should be Open after exceeding the failure threshold");
    }

    // ---------------------------------------------------------------------
    // TEST 3 — Circuit rejects subsequent calls without invoking operation
    // ---------------------------------------------------------------------
    [Fact]
    public async Task CircuitWhenOpenRejectsWithoutInvoking()
    {
        var policyName = NewPolicyName();
        const int failureThreshold = 3;

        using var provider = TestHarness.CreateProviderWithPolicy(
            policyName, failureThreshold: failureThreshold);

        var executor = provider.GetRequiredService<IResilienceExecutor>();

        for (var i = 0; i < failureThreshold + 1; i++)
        {
            try
            {
                await executor.ExecuteAsync(
                    policyName: policyName,
                    operation: _ => throw new IOException("trip"),
                    ct: CancellationToken.None);
            }
            catch (ResilienceException) { /* expected */ }
        }

        var operationWasInvoked = false;

        var act = async () => await executor.ExecuteAsync(
            policyName: policyName,
            operation: _ =>
            {
                operationWasInvoked = true;
                return Task.FromResult("should not run");
            },
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ResilienceException>();
        ex.Which.Category.Should().Be(ResilienceErrorCategory.CircuitOpen);
        ex.Which.AttemptsMade.Should().Be(0,
            "the operation should never have been invoked while the circuit is Open");
        operationWasInvoked.Should().BeFalse(
            "the operation lambda must not run when the circuit is Open");
    }

    // ---------------------------------------------------------------------
    // TEST 4 — Fallback returns degraded response
    // ---------------------------------------------------------------------
    [Fact]
    public async Task FallbackWhenPipelineFailsReturnsFallbackValue()
    {
        var policyName = NewPolicyName();

        using var provider = TestHarness.CreateProviderWithPolicy(
            policyName, failureThreshold: 100, maxAttempts: 1);

        var executor = provider.GetRequiredService<IResilienceExecutor>();

        var result = await executor.ExecuteAsync(
            policyName: policyName,
            operation: _ => throw new IOException("simulated"),
            fallback: _ => Task.FromResult("fallback-value"),
            ct: CancellationToken.None);

        result.Should().Be("fallback-value");
    }

    // ---------------------------------------------------------------------
    // TEST 5 — Correlation ID flows through the operation
    // ---------------------------------------------------------------------
    [Fact]
    public async Task CorrelationIdFlowsThroughPipeline()
    {
        var policyName = NewPolicyName();
        using var provider = TestHarness.CreateProviderWithPolicy(policyName);
        var executor = provider.GetRequiredService<IResilienceExecutor>();

        var jobCorrelationId = CorrelationContext.NewId();
        string? observedCorrelationId = null;

        using (CorrelationContext.Push(jobCorrelationId))
        {
            await executor.ExecuteAsync(
                policyName: policyName,
                operation: _ =>
                {
                    observedCorrelationId = CorrelationContext.CurrentId;
                    return Task.FromResult("ok");
                },
                ct: CancellationToken.None);
        }

        observedCorrelationId.Should().Be(jobCorrelationId,
            "the pipeline must propagate the ambient correlation ID to the operation");
    }
}
