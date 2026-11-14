// ============================================================================
// FILE:     src/FileHarvester.App/ViewModels/AppPage.cs
// PURPOSE:  The set of full-body pages the main window can host. The
//           harvest page is the three-panel workflow; the cloud page is
//           the cloud connection destination.
// RELATION: Used by MainWindowViewModel.CurrentPage and by the page-host
//           assignment in MainWindow.axaml.cs.
// ============================================================================

namespace FileHarvester.App.ViewModels;

/// <summary>
/// The full-body pages the main window can host.
/// </summary>
public enum AppPage
{
    /// <summary>
    /// The three-panel harvest workflow. The default page.
    /// </summary>
    Harvest = 0,

    /// <summary>
    /// The cloud connection destination.
    /// </summary>
    Cloud = 1,

    /// <summary>
    /// Bulk path joiner workspace.
    /// </summary>
    LinkJoiner = 2,

    /// <summary>
    /// Local text extraction workspace.
    /// </summary>
    Extract = 3,

    /// <summary>
    /// Read-only database explorer workspace.
    /// </summary>
    SchemaLence = 4,

    /// <summary>
    /// The ImageTextify extraction workspace: images, PDFs,
    /// audio, and video into editable text.
    /// </summary>
    ImageTextify = 5,

    /// <summary>
    /// The Prompt App: a local prompt and document manager.
    /// </summary>
    Prompt = 6,

    /// <summary>
    /// The CodeViewer workspace: local project structure and code
    /// inspection. Appended; existing values are unchanged.
    /// </summary>
    CodeViewer = 7,

    /// <summary>
    /// The live transcription workspace: microphone and system audio
    /// loopback into a running transcript.
    /// </summary>
    LiveTranscription = 8,

    /// <summary>
    /// The live camera capture workspace: enumerate a camera, preview, freeze
    /// a frame, and send it through the same OCR pipeline. Appended;
    /// existing values are unchanged.
    /// </summary>
    CameraCapture = 9,

    /// <summary>
    /// The document/image conversion workspace. Appended; existing values
    /// are unchanged.
    /// </summary>
    Conversion = 10,

    /// <summary>
    /// The text-to-speech workspace. Appended; existing values are unchanged.
    /// </summary>
    TextToSpeech = 11,
}
