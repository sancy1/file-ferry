// ============================================================================
// FILE:     src/FileHarvester.Tests/ImageTextify/CompositionTests.cs
// PURPOSE:  Structural tests for the ImageTextify composition seam.
//           Asserts argument validation, root-directory creation, absence of
//           speculative subdirectories, fluent return, absence of premature
//           service registrations, and the invariants of the ImageTextify
//           policy-name constants including their XML documentation.
// RELATION: Exercises ImageTextifyServiceCollectionExtensions.AddImageTextify
//           and ImageTextifyPolicyNames. Phase 1.2 exit-criteria test.
// ============================================================================

using System.Reflection;
using System.Xml;
using FluentAssertions;
using ImageTextify.Platform.Composition;
using ImageTextify.Core.Abstractions;
using ImageTextify.Platform.Intake;
using ImageTextify.Platform.Models;
using ImageTextify.Platform.Validation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Phase 1.2 structural tests for AddImageTextify and ImageTextifyPolicyNames.
/// </summary>
public sealed class CompositionTests : IDisposable
{
    private readonly string _root;

    public CompositionTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "file-ferry-tests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Cleanup is best-effort; a lock or antivirus can hold
            // the directory briefly after the test ends.
        }
    }

    // -----------------------------------------------------------------
    // Argument validation
    // -----------------------------------------------------------------

    [Fact]
    public void AddImageTextifyThrowsWhenServicesNull()
    {
        IServiceCollection? services = null;

        var act = () => services!.AddImageTextify(_root);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddImageTextifyThrowsWhenDirectoryNullOrWhitespace(string? dataRoot)
    {
        var services = new ServiceCollection();

        var act = () => services.AddImageTextify(dataRoot!);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("applicationDataDirectory");
    }

    // -----------------------------------------------------------------
    // Directory creation
    // -----------------------------------------------------------------

    [Fact]
    public void AddImageTextifyCreatesImageTextifyFolderUnderAppData()
    {
        var services = new ServiceCollection();

        services.AddImageTextify(_root);

        var expected = Path.Combine(_root, "ImageTextify");
        Directory.Exists(expected).Should().BeTrue(
            "AddImageTextify owns and creates the ImageTextify root directory.");
    }

    [Fact]
    public void AddImageTextifyDoesNotCreateSubdirectoriesInPhase12()
    {
        var services = new ServiceCollection();

        services.AddImageTextify(_root);

        var imageTextifyRoot = Path.Combine(_root, "ImageTextify");

        Directory.Exists(Path.Combine(imageTextifyRoot, "Models")).Should().BeFalse(
            "Models is created by Phase 2, not by composition.");
        Directory.Exists(Path.Combine(imageTextifyRoot, "Temp")).Should().BeFalse(
            "Temp is created by Phase 5, not by composition.");
        Directory.Exists(Path.Combine(imageTextifyRoot, "Exports")).Should().BeFalse(
            "Exports is created by Phase 4, not by composition.");
    }

    // -----------------------------------------------------------------
    // Fluent return and no premature registrations
    // -----------------------------------------------------------------

    [Fact]
    public void AddImageTextifyReturnsSameCollection()
    {
        var services = new ServiceCollection();

        var returned = services.AddImageTextify(_root);

        returned.Should().BeSameAs(services,
            "the extension must return the same collection for fluent chaining.");
    }

    [Fact]
    public void AddImageTextifyRegistersApprovedServices()
    {
        var services = new ServiceCollection();

        services.AddImageTextify(_root);


        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IImageTextifyFileValidator) &&
            d.ImplementationType == typeof(ImageTextifyFileValidator) &&
            d.Lifetime == ServiceLifetime.Singleton);

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IImageTextifyIntakeService) &&
            d.ImplementationType == typeof(ImageTextifyIntakeService) &&
            d.Lifetime == ServiceLifetime.Singleton);

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IFileHasher) &&
            d.ImplementationType == typeof(Sha256FileHasher) &&
            d.Lifetime == ServiceLifetime.Singleton);

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IModelStore) &&
            d.ImplementationType == null &&
            d.ImplementationFactory != null &&
            d.Lifetime == ServiceLifetime.Singleton);
    }

    // -----------------------------------------------------------------
    // Policy name invariants
    // -----------------------------------------------------------------

    private static FieldInfo[] PolicyFields() =>
        typeof(ImageTextifyPolicyNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
            .ToArray();

    [Fact]
    public void PolicyNamesAllUseImageTextifyPrefix()
    {
        var fields = PolicyFields();
        fields.Should().NotBeEmpty();

        foreach (var field in fields)
        {
            var value = (string)field.GetRawConstantValue()!;
            value.Should().StartWith("image-textify-",
                "policy name " + field.Name + " must carry the ImageTextify prefix.");
        }
    }

    [Fact]
    public void PolicyNamesAreDistinct()
    {
        var values = PolicyFields()
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        values.Should().OnlyHaveUniqueItems(
            "each policy must have a distinct name to avoid silent collisions.");
    }

    [Fact]
    public void PolicyNamesAreInLowerCaseKebabCase()
    {
        var regex = new System.Text.RegularExpressions.Regex("^[a-z][a-z0-9-]*$");

        foreach (var field in PolicyFields())
        {
            var value = (string)field.GetRawConstantValue()!;
            regex.IsMatch(value).Should().BeTrue(
                "policy name " + value + " must be lowercase kebab-case.");
        }
    }

    [Fact]
    public void PolicyNamesAreDocumented()
    {
        var xmlPath = Path.Combine(
            AppContext.BaseDirectory,
            "ImageTextify.Platform.xml");

        File.Exists(xmlPath).Should().BeTrue(
            "ImageTextify.Platform.xml must exist next to the test assembly; " +
            "enable GenerateDocumentationFile if it is missing.");

        var doc = new XmlDocument();
        doc.Load(xmlPath);

        foreach (var field in PolicyFields())
        {
            var memberName = "F:ImageTextify.Platform.Composition.ImageTextifyPolicyNames." + field.Name;
            var node = doc.SelectSingleNode(
                "//member[@name='" + memberName + "']/summary");

            node.Should().NotBeNull(
                "public constant " + field.Name + " must have XML summary documentation.");
            node!.InnerText.Trim().Should().NotBeEmpty(
                "the summary for " + field.Name + " must not be empty.");
        }
    }
}
