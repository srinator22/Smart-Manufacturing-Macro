// Purpose: Translate the live Inventor 2027 active assembly into COM-free naming snapshots and perform renames.
// Inputs: The live Inventor application; documents already open or opened invisibly by this gateway.
// Outputs: ActiveAssemblySnapshot/DocumentSnapshot records; in-session renames via SaveAs and property writes.
// Dependencies: Inventor 2027 interop v31, conditionally compiled when its installed assembly exists.
// Assumptions: Every call occurs synchronously on Inventor's owning STA thread. A companion drawing must
//   already be open in the session before its model is renamed with SaveAs(new, false), because only an
//   open drawing has its reference rewritten in memory by the model's SaveAs; a drawing opened afterwards
//   resolves to the original file, which by then has been moved to _renamed-originals. EnsureDocumentOpen
//   exists so the workflow can open companion drawings ahead of a model rename.
// Validation source: Installed Autodesk.Inventor.Interop.xml and INVENTOR_2027_API_COMPATIBILITY.md.

#if INVENTOR_INTEROP
using System.Runtime.InteropServices;
using FileNamingManager.Application;
using FileNamingManager.Core;
using Inventor;
using IOFile = System.IO.File;
using IOPath = System.IO.Path;

namespace FileNamingManager.InventorAdapter;

public sealed class InventorNamingGateway(Inventor.Application inventorApplication) : IInventorNamingGateway
{
    private readonly List<Document> _documentsOpenedHere = [];

    public ActiveAssemblySnapshot? ScanActiveAssembly()
    {
        AssemblyDocument? root;
        try
        {
            if (inventorApplication.ActiveDocumentType != DocumentTypeEnum.kAssemblyDocumentObject)
            {
                return null;
            }

            root = inventorApplication.ActiveDocument as AssemblyDocument;
        }
        catch (COMException)
        {
            return null;
        }

        if (root is null || string.IsNullOrEmpty(root.FullFileName))
        {
            return null;
        }

        try
        {
            var knownFullPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root.FullFileName };
            var children = new List<Document>();

            DocumentsEnumerator referenced = root.AllReferencedDocuments;
            for (int index = 1; index <= referenced.Count; index++)
            {
                Document child = referenced[index];
                if (string.IsNullOrEmpty(child.FullFileName))
                {
                    continue;
                }

                knownFullPaths.Add(child.FullFileName);
                children.Add(child);
            }

            Dictionary<string, bool> suppressedOnly = ComputeSuppressedOnly(root);

            // The root is never anyone's child in this snapshot, so every one of its referencing
            // documents is by definition outside it - typically the assembly this root is itself a
            // sub-assembly of, when the user activated it while that parent is also open. Without this,
            // renaming the root here would silently strand that parent's reference (F1).
            List<string> rootExternalParentFullPaths = ReadExternalParentFullPaths((Document)root);

            var documents = new List<DocumentSnapshot>
            {
                new(
                    root.FullFileName,
                    DocumentKind.Assembly,
                    true,
                    root.IsModifiable,
                    root.Dirty,
                    false,
                    [],
                    rootExternalParentFullPaths,
                    ReadPartNumber((Document)root)),
            };

            foreach (Document child in children)
            {
                var parentFullPaths = new List<string>();
                var externalParentFullPaths = new List<string>();
                DocumentsEnumerator referencing = child.ReferencingDocuments;
                for (int index = 1; index <= referencing.Count; index++)
                {
                    Document parent = referencing[index];
                    if (string.IsNullOrEmpty(parent.FullFileName))
                    {
                        continue;
                    }

                    // A referencing document outside the active assembly's own set - typically a drawing,
                    // or another assembly the user also has open - still points at this file by name, so
                    // the planner has to know about it before it renames the file underneath it.
                    if (knownFullPaths.Contains(parent.FullFileName))
                    {
                        AddUnique(parentFullPaths, parent.FullFileName);
                    }
                    else
                    {
                        AddUnique(externalParentFullPaths, parent.FullFileName);
                    }
                }

                bool isSuppressedOnly = suppressedOnly.TryGetValue(child.FullFileName, out bool value) && value;

                documents.Add(new DocumentSnapshot(
                    child.FullFileName,
                    DocumentKindExtensions.FromExtension(IOPath.GetExtension(child.FullFileName)),
                    false,
                    child.IsModifiable,
                    child.Dirty,
                    isSuppressedOnly,
                    parentFullPaths,
                    externalParentFullPaths,
                    ReadPartNumber(child)));
            }

            return new ActiveAssemblySnapshot(
                root.FullFileName,
                root.Dirty,
                root.HasReferencesMissing,
                documents);
        }
        catch (COMException)
        {
            return null;
        }
    }

    public void RenameDocument(string currentFullPath, string newFullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentFullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(newFullPath);

        if (!IOPath.IsPathRooted(currentFullPath))
        {
            throw new InvalidOperationException($"The current path is not an absolute path: '{currentFullPath}'.");
        }

        if (!IOPath.IsPathRooted(newFullPath))
        {
            throw new InvalidOperationException($"The new path is not an absolute path: '{newFullPath}'.");
        }

        if (!string.Equals(IOPath.GetExtension(currentFullPath), IOPath.GetExtension(newFullPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Rename must keep the same extension: '{currentFullPath}' -> '{newFullPath}'.");
        }

        if (IOFile.Exists(newFullPath))
        {
            throw new InvalidOperationException(
                $"The rename target already exists and will not be overwritten: '{newFullPath}'.");
        }

        Document document = FindOpenDocument(currentFullPath) ?? OpenInvisibly(currentFullPath);

        try
        {
            document.SaveAs(newFullPath, false);
        }
        catch (COMException exception)
        {
            throw new InvalidOperationException(
                $"Inventor could not rename '{currentFullPath}' to '{newFullPath}' (HRESULT 0x{exception.HResult:X8}).",
                exception);
        }
    }

    public void SaveDocument(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        Document document = FindOpenDocument(fullPath)
            ?? throw new InvalidOperationException(
                $"Cannot save a document that is not open in this session: '{fullPath}'.");

        if (!document.Dirty)
        {
            return;
        }

        if (!document.IsModifiable)
        {
            throw new InvalidOperationException($"Cannot save a document that is not modifiable: '{fullPath}'.");
        }

        try
        {
            document.Save();
        }
        catch (COMException exception)
        {
            throw new InvalidOperationException(
                $"Inventor could not save '{fullPath}' (HRESULT 0x{exception.HResult:X8}).",
                exception);
        }
    }

    public void SetPartNumber(string fullPath, string partNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentNullException.ThrowIfNull(partNumber);

        Document document = FindOpenDocument(fullPath) ?? OpenInvisibly(fullPath);

        try
        {
            document.PropertySets["Design Tracking Properties"]["Part Number"].Value = partNumber;
        }
        catch (COMException exception)
        {
            throw new InvalidOperationException(
                $"Inventor could not set the Part Number property on '{fullPath}' (HRESULT 0x{exception.HResult:X8}).",
                exception);
        }

        if (!document.IsModifiable)
        {
            throw new InvalidOperationException($"Cannot save a document that is not modifiable: '{fullPath}'.");
        }

        try
        {
            document.Save();
        }
        catch (COMException exception)
        {
            throw new InvalidOperationException(
                $"Inventor could not save '{fullPath}' (HRESULT 0x{exception.HResult:X8}).",
                exception);
        }
    }

    /// <summary>
    /// Ensures fullPath is open in the session, opening it invisibly and tracking it for
    /// <see cref="CloseDocumentsOpenedHere"/> when it was not already open. Idempotent.
    /// A companion drawing must be opened through this method before its model is renamed,
    /// so the drawing's reference is rewritten in memory by the model's SaveAs.
    /// </summary>
    public void EnsureDocumentOpen(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        if (FindOpenDocument(fullPath) is not null)
        {
            return;
        }

        OpenInvisibly(fullPath);
    }

    /// <summary>
    /// Closes, with Close(true), only the documents this gateway instance opened and that are
    /// not the active document. Not part of the port interface; the AddIn calls it at the end
    /// of an Apply once every parent and companion rename has been saved.
    /// </summary>
    public void CloseDocumentsOpenedHere()
    {
        Document? active;
        try
        {
            active = inventorApplication.ActiveDocument;
        }
        catch (COMException)
        {
            active = null;
        }

        foreach (Document document in _documentsOpenedHere)
        {
            if (ReferenceEquals(document, active))
            {
                continue;
            }

            try
            {
                document.Close(true);
            }
            catch (COMException)
            {
                // Best-effort cleanup; a document already closed by the user is not fatal here.
            }
        }

        _documentsOpenedHere.Clear();
    }

    private Document OpenInvisibly(string fullPath)
    {
        if (!IOFile.Exists(fullPath))
        {
            throw new InvalidOperationException($"Cannot open a document that does not exist on disk: '{fullPath}'.");
        }

        Document opened;
        try
        {
            opened = (Document)inventorApplication.Documents.Open(fullPath, false);
        }
        catch (COMException exception)
        {
            throw new InvalidOperationException(
                $"Inventor could not open '{fullPath}' (HRESULT 0x{exception.HResult:X8}).",
                exception);
        }

        _documentsOpenedHere.Add(opened);
        return opened;
    }

    private Document? FindOpenDocument(string fullPath)
    {
        Documents documents = inventorApplication.Documents;
        for (int index = 1; index <= documents.Count; index++)
        {
            Document document = documents[index];
            string documentFullPath;
            try
            {
                documentFullPath = document.FullFileName;
            }
            catch (COMException)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(documentFullPath)
                && string.Equals(documentFullPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return document;
            }
        }

        return null;
    }

    /// <summary>
    /// Every referencing document of document, deduplicated, for callers where none of them can be
    /// inside the snapshot (the root: see ScanActiveAssembly).
    /// </summary>
    private static List<string> ReadExternalParentFullPaths(Document document)
    {
        var externalParentFullPaths = new List<string>();
        DocumentsEnumerator referencing = document.ReferencingDocuments;
        for (int index = 1; index <= referencing.Count; index++)
        {
            Document parent = referencing[index];
            if (string.IsNullOrEmpty(parent.FullFileName))
            {
                continue;
            }

            AddUnique(externalParentFullPaths, parent.FullFileName);
        }

        return externalParentFullPaths;
    }

    private static void AddUnique(List<string> parentFullPaths, string value)
    {
        foreach (string existing in parentFullPaths)
        {
            if (string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        parentFullPaths.Add(value);
    }

    private static string? ReadPartNumber(Document document)
    {
        try
        {
            return document.PropertySets["Design Tracking Properties"]["Part Number"].Value as string;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>
    /// Walks every occurrence reachable from the root, once, recording per referenced full file
    /// name whether any unsuppressed occurrence of it exists. Suppressed occurrences are not
    /// descended into. A document with no occurrence found (e.g. referenced only by a drawing)
    /// is absent from the result and is therefore never reported as suppressed-only.
    /// </summary>
    private static Dictionary<string, bool> ComputeSuppressedOnly(AssemblyDocument root)
    {
        var hasUnsuppressedOccurrence = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        WalkOccurrences(root.ComponentDefinition.Occurrences, hasUnsuppressedOccurrence);

        var suppressedOnly = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, bool> entry in hasUnsuppressedOccurrence)
        {
            suppressedOnly[entry.Key] = !entry.Value;
        }

        return suppressedOnly;
    }

    private static void WalkOccurrences(ComponentOccurrences occurrences, Dictionary<string, bool> hasUnsuppressedOccurrence)
    {
        for (int index = 1; index <= occurrences.Count; index++)
        {
            WalkOccurrence(occurrences[index], hasUnsuppressedOccurrence);
        }
    }

    private static void WalkOccurrences(ComponentOccurrencesEnumerator occurrences, Dictionary<string, bool> hasUnsuppressedOccurrence)
    {
        for (int index = 1; index <= occurrences.Count; index++)
        {
            WalkOccurrence(occurrences[index], hasUnsuppressedOccurrence);
        }
    }

    private static void WalkOccurrence(ComponentOccurrence occurrence, Dictionary<string, bool> hasUnsuppressedOccurrence)
    {
        string? fullFileName;
        try
        {
            fullFileName = occurrence.ReferencedDocumentDescriptor?.FullDocumentName;
        }
        catch (COMException)
        {
            return;
        }

        bool suppressed = occurrence.Suppressed;

        if (!string.IsNullOrEmpty(fullFileName))
        {
            bool existing = hasUnsuppressedOccurrence.TryGetValue(fullFileName, out bool value) && value;
            hasUnsuppressedOccurrence[fullFileName] = existing || !suppressed;
        }

        if (suppressed)
        {
            return;
        }

        try
        {
            WalkOccurrences(occurrence.SubOccurrences, hasUnsuppressedOccurrence);
        }
        catch (COMException)
        {
            // A resolvable-but-unwalkable occurrence still records its own suppression state above.
        }
    }
}
#endif
