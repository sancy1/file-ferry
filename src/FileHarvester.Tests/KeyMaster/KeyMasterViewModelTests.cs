// ============================================================================
// filepath: src/FileHarvester.Tests/KeyMaster/KeyMasterViewModelTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.KeyMaster
// purpose:  Unit tests for KeyMasterViewModel: selection, generation,
//           masking/reveal, copy, batch, history, Pro sample match, errors.
// ============================================================================

using DeploymentVault.Core.KeyMaster.Abstractions;
using DeploymentVault.Core.KeyMaster.Domain;
using DeploymentVault.Platform.KeyMaster.Generation;
using DeploymentVault.Platform.KeyMaster.Storage;
using FileHarvester.App.ViewModels;
using FluentAssertions;
using Portfolio.Resilience.Abstractions;

namespace FileHarvester.Tests.KeyMaster;

public sealed class KeyMasterViewModelTests
{
    private static KeyMasterViewModel CreateVm(Func<string, Task>? clipboard = null)
    {
        IKeyGenerator[] generators =
        {
            new SymmetricSecretGenerator(),
            new DjangoSecretGenerator(),
            new FastApiSecretGenerator(),
            new FlaskSecretGenerator(),
            new AesGcmKeyGenerator(),
            new RsaKeyPairGenerator(),
            new EcdsaKeyPairGenerator(),
            new Ed25519Generator(),
            new ProGenerator(),
        };
        var registry = new KeyGeneratorRegistry(generators, new StubExecutor());
        var store = new InMemoryKeyMasterStore();
        return new KeyMasterViewModel(registry, store, clipboard);
    }

    private static FrameworkProfile Profile(KeyKind kind) => FrameworkProfile.For(kind);

    private static void SelectKind(KeyMasterViewModel vm, KeyKind kind)
    {
        var profile = Profile(kind);
        vm.SelectedCategory = profile.Category;
        vm.SelectedProfile = vm.Profiles.First(p => p.Kind == kind);
    }

    // ---------------------------------------------------------------
    // Selection / defaults
    // ---------------------------------------------------------------

    [Fact]
    void CtorSelectsFirstProfileAndComputesEntropy()
    {
        var vm = CreateVm();
        vm.SelectedProfile.Should().NotBeNull();
        vm.Profiles.Should().NotBeEmpty();
        vm.EntropyBits.Should().BeGreaterThan(0);
        vm.EntropyLabel.Should().NotBe("—");
    }

    [Fact]
    void CategorySwitchRepopulatesProfiles()
    {
        var vm = CreateVm();
        vm.SelectedCategory = KeyCategory.Python;
        vm.Profiles.Select(p => p.Kind).Should().BeEquivalentTo(
            new[] { KeyKind.DjangoSecret, KeyKind.FastApiSecret, KeyKind.FlaskSecret });
        vm.SelectedProfile.Should().NotBeNull();
    }

    [Fact]
    void SelectKindUpdatesVisibilityFlags()
    {
        var vm = CreateVm();
        SelectKind(vm, KeyKind.JwtRsa);
        vm.ShowModulus.Should().BeTrue();
        vm.ShowLength.Should().BeFalse();
        vm.ShowCurve.Should().BeFalse();

        SelectKind(vm, KeyKind.JwtEcdsa);
        vm.ShowCurve.Should().BeTrue();
        vm.ShowModulus.Should().BeFalse();

        SelectKind(vm, KeyKind.ProKey);
        vm.ShowProControls.Should().BeTrue();
        vm.ShowSampleMatch.Should().BeTrue();
        vm.ShowCharsetToggles.Should().BeTrue();
    }

    // ---------------------------------------------------------------
    // Generation
    // ---------------------------------------------------------------

    [Fact]
    async Task GenerateSetsMaskedOutputAndHistory()
    {
        var vm = CreateVm();
        SelectKind(vm, KeyKind.JwtHmac);
        vm.Algorithm = "HS256";

        await ExecAsync(vm.GenerateCommand);

        vm.HasCurrentKey.Should().BeTrue();
        vm.CurrentKey!.Kind.Should().Be(KeyKind.JwtHmac);
        vm.DisplayValue.Should().NotBe(vm.CurrentKey.Value); // masked
        vm.DisplayValue.Should().Contain("•");
        vm.History.Should().HaveCount(1);
        vm.SessionCount.Should().Be(1);
        vm.ErrorText.Should().BeEmpty();
    }

    [Fact]
    async Task RevealTogglesDisplayValue()
    {
        var vm = CreateVm();
        SelectKind(vm, KeyKind.JwtHmac);
        await ExecAsync(vm.GenerateCommand);

        var masked = vm.DisplayValue;
        await ExecAsync(vm.RevealCommand);
        vm.DisplayValue.Should().Be(vm.CurrentKey!.Value);
        vm.IsRevealed.Should().BeTrue();

        await ExecAsync(vm.RevealCommand);
        vm.DisplayValue.Should().Be(masked);
    }

    [Fact]
    async Task CopyCopiesRawValueNotMasked()
    {
        string? copied = null;
        var vm = CreateVm(s => { copied = s; return Task.CompletedTask; });
        SelectKind(vm, KeyKind.JwtHmac);
        await ExecAsync(vm.GenerateCommand);

        await ExecAsync(vm.CopyCommand);
        copied.Should().Be(vm.CurrentKey!.Value);
    }

    [Fact]
    async Task GenerateBatchAddsAllToHistoryCappedAtFive()
    {
        var vm = CreateVm();
        SelectKind(vm, KeyKind.JwtHmac);
        vm.BatchCount = 10;

        await ExecAsync(vm.GenerateBatchCommand);

        vm.BatchResults.Should().HaveCount(10);
        vm.History.Should().HaveCount(5); // store cap
        vm.SessionCount.Should().Be(10);
    }

    [Fact]
    async Task KeyPairGenerateSetsPublicValue()
    {
        var vm = CreateVm();
        SelectKind(vm, KeyKind.GoEd25519);
        vm.Format = KeyFormat.Pem;

        await ExecAsync(vm.GenerateCommand);

        vm.IsKeyPair.Should().BeTrue();
        vm.CurrentKey!.PublicValue.Should().NotBeNullOrEmpty();
        vm.DisplayPublicValue.Should().Contain("•"); // masked
    }

    [Fact]
    async Task InvalidSpecSetsErrorText()
    {
        var vm = CreateVm();
        SelectKind(vm, KeyKind.GoAesGcm);
        vm.Length = 20; // not 16/24/32

        await ExecAsync(vm.GenerateCommand);

        vm.ErrorText.Should().NotBeEmpty();
        vm.HasCurrentKey.Should().BeFalse();
    }

    [Fact]
    async Task ClearEmptiesHistoryAndResetsCount()
    {
        var vm = CreateVm();
        SelectKind(vm, KeyKind.JwtHmac);
        await ExecAsync(vm.GenerateCommand);
        await ExecAsync(vm.GenerateCommand);
        vm.SessionCount.Should().Be(2);

        await ExecAsync(vm.ClearCommand);

        vm.SessionCount.Should().Be(0);
        vm.History.Should().BeEmpty();
        vm.HasCurrentKey.Should().BeFalse();
    }

    // ---------------------------------------------------------------
    // Pro mode / sample match
    // ---------------------------------------------------------------

    [Fact]
    void MatchSampleAppliesProfileToProControls()
    {
        var vm = CreateVm();
        SelectKind(vm, KeyKind.ProKey);
        vm.SampleText = "Ab3!xY9#kLm2$"; // mixed charset, 13 chars

        vm.MatchSampleCommand.Execute(null);

        vm.Length.Should().Be(13);
        vm.UseUpper.Should().BeTrue();
        vm.UseLower.Should().BeTrue();
        vm.UseDigits.Should().BeTrue();
        vm.UseSymbols.Should().BeTrue();
        vm.StatusText.Should().Contain("Matched sample");
    }

    [Fact]
    void MatchSampleIgnoredForNonProKind()
    {
        var vm = CreateVm();
        SelectKind(vm, KeyKind.JwtHmac);
        vm.SampleText = "Ab3!xY9#kLm2$";
        var lengthBefore = vm.Length;

        vm.MatchSampleCommand.Execute(null);

        vm.Length.Should().Be(lengthBefore);
    }

    [Fact]
    void AnalyzeSampleSetsProfileText()
    {
        var vm = CreateVm();
        vm.SampleText = "0123456789abcdef";

        vm.AnalyzeSampleCommand.Execute(null);

        vm.SampleProfileText.Should().Contain("hex");
    }

    // ---------------------------------------------------------------
    // Entropy meter
    // ---------------------------------------------------------------

    [Fact]
    void EntropyUpdatesWithLength()
    {
        var vm = CreateVm();
        SelectKind(vm, KeyKind.ProKey);
        var before = vm.EntropyBits;

        vm.Length = 128;

        vm.EntropyBits.Should().BeGreaterThan(before);
    }

    [Fact]
    async Task UnsupportedFormatBlocksGenerationWithErrorBanner()
    {
        var vm = CreateVm();
        SelectKind(vm, KeyKind.JwtHmac);
        vm.Format = KeyFormat.Json; // not supported for HMAC secrets

        vm.IsFormatSupported.Should().BeFalse();
        await ExecAsync(vm.GenerateCommand);

        vm.ErrorText.Should().Contain("not supported");
        vm.HasCurrentKey.Should().BeFalse();
    }

    [Fact]
    void FormatOptionsArePerKind()
    {
        var vm = CreateVm();
        SelectKind(vm, KeyKind.JwtRsa);
        vm.FormatOptions.Should().BeEquivalentTo(new[] { KeyFormat.Pem, KeyFormat.Json });

        SelectKind(vm, KeyKind.GoEd25519);
        vm.FormatOptions.Should().Contain(KeyFormat.Hex);

        SelectKind(vm, KeyKind.ProKey);
        vm.FormatOptions.Should().HaveCount(Enum.GetValues<KeyFormat>().Length);
    }

    [Fact]
    void EntropyExclusionsReducePool()
    {
        var vm = CreateVm();
        SelectKind(vm, KeyKind.ProKey);
        var before = vm.EntropyBits;

        vm.Exclusions = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        vm.EntropyBits.Should().BeLessThan(before);
    }

    private static async Task ExecAsync(System.Windows.Input.ICommand command)
    {
        command.Execute(null);
        if (command is AsyncRelayCommand<object> asyncCommand)
        {
            await asyncCommand.ExecutionTask.ConfigureAwait(false);
        }
    }

    private sealed class StubExecutor : IResilienceExecutor
    {
        public async Task<T> ExecuteAsync<T>(string p, Func<CancellationToken, Task<T>> op,
            Func<CancellationToken, Task<T>>? fb, string? k, int? t, CancellationToken ct)
            => await op(ct).ConfigureAwait(false);
        public Task ExecuteAsync(string p, Func<CancellationToken, Task> op,
            Func<CancellationToken, Task>? fb, string? k, int? t, CancellationToken ct)
            => op(ct);
    }
}
