namespace NotificationService;

/// <summary>
/// Abstraction for sending email messages from the notification service.
/// Implementations are responsible for delivering the message to the recipient
/// and should honor the provided <see cref="CancellationToken"/>.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends an email message asynchronously.
    /// </summary>
    /// <param name="to">Recipient email address.</param>
    /// <param name="subject">Subject of the email message.</param>
    /// <param name="body">Body of the email. May contain HTML markup.</param>
    /// <param name="cancellationToken">Optional token to cancel the send operation.</param>
    /// <returns>A <see cref="Task"/> that completes when the send operation has finished.</returns>
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}
