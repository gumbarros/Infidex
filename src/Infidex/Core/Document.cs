using Infidex.Api;

namespace Infidex.Core;

/// <summary>
/// Represents a document in the search engine.
/// </summary>
public class Document
{
    public int Id { get; internal set; }
    public long DocumentKey { get; set; }
    public int SegmentNumber { get; set; }
    public DocumentFields Fields { get; set; }
    public string IndexedText { get; internal set; } = string.Empty;
    public string? DocumentClientInformation { get; set; }
    public string Reserved { get; set; }
    public int JsonIndex { get; set; }
    public bool Deleted { get; set; }
    internal float VectorLength { get; set; }

    public Document(long documentKey, string text)
    {
        DocumentKey = documentKey;
        SegmentNumber = 0;
        Reserved = string.Empty;
        Fields = new DocumentFields();
        Fields.AddField("content", text);
    }

    public Document(long documentKey, int segmentNumber, string text)
    {
        DocumentKey = documentKey;
        SegmentNumber = segmentNumber;
        Reserved = string.Empty;
        Fields = new DocumentFields();
        Fields.AddField("content", text);
    }

    public Document(long documentKey, DocumentFields fields)
    {
        DocumentKey = documentKey;
        SegmentNumber = 0;
        Fields = fields;
        Reserved = string.Empty;
    }

    public Document(long documentKey, int segmentNumber, DocumentFields fields, string? documentClientInformation = null)
    {
        DocumentKey = documentKey;
        SegmentNumber = segmentNumber;
        Fields = fields;
        DocumentClientInformation = documentClientInformation;
        Reserved = string.Empty;
    }

    internal Document(Document source)
    {
        Id = source.Id;
        DocumentKey = source.DocumentKey;
        SegmentNumber = source.SegmentNumber;
        Fields = source.Fields;
        IndexedText = source.IndexedText;
        DocumentClientInformation = source.DocumentClientInformation;
        Reserved = source.Reserved;
        JsonIndex = source.JsonIndex;
        Deleted = source.Deleted;
        VectorLength = source.VectorLength;
    }

    public override string ToString()
    {
        string preview = IndexedText;
        if (string.IsNullOrEmpty(preview))
        {
            Field? firstField = Fields?.GetSearchAbleFieldList().FirstOrDefault();
            preview = firstField?.Value?.ToString() ?? "(empty)";
        }

        int previewLength = Math.Min(50, preview.Length);
        return $"Doc {DocumentKey}:{SegmentNumber} - {preview[..previewLength]}...";
    }
}
