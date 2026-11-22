// ============================================================================
// filepath: src/FileHarvester.Tests/DeploymentVault/EnvSafe/EnvSafeViewModelTests.cs
// layer:    Tests
// module:   FileHarvester.Tests.DeploymentVault.EnvSafe
// since:    Phase A (EnvSafe)
// purpose:  Unit tests for EnvSafeViewModel: validation gate, conversion,
//           safety scan, download (with temp desktop), import, clear/reset.
// ============================================================================

using DeploymentVault.Core.EnvSafe.Domain;
using DeploymentVault.Platform.EnvSafe.Conversion;
using DeploymentVault.Platform.EnvSafe.Documents;
using DeploymentVault.Platform.EnvSafe.Safety;
using DeploymentVault.Platform.EnvSafe.Validation;
using FileHarvester.App.ViewModels;
using Xunit;

namespace FileHarvester.Tests.DeploymentVault.EnvSafe;

/// <summary>
/// Unit tests for <see cref="EnvSafeViewModel"/>.
/// </summary>
public sealed class EnvSafeViewModelTests
{
    private static EnvSafeViewModel NewVm(
        string? desktopDir = null,
        Func<string, string, Task<bool>>? confirm = null,
        Func<string, string, string, Task<string?>>? saveFile = null,
        Func<Task<string?>>? pickEnvFile = null,
        Func<string, Task>? copyToClipboard = null) =>
        new(
            new EnvFileValidator(),
            new EnvFileConverter(),
            new SafetyScanner(),
            new EnvFileReader(),
            confirm,
            saveFile,
            pickEnvFile,
            copyToClipboard,
            desktopDir);

    private static async Task<EnvSafeViewModel> ConvertedVmAsync(string input)
    {
        var vm = NewVm();
        vm.OriginalText = input;
        var cmd = (AsyncRelayCommand<object>)vm.ConvertCommand;
        cmd.Execute(null);
        await cmd.ExecutionTask;
        return vm;
    }

    [Fact]
    public void ConvertDisabledWhenNoKeyValue()
    {
        var vm = NewVm();
        vm.OriginalText = "# just a comment\nno variables\n";

        Assert.False(vm.CanConvert);
        Assert.Contains("No valid environment variables", vm.ValidationMessage);
    }

    [Fact]
    public void ConvertEnabledWhenKeyValuePresent()
    {
        var vm = NewVm();
        vm.OriginalText = "API_KEY=sk-123\n";

        Assert.True(vm.CanConvert);
        Assert.Equal(string.Empty, vm.ValidationMessage);
    }

    [Fact]
    public async Task ConvertProducesOutputAndSummary()
    {
        var vm = await ConvertedVmAsync("API_KEY=sk-123\n# note\nDB_HOST=localhost\n");

        Assert.Contains("API_KEY=your_api_key_here", vm.ConvertedText);
        Assert.Contains("# note", vm.ConvertedText);
        Assert.Contains("2 variables detected", vm.SummaryText);
        Assert.Contains("1 comments preserved", vm.SummaryText);
        Assert.False(vm.HasWarnings);
    }

    [Fact]
    public async Task ConvertWithRemainingSecretPopulatesWarnings()
    {
        // Hand-edit the output to reintroduce a secret, then re-scan.
        var vm = await ConvertedVmAsync("API_KEY=sk-123\n");
        vm.ConvertedText = "API_KEY=sk_live_" + new string('x', 20) + "123456\n";
        var cmd = (AsyncRelayCommand<object>)vm.SafetyScanCommand;
        cmd.Execute(null);
        await cmd.ExecutionTask;

        Assert.True(vm.HasWarnings);
        Assert.NotEmpty(vm.Warnings);
    }

    [Fact]
    public async Task DownloadWritesToDesktopDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "envsafe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var vm = NewVm(desktopDir: dir);
            vm.OriginalText = "API_KEY=sk-123\n";
            var convert = (AsyncRelayCommand<object>)vm.ConvertCommand;
            convert.Execute(null);
            await convert.ExecutionTask;

            var download = (AsyncRelayCommand<object>)vm.DownloadCommand;
            download.Execute(null);
            await download.ExecutionTask;

            var path = Path.Combine(dir, ".env.example");
            Assert.True(File.Exists(path));
            var content = await File.ReadAllTextAsync(path);
            Assert.Contains("API_KEY=your_api_key_here", content);
            Assert.Contains("Saved to", vm.StatusText);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task DownloadPromptsWhenFileExists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "envsafe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var existing = Path.Combine(dir, ".env.example");
            await File.WriteAllTextAsync(existing, "old");

            var confirmCalls = 0;
            Func<string, string, Task<bool>> confirm = (_, _) =>
            {
                confirmCalls++;
                return Task.FromResult(true); // replace
            };
            var vm = NewVm(desktopDir: dir, confirm: confirm);
            vm.OriginalText = "API_KEY=sk-123\n";
            var convert = (AsyncRelayCommand<object>)vm.ConvertCommand;
            convert.Execute(null);
            await convert.ExecutionTask;

            var download = (AsyncRelayCommand<object>)vm.DownloadCommand;
            download.Execute(null);
            await download.ExecutionTask;

            Assert.True(confirmCalls >= 1);
            var content = await File.ReadAllTextAsync(existing);
            Assert.Contains("your_api_key_here", content);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ImportFileReadsEnvContent()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "import-" + Guid.NewGuid().ToString("N") + ".env");
        await File.WriteAllTextAsync(tempFile, "API_KEY=sk-123\n");
        try
        {
            Func<Task<string?>> pick = () => Task.FromResult<string?>(tempFile);
            var vm = NewVm(pickEnvFile: pick);
            var cmd = (AsyncRelayCommand<object>)vm.ImportFileCommand;
            cmd.Execute(null);
            await cmd.ExecutionTask;

            Assert.Contains("API_KEY=sk-123", vm.OriginalText);
            Assert.Contains("import-", vm.SourceLabel);
            Assert.True(vm.CanConvert);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ClearResetsPanes()
    {
        var vm = await ConvertedVmAsync("API_KEY=sk-123\n");
        vm.ClearCommand.Execute(null);

        Assert.Equal(string.Empty, vm.OriginalText);
        Assert.Equal(string.Empty, vm.ConvertedText);
        Assert.False(vm.HasOutput);
    }

    [Fact]
    public async Task ResetRestoresDefaults()
    {
        var vm = await ConvertedVmAsync("API_KEY=sk-123\n");
        vm.SaveFileName = "custom";
        vm.OutputFormat = ".txt";
        vm.ResetCommand.Execute(null);

        Assert.Equal(".env.example", vm.SaveFileName);
        Assert.Equal(".env.example", vm.OutputFormat);
        Assert.Equal(PlaceholderStyle.YourXHere, vm.PlaceholderStyle);
        Assert.Equal(string.Empty, vm.ConvertedText);
    }

    [Fact]
    public async Task CopyOutputInvokesClipboard()
    {
        string? copied = null;
        Func<string, Task> copy = s => { copied = s; return Task.CompletedTask; };
        var vm = NewVm(copyToClipboard: copy);
        vm.OriginalText = "API_KEY=sk-123\n";
        var convert = (AsyncRelayCommand<object>)vm.ConvertCommand;
        convert.Execute(null);
        await convert.ExecutionTask;

        var copyCmd = (AsyncRelayCommand<object>)vm.CopyOutputCommand;
        copyCmd.Execute(null);
        await copyCmd.ExecutionTask;

        Assert.NotNull(copied);
        Assert.Contains("your_api_key_here", copied);
    }
}
