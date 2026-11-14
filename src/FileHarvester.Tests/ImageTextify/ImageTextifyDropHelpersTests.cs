// ============================================================================
// FILE:     src/FileHarvester.Tests/ImageTextify/ImageTextifyDropHelpersTests.cs
// PURPOSE:  Unit tests for the pure drag-and-drop path extractor used by
//           the ImageTextify page code-behind. The OS-level drop itself
//           cannot be tested here; the null, non-file, and order handling
//           can.
// RELATION: Companion to ImageTextifyPage.axaml.cs. Uses InternalsVisibleTo.
// ============================================================================

using System;
using Avalonia.Platform.Storage;
using FileHarvester.App.Views;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace FileHarvester.Tests.ImageTextify;

/// <summary>
/// Tests for the pure drop-path extractor.
/// </summary>
public sealed class ImageTextifyDropHelpersTests
{
    private static IStorageItem FileItem(string uri)
    {
        var item = Substitute.For<IStorageItem>();
        item.Path.Returns(new Uri(uri));
        return item;
    }

    private static IStorageItem NonFileItem()
    {
        var item = Substitute.For<IStorageItem>();
        item.Path.Returns(new Uri("https://example.com/remote.png"));
        return item;
    }

    [Fact]
    public void ExtractLocalPathsReturnsEmptyForNullInput()
    {
        var result = ImageTextifyDropHelpers.ExtractLocalPaths(null);
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractLocalPathsReturnsEmptyForEmptyInput()
    {
        var result = ImageTextifyDropHelpers.ExtractLocalPaths(Array.Empty<IStorageItem>());
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractLocalPathsSkipsNonFileItems()
    {
        var good = new Uri("file:///C:/a/b.png");
        var items = new[] { NonFileItem(), FileItem(good.AbsoluteUri), NonFileItem() };

        var result = ImageTextifyDropHelpers.ExtractLocalPaths(items);

        result.Should().ContainSingle().Which.Should().Be(good.LocalPath);
    }

    [Fact]
    public void ExtractLocalPathsPreservesOrder()
    {
        var a = new Uri("file:///C:/one.png");
        var b = new Uri("file:///C:/two.png");
        var c = new Uri("file:///C:/three.png");
        var items = new[] { FileItem(a.AbsoluteUri), FileItem(b.AbsoluteUri), FileItem(c.AbsoluteUri) };

        var result = ImageTextifyDropHelpers.ExtractLocalPaths(items);

        result.Should().Equal(a.LocalPath, b.LocalPath, c.LocalPath);
    }

    [Fact]
    public void ExtractLocalPathsKeepsOnlyValidPathsFromMixedInput()
    {
        var a = new Uri("file:///C:/a.png");
        var c = new Uri("file:///C:/c.png");
        var items = new[] { FileItem(a.AbsoluteUri), NonFileItem(), FileItem(c.AbsoluteUri) };

        var result = ImageTextifyDropHelpers.ExtractLocalPaths(items);

        result.Should().Equal(a.LocalPath, c.LocalPath);
    }
}