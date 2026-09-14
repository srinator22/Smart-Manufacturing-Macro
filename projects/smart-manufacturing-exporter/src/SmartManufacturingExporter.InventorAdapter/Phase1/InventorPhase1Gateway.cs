// Purpose: Translate Inventor 2027 assembly and STEP translator state into the Phase 1 application ports.
// Inputs: The live Inventor application, active top-level occurrences, and explicit part/output paths.
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

        ComponentOccurrences occurrences = assembly.ComponentDefinition.Occurrences;
        List<TopLevelOccurrenceSnapshot> snapshots = new(occurrences.Count);
        for (int index = 1; index <= occurrences.Count; index++)
        {
            ComponentOccurrence occurrence = occurrences[index];
            if (occurrence.Suppressed)
            {
                snapshots.Add(new(occurrence.Name, null, ComponentDocumentKind.Other, true));
                continue;
            }

            ComponentDocumentKind documentKind = occurrence.DefinitionDocumentType switch
            {
                DocumentTypeEnum.kPartDocumentObject => ComponentDocumentKind.Part,
                DocumentTypeEnum.kAssemblyDocumentObject => ComponentDocumentKind.Assembly,
                _ => ComponentDocumentKind.Other,
            };

            string? sourcePath = null;
            if (documentKind == ComponentDocumentKind.Part)
            {
                try
                {
                    PartComponentDefinition definition = (PartComponentDefinition)occurrence.Definition;
                    PartDocument partDocument = (PartDocument)definition.Document;
                    sourcePath = partDocument.FullFileName;
                }
                catch (COMException)
                {
                    // A Part snapshot with no path lets the application report the unresolved source explicitly.
                }
                catch (InvalidCastException)
                {
                    // A Part snapshot with no path lets the application report the inconsistent source explicitly.
                }
            }

            snapshots.Add(new(occurrence.Name, sourcePath, documentKind, false));
        }

        return new(assembly.FullFileName, snapshots);
    }

    public void ExportPartAsStep(string sourcePath, string outputPath)
    {
        ValidateExportPaths(sourcePath, outputPath);

        PartDocument? partDocument = null;
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
                partDocument = FindOpenPartDocument(sourcePath);
                if (partDocument is null)
                {
                    partDocument = (PartDocument)inventorApplication.Documents.Open(sourcePath, false);
                    openedHere = true;
                }

                TranslatorAddIn translator = ResolveStepTranslator();
                TranslationContext context = inventorApplication.TransientObjects.CreateTranslationContext();
                context.Type = IOMechanismEnum.kFileBrowseIOMechanism;
                NameValueMap options = inventorApplication.TransientObjects.CreateNameValueMap();
                DataMedium medium = inventorApplication.TransientObjects.CreateDataMedium();
                medium.FileName = temporaryOutputPath;

                _ = translator.HasSaveCopyAsOptions[partDocument, context, options];

                if (IOFile.Exists(outputPath))
                {
                    throw new InvalidOperationException(
                        $"The STEP output already exists and will not be overwritten: '{outputPath}'.");
                }

                translator.SaveCopyAs(partDocument, context, options, medium);
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

            if (openedHere && partDocument is not null)
            {
                try
                {
                    partDocument.Close(true);
                }
                catch (COMException exception)
                {
                    comFailure ??= new InvalidOperationException(
                        $"Inventor could not close the part opened for STEP translator '{StepTranslatorId}': '{sourcePath}'.",
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
            throw new ArgumentException("A nonblank Inventor part source path is required.", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("A nonblank STEP output path is required.", nameof(outputPath));
        }

        if (!string.Equals(IOPath.GetExtension(sourcePath), ".ipt", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"The Inventor source must be an .ipt part: '{sourcePath}'.", nameof(sourcePath));
        }

        if (!string.Equals(IOPath.GetExtension(outputPath), ".step", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"The output must use the .step extension: '{outputPath}'.", nameof(outputPath));
        }

        if (!IOFile.Exists(sourcePath))
        {
            throw new FileNotFoundException($"The Inventor part source does not exist: '{sourcePath}'.", sourcePath);
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

    private PartDocument? FindOpenPartDocument(string sourcePath)
    {
        Documents documents = inventorApplication.Documents;
        for (int index = 1; index <= documents.Count; index++)
        {
            Document document = documents[index];
            if (document.DocumentType == DocumentTypeEnum.kPartDocumentObject
                && string.Equals(document.FullFileName, sourcePath, StringComparison.OrdinalIgnoreCase))
            {
                return (PartDocument)document;
            }
        }

        return null;
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
