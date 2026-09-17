// Purpose: Translate Inventor 2027 assembly hierarchy and STEP translator state into application ports.
// Inputs: The live Inventor application, recursive occurrences, and explicit STEP export settings.
// Outputs: COM-free scan snapshots and STEP files created by Inventor's installed translator.
// Dependencies: Inventor 2027 interop v31, conditionally compiled when its installed assembly exists.
// Assumptions: Every call occurs synchronously on Inventor's owning STA thread.
// Validation source: Installed Autodesk.Inventor.Interop.xml and INVENTOR_2027_API_COMPATIBILITY.md.

#if INVENTOR_INTEROP
using System.Runtime.InteropServices;
using Inventor;
using SmartManufacturingExporter.Application.Phase1;
using SmartManufacturingExporter.Core.Phase1;
using IOFile = System.IO.File;
using IOPath = System.IO.Path;

namespace SmartManufacturingExporter.InventorAdapter.Phase1;

public sealed class InventorPhase1Gateway(Inventor.Application inventorApplication) : IInventorPhase1Gateway
{
    private const string StepTranslatorId = "{90AF7F40-0C01-11D5-8E83-0010B541CD80}";

    public ActiveAssemblyScan? ScanActiveAssembly()
    {
        AssemblyDocument? assembly;
        try
        {
            if (inventorApplication.ActiveDocumentType != DocumentTypeEnum.kAssemblyDocumentObject)
            {
                return null;
            }

            assembly = inventorApplication.ActiveDocument as AssemblyDocument;
        }
        catch (COMException)
        {
            return null;
        }

        if (assembly is null)
        {
            return null;
        }

        return new(
            assembly.FullFileName,
            SnapshotOccurrences(assembly.ComponentDefinition.Occurrences));
    }

    public void ExportDocumentAsStep(
        string sourcePath,
        string outputPath,
        StepExportPrecision precision)
    {
        ValidateExportPaths(sourcePath, outputPath);
        double fitToleranceCentimeters = precision.GetFitToleranceCentimeters();

        Document? sourceDocument = null;
        bool openedHere = false;
        InvalidOperationException? comFailure = null;
        string destinationDirectory = IOPath.GetDirectoryName(outputPath)!;
        string temporaryOutputPath = IOPath.Combine(
            destinationDirectory,
            $".SmartManufacturingExporter.{Guid.NewGuid():N}.step");
        try
        {
            try
            {
                sourceDocument = FindOpenDocument(sourcePath);
                if (sourceDocument is null)
                {
                    sourceDocument = (Document)inventorApplication.Documents.Open(sourcePath, false);
                    openedHere = true;
                }

                TranslatorAddIn translator = ResolveStepTranslator();
                TranslationContext context = inventorApplication.TransientObjects.CreateTranslationContext();
                context.Type = IOMechanismEnum.kFileBrowseIOMechanism;
                NameValueMap options = inventorApplication.TransientObjects.CreateNameValueMap();
                DataMedium medium = inventorApplication.TransientObjects.CreateDataMedium();
                medium.FileName = temporaryOutputPath;

                if (!translator.HasSaveCopyAsOptions[sourceDocument, context, options])
                {
                    throw new InvalidOperationException(
                        $"Inventor STEP translator '{StepTranslatorId}' did not provide export options for '{sourcePath}'. " +
                        "Confirm that the Inventor 2027 STEP translator is enabled and the source part is valid.");
                }

                options.Value["export_fit_tolerance"] = fitToleranceCentimeters;

                if (IOFile.Exists(outputPath))
                {
                    throw new InvalidOperationException(
                        $"The STEP output already exists and will not be overwritten: '{outputPath}'.");
                }

                translator.SaveCopyAs(sourceDocument, context, options, medium);
                try
                {
                    IOFile.Move(temporaryOutputPath, outputPath, false);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    throw new InvalidOperationException(
                        $"The STEP output could not be finalized without overwriting the final path: '{outputPath}'.",
                        exception);
                }
            }
            catch (COMException exception)
            {
                comFailure = new InvalidOperationException(
                    $"Inventor STEP translator '{StepTranslatorId}' failed to export '{sourcePath}' to '{outputPath}'.",
                    exception);
            }
        }
        finally
        {
            TryDeleteTemporaryOutput(temporaryOutputPath);

            if (openedHere && sourceDocument is not null)
            {
                try
                {
                    sourceDocument.Close(true);
                }
                catch (COMException exception)
                {
                    comFailure ??= new InvalidOperationException(
                        $"Inventor could not close the document opened for STEP translator '{StepTranslatorId}': '{sourcePath}'.",
                        exception);
                }
            }
        }

        if (comFailure is not null)
        {
            throw comFailure;
        }
    }

    private static void TryDeleteTemporaryOutput(string temporaryOutputPath)
    {
        try
        {
            IOFile.Delete(temporaryOutputPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup must never replace the primary translator or atomic-finalization failure.
        }
    }

    private static void ValidateExportPaths(string sourcePath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("A nonblank Inventor source path is required.", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("A nonblank STEP output path is required.", nameof(outputPath));
        }

        string sourceExtension = IOPath.GetExtension(sourcePath);
        if (!string.Equals(sourceExtension, ".ipt", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(sourceExtension, ".iam", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The Inventor source must be an .ipt part or .iam assembly: '{sourcePath}'.",
                nameof(sourcePath));
        }

        if (!string.Equals(IOPath.GetExtension(outputPath), ".step", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"The output must use the .step extension: '{outputPath}'.", nameof(outputPath));
        }

        if (!IOFile.Exists(sourcePath))
        {
            throw new FileNotFoundException($"The Inventor source does not exist: '{sourcePath}'.", sourcePath);
        }

        string? destinationDirectory = IOPath.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
        {
            throw new DirectoryNotFoundException($"The STEP destination directory does not exist: '{destinationDirectory}'.");
        }

        if (IOFile.Exists(outputPath))
        {
            throw new InvalidOperationException($"The STEP output already exists and will not be overwritten: '{outputPath}'.");
        }
    }

    private Document? FindOpenDocument(string sourcePath)
    {
        Documents documents = inventorApplication.Documents;
        for (int index = 1; index <= documents.Count; index++)
        {
            Document document = documents[index];
            bool supportedDocument = document.DocumentType is DocumentTypeEnum.kPartDocumentObject
                or DocumentTypeEnum.kAssemblyDocumentObject;
            if (supportedDocument
                && string.Equals(document.FullFileName, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return document;
            }
        }

        return null;
    }

    private static List<ComponentOccurrenceSnapshot> SnapshotOccurrences(ComponentOccurrences occurrences)
    {
        List<ComponentOccurrenceSnapshot> snapshots = new(occurrences.Count);
        for (int index = 1; index <= occurrences.Count; index++)
        {
            ComponentOccurrence occurrence = occurrences[index];
            snapshots.Add(SnapshotOccurrence(occurrence));
        }

        return snapshots;
    }

    private static ComponentOccurrenceSnapshot SnapshotOccurrence(ComponentOccurrence occurrence)
    {
        if (occurrence.Suppressed)
        {
            return new(occurrence.Name, null, ComponentDocumentKind.Other, true, []);
        }

        ComponentDocumentKind documentKind = occurrence.DefinitionDocumentType switch
        {
            DocumentTypeEnum.kPartDocumentObject => ComponentDocumentKind.Part,
            DocumentTypeEnum.kAssemblyDocumentObject => ComponentDocumentKind.Assembly,
            _ => ComponentDocumentKind.Other,
        };

        string? sourcePath = null;
        try
        {
            if (documentKind is ComponentDocumentKind.Part or ComponentDocumentKind.Assembly)
            {
                ComponentDefinition definition = occurrence.Definition;
                sourcePath = ((Document)definition.Document).FullFileName;
            }
        }
        catch (COMException)
        {
            // A pathless snapshot lets the application report the unresolved document explicitly.
        }
        catch (InvalidCastException)
        {
            // A pathless snapshot lets the application report inconsistent API state explicitly.
        }

        IReadOnlyList<ComponentOccurrenceSnapshot> children = documentKind == ComponentDocumentKind.Assembly
            ? SnapshotSubOccurrences(occurrence.SubOccurrences)
            : [];
        return new(occurrence.Name, sourcePath, documentKind, false, children);
    }

    private static List<ComponentOccurrenceSnapshot> SnapshotSubOccurrences(
        ComponentOccurrencesEnumerator occurrences)
    {
        List<ComponentOccurrenceSnapshot> snapshots = new(occurrences.Count);
        for (int index = 1; index <= occurrences.Count; index++)
        {
            snapshots.Add(SnapshotOccurrence(occurrences[index]));
        }

        return snapshots;
    }

    private TranslatorAddIn ResolveStepTranslator()
    {
        ApplicationAddIn addIn = inventorApplication.ApplicationAddIns.ItemById[StepTranslatorId];
        if (addIn is not TranslatorAddIn translator)
        {
            throw new InvalidOperationException($"Inventor STEP translator '{StepTranslatorId}' is not installed.");
        }

        if (!translator.TranslatorAvailable || !translator.SupportsSaveCopyAs)
        {
            throw new InvalidOperationException(
                $"Inventor STEP translator '{StepTranslatorId}' is unavailable or does not support SaveCopyAs.");
        }

        if (!translator.Activated)
        {
            translator.Activate();
        }

        return translator;
    }
}
#endif
