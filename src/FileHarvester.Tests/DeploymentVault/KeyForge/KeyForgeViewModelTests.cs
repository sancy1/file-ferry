// ============================================================================
// filepath: src/FileHarvester.Tests/DeploymentVault/KeyForge/KeyForgeViewModelTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.DeploymentVault.KeyForge
// since:    Phase A (KeyForge)
// purpose:  Unit tests for KeyForgeViewModel: convert, regenerate, save,
//           copy, clear, reset, import.
// ============================================================================

using DeploymentVault.Core.KeyForge.Domain;
using DeploymentVault.Platform.KeyForge.Documents;
using DeploymentVault.Platform.KeyForge.Extraction;
using DeploymentVault.Platform.KeyForge.Forgery;
using FileHarvester.App.ViewModels;
using Xunit;

namespace FileHarvester.Tests.DeploymentVault.KeyForge;

/// <summary>
/// Unit tests for <see cref="KeyForgeViewModel"/>.
/// </summary>
public sealed class KeyForgeViewModelTests
{
    private static string SampleKey => "gsk_" + new string('a', 48);

    private static KeyForgeViewModel NewVm(
        string? desktopDir = null,
        Func<string, string, Task<bool>>? confirm = null,
        Func<string, string, string, Task<string?>>? saveFile = null,
        Func<Task<string?>>? pickDocument = null,
        Func<string, Task>? copyToClipboard = null) =>
        new(
            new RegexKeyExtractor(),
            new ShapePreservingKeyForger(),
            new KeyForgeDocumentReader(),
            confirm,
            saveFile,
            pickDocument,
            copyToClipboard,
            desktopDir);

    private static async Task<KeyForgeViewModel> ConvertedVmAsync(string input)
    {
        var vm = NewVm();
        vm.OriginalText = input;
        var cmd = (AsyncRelayCommand<object>)vm.ConvertCommand;
        cmd.Execute(null);
        await cmd.ExecutionTask;
        return vm;
    }

    [Fact]
    public void ConvertDisabledWhenEmpty()
    {
        var vm = NewVm();
        Assert.False(vm.CanConvert);
    }

    [Fact]
    public void ConvertEnabledWhenHasInput()
    {
        var vm = NewVm();
        vm.OriginalText = "hello";
        Assert.True(vm.CanConvert);
    }

    [Fact]
    public async Task ConvertForgesKeysAndPopulatesPanel()
    {
        var vm = await ConvertedVmAsync($"API_KEY={SampleKey}\n");

        Assert.DoesNotContain(SampleKey, vm.ForgedText);
        Assert.Contains("gsk_", vm.ForgedText);
        Assert.Single(vm.DetectedKeys);
        Assert.Contains("1 key(s) detected", vm.SummaryText);
    }

    [Fact]
    public async Task ConvertPreservesSurroundingText()
    {
        var input = $"# config\nAPI_KEY={SampleKey}\nDB_HOST=localhost\n";
        var vm = await ConvertedVmAsync(input);

        Assert.Contains("# config", vm.ForgedText);
        Assert.Contains("DB_HOST=localhost", vm.ForgedText);
    }

    [Fact]
    public async Task ConvertNoKeysLeavesOutputUnchanged()
    {
        var vm = await ConvertedVmAsync("just some plain text\n");
        Assert.Equal("just some plain text\n", vm.ForgedText);
        Assert.Empty(vm.DetectedKeys);
    }

    [Fact]
    public async Task DeterministicSameInputSameFake()
    {
        var vm1 = await ConvertedVmAsync($"API_KEY={SampleKey}\n");
        var vm2 = await ConvertedVmAsync($"API_KEY={SampleKey}\n");
        Assert.Equal(vm1.ForgedText, vm2.ForgedText);
    }

    [Fact]
    public async Task RegenerateProducesDifferentFake()
    {
        var vm = await ConvertedVmAsync($"API_KEY={SampleKey}\n");
        var first = vm.ForgedText;
        var cmd = (AsyncRelayCommand<object>)vm.RegenerateCommand;
        cmd.Execute(null);
        await cmd.ExecutionTask;
        Assert.NotEqual(first, vm.ForgedText);
    }

    [Fact]
    public async Task SaveForgedWritesToDesktop()
    {
        var dir = Path.Combine(Path.GetTempPath(), "keyforge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var vm = NewVm(desktopDir: dir);
            vm.OriginalText = $"API_KEY={SampleKey}\n";
            var convert = (AsyncRelayCommand<object>)vm.ConvertCommand;
            convert.Execute(null);
            await convert.ExecutionTask;

            var save = (AsyncRelayCommand<object>)vm.SaveForgedCommand;
            save.Execute(null);
            await save.ExecutionTask;

            var files = Directory.GetFiles(dir, "keyforge-*.txt");
            Assert.Single(files);
            var content = await File.ReadAllTextAsync(files[0]);
            Assert.DoesNotContain(SampleKey, content);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ImportFileReadsContent()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "kf-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(tempFile, $"API_KEY={SampleKey}\n");
        try
        {
            Func<Task<string?>> pick = () => Task.FromResult<string?>(tempFile);
            var vm = NewVm(pickDocument: pick);
            var cmd = (AsyncRelayCommand<object>)vm.ImportFileCommand;
            cmd.Execute(null);
            await cmd.ExecutionTask;

            Assert.Contains(SampleKey, vm.OriginalText);
            Assert.Contains("kf-", vm.SourceLabel);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ClearResetsPanes()
    {
        var vm = await ConvertedVmAsync($"API_KEY={SampleKey}\n");
        vm.ClearCommand.Execute(null);
        Assert.Equal(string.Empty, vm.OriginalText);
        Assert.Equal(string.Empty, vm.ForgedText);
        Assert.Empty(vm.DetectedKeys);
    }

    [Fact]
    public async Task ResetRestoresDefaults()
    {
        var vm = await ConvertedVmAsync($"API_KEY={SampleKey}\n");
        vm.SaveFileName = "custom";
        vm.FakePolicy = FakePolicy.Random;
        vm.ResetCommand.Execute(null);
        Assert.Equal(string.Empty, vm.SaveFileName);
        Assert.Equal(FakePolicy.Deterministic, vm.FakePolicy);
    }

    [Fact]
    public async Task CopyForgedInvokesClipboard()
    {
        string? copied = null;
        Func<string, Task> copy = s => { copied = s; return Task.CompletedTask; };
        var vm = NewVm(copyToClipboard: copy);
        vm.OriginalText = $"API_KEY={SampleKey}\n";
        var convert = (AsyncRelayCommand<object>)vm.ConvertCommand;
        convert.Execute(null);
        await convert.ExecutionTask;

        var copyCmd = (AsyncRelayCommand<object>)vm.CopyForgedCommand;
        copyCmd.Execute(null);
        await copyCmd.ExecutionTask;

        Assert.NotNull(copied);
        Assert.DoesNotContain(SampleKey, copied);
    }
}
