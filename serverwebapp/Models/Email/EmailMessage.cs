namespace AsaServerManager.Web.Models.Email;

public record EmailInlineAsset(
    string ContentId,
    string FilePath,
    string MediaType
);

public record EmailMessage(
    string Subject,
    string Body,
    string Receiver,
    string? Sender = null,
    IReadOnlyList<EmailInlineAsset>? InlineAssets = null
);
